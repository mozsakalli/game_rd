// host_android.c — Android platform host'unun C yarisi (docs/platform-hosts.md).
// Kotlin taraf (platforms/android/java/com/digitoy/host/*.kt): GameActivity + GLSurfaceView (EGL/GLES3)
// pencereyi, donguyu, dokunmayi ve yasam dongusunu yonetir; buraya JNI ile iner. Burasi yalnizca
// JNI -> de_app_* koprusu + host servisleri (log, baslik) + stdout/stderr -> logcat.
// Thread: init/frame/pause/resume/shutdown GL thread'inden (GLSurfaceView.Renderer / queueEvent);
// event UI thread'inden gelebilir (de_app_event kuyrugu thread-safe).
#include <jni.h>
#include <android/log.h>
#include <android/asset_manager_jni.h>
#include <pthread.h>
#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "de_app.h"
#include "de_fs.h"
#include "de_plugin.h"
#include "vmrt.h"

#define TAG "digitoy"

// ---- de_host_fatal (docs/plugins.md): yakalanmayan managed exception -> Java ManagedException (satir bilgili .cs frame'leri
// setStackTrace ile) -> JNI girisine longjmp -> pending exception ile Java'ya donus -> ART uncaught -> surec olur, Play vitals /
// Crashlytics managed trace'i gosterir. C stack'te yalniz bizim frame'ler var (Java frame'i atlanmaz) -> longjmp guvenli;
// shadow stack'e artik gerek yok (surec oluyor). exit() yok: reporter'lara gorunmezdi.
static jmp_buf g_fatal_jmp;
static volatile int g_fatal;         // 1 = olum sirasinda: sonraki JNI girisleri hemen doner
static JNIEnv* g_fatal_env;
static int g_fatal_armed;

static int fatal_enter(JNIEnv* env)
{
    if (g_fatal) return 1;
    g_fatal_env = env;
    g_fatal_armed = 1;
    if (setjmp(g_fatal_jmp)) { g_fatal_armed = 0; return 1; } // de_host_fatal'dan dondu: Java'ya pending exception ile cik
    return 0;
}
static void fatal_leave(void) { g_fatal_armed = 0; }

void de_host_fatal(const char* reason, const RtFrame* frames, int count)
{
    g_fatal = 1;
    JNIEnv* env = g_fatal_env;
    if (!env || !g_fatal_armed) { __android_log_print(ANDROID_LOG_FATAL, TAG, "managed exception (JNI disinda): %s", reason); abort(); }
    jclass exCls = (*env)->FindClass(env, "com/digitoy/host/ManagedException");
    jclass steCls = (*env)->FindClass(env, "java/lang/StackTraceElement");
    if (!exCls || !steCls) { (*env)->ExceptionClear(env); __android_log_print(ANDROID_LOG_FATAL, TAG, "%s", reason); abort(); }
    jmethodID exCtor = (*env)->GetMethodID(env, exCls, "<init>", "(Ljava/lang/String;)V");
    jmethodID steCtor = (*env)->GetMethodID(env, steCls, "<init>", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)V");
    jmethodID setTrace = (*env)->GetMethodID(env, exCls, "setStackTrace", "([Ljava/lang/StackTraceElement;)V");
    jstring jreason = (*env)->NewStringUTF(env, reason ? reason : "managed exception");
    jobject ex = (*env)->NewObject(env, exCls, exCtor, jreason);
    int n = count > 64 ? 64 : count;
    jobjectArray arr = (*env)->NewObjectArray(env, n, steCls, NULL);
    for (int i = 0; i < n; i++)
    {
        const RtFrame* f = &frames[count - 1 - i]; // en ust frame ilk (Java sirasi)
        char type[128], method[128]; const char* file; int line, col;
        digitoyengine_frame_info(f, type, sizeof type, method, sizeof method, &file, &line, &col);
        const char* fname = file ? strrchr(file, '/') : 0; fname = fname ? fname + 1 : file;
        const char* bname = fname ? strrchr(fname, '\\') : 0; fname = bname ? bname + 1 : fname;
        jstring jt = (*env)->NewStringUTF(env, type[0] ? type : "Managed");
        jstring jm = (*env)->NewStringUTF(env, method[0] ? method : "?");
        jstring jf = fname ? (*env)->NewStringUTF(env, fname) : NULL;
        jobject ste = (*env)->NewObject(env, steCls, steCtor, jt, jm, jf, line);
        (*env)->SetObjectArrayElement(env, arr, i, ste);
        (*env)->DeleteLocalRef(env, jt); (*env)->DeleteLocalRef(env, jm); if (jf) (*env)->DeleteLocalRef(env, jf); (*env)->DeleteLocalRef(env, ste);
    }
    (*env)->CallVoidMethod(env, ex, setTrace, arr);
    (*env)->Throw(env, (jthrowable)ex); // pending; JNI girisinden donunce ART firlatir
    longjmp(g_fatal_jmp, 1);
}

