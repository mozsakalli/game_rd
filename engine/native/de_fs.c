// ---------------------------------------------------------------------------
// de_fs.c — stdio backend (Windows/macOS/Linux/iOS) + worker job tablosu
// ---------------------------------------------------------------------------
// Thread shim: Win32 (CreateThread/CRITICAL_SECTION/Semaphore) veya pthread.
// Tek worker: FILE* handle'lari yalniz worker okur (ana thread acar/kapar;
// kapatmadan once bagli job'lar bitmis olmali — managed sozlesme).
// ---------------------------------------------------------------------------
#include "de_fs.h"
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
#define DE_MAX_JOBS 128
#define DE_PATH_MAX 512

enum { JOB_FREE = 0, JOB_QUEUED = 1, JOB_RUNNING = 2, JOB_DONE = 3, JOB_FAILED = 4 };
enum { KIND_OPEN = 0, KIND_READ = 1, KIND_DECODE = 2 };

typedef struct
{
    FILE *f;
    long long base; // dosya icindeki baslangic (APK icine gomulu asset: fd+offset); normal dosyada 0
    long long size;
} de_handle_t;

typedef struct
{
    de_state_t state;
    int kind;
    int handle;
    char path[DE_PATH_MAX];
    long long offset, length, rawLength;
    unsigned char *data;
    long long len;
    int w, h;
} de_job_t;

static de_handle_t de_handles[DE_MAX_HANDLES];
static de_job_t de_jobs[DE_MAX_JOBS];
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

// ---- DTEX: build'de cozulmus texture [magic][format][w][h][veri] ----
static int de_paeth(int a, int b, int c)
{
    int p = a + b - c;
    int pa = abs(p - a), pb = abs(p - b), pc = abs(p - c);
    return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
}

static unsigned char *de_try_dtex(const unsigned char *data, long long len, int *w, int *h)
{
    if (len < 16)
        return NULL;
    int magic, fmt, tw, th;
    memcpy(&magic, data, 4);
    if (magic != 0x58455444)
        return NULL;
    memcpy(&fmt, data + 4, 4);
    memcpy(&tw, data + 8, 4);
    memcpy(&th, data + 12, 4);
    if (tw <= 0 || th <= 0)
        return NULL;
    long long stride = (long long)tw * 4;
    if (fmt == 0)
    {
        if (stride * th != len - 16)
            return NULL;
        unsigned char *px = (unsigned char *)malloc((size_t)(stride * th));
        if (!px)
            return NULL;
        memcpy(px, data + 16, (size_t)(stride * th));
        *w = tw;
        *h = th;
        return px;
    }
    if (fmt == 1)
    {
        if ((stride + 1) * th != len - 16)
            return NULL;
        unsigned char *px = (unsigned char *)malloc((size_t)(stride * th));
        if (!px)
            return NULL;
        const unsigned char *src = data + 16;
        for (int y = 0; y < th; y++)
        {
            int f = *src++;
            unsigned char *cur = px + (size_t)y * stride;
            const unsigned char *prev = y > 0 ? cur - stride : NULL;
            for (long long x = 0; x < stride; x++)
            {
                int left = x >= 4 ? cur[x - 4] : 0;
                int up = prev ? prev[x] : 0;
                int ul = (prev && x >= 4) ? prev[x - 4] : 0;
                int pred = f == 1 ? left : f == 2 ? up
                                       : f == 3   ? ((left + up) >> 1)
                                       : f == 4   ? de_paeth(left, up, ul)
                                                  : 0;
                cur[x] = (unsigned char)(src[x] + pred);
            }
            src += stride;
        }
        *w = tw;
        *h = th;
        return px;
    }
    return NULL;
}

// ---- worker ----

