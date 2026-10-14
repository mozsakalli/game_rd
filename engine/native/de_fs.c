// ---------------------------------------------------------------------------
// de_fs.c — stdio backend (Windows/macOS/Linux/iOS) + worker job kuyrugu
// ---------------------------------------------------------------------------
// Thread shim: Win32 (CreateThread/CRITICAL_SECTION/Semaphore) veya pthread.
// Tek worker: FILE* handle'lari yalniz worker okur (ana thread acar/kapar;
// kapatmadan once bagli job'lar bitmis olmali — managed sozlesme).
// Sonuclar de_async kuyruguna yazilir (de_async.h); burada poll/slot tablosu yok.
// ---------------------------------------------------------------------------
#include "de_fs.h"
#include "de_async.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STB_IMAGE_IMPLEMENTATION
#define STBI_STATIC
#include "stb_image.h"

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
typedef CRITICAL_SECTION de_mutex_t;
static void de_mutex_init(de_mutex_t *m) { InitializeCriticalSection(m); }
static void de_mutex_lock(de_mutex_t *m) { EnterCriticalSection(m); }
static void de_mutex_unlock(de_mutex_t *m) { LeaveCriticalSection(m); }
static HANDLE de_sem;
static void de_sem_init(int max) { de_sem = CreateSemaphoreA(NULL, 0, max, NULL); }
static void de_sem_post(void) { ReleaseSemaphore(de_sem, 1, NULL); }
static void de_sem_wait(void) { WaitForSingleObject(de_sem, INFINITE); }
#define de_atomic_store(p, v) InterlockedExchange((volatile LONG *)(p), (v))
#define de_atomic_load(p) InterlockedCompareExchange((volatile LONG *)(p), 0, 0)
#define de_fseek64(f, off) _fseeki64((f), (off), SEEK_SET)
#define de_ftell64(f) _ftelli64(f)
typedef volatile LONG de_state_t;
#else
#include <pthread.h>
#include <semaphore.h>
typedef pthread_mutex_t de_mutex_t;
static void de_mutex_init(de_mutex_t *m) { pthread_mutex_init(m, NULL); }
static void de_mutex_lock(de_mutex_t *m) { pthread_mutex_lock(m); }
static void de_mutex_unlock(de_mutex_t *m) { pthread_mutex_unlock(m); }
// macOS'ta adsiz sem_init yok: mutex+cond ile sayac
static pthread_mutex_t de_sem_lock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t de_sem_cond = PTHREAD_COND_INITIALIZER;
static int de_sem_count;
static void de_sem_init(int max) { (void)max; }
static void de_sem_post(void)
{
    pthread_mutex_lock(&de_sem_lock);
    de_sem_count++;
    pthread_cond_signal(&de_sem_cond);
    pthread_mutex_unlock(&de_sem_lock);
}
static void de_sem_wait(void)
{
    pthread_mutex_lock(&de_sem_lock);
    while (de_sem_count == 0)
        pthread_cond_wait(&de_sem_cond, &de_sem_lock);
    de_sem_count--;
    pthread_mutex_unlock(&de_sem_lock);
}
#define de_atomic_store(p, v) __atomic_store_n((p), (v), __ATOMIC_RELEASE)
#define de_atomic_load(p) __atomic_load_n((p), __ATOMIC_ACQUIRE)
#define de_fseek64(f, off) fseeko((f), (off_t)(off), SEEK_SET)
#define de_ftell64(f) ftello(f)
typedef volatile int de_state_t;
#endif

#define DE_MAX_HANDLES 32
#define DE_PATH_MAX 512

enum
{
    KIND_OPEN = 0,
    KIND_READ = 1,
    KIND_DECODE = 2
};

typedef struct
{
    FILE *f;
    long long base; // dosya icindeki baslangic (APK icine gomulu asset: fd+offset); normal dosyada 0
    long long size;
    const unsigned char *mem; // web: bellek dosyasi (de_fs_mount_memory); f bu durumda sentinel (acik isareti), okunmaz
} de_handle_t;

