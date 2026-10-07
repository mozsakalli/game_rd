// de_app.h — Platform host <-> oyun kutuphanesi ABI'si (docs/platform-hosts.md)
//
// Oyun = generated.c + vmrt/corelib/vmint + de_app.c + sokol_gfx/shim'ler: platformsuz C.
// Host = pencere/surface, dongu, girdi, yasam dongusu (host_desktop.c, Android GameActivity,
// iOS GameViewController, wasm host.js). Yeni platform = yeni host, ayni oyun kutuphanesi.
//
// THREAD: de_app_init/frame/pause/resume/shutdown TEK thread'den (render thread). Managed koda
// giris yalniz buradan -> GC safepoint = de_app_frame icinde managed Frame dondukten sonra.
// de_app_event HER thread'den cagrilabilir (kuyruk); Frame basinda bosaltilir.
#ifndef DE_APP_H
#define DE_APP_H

#ifdef __cplusplus
extern "C" {
#endif

typedef enum DeEventType {
    DE_EV_POINTER_DOWN = 1, DE_EV_POINTER_MOVE = 2, DE_EV_POINTER_UP = 3, DE_EV_POINTER_CANCEL = 4,
    DE_EV_KEY_DOWN = 10, DE_EV_KEY_UP = 11, DE_EV_TEXT = 12,
    DE_EV_BACK = 20, DE_EV_FOCUS = 21, DE_EV_RESIZE = 22
} DeEventType;

typedef struct DeEvent {
    int type;      // DeEventType
    int id;        // pointer id (multitouch) / tus kodu
    float x, y;    // pointer: MANTIKSAL px (fbw/scale uzayi); host olcekler
    int a, b;      // ek (text: codepoint; resize: w,h; focus: 0/1)
} DeEvent;

// ---- host -> oyun ----
// dataRoot: game.pak'in bulundugu <root>/Build icin <root> (NULL = ".." : exe Build/ icinde). writableRoot: log/crash/kayit.
void de_app_init(const char* dataRoot, const char* writableRoot, int fbw, int fbh, float scale);
int  de_app_frame(float dt, int fbw, int fbh, float scale);   // 0 = oyun cikmak istiyor
void de_app_event(const DeEvent* e);                          // thread-safe kuyruk
void de_app_pause(void);                                      // arka plan: ses sustur, dt durur
void de_app_resume(void);
void de_app_low_memory(void);
void de_app_shutdown(void);

// ---- oyun -> host (host SAGLAR; eksikse link hatasi = acik sozlesme) ----
void de_host_set_title(const char* utf8);
void de_host_log(int level, const char* utf8);

// ---- generated.c SAGLAR (aotcompiler koprusu; host dokunmaz) ----
void digitoyengine_init(void);
void de_managed_init(const char* root, int fbw, int fbh, float scale);
int  de_managed_frame(float dt, int fbw, int fbh, float scale);
void de_managed_event(int type, int id, float x, float y, int a, int b);
void de_managed_pause(void);
void de_managed_resume(void);
void de_managed_shutdown(void);

#ifdef __cplusplus
}
#endif
#endif
