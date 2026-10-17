// host_wasm.c — Tarayici (wasm32/emscripten) platform host'u (docs/platform-hosts.md).
// Pencere = <canvas id="canvas"> (platforms/wasm/index.html), GL = WebGL2 (sokol SOKOL_GLES3), dongu =
// emscripten_set_main_loop -> de_app_frame (requestAnimationFrame). Girdi: html5 callback'leri -> de_app_event
// kuyrugu (tek thread; kuyruk yine de sozlesme geregi). Pak: dosya sistemi YOK (-sFILESYSTEM=0); game.pak fetch ile heap'e
// alinir ve de_fs_mount_memory("/data/Build/game.pak") ile baglanir; GameHost yine "<root>/Build/game.pak" acar.
// Koordinat: canvas CSS px = MANTIKSAL px (fbw = css * devicePixelRatio, scale = dpr) -> olaylar oldugu gibi gecer.
#include "de_app.h"
#include "de_fs.h"
#include "de_plugin.h"
#include "vmrt.h"
#include <emscripten/emscripten.h>
#include <emscripten/html5.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define CANVAS "#canvas"

// ---- plugin koprusu (docs/plugins.md): JS tarafi window.DE (index.html). Tek thread: dispatch aninda. ----
// clang-format off
EM_JS(int, de_js_plugin_available, (const char* name), {
    return (typeof DE !== 'undefined' && DE.plugins && DE.plugins[UTF8ToString(name)]) ? 1 : 0;
});
EM_JS(void, de_js_plugin_dispatch, (const char* name, const char* msg), {
    var n = UTF8ToString(name), m = UTF8ToString(msg);
    var p = (typeof DE !== 'undefined' && DE.plugins) ? DE.plugins[n] : null;
    if (!p) return;
    try { p.onMessage(m); }
    catch (e) { console.error('[plugin] hata (' + n + ') — oyun devam ediyor', e); try { if (DE.errorReporter) DE.errorReporter(e); } catch (_) {} }
});
EM_JS(void, de_js_plugin_install, (), {
    // DE.send(name, msg): plugin -> managed inbox (host -> managed yolu). Sayfa yuklenirken DE.plugins doldurulmus olmali
    // (plugins.js senkron, main()'den once); DE.register(obj) obj.name ile kaydeder.
    if (typeof DE === 'undefined') window.DE = {};
    DE.plugins = DE.plugins || {};
    DE.register = DE.register || function (p) { if (!p || !p.name) { console.error('[plugin] name yok'); return; } DE.plugins[p.name] = p; };
    DE.send = function (name, msg) {
        var n = lengthBytesUTF8(name) + 1, m = lengthBytesUTF8(msg) + 1;
        var pn = _malloc(n), pm = _malloc(m);
        stringToUTF8(name, pn, n); stringToUTF8(msg, pm, m);
        _de_plugin_post(pn, pm);
        _free(pn); _free(pm);
    };
    for (var k in DE.plugins) _de_wasm_plugin_register(stringToNewUTF8(k));
});
// clang-format on
static void plugin_noop(void *user, const char *msg) { (void)user; (void)msg; }
EMSCRIPTEN_KEEPALIVE void de_wasm_plugin_register(char *name) { de_plugin_register(name, plugin_noop, 0); free(name); }
static void plugin_run_now(DePluginDispatchFn fn, void *user, char *name, char *msg)
{
    (void)fn; (void)user;
    de_js_plugin_dispatch(name, msg);
    de_plugin_free_dispatch(name, msg);
}

// de_host_fatal (docs/plugins.md): yakalanmayan managed exception -> JS Error throw (window.onerror / Sentry-browser yakalar).
// Ana dongu iptal; throw wasm frame'lerinden JS'e cikar (JS exception, wasm trap degil). Donmez.
// clang-format off
EM_JS(void, de_js_fatal, (const char* text), {
    var e = new Error(UTF8ToString(text)); e.name = 'DigitoyManagedException';
    try { if (typeof DE !== 'undefined' && DE.errorReporter) DE.errorReporter(e); } catch (_) {}
    throw e;
});
// clang-format on
void de_host_fatal(const char *reason, const RtFrame *frames, int count)
{
    emscripten_cancel_main_loop();
    char text[4096]; size_t pos = 0;
    pos += (size_t)snprintf(text + pos, sizeof text - pos, "%s", reason ? reason : "managed exception");
    int n = count > 64 ? 64 : count;
    for (int i = 0; i < n && pos < sizeof text - 64; i++)
    {
        const RtFrame *f = &frames[count - 1 - i];
        char type[128], method[128]; const char *file; int line, col;
        digitoyengine_frame_info(f, type, sizeof type, method, sizeof method, &file, &line, &col);
        const char *fname = file ? strrchr(file, '/') : 0; fname = fname ? fname + 1 : file;
        pos += (size_t)snprintf(text + pos, sizeof text - pos, "\n  at %s.%s(%s:%d)", type[0] ? type : "Managed", method[0] ? method : "?", fname ? fname : "?", line);
    }
    de_js_fatal(text);
    abort();
}

