// host_wasm.c — Tarayici (wasm32/emscripten) platform host'u (docs/platform-hosts.md).
// Pencere = <canvas id="canvas"> (platforms/wasm/index.html), GL = WebGL2 (sokol SOKOL_GLES3), dongu =
// emscripten_set_main_loop -> de_app_frame (requestAnimationFrame). Girdi: html5 callback'leri -> de_app_event
// kuyrugu (tek thread; kuyruk yine de sozlesme geregi). Pak: --preload-file ile MEMFS'e /data/Build/game.pak.
// Koordinat: canvas CSS px = MANTIKSAL px (fbw = css * devicePixelRatio, scale = dpr) -> olaylar oldugu gibi gecer.
#include "de_app.h"
#include <emscripten/emscripten.h>
#include <emscripten/html5.h>
#include <stdio.h>
#include <stdlib.h>

#define CANVAS "#canvas"

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
    // --preload-file <pak>@/data/Build/game.pak -> dataRoot "/data"; yazilabilir /tmp (MEMFS, kalici degil).
    de_app_init("/data", "/tmp", g_fbw, g_fbh, g_scale);
    g_running = 1;
    emscripten_set_main_loop(frame, 0, 0); // 0 fps = requestAnimationFrame; simulate_infinite_loop=0 -> main doner, runtime yasar
    return 0;
}