#if defined(__EMSCRIPTEN__)
// ---- web: dosya sistemi yok; host pak'i heap'e alip baglar. Tek kayit yeter (game.pak); kucuk sabit tablo.
#define DE_MEM_FILES 4
static struct { char path[DE_PATH_MAX]; const unsigned char *data; long long len; } de_mem_files[DE_MEM_FILES];
static FILE *const DE_MEM_SENTINEL = (FILE *)1; // de_handle_t.f: "acik" bayragi (stdio handle'i degil)

DE_FS_API void de_fs_mount_memory(const char *path, const void *data, long long len)
{
    for (int i = 0; i < DE_MEM_FILES; i++)
        if (!de_mem_files[i].data || strcmp(de_mem_files[i].path, path) == 0)
        {
            strncpy(de_mem_files[i].path, path, DE_PATH_MAX - 1);
            de_mem_files[i].path[DE_PATH_MAX - 1] = 0;
            de_mem_files[i].data = (const unsigned char *)data;
            de_mem_files[i].len = len;
            return;
        }
}
DE_FS_API int de_fs_mem_range(const char *path, const void **data, long long *len)
{
    for (int i = 0; i < DE_MEM_FILES; i++)
        if (de_mem_files[i].data && strcmp(de_mem_files[i].path, path) == 0)
        {
            *data = de_mem_files[i].data;
            *len = de_mem_files[i].len;
            return 1;
        }
    return 0;
}
#endif

// Kuyrukta bekleyen is (malloc'lu, worker bitirince free). Sonuc de_async'e gider.
typedef struct de_job_s
{
    struct de_job_s *next;
    int id;
    int kind;
    int handle;
    char path[DE_PATH_MAX];
    long long offset, length, rawLength;
} de_job_t;

static de_handle_t de_handles[DE_MAX_HANDLES];
static de_job_t *de_q_head, *de_q_tail;
static de_mutex_t de_lock;
static int de_started;

#if defined(__ANDROID__)
// ---- APK assets/: AAssetManager -> fd + offset. Asset sikistirilmamissa (Gradle noCompress) APK'nin
// kendisi bir dosya olarak acilir, asset o dosyanin [start, start+len) araligidir -> stdio ile ayni yol.
#include <android/asset_manager.h>
#include <unistd.h>
static AAssetManager *de_android_assets;

DE_FS_API void de_fs_set_asset_manager(void *aassetManager) { de_android_assets = (AAssetManager *)aassetManager; }

static FILE *de_open_asset(const char *name, long long *base, long long *size)
{
    if (!de_android_assets)
        return NULL;
    while (*name == '/')
        name++;
    AAsset *a = AAssetManager_open(de_android_assets, name, AASSET_MODE_RANDOM);
    if (!a)
        return NULL;
    off64_t start = 0, len = 0;
    int fd = AAsset_openFileDescriptor64(a, &start, &len); // fd bize ait (dup'li)
    AAsset_close(a);
    if (fd < 0)
        return NULL; // sikistirilmis asset: fd verilmez
    FILE *f = fdopen(fd, "rb");
    if (!f)
    {
        close(fd);
        return NULL;
    }
    *base = start;
    *size = len;
    return f;
}
#endif

// ---- worker ----

// Ham baytlari okur (+ gerekirse inflate). Donus malloc'lu; *outLen dolu. NULL = hata.
static unsigned char *de_read_blob(de_job_t *job, long long *outLen)
{
    FILE *f;
    int own = 0;
    long long base = 0;
    const unsigned char *mem = NULL;
    if (job->handle >= 0)
    {
        f = de_handles[job->handle].f;
        base = de_handles[job->handle].base;
        mem = de_handles[job->handle].mem;
    }
    else
    {
#if defined(__EMSCRIPTEN__)
        return NULL; // web: yol-tabanli okuma yok (dosya sistemi yok); yalniz bagli bellek dosyalari
#else
        f = fopen(job->path, "rb");
        own = 1;
#endif
    }
    if (!f)
        return NULL;
    long long length = job->length;
    if (length <= 0)
    {
        if (job->handle >= 0)
            length = de_handles[job->handle].size - job->offset;
        else
        {
            fseek(f, 0, SEEK_END);
            length = de_ftell64(f) - job->offset;
        }
        if (job->rawLength <= 0)
            job->rawLength = length;
    }
    unsigned char *buf = (unsigned char *)malloc((size_t)(length > 0 ? length : 1));
    unsigned char *result = NULL;
    int got = 0;
    if (buf)
    {
        if (mem) // bellek dosyasi: sinir denetimli kopya
        {
            long long sz = de_handles[job->handle].size;
            if (job->offset >= 0 && length >= 0 && job->offset + length <= sz) { memcpy(buf, mem + job->offset, (size_t)length); got = 1; }
        }
        else
            got = de_fseek64(f, base + job->offset) == 0 && fread(buf, 1, (size_t)length, f) == (size_t)length;
    }
    if (got)
    {
        if (job->rawLength != length)
        {
            unsigned char *raw = (unsigned char *)malloc((size_t)job->rawLength);
            if (raw && stbi_zlib_decode_buffer((char *)raw, (int)job->rawLength,
                                               (const char *)buf, (int)length) == (int)job->rawLength)
            {
                result = raw;
                *outLen = job->rawLength;
            }
            else
                free(raw);
            free(buf);
        }
        else
        {
            result = buf;
            *outLen = length;
        }
    }
    else
        free(buf);
    if (own && !mem)
        fclose(f);
    return result;
}