static int g_fbw, g_fbh;
static float g_scale = 1.0f;
static double g_last;
static int g_running;
static int g_mouseDown;

static void push(int type, int id, float x, float y, int a, int b)
{
    DeEvent e = { type, id, x, y, a, b };
    de_app_event(&e);
}

// Canvas'i CSS boyutu * dpr piksele ayarlar; fbw/fbh/scale'i gunceller.
static void measure(void)
{
    double cw = 0, ch = 0;
    emscripten_get_element_css_size(CANVAS, &cw, &ch);
    double dpr = emscripten_get_device_pixel_ratio();
    if (dpr <= 0) dpr = 1;
    int w = (int)(cw * dpr + 0.5), h = (int)(ch * dpr + 0.5);
    if (w != g_fbw || h != g_fbh)
    {
        emscripten_set_canvas_element_size(CANVAS, w, h);
        g_fbw = w; g_fbh = h;
        push(DE_EV_RESIZE, 0, 0, 0, w, h);
    }
    g_scale = (float)dpr;
}

static EM_BOOL on_mouse(int type, const EmscriptenMouseEvent* e, void* ud)
{
    (void)ud;
    float x = (float)e->targetX, y = (float)e->targetY;
    switch (type)
    {
    case EMSCRIPTEN_EVENT_MOUSEDOWN:
        if (e->button != 0) return EM_FALSE;
        g_mouseDown = 1; push(DE_EV_POINTER_DOWN, 0, x, y, 0, 0); break;
    case EMSCRIPTEN_EVENT_MOUSEUP:
        if (e->button != 0 || !g_mouseDown) return EM_FALSE;
        g_mouseDown = 0; push(DE_EV_POINTER_UP, 0, x, y, 0, 0); break;
    case EMSCRIPTEN_EVENT_MOUSEMOVE:
        if (g_mouseDown) push(DE_EV_POINTER_MOVE, 0, x, y, 0, 0); break;
    }
    return EM_TRUE;
}

static EM_BOOL on_touch(int type, const EmscriptenTouchEvent* e, void* ud)
{
    (void)ud;
    int ev = type == EMSCRIPTEN_EVENT_TOUCHSTART ? DE_EV_POINTER_DOWN
           : type == EMSCRIPTEN_EVENT_TOUCHMOVE ? DE_EV_POINTER_MOVE
           : type == EMSCRIPTEN_EVENT_TOUCHEND ? DE_EV_POINTER_UP : DE_EV_POINTER_CANCEL;
    for (int i = 0; i < e->numTouches; i++)
    {
        const EmscriptenTouchPoint* t = &e->touches[i];
        if (!t->isChanged) continue;
        push(ev, (int)t->identifier, (float)t->targetX, (float)t->targetY, 0, 0);
    }
    return EM_TRUE; // preventDefault: kaydirma/zoom yok
}

static EM_BOOL on_key(int type, const EmscriptenKeyboardEvent* e, void* ud)
{
    (void)ud;
    if (type == EMSCRIPTEN_EVENT_KEYPRESS)
    {
        if (e->charCode) push(DE_EV_TEXT, 0, 0, 0, (int)e->charCode, 0);
        return EM_TRUE;
    }
    if (e->repeat) return EM_TRUE;
    push(type == EMSCRIPTEN_EVENT_KEYDOWN ? DE_EV_KEY_DOWN : DE_EV_KEY_UP, (int)e->keyCode, 0, 0, 0, 0);
    // Tarayici kisayollarini (F5, Ctrl+R vb.) yutma; yalniz oyun tuslarini (ok/bosluk) yakala.
    return (e->keyCode >= 37 && e->keyCode <= 40) || e->keyCode == 32 ? EM_TRUE : EM_FALSE;
}

static EM_BOOL on_focus(int type, const EmscriptenFocusEvent* e, void* ud)
{
    (void)e; (void)ud;
    push(DE_EV_FOCUS, 0, 0, 0, type == EMSCRIPTEN_EVENT_FOCUS ? 1 : 0, 0);
    return EM_FALSE;
}

static EM_BOOL on_visibility(int type, const EmscriptenVisibilityChangeEvent* e, void* ud)
{
    (void)type; (void)ud;
    if (!g_running) return EM_FALSE;
    if (e->hidden) de_app_pause(); else { de_app_resume(); g_last = 0; }
    return EM_FALSE;
}

static EM_BOOL on_resize(int type, const EmscriptenUiEvent* e, void* ud)
{
    (void)type; (void)e; (void)ud;
    measure();
    return EM_FALSE;
}

EM_JS(void, de_js_set_title, (const char* s), { document.title = UTF8ToString(s); });