// printf/fprintf(stderr) ciktilari (runtime [host]/[app]/[player] loglari) logcat'e aksin.
// Iki pipe: stdout -> INFO, stderr ([error]/[exception]/[warn]) -> ERROR (logcat'te filtrelenebilir).
static int g_pipe_out[2], g_pipe_err[2];
static void* stdio_pump_fn(int fd, int prio)
{
    char buf[4096];
    ssize_t n;
    while ((n = read(fd, buf, sizeof buf - 1)) > 0)
    {
        if (buf[n - 1] == '\n') n--;
        buf[n] = 0;
        if (n) __android_log_write(prio, TAG, buf);
    }
    return 0;
}
static void* stdio_pump_out(void* arg) { (void)arg; return stdio_pump_fn(g_pipe_out[0], ANDROID_LOG_INFO); }
static void* stdio_pump_err(void* arg) { (void)arg; return stdio_pump_fn(g_pipe_err[0], ANDROID_LOG_ERROR); }
static void redirect_stdio(void)
{
    static int done;
    if (done) return;
    done = 1;
    setvbuf(stdout, 0, _IOLBF, 0);
    setvbuf(stderr, 0, _IONBF, 0);
    pthread_t t;
    if (pipe(g_pipe_out) == 0) { dup2(g_pipe_out[1], 1); if (pthread_create(&t, 0, stdio_pump_out, 0) == 0) pthread_detach(t); }
    if (pipe(g_pipe_err) == 0) { dup2(g_pipe_err[1], 2); if (pthread_create(&t, 0, stdio_pump_err, 0) == 0) pthread_detach(t); }
}

void de_host_set_title(const char* utf8) { __android_log_print(ANDROID_LOG_INFO, TAG, "title: %s", utf8); }
void de_host_log(int level, const char* utf8) { __android_log_write(level >= 2 ? ANDROID_LOG_ERROR : ANDROID_LOG_INFO, TAG, utf8); }

#define JNI_FN(name) JNIEXPORT JNICALL Java_com_digitoy_host_GameLib_##name

// Muzik koprusu (audio_shim.c __ANDROID__): Music sinifi APP classloader'indan burada cozulur
// (JNI_OnLoad System.loadLibrary'yi cagiran loader'la kosar; GL thread'inden FindClass bulamaz).
void de_android_music_bind(void* vm, void* env, void* cls);
// Ag koprusu (de_net.c __ANDROID__): Net sinifi (Net.kt) ayni sekilde burada cozulur.
void de_android_net_bind(void* vm, void* env, void* cls);

// Plugin koprusu (docs/plugins.md): managed Plugin.Send -> de_plugin_send -> buradaki dispatcher -> Java
// PluginHost.dispatch(name, msg) (main Handler'a post eder, plugin.onMessage orada). Cagiran thread render
// thread'i (Java'nin GL thread'i) -> GetEnv gecerli; degilse attach.
static JavaVM* g_vm;
static jclass g_pluginHost;     // global ref
static jmethodID g_dispatch;    // static void dispatch(String, String)
static JNIEnv* plugin_env(int* attached)
{
    JNIEnv* env = NULL; *attached = 0;
    if (!g_vm) return NULL;
    if ((*g_vm)->GetEnv(g_vm, (void**)&env, JNI_VERSION_1_6) == JNI_EDETACHED)
    {
        if ((*g_vm)->AttachCurrentThread(g_vm, &env, NULL) != JNI_OK) return NULL;
        *attached = 1;
    }
    return env;
}
static void plugin_run_on_main(DePluginDispatchFn fn, void* user, char* name, char* msg)
{
    (void)fn; (void)user; // kayitli callback kullanilmaz: Java tarafi plugin'i adiyla bulur
    int attached;
    JNIEnv* env = plugin_env(&attached);
    if (env && g_pluginHost && g_dispatch)
    {
        jstring jn = (*env)->NewStringUTF(env, name);
        jstring jm = (*env)->NewStringUTF(env, msg);
        (*env)->CallStaticVoidMethod(env, g_pluginHost, g_dispatch, jn, jm);
        if ((*env)->ExceptionCheck(env)) { (*env)->ExceptionDescribe(env); (*env)->ExceptionClear(env); }
        (*env)->DeleteLocalRef(env, jn);
        (*env)->DeleteLocalRef(env, jm);
    }
    if (attached) (*g_vm)->DetachCurrentThread(g_vm);
    de_plugin_free_dispatch(name, msg);
}
static void plugin_noop(void* user, const char* msg) { (void)user; (void)msg; }

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* reserved)
{
    (void)reserved;
    JNIEnv* env = NULL;
    if ((*vm)->GetEnv(vm, (void**)&env, JNI_VERSION_1_6) != JNI_OK) return JNI_ERR;
    g_vm = vm;
    jclass cls = (*env)->FindClass(env, "com/digitoy/host/Music");
    if (cls) { de_android_music_bind(vm, env, cls); (*env)->DeleteLocalRef(env, cls); }
    else { (*env)->ExceptionClear(env); __android_log_write(ANDROID_LOG_WARN, TAG, "Music sinifi yok: de_music_* kapali"); }
    jclass pcls = (*env)->FindClass(env, "com/digitoy/host/Prefs");
    if (pcls) { de_android_prefs_bind(vm, env, pcls); (*env)->DeleteLocalRef(env, pcls); }
    else { (*env)->ExceptionClear(env); __android_log_write(ANDROID_LOG_WARN, TAG, "Prefs sinifi yok: PlayerPrefs kapali"); }
    jclass ncls = (*env)->FindClass(env, "com/digitoy/host/Net");
    if (ncls) { de_android_net_bind(vm, env, ncls); (*env)->DeleteLocalRef(env, ncls); }
    else { (*env)->ExceptionClear(env); __android_log_write(ANDROID_LOG_WARN, TAG, "Net sinifi yok: Http/WebSocket kapali"); }
    jclass hcls = (*env)->FindClass(env, "com/digitoy/host/PluginHost");
    if (hcls)
    {
        g_pluginHost = (jclass)(*env)->NewGlobalRef(env, hcls);
        g_dispatch = (*env)->GetStaticMethodID(env, hcls, "dispatch", "(Ljava/lang/String;Ljava/lang/String;)V");
        (*env)->DeleteLocalRef(env, hcls);
        de_plugin_set_dispatcher(plugin_run_on_main);
    }
    else { (*env)->ExceptionClear(env); __android_log_write(ANDROID_LOG_WARN, TAG, "PluginHost sinifi yok: plugin koprusu kapali"); }
    return JNI_VERSION_1_6;
}