// Isi kosar, sonucu de_async'e yazar, job'u serbest birakir. code: 1 tamam, -1 hata; decode a=w b=h.
static void de_run_job(de_job_t *job)
{
    long long len = 0;
    unsigned char *blob = de_read_blob(job, &len);
    if (!blob)
        de_async_complete(job->id, -1, NULL, 0, 0, 0);
    else if (job->kind == KIND_READ)
        de_async_complete(job->id, 1, blob, len, 0, 0);
    else
    {
        int w = 0, h = 0, comp = 0;
        unsigned char *px = NULL;
        int ch = 4;
        {
            // Dosyadaki kanal korunur: gray (1) -> R8, digerleri RGBA'ya tamamlanir (2/3 kanal GPU formati yok).
            if (stbi_info_from_memory(blob, (int)len, &w, &h, &comp) && comp == 1)
                ch = 1;
            px = stbi_load_from_memory(blob, (int)len, &w, &h, &comp, ch); // stbi allocator = malloc
        }
        free(blob);
        if (px)
            de_async_complete(job->id, 1, px, (long long)w * h * ch, w, h);
        else
            de_async_complete(job->id, -1, NULL, 0, 0, 0);
    }
    free(job);
}

#if defined(_WIN32)
static DWORD WINAPI de_worker(LPVOID arg)
#else
static void *de_worker(void *arg)
#endif
{
    (void)arg;
    for (;;)
    {
        de_sem_wait();
        de_mutex_lock(&de_lock);
        de_job_t *job = de_q_head;
        if (job)
        {
            de_q_head = job->next;
            if (!de_q_head)
                de_q_tail = NULL;
        }
        de_mutex_unlock(&de_lock);
        if (job)
            de_run_job(job);
    }
#if !defined(_WIN32)
    return NULL;
#endif
}

static void de_ensure(void)
{
    if (de_started)
        return;
    de_started = 1;
    stbi_set_flip_vertically_on_load(1); // satir 0 altta: quad UV'leriyle uyumlu
    for (int i = 0; i < DE_MAX_HANDLES; i++)
        de_handles[i].f = NULL;
    de_mutex_init(&de_lock);
    de_sem_init(1 << 20);
#if defined(__EMSCRIPTEN__)
    // Tarayici: pthread yok (tek thread); job'lar submit aninda senkron kosar (pak heap'te bellek dosyasi, memcpy).
#elif defined(_WIN32)
    CreateThread(NULL, 0, de_worker, NULL, 0, NULL);
#else
    pthread_t t;
    if (pthread_create(&t, NULL, de_worker, NULL) == 0)
        pthread_detach(t);
#endif
}