// game.pak -> heap: fetch + arrayBuffer -> _malloc + HEAPU8.set -> C geri cagri. Hata: len = -1. (Ileride: Module.pakBase64 -> tek HTML.)
// clang-format off
EM_JS(void, de_js_fetch_pak, (const char* url), {
    var u = UTF8ToString(url);
    fetch(u).then(function(r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.arrayBuffer(); })
        .then(function(ab) { var n = ab.byteLength; var p = _malloc(n); HEAPU8.set(new Uint8Array(ab), p); _de_wasm_pak_ready(p, n); })
        .catch(function(e) { console.error('[host] game.pak yuklenemedi: ' + e); _de_wasm_pak_ready(0, -1); });
});
// clang-format on

static const char* PAK_PATH = "/data/Build/game.pak"; // GameHost: dataRoot "/data" + "/Build/game.pak"

EMSCRIPTEN_KEEPALIVE void de_wasm_pak_ready(void* data, int len)
{
    if (!data || len < 0) { fprintf(stderr, "[host] game.pak indirilemedi\n"); return; }
    de_fs_mount_memory(PAK_PATH, data, len); // bellek host'a ait, omur boyu
    de_app_init("/data", "/tmp", g_fbw, g_fbh, g_scale);
    g_running = 1;
    g_last = 0;
}

void de_host_set_title(const char* utf8) { de_js_set_title(utf8); }
void de_host_log(int level, const char* utf8) { emscripten_log(level >= 2 ? EM_LOG_ERROR : EM_LOG_CONSOLE, "%s", utf8); }

static void frame(void)
{
    if (!g_running) return;
    measure();
    double t = emscripten_get_now() / 1000.0;
    float dt = g_last > 0 ? (float)(t - g_last) : 0.0f;
    g_last = t;
    if (dt > 0.25f) dt = 0.25f; // sekme arka plandayken rAF durur; donuste dev adim atma
    if (!de_app_frame(dt, g_fbw, g_fbh, g_scale))
    {
        g_running = 0;
        emscripten_cancel_main_loop();
        de_app_shutdown();
        fprintf(stderr, "[host] oyun cikti\n");
    }
}

int main(void)
{
    EmscriptenWebGLContextAttributes attrs;
    emscripten_webgl_init_context_attributes(&attrs);
    attrs.majorVersion = 2; attrs.minorVersion = 0;
    attrs.alpha = EM_FALSE;
    attrs.depth = EM_TRUE;
    attrs.stencil = EM_TRUE;
    attrs.antialias = EM_FALSE;
    attrs.premultipliedAlpha = EM_TRUE;
    attrs.preserveDrawingBuffer = EM_FALSE;
    attrs.powerPreference = EM_WEBGL_POWER_PREFERENCE_HIGH_PERFORMANCE;
    EMSCRIPTEN_WEBGL_CONTEXT_HANDLE ctx = emscripten_webgl_create_context(CANVAS, &attrs);
    if (ctx <= 0) { fprintf(stderr, "[host] WebGL2 context acilamadi (%d)\n", (int)ctx); return 1; }
    emscripten_webgl_make_context_current(ctx);

    emscripten_set_mousedown_callback(CANVAS, 0, EM_TRUE, on_mouse);
    emscripten_set_mouseup_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_mouse); // canvas disinda birakma
    emscripten_set_mousemove_callback(CANVAS, 0, EM_TRUE, on_mouse);
    emscripten_set_touchstart_callback(CANVAS, 0, EM_TRUE, on_touch);
    emscripten_set_touchmove_callback(CANVAS, 0, EM_TRUE, on_touch);
    emscripten_set_touchend_callback(CANVAS, 0, EM_TRUE, on_touch);
    emscripten_set_touchcancel_callback(CANVAS, 0, EM_TRUE, on_touch);
    emscripten_set_keydown_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_key);
    emscripten_set_keyup_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_key);
    emscripten_set_keypress_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_key);
    emscripten_set_focus_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_focus);
    emscripten_set_blur_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_focus);
    emscripten_set_visibilitychange_callback(0, EM_TRUE, on_visibility);
    emscripten_set_resize_callback(EMSCRIPTEN_EVENT_TARGET_WINDOW, 0, EM_TRUE, on_resize);

    measure();
    // Pak asenkron gelir; dongu simdi kurulur, g_running=0 iken frame bos doner. Init de_wasm_pak_ready'de.
    de_plugin_set_dispatcher(plugin_run_now);
    de_js_plugin_install(); // DE.plugins'tekileri kaydet (plugins.js senkron yuklenmis olmali), DE.send'i kur
    emscripten_set_main_loop(frame, 0, 0); // 0 fps = requestAnimationFrame; simulate_infinite_loop=0 -> main doner, runtime yasar
    de_js_fetch_pak("game.pak"); // index.html ile ayni dizin
    return 0;
}