// Plugin JNI yuzeyi (PluginLib.kt). register: Java listesi -> native kayit (available=1); post: her thread -> inbox.
JNIEXPORT void JNICALL Java_com_digitoy_host_PluginLib_register(JNIEnv* env, jclass cls, jstring name)
{
    (void)cls;
    const char* n = (*env)->GetStringUTFChars(env, name, 0);
    de_plugin_register(n, plugin_noop, 0);
    (*env)->ReleaseStringUTFChars(env, name, n);
}
JNIEXPORT void JNICALL Java_com_digitoy_host_PluginLib_post(JNIEnv* env, jclass cls, jstring name, jstring msg)
{
    (void)cls;
    const char* n = (*env)->GetStringUTFChars(env, name, 0);
    const char* m = (*env)->GetStringUTFChars(env, msg, 0);
    de_plugin_post(n, m);
    (*env)->ReleaseStringUTFChars(env, name, n);
    (*env)->ReleaseStringUTFChars(env, msg, m);
}

// game.pak APK assets/Build/game.pak icinde (sikistirilmadan). dataRoot = "asset:" -> GameHost "asset:/Build/game.pak"
// acar; de_fs bunu AAssetManager fd+offset ile okur (filesDir'e kopya yok). AssetManager global ref: AAssetManager*
// Java nesnesi yasadikca gecerli; surec omurlu tutuyoruz (Activity yok olunca surec zaten biter).
static jobject g_assets_ref;

void JNI_FN(init)(JNIEnv* env, jclass cls, jobject assets, jstring writableRoot, jint fbw, jint fbh, jfloat scale)
{
    (void)cls;
    redirect_stdio();
    if (fatal_enter(env)) return;
    if (!g_assets_ref)
    {
        g_assets_ref = (*env)->NewGlobalRef(env, assets);
        de_fs_set_asset_manager(AAssetManager_fromJava(env, g_assets_ref));
    }
    const char* w = (*env)->GetStringUTFChars(env, writableRoot, 0);
    __android_log_print(ANDROID_LOG_INFO, TAG, "init data=asset: writable=%s %dx%d @%.2f", w, fbw, fbh, scale);
    de_app_init("asset:", w, fbw, fbh, scale);
    (*env)->ReleaseStringUTFChars(env, writableRoot, w);
    fatal_leave();
}

jint JNI_FN(frame)(JNIEnv* env, jclass cls, jfloat dt, jint fbw, jint fbh, jfloat scale)
{
    (void)cls;
    if (fatal_enter(env)) return 1;
    jint r = de_app_frame(dt, fbw, fbh, scale);
    fatal_leave();
    return r;
}

void JNI_FN(event)(JNIEnv* env, jclass cls, jint type, jint id, jfloat x, jfloat y, jint a, jint b)
{
    (void)env; (void)cls;
    if (g_fatal) return; // olum sirasinda UI thread'den gelen olaylar
    DeEvent e = { type, id, x, y, a, b };
    de_app_event(&e);
}

void JNI_FN(pause)(JNIEnv* env, jclass cls) { (void)cls; if (fatal_enter(env)) return; de_app_pause(); fatal_leave(); }
void JNI_FN(resume)(JNIEnv* env, jclass cls) { (void)cls; if (fatal_enter(env)) return; de_app_resume(); fatal_leave(); }
void JNI_FN(lowMemory)(JNIEnv* env, jclass cls) { (void)cls; if (fatal_enter(env)) return; de_app_low_memory(); fatal_leave(); }
void JNI_FN(shutdown)(JNIEnv* env, jclass cls) { (void)cls; if (fatal_enter(env)) return; de_app_shutdown(); fatal_leave(); }