// Job'u kurar ve worker kuyruguna atar (web: hemen kosar). Donus job id; bellek yoksa -1. Yalniz ana thread.
static int de_submit(int kind, int handle, const char *path, long long off, long long len, long long raw)
{
    de_ensure();
    de_job_t *j = (de_job_t *)calloc(1, sizeof(de_job_t));
    if (!j)
        return -1;
    j->id = de_async_new_job();
    j->kind = kind;
    j->handle = handle;
    j->offset = off;
    j->length = len;
    j->rawLength = raw;
    if (path)
    {
        strncpy(j->path, path, DE_PATH_MAX - 1);
        j->path[DE_PATH_MAX - 1] = 0;
    }
    int id = j->id;
#if defined(__EMSCRIPTEN__)
    de_run_job(j); // tarayici: tek thread, senkron (pak heap'te: memcpy)
#else
    de_mutex_lock(&de_lock);
    if (de_q_tail)
        de_q_tail->next = j;
    else
        de_q_head = j;
    de_q_tail = j;
    de_mutex_unlock(&de_lock);
    de_sem_post();
#endif
    return id;
}

// ---- API ----

// stdio: acma ana thread'de aninda biter (web backend'inde fetch job'u olur). Sonuc de_async: a = handle.
DE_FS_API int de_fs_open(const char *path)
{
    de_ensure();
    int h = -1;
    for (int i = 0; i < DE_MAX_HANDLES; i++)
        if (!de_handles[i].f)
        {
            h = i;
            break;
        }
    int job = de_async_new_job();
    FILE *f = NULL;
    long long base = 0, size = 0;
    const unsigned char *mem = NULL;
    if (h >= 0)
    {
#if defined(__EMSCRIPTEN__)
        const void *md; long long ml;
        if (de_fs_mem_range(path, &md, &ml)) { mem = (const unsigned char *)md; size = ml; f = DE_MEM_SENTINEL; }
#elif defined(__ANDROID__)
        if (strncmp(path, "asset:", 6) == 0)
            f = de_open_asset(path + 6, &base, &size);
        else
#endif
#if !defined(__EMSCRIPTEN__)
        {
            f = fopen(path, "rb");
            if (f)
            {
                fseek(f, 0, SEEK_END);
                size = de_ftell64(f);
            }
        }
#endif
    }
    if (f)
    {
        de_handles[h].base = base;
        de_handles[h].size = size;
        de_handles[h].f = f;
        de_handles[h].mem = mem;
        de_async_complete(job, 1, NULL, 0, h, 0);
    }
    else
        de_async_complete(job, -1, NULL, 0, 0, 0);
    return job;
}

DE_FS_API long long de_fs_size(int handle)
{
    return handle >= 0 && handle < DE_MAX_HANDLES && de_handles[handle].f ? de_handles[handle].size : -1;
}

DE_FS_API void de_fs_close(int handle)
{
    if (handle < 0 || handle >= DE_MAX_HANDLES || !de_handles[handle].f)
        return;
    if (!de_handles[handle].mem)
        fclose(de_handles[handle].f);
    de_handles[handle].f = NULL;
    de_handles[handle].mem = NULL;
    de_handles[handle].base = 0;
    de_handles[handle].size = 0;
}

DE_FS_API int de_fs_read(int handle, const char *path, long long offset, long long length, long long rawLength)
{
    return de_submit(KIND_READ, handle, path, offset, length, rawLength);
}

DE_FS_API int de_fs_decode(int handle, const char *path, long long offset, long long length, long long rawLength)
{
    return de_submit(KIND_DECODE, handle, path, offset, length, rawLength);
}

// ===========================================================================
// PREFS — kalici anahtar/deger (Unity PlayerPrefs karsiligi; DigitoyEngine.PlayerPrefs)
// ===========================================================================
// Platformun KENDI deposu: iOS CFPreferences (NSUserDefaults), Android SharedPreferences
// (JNI -> com.digitoy.host.Prefs), Web localStorage; Windows/macOS/Linux <root>/prefs.dat
// (bellekte harita, Save'de atomik yazim). Degerler UTF-8 string; tip donusumu managed'da.
// Tek thread (ana). Donen tamponlar malloc'lu -> de_prefs_free.

static char *de_strdup(const char *s)
{
    size_t n = strlen(s) + 1;
    char *d = (char *)malloc(n);
    if (d)
        memcpy(d, s, n);
    return d;
}

#if defined(__APPLE__)
#include <TargetConditionals.h>
#if TARGET_OS_IPHONE
#define DE_PREFS_CF 1
#endif
#endif

