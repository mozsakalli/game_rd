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
#include <stdio.h>
#include <string.h>
#include <unistd.h>
#include "de_app.h"
#include "de_fs.h"

#define TAG "digitoy"

// printf/fprintf(stderr) ciktilari (runtime [host]/[app]/[player] loglari) logcat'e aksin.
static int g_pipe[2];
static void* stdio_pump(void* arg)
{
    (void)arg;
    char buf[1024];
    ssize_t n;
    while ((n = read(g_pipe[0], buf, sizeof buf - 1)) > 0)
    {
        if (buf[n - 1] == '\n') n--;
        buf[n] = 0;
        if (n) __android_log_write(ANDROID_LOG_INFO, TAG, buf);
    }
    return 0;
}
static void redirect_stdio(void)
{
    static int done;
    if (done) return;
    done = 1;
    setvbuf(stdout, 0, _IOLBF, 0);
    setvbuf(stderr, 0, _IONBF, 0);
    if (pipe(g_pipe) != 0) return;
    dup2(g_pipe[1], 1);
    dup2(g_pipe[1], 2);
    pthread_t t;
    if (pthread_create(&t, 0, stdio_pump, 0) == 0) pthread_detach(t);
}

void de_host_set_title(const char* utf8) { __android_log_print(ANDROID_LOG_INFO, TAG, "title: %s", utf8); }
void de_host_log(int level, const char* utf8) { __android_log_write(level >= 2 ? ANDROID_LOG_ERROR : ANDROID_LOG_INFO, TAG, utf8); }

#define JNI_FN(name) JNIEXPORT JNICALL Java_com_digitoy_host_GameLib_##name

// game.pak APK assets/Build/game.pak icinde (sikistirilmadan). dataRoot = "asset:" -> GameHost "asset:/Build/game.pak"
// acar; de_fs bunu AAssetManager fd+offset ile okur (filesDir'e kopya yok). AssetManager global ref: AAssetManager*
// Java nesnesi yasadikca gecerli; surec omurlu tutuyoruz (Activity yok olunca surec zaten biter).
static jobject g_assets_ref;

void JNI_FN(init)(JNIEnv* env, jclass cls, jobject assets, jstring writableRoot, jint fbw, jint fbh, jfloat scale)
{
    (void)cls;
    redirect_stdio();
    if (!g_assets_ref)
    {
        g_assets_ref = (*env)->NewGlobalRef(env, assets);
        de_fs_set_asset_manager(AAssetManager_fromJava(env, g_assets_ref));
    }
    const char* w = (*env)->GetStringUTFChars(env, writableRoot, 0);
    __android_log_print(ANDROID_LOG_INFO, TAG, "init data=asset: writable=%s %dx%d @%.2f", w, fbw, fbh, scale);
    de_app_init("asset:", w, fbw, fbh, scale);
    (*env)->ReleaseStringUTFChars(env, writableRoot, w);
}

jint JNI_FN(frame)(JNIEnv* env, jclass cls, jfloat dt, jint fbw, jint fbh, jfloat scale)
{
    (void)env; (void)cls;
    return de_app_frame(dt, fbw, fbh, scale);
}

void JNI_FN(event)(JNIEnv* env, jclass cls, jint type, jint id, jfloat x, jfloat y, jint a, jint b)
{
    (void)env; (void)cls;
    DeEvent e = { type, id, x, y, a, b };
    de_app_event(&e);
}

void JNI_FN(pause)(JNIEnv* env, jclass cls) { (void)env; (void)cls; de_app_pause(); }
void JNI_FN(resume)(JNIEnv* env, jclass cls) { (void)env; (void)cls; de_app_resume(); }
void JNI_FN(lowMemory)(JNIEnv* env, jclass cls) { (void)env; (void)cls; de_app_low_memory(); }
void JNI_FN(shutdown)(JNIEnv* env, jclass cls) { (void)env; (void)cls; de_app_shutdown(); }