// Ham baytlari okur (+ gerekirse inflate). Donus malloc'lu; *outLen dolu. NULL = hata.
static unsigned char *de_read_blob(de_job_t *job, long long *outLen)
{
    FILE *f;
    int own = 0;
    long long base = 0;
    if (job->handle >= 0)
    {
        f = de_handles[job->handle].f;
        base = de_handles[job->handle].base;
    }
    else
    {
        f = fopen(job->path, "rb");
        own = 1;
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
    if (buf && de_fseek64(f, base + job->offset) == 0 && fread(buf, 1, (size_t)length, f) == (size_t)length)
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
    if (own)
        fclose(f);
    return result;
}

static void de_run_job(de_job_t *job)
{
    long long len = 0;
    unsigned char *blob = de_read_blob(job, &len);
    if (!blob)
    {
        de_atomic_store(&job->state, JOB_FAILED);
        return;
    }
    if (job->kind == KIND_READ)
    {
        job->data = blob;
        job->len = len;
        de_atomic_store(&job->state, JOB_DONE);
        return;
    }
    int w = 0, h = 0, comp = 0;
    unsigned char *px = de_try_dtex(blob, len, &w, &h);
    if (!px)
        px = stbi_load_from_memory(blob, (int)len, &w, &h, &comp, 4);
    free(blob);
    job->data = px;
    job->len = px ? (long long)w * h * 4 : 0;
    job->w = w;
    job->h = h;
    de_atomic_store(&job->state, px ? JOB_DONE : JOB_FAILED);
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
        de_job_t *job = NULL;
        de_mutex_lock(&de_lock);
        for (int i = 0; i < DE_MAX_JOBS; i++)
            if (de_jobs[i].state == JOB_QUEUED)
            {
                de_jobs[i].state = JOB_RUNNING;
                job = &de_jobs[i];
                break;
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
    de_sem_init(DE_MAX_JOBS);
#if defined(__EMSCRIPTEN__)
    // Tarayici: pthread yok (tek thread); job'lar submit aninda senkron kosar (pak zaten MEMFS'te, bellek kopyasi).
#elif defined(_WIN32)
    CreateThread(NULL, 0, de_worker, NULL, 0, NULL);
#else
    pthread_t t;
    if (pthread_create(&t, NULL, de_worker, NULL) == 0)
        pthread_detach(t);
#endif
}

// Kuyruga alinan job'u worker'a bildirir (web: hemen kosar).
static void de_kick(int job)
{
#if defined(__EMSCRIPTEN__)
    de_jobs[job].state = JOB_RUNNING;
    de_run_job(&de_jobs[job]);
#else
    (void)job;
    de_sem_post();
#endif
}

// Bos slot alir ve doldurur; kuyruk doluysa -1. Yalniz ana thread.
static int de_submit(int kind, int handle, const char *path, long long off, long long len, long long raw)
{
    de_ensure();
    int id = -1;
    de_mutex_lock(&de_lock);
    for (int i = 0; i < DE_MAX_JOBS; i++)
        if (de_jobs[i].state == JOB_FREE)
        {
            de_job_t *j = &de_jobs[i];
            j->kind = kind;
            j->handle = handle;
            j->offset = off;
            j->length = len;
            j->rawLength = raw;
            j->data = NULL;
            j->len = 0;
            j->w = j->h = 0;
            if (path)
            {
                strncpy(j->path, path, DE_PATH_MAX - 1);
                j->path[DE_PATH_MAX - 1] = 0;
            }
            else
                j->path[0] = 0;
            j->state = JOB_QUEUED;
            id = i;
            break;
        }
    de_mutex_unlock(&de_lock);
    return id;
}

// ---- API ----

// stdio: acma ana thread'de aninda biter (web backend'inde fetch job'u olur).
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
    int job = de_submit(KIND_OPEN, -1, path, 0, 0, 0);
    if (job < 0)
        return -1;
    de_job_t *j = &de_jobs[job];
    FILE *f = NULL;
    long long base = 0, size = 0;
    if (h >= 0)
    {
#if defined(__ANDROID__)
        if (strncmp(path, "asset:", 6) == 0)
            f = de_open_asset(path + 6, &base, &size);
        else
#endif
        {
            f = fopen(path, "rb");
            if (f)
            {
                fseek(f, 0, SEEK_END);
                size = de_ftell64(f);
            }
        }
    }
    if (f)
    {
        de_handles[h].base = base;
        de_handles[h].size = size;
        de_handles[h].f = f;
        j->w = h;
        de_atomic_store(&j->state, JOB_DONE);
    }
    else
        de_atomic_store(&j->state, JOB_FAILED);
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
    fclose(de_handles[handle].f);
    de_handles[handle].f = NULL;
    de_handles[handle].base = 0;
    de_handles[handle].size = 0;
}

DE_FS_API int de_fs_read(int handle, const char *path, long long offset, long long length, long long rawLength)
{
    int job = de_submit(KIND_READ, handle, path, offset, length, rawLength);
    if (job >= 0)
        de_kick(job);
    return job;
}

DE_FS_API int de_fs_decode(int handle, const char *path, long long offset, long long length, long long rawLength)
{
    int job = de_submit(KIND_DECODE, handle, path, offset, length, rawLength);
    if (job >= 0)
        de_kick(job);
    return job;
}

DE_FS_API int de_job_poll(int job, void **data, long long *len, int *w, int *h)
{
    if (job < 0 || job >= DE_MAX_JOBS)
        return -1;
    int s = (int)de_atomic_load(&de_jobs[job].state);
    if (s == JOB_DONE)
    {
        if (data) *data = de_jobs[job].data;
        if (len) *len = de_jobs[job].len;
        if (w) *w = de_jobs[job].w;
        if (h) *h = de_jobs[job].h;
        return 1;
    }
    return s == JOB_FAILED ? -1 : 0;
}

DE_FS_API void de_job_free(int job)
{
    if (job < 0 || job >= DE_MAX_JOBS)
        return;
    int s = (int)de_atomic_load(&de_jobs[job].state);
    if (s != JOB_DONE && s != JOB_FAILED)
        return; // calisan job serbest birakilamaz
    if (de_jobs[job].data)
    {
        free(de_jobs[job].data); // stbi default allocator = malloc/free
        de_jobs[job].data = NULL;
    }
    de_atomic_store(&de_jobs[job].state, JOB_FREE);
}