#if defined(__EMSCRIPTEN__)
// Anahtar oneki sayfa yoluna gore: ayni origin'deki farkli oyunlar carpismasin.
// EM_JS govdesi JS'tir: clang-format C sanip '===' → '== =' bozar (link'te acorn SyntaxError) → format kapali.
#include <emscripten/emscripten.h>
// clang-format off
EM_JS(char *, de_js_prefs_get, (const char *key), {
    var v = localStorage.getItem('de:' + location.pathname + ':' + UTF8ToString(key));
    if (v === null)
        return 0;
    var n = lengthBytesUTF8(v) + 1, p = _malloc(n);
    stringToUTF8(v, p, n);
    return p;
});
EM_JS(void, de_js_prefs_set, (const char *key, const char *value), {
    try { localStorage.setItem('de:' + location.pathname + ':' + UTF8ToString(key), UTF8ToString(value)); }
    catch (e) {}
});
EM_JS(void, de_js_prefs_delete, (const char *key), {
    localStorage.removeItem('de:' + location.pathname + ':' + UTF8ToString(key));
});
EM_JS(void, de_js_prefs_delete_all, (void), {
    var pre = 'de:' + location.pathname + ':', dead = [];
    for (var i = 0; i < localStorage.length; i++) {
        var k = localStorage.key(i);
        if (k && k.indexOf(pre) === 0)
            dead.push(k);
    }
    dead.forEach(function(k) { localStorage.removeItem(k); });
});
// clang-format on

DE_FS_API void de_prefs_set_root(const char *root) { (void)root; }
DE_FS_API int de_prefs_get(const char *key, void **out, int *len)
{
    *out = NULL;
    *len = 0;
    char *v = de_js_prefs_get(key);
    if (!v)
        return 0;
    *out = v;
    *len = (int)strlen(v);
    return 1;
}
DE_FS_API void de_prefs_set(const char *key, const char *value) { de_js_prefs_set(key, value ? value : ""); }
DE_FS_API void de_prefs_delete(const char *key) { de_js_prefs_delete(key); }
DE_FS_API void de_prefs_delete_all(void) { de_js_prefs_delete_all(); }
DE_FS_API int de_prefs_has(const char *key)
{
    char *v = de_js_prefs_get(key);
    if (!v)
        return 0;
    free(v);
    return 1;
}
DE_FS_API void de_prefs_save(void) {}

#elif defined(__ANDROID__)
// JNI -> com.digitoy.host.Prefs (SharedPreferences). Host JNI_OnLoad de_android_prefs_bind cagirir
// (sinif APP classloader'indan; GL thread FindClass bulamaz). Baglanmamissa her sey yok/no-op.
#include <jni.h>
static JavaVM *de_pref_vm;
static jclass de_pref_cls;
static jmethodID de_pm_get, de_pm_set, de_pm_delete, de_pm_delete_all, de_pm_has, de_pm_save;

DE_FS_API void de_android_prefs_bind(void *vm, void *env_, void *cls_)
{
    JNIEnv *env = (JNIEnv *)env_;
    jclass cls = (jclass)cls_;
    de_pref_vm = (JavaVM *)vm;
    de_pref_cls = (jclass)(*env)->NewGlobalRef(env, cls);
    de_pm_get = (*env)->GetStaticMethodID(env, cls, "get", "(Ljava/lang/String;)Ljava/lang/String;");
    de_pm_set = (*env)->GetStaticMethodID(env, cls, "set", "(Ljava/lang/String;Ljava/lang/String;)V");
    de_pm_delete = (*env)->GetStaticMethodID(env, cls, "delete", "(Ljava/lang/String;)V");
    de_pm_delete_all = (*env)->GetStaticMethodID(env, cls, "deleteAll", "()V");
    de_pm_has = (*env)->GetStaticMethodID(env, cls, "has", "(Ljava/lang/String;)Z");
    de_pm_save = (*env)->GetStaticMethodID(env, cls, "save", "()V");
    if ((*env)->ExceptionCheck(env))
    {
        (*env)->ExceptionClear(env);
        de_pref_cls = NULL;
    }
}
static JNIEnv *de_pref_env(void)
{
    if (!de_pref_vm || !de_pref_cls)
        return NULL;
    JNIEnv *env = NULL;
    if ((*de_pref_vm)->GetEnv(de_pref_vm, (void **)&env, JNI_VERSION_1_6) == JNI_OK)
        return env;
    return (*de_pref_vm)->AttachCurrentThread(de_pref_vm, &env, NULL) == JNI_OK ? env : NULL;
}
static void de_pref_clear(JNIEnv *env)
{
    if ((*env)->ExceptionCheck(env))
        (*env)->ExceptionClear(env);
}

DE_FS_API void de_prefs_set_root(const char *root) { (void)root; }
DE_FS_API int de_prefs_get(const char *key, void **out, int *len)
{
    *out = NULL;
    *len = 0;
    JNIEnv *env = de_pref_env();
    if (!env)
        return 0;
    jstring jk = (*env)->NewStringUTF(env, key);
    jstring jv = (jstring)(*env)->CallStaticObjectMethod(env, de_pref_cls, de_pm_get, jk);
    (*env)->DeleteLocalRef(env, jk);
    de_pref_clear(env);
    if (!jv)
        return 0;
    const char *s = (*env)->GetStringUTFChars(env, jv, NULL);
    if (s)
    {
        *out = de_strdup(s);
        *len = (int)strlen(s);
        (*env)->ReleaseStringUTFChars(env, jv, s);
    }
    (*env)->DeleteLocalRef(env, jv);
    return *out != NULL;
}
DE_FS_API void de_prefs_set(const char *key, const char *value)
{
    JNIEnv *env = de_pref_env();
    if (!env)
        return;
    jstring jk = (*env)->NewStringUTF(env, key), jv = (*env)->NewStringUTF(env, value ? value : "");
    (*env)->CallStaticVoidMethod(env, de_pref_cls, de_pm_set, jk, jv);
    (*env)->DeleteLocalRef(env, jk);
    (*env)->DeleteLocalRef(env, jv);
    de_pref_clear(env);
}
DE_FS_API void de_prefs_delete(const char *key)
{
    JNIEnv *env = de_pref_env();
    if (!env)
        return;
    jstring jk = (*env)->NewStringUTF(env, key);
    (*env)->CallStaticVoidMethod(env, de_pref_cls, de_pm_delete, jk);
    (*env)->DeleteLocalRef(env, jk);
    de_pref_clear(env);
}
DE_FS_API void de_prefs_delete_all(void)
{
    JNIEnv *env = de_pref_env();
    if (!env)
        return;
    (*env)->CallStaticVoidMethod(env, de_pref_cls, de_pm_delete_all);
    de_pref_clear(env);
}
DE_FS_API int de_prefs_has(const char *key)
{
    JNIEnv *env = de_pref_env();
    if (!env)
        return 0;
    jstring jk = (*env)->NewStringUTF(env, key);
    jboolean r = (*env)->CallStaticBooleanMethod(env, de_pref_cls, de_pm_has, jk);
    (*env)->DeleteLocalRef(env, jk);
    de_pref_clear(env);
    return r ? 1 : 0;
}
DE_FS_API void de_prefs_save(void)
{
    JNIEnv *env = de_pref_env();
    if (!env)
        return;
    (*env)->CallStaticVoidMethod(env, de_pref_cls, de_pm_save);
    de_pref_clear(env);
}

#elif defined(DE_PREFS_CF)
// iOS: CFPreferences (NSUserDefaults'un C yuzeyi; ObjC gerekmez), uygulama alani.
#include <CoreFoundation/CoreFoundation.h>
static CFStringRef de_cf(const char *s) { return CFStringCreateWithCString(NULL, s ? s : "", kCFStringEncodingUTF8); }

DE_FS_API void de_prefs_set_root(const char *root) { (void)root; }
DE_FS_API int de_prefs_get(const char *key, void **out, int *len)
{
    *out = NULL;
    *len = 0;
    CFStringRef k = de_cf(key);
    CFPropertyListRef v = CFPreferencesCopyAppValue(k, kCFPreferencesCurrentApplication);
    CFRelease(k);
    if (!v)
        return 0;
    if (CFGetTypeID(v) == CFStringGetTypeID())
    {
        CFIndex max = CFStringGetMaximumSizeForEncoding(CFStringGetLength((CFStringRef)v), kCFStringEncodingUTF8) + 1;
        char *buf = (char *)malloc((size_t)max);
        if (buf && CFStringGetCString((CFStringRef)v, buf, max, kCFStringEncodingUTF8))
        {
            *out = buf;
            *len = (int)strlen(buf);
        }
        else
            free(buf);
    }
    CFRelease(v);
    return *out != NULL;
}
DE_FS_API void de_prefs_set(const char *key, const char *value)
{
    CFStringRef k = de_cf(key), v = de_cf(value);
    CFPreferencesSetAppValue(k, v, kCFPreferencesCurrentApplication);
    CFRelease(k);
    CFRelease(v);
}
DE_FS_API void de_prefs_delete(const char *key)
{
    CFStringRef k = de_cf(key);
    CFPreferencesSetAppValue(k, NULL, kCFPreferencesCurrentApplication);
    CFRelease(k);
}
DE_FS_API void de_prefs_delete_all(void)
{
    CFArrayRef keys = CFPreferencesCopyKeyList(kCFPreferencesCurrentApplication, kCFPreferencesCurrentUser, kCFPreferencesAnyHost);
    if (!keys)
        return;
    for (CFIndex i = 0; i < CFArrayGetCount(keys); i++)
        CFPreferencesSetAppValue((CFStringRef)CFArrayGetValueAtIndex(keys, i), NULL, kCFPreferencesCurrentApplication);
    CFRelease(keys);
}
DE_FS_API int de_prefs_has(const char *key)
{
    void *p;
    int n;
    if (!de_prefs_get(key, &p, &n))
        return 0;
    free(p);
    return 1;
}
DE_FS_API void de_prefs_save(void) { CFPreferencesAppSynchronize(kCFPreferencesCurrentApplication); }

#else
// Desktop (Windows/macOS/Linux): <root>/prefs.dat — satir basina "anahtar\tdeger\n" (\t \n \\ kacisli),
// bellekte harita (tembel yukleme), Save'de .tmp + rename (atomik). root yoksa "." (cwd).
#if defined(_WIN32)
#include <direct.h>
#define de_prefs_mkdir(p) _mkdir(p)
#else
#include <sys/stat.h>
#define de_prefs_mkdir(p) mkdir(p, 0755)
#endif

typedef struct
{
    char *key, *value;
} de_pref_t;
static de_pref_t *de_prefs;
static int de_prefs_n, de_prefs_cap, de_prefs_loaded, de_prefs_dirty;
static char de_prefs_root[1024] = ".";

static void de_prefs_clear_mem(void)
{
    for (int i = 0; i < de_prefs_n; i++)
    {
        free(de_prefs[i].key);
        free(de_prefs[i].value);
    }
    de_prefs_n = 0;
}

// \t \n \\ kacisi coz; yerinde yazar, uzunluk doner.
static int de_prefs_unescape(char *s)
{
    char *w = s;
    for (char *r = s; *r; r++)
    {
        if (*r == '\\' && r[1])
        {
            r++;
            *w++ = *r == 't' ? '\t' : *r == 'n' ? '\n'
                                                : *r;
        }
        else
            *w++ = *r;
    }
    *w = 0;
    return (int)(w - s);
}
static void de_prefs_fput_escaped(FILE *f, const char *s)
{
    for (; *s; s++)
    {
        if (*s == '\t')
            fputs("\\t", f);
        else if (*s == '\n')
            fputs("\\n", f);
        else if (*s == '\\')
            fputs("\\\\", f);
        else
            fputc(*s, f);
    }
}

static void de_prefs_load(void)
{
    if (de_prefs_loaded)
        return;
    de_prefs_loaded = 1;
    char path[1200];
    snprintf(path, sizeof path, "%s/prefs.dat", de_prefs_root);
    FILE *f = fopen(path, "rb");
    if (!f)
        return;
    fseek(f, 0, SEEK_END);
    long n = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (n <= 0)
    {
        fclose(f);
        return;
    }
    char *buf = (char *)malloc((size_t)n + 1);
    if (!buf)
    {
        fclose(f);
        return;
    }
    size_t got = fread(buf, 1, (size_t)n, f);
    fclose(f);
    buf[got] = 0;
    char *line = buf;
    while (line && *line)
    {
        char *nl = strchr(line, '\n');
        if (nl)
            *nl = 0;
        char *tab = strchr(line, '\t'); // kacissiz ilk tab = ayirici (anahtardaki tab \t olarak kacisli)
        if (tab)
        {
            *tab = 0;
            de_prefs_unescape(line);
            de_prefs_unescape(tab + 1);
            de_prefs_set(line, tab + 1);
        }
        line = nl ? nl + 1 : NULL;
    }
    free(buf);
    de_prefs_dirty = 0;
}

static int de_prefs_find(const char *key)
{
    for (int i = 0; i < de_prefs_n; i++)
        if (strcmp(de_prefs[i].key, key) == 0)
            return i;
    return -1;
}

DE_FS_API void de_prefs_set_root(const char *root)
{
    // Root degisimi: bellekteki harita dusurulur, sonraki erisimde yeni kokten yuklenir (kaydedilmemis degisiklik GIDER).
    de_prefs_clear_mem();
    de_prefs_loaded = 0;
    de_prefs_dirty = 0;
    snprintf(de_prefs_root, sizeof de_prefs_root, "%s", root && *root ? root : ".");
}
DE_FS_API int de_prefs_get(const char *key, void **out, int *len)
{
    *out = NULL;
    *len = 0;
    de_prefs_load();
    int i = de_prefs_find(key);
    if (i < 0)
        return 0;
    *out = de_strdup(de_prefs[i].value);
    *len = (int)strlen(de_prefs[i].value);
    return *out != NULL;
}
DE_FS_API void de_prefs_set(const char *key, const char *value)
{
    if (!key)
        return;
    de_prefs_load();
    if (!value)
        value = "";
    int i = de_prefs_find(key);
    if (i >= 0)
    {
        if (strcmp(de_prefs[i].value, value) == 0)
            return;
        free(de_prefs[i].value);
        de_prefs[i].value = de_strdup(value);
    }
    else
    {
        if (de_prefs_n == de_prefs_cap)
        {
            de_prefs_cap = de_prefs_cap ? de_prefs_cap * 2 : 32;
            de_prefs = (de_pref_t *)realloc(de_prefs, (size_t)de_prefs_cap * sizeof(de_pref_t));
        }
        de_prefs[de_prefs_n].key = de_strdup(key);
        de_prefs[de_prefs_n].value = de_strdup(value);
        de_prefs_n++;
    }
    de_prefs_dirty = 1;
}
DE_FS_API void de_prefs_delete(const char *key)
{
    de_prefs_load();
    int i = de_prefs_find(key);
    if (i < 0)
        return;
    free(de_prefs[i].key);
    free(de_prefs[i].value);
    de_prefs[i] = de_prefs[--de_prefs_n];
    de_prefs_dirty = 1;
}
DE_FS_API void de_prefs_delete_all(void)
{
    de_prefs_load();
    if (de_prefs_n)
        de_prefs_dirty = 1;
    de_prefs_clear_mem();
}
DE_FS_API int de_prefs_has(const char *key)
{
    de_prefs_load();
    return de_prefs_find(key) >= 0;
}
DE_FS_API void de_prefs_save(void)
{
    if (!de_prefs_loaded || !de_prefs_dirty)
        return;
    char path[1200], tmp[1210];
    snprintf(path, sizeof path, "%s/prefs.dat", de_prefs_root);
    snprintf(tmp, sizeof tmp, "%s.tmp", path);
    de_prefs_mkdir(de_prefs_root);
    FILE *f = fopen(tmp, "wb");
    if (!f)
        return;
    for (int i = 0; i < de_prefs_n; i++)
    {
        de_prefs_fput_escaped(f, de_prefs[i].key);
        fputc('\t', f);
        de_prefs_fput_escaped(f, de_prefs[i].value);
        fputc('\n', f);
    }
    int ok = fclose(f) == 0;
    if (!ok)
    {
        remove(tmp);
        return;
    }
#if defined(_WIN32)
    if (!MoveFileExA(tmp, path, MOVEFILE_REPLACE_EXISTING))
    {
        remove(tmp);
        return;
    }
#else
    if (rename(tmp, path) != 0)
    {
        remove(tmp);
        return;
    }
#endif
    de_prefs_dirty = 0;
}
#endif

DE_FS_API void de_prefs_free(void *p) { free(p); }
