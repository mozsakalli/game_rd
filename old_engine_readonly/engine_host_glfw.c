// engine_host_glfw.c - desktop host: GLFW pencereleri + tek sokol_gfx context.
// Tum sg_* cagrilari ana GL context'te kalir; ikincil pencereler paylasilan
// renderbuffer'lara render alir ve kendi context'lerinde blit ile sunulur.
#if defined(ENGINE_USE_GLFW)

#include <stdio.h>
#include <stdint.h>
#include <stdbool.h>

#include "sokol_gfx.h"
#include "sokol_log.h"
#include "imgui.h"
#include "GLFW/glfw3.h"

// engine.c host kancalari
void engine_host_bootstrap(void);
void engine_host_frame_tick(void);
void engine_host_shutdown_tick(void);
void engine_host_update_mouse(float x, float y);
void engine_host_push_imgui_event(const imgui_event *event);
void engine_host_render_panel(uint32_t panelId, int width, int height,
                              const imgui_event *event, float mouseX, float mouseY);
void engine_editor_panel_closed(uint32_t panelId);

// FBO/blit icin gereken minimal GL yuzeyi (glfwGetProcAddress ile yuklenir).
typedef unsigned int GLuint;
typedef unsigned int GLenum;
typedef int GLint;
typedef int GLsizei;
typedef unsigned int GLbitfield;

#define _GL_FRAMEBUFFER 0x8D40
#define _GL_READ_FRAMEBUFFER 0x8CA8
#define _GL_DRAW_FRAMEBUFFER 0x8CA9
#define _GL_RENDERBUFFER 0x8D41
#define _GL_COLOR_ATTACHMENT0 0x8CE0
#define _GL_DEPTH_STENCIL_ATTACHMENT 0x821A
#define _GL_RGBA8 0x8058
#define _GL_DEPTH24_STENCIL8 0x88F0
#define _GL_FRAMEBUFFER_COMPLETE 0x8CD5
#define _GL_COLOR_BUFFER_BIT 0x00004000
#define _GL_LINEAR 0x2601

static void (*_glGenRenderbuffers)(GLsizei, GLuint *);
static void (*_glBindRenderbuffer)(GLenum, GLuint);
static void (*_glRenderbufferStorage)(GLenum, GLenum, GLsizei, GLsizei);
static void (*_glDeleteRenderbuffers)(GLsizei, const GLuint *);
static void (*_glGenFramebuffers)(GLsizei, GLuint *);
static void (*_glBindFramebuffer)(GLenum, GLuint);
static void (*_glFramebufferRenderbuffer)(GLenum, GLenum, GLenum, GLuint);
static GLenum (*_glCheckFramebufferStatus)(GLenum);
static void (*_glDeleteFramebuffers)(GLsizei, const GLuint *);
static void (*_glBlitFramebuffer)(GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLbitfield, GLenum);

static bool _engine_glfw_load_gl(void)
{
    _glGenRenderbuffers = (void (*)(GLsizei, GLuint *))glfwGetProcAddress("glGenRenderbuffers");
    _glBindRenderbuffer = (void (*)(GLenum, GLuint))glfwGetProcAddress("glBindRenderbuffer");
    _glRenderbufferStorage = (void (*)(GLenum, GLenum, GLsizei, GLsizei))glfwGetProcAddress("glRenderbufferStorage");
    _glDeleteRenderbuffers = (void (*)(GLsizei, const GLuint *))glfwGetProcAddress("glDeleteRenderbuffers");
    _glGenFramebuffers = (void (*)(GLsizei, GLuint *))glfwGetProcAddress("glGenFramebuffers");
    _glBindFramebuffer = (void (*)(GLenum, GLuint))glfwGetProcAddress("glBindFramebuffer");
    _glFramebufferRenderbuffer = (void (*)(GLenum, GLenum, GLenum, GLuint))glfwGetProcAddress("glFramebufferRenderbuffer");
    _glCheckFramebufferStatus = (GLenum (*)(GLenum))glfwGetProcAddress("glCheckFramebufferStatus");
    _glDeleteFramebuffers = (void (*)(GLsizei, const GLuint *))glfwGetProcAddress("glDeleteFramebuffers");
    _glBlitFramebuffer = (void (*)(GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLbitfield, GLenum))glfwGetProcAddress("glBlitFramebuffer");
    return _glGenRenderbuffers && _glBindRenderbuffer && _glRenderbufferStorage &&
           _glGenFramebuffers && _glBindFramebuffer && _glFramebufferRenderbuffer &&
           _glCheckFramebufferStatus && _glDeleteFramebuffers && _glBlitFramebuffer &&
           _glDeleteRenderbuffers;
}

typedef struct
{
    GLFWwindow *glfw;
    int width;
    int height;
    GLuint colorRb;
    GLuint depthRb;
    GLuint mainFb; // ana context'te sokol'un cizdigi FBO
    GLuint winFb;  // pencere context'inde blit kaynagi
} _engine_glfw_secondary;

#define ENGINE_GLFW_MAX_PANEL_WINDOWS 8

typedef struct
{
    _engine_glfw_secondary window;
    uint32_t panelId;
    bool used;
    imgui_event pendingEvent; // pencere-lokal koordinatli bekleyen input
    bool hasPending;
    float mouseX;
    float mouseY;
} _engine_glfw_panel_window;

static struct
{
    GLFWwindow *main;
    _engine_glfw_panel_window panels[ENGINE_GLFW_MAX_PANEL_WINDOWS];
    int mainWidth;
    int mainHeight;
} _engine_glfw;

int engine_host_width(void)
{
    return _engine_glfw.mainWidth;
}

int engine_host_height(void)
{
    return _engine_glfw.mainHeight;
}

// Pencerenin monitor icerik olcegi (1.0 = %100, 1.5 = %150). UI olcekleme icin.
float engine_host_content_scale(void)
{
    if (!_engine_glfw.main)
        return 1.0f;
    float scaleX = 1.0f, scaleY = 1.0f;
    glfwGetWindowContentScale(_engine_glfw.main, &scaleX, &scaleY);
    return scaleX > 0.0f ? scaleX : 1.0f;
}

// engine_begin_frame'in aktif hedefi; ikincil render sirasinda gecici degisir.
static sg_swapchain _engine_glfw_active_swapchain;

sg_swapchain engine_host_swapchain(void)
{
    return _engine_glfw_active_swapchain;
}

static void _engine_glfw_set_main_swapchain(void)
{
    sg_swapchain sc = {0};
    sc.width = _engine_glfw.mainWidth;
    sc.height = _engine_glfw.mainHeight;
    sc.sample_count = 1;
    sc.color_format = SG_PIXELFORMAT_RGBA8;
    sc.depth_format = SG_PIXELFORMAT_DEPTH_STENCIL;
    sc.gl.framebuffer = 0;
    _engine_glfw_active_swapchain = sc;
}

static void _engine_glfw_set_secondary_swapchain(const _engine_glfw_secondary *window)
{
    sg_swapchain sc = {0};
    sc.width = window->width;
    sc.height = window->height;
    sc.sample_count = 1;
    sc.color_format = SG_PIXELFORMAT_RGBA8;
    sc.depth_format = SG_PIXELFORMAT_DEPTH_STENCIL;
    sc.gl.framebuffer = window->mainFb;
    _engine_glfw_active_swapchain = sc;
}

static _engine_glfw_panel_window *_engine_glfw_find_panel(GLFWwindow *window)
{
    for (int i = 0; i < ENGINE_GLFW_MAX_PANEL_WINDOWS; ++i)
        if (_engine_glfw.panels[i].used && _engine_glfw.panels[i].window.glfw == window)
            return &_engine_glfw.panels[i];
    return NULL;
}

// Event ana pencereden geldiyse global imgui kuyruguna, panel penceresinden
// geldiyse o panelin pencere-lokal bekleyen event'ine yazilir.
// DRAG, ayni frame'de bekleyen daha onemli bir event'i (down/up) ezmez.
static void _engine_glfw_dispatch_event(GLFWwindow *window, const imgui_event *event)
{
    _engine_glfw_panel_window *panel = _engine_glfw_find_panel(window);
    if (panel)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DRAG && panel->hasPending &&
            panel->pendingEvent.type != IMGUI_EVENT_MOUSE_DRAG)
            return;
        panel->pendingEvent = *event;
        panel->hasPending = true;
        panel->mouseX = event->mouseX;
        panel->mouseY = event->mouseY;
    }
    else
        engine_host_push_imgui_event(event);
}

static void _engine_glfw_fill_mods(GLFWwindow *window, imgui_event *event)
{
    event->shift = glfwGetKey(window, GLFW_KEY_LEFT_SHIFT) == GLFW_PRESS ||
                   glfwGetKey(window, GLFW_KEY_RIGHT_SHIFT) == GLFW_PRESS;
    event->control = glfwGetKey(window, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS ||
                     glfwGetKey(window, GLFW_KEY_RIGHT_CONTROL) == GLFW_PRESS ||
                     glfwGetKey(window, GLFW_KEY_LEFT_SUPER) == GLFW_PRESS ||
                     glfwGetKey(window, GLFW_KEY_RIGHT_SUPER) == GLFW_PRESS;
    event->alt = glfwGetKey(window, GLFW_KEY_LEFT_ALT) == GLFW_PRESS ||
                 glfwGetKey(window, GLFW_KEY_RIGHT_ALT) == GLFW_PRESS;
}

static void _engine_glfw_mouse_button_cb(GLFWwindow *window, int button, int action, int mods)
{
    (void)mods;
    static double lastClickTime;
    static double lastClickX, lastClickY;
    static GLFWwindow *lastClickWindow;
    static int clickCount = 1;
    double x, y;
    glfwGetCursorPos(window, &x, &y);
    imgui_event event = {0};
    event.type = action == GLFW_PRESS ? IMGUI_EVENT_MOUSE_DOWN : IMGUI_EVENT_MOUSE_UP;
    event.mouseX = (float)x;
    event.mouseY = (float)y;
    event.button = button;
    _engine_glfw_fill_mods(window, &event);
    if (action == GLFW_PRESS)
    {
        double now = glfwGetTime();
        double dx = x - lastClickX, dy = y - lastClickY;
        if (window == lastClickWindow && now - lastClickTime < 0.35 && dx * dx + dy * dy < 25.0)
            clickCount++;
        else
            clickCount = 1;
        lastClickTime = now;
        lastClickX = x;
        lastClickY = y;
        lastClickWindow = window;
        event.clickCount = clickCount;
    }
    _engine_glfw_dispatch_event(window, &event);
}

static void _engine_glfw_cursor_cb(GLFWwindow *window, double x, double y)
{
    _engine_glfw_panel_window *panel = _engine_glfw_find_panel(window);
    if (panel)
    {
        panel->mouseX = (float)x;
        panel->mouseY = (float)y;
    }
    else
        engine_host_update_mouse((float)x, (float)y);

    int button = -1;
    if (glfwGetMouseButton(window, GLFW_MOUSE_BUTTON_LEFT) == GLFW_PRESS)
        button = GLFW_MOUSE_BUTTON_LEFT;
    else if (glfwGetMouseButton(window, GLFW_MOUSE_BUTTON_RIGHT) == GLFW_PRESS)
        button = GLFW_MOUSE_BUTTON_RIGHT;
    else if (glfwGetMouseButton(window, GLFW_MOUSE_BUTTON_MIDDLE) == GLFW_PRESS)
        button = GLFW_MOUSE_BUTTON_MIDDLE;
    if (button >= 0)
    {
        imgui_event event = {0};
        event.type = IMGUI_EVENT_MOUSE_DRAG;
        event.mouseX = (float)x;
        event.mouseY = (float)y;
        event.button = button;
        _engine_glfw_fill_mods(window, &event);
        _engine_glfw_dispatch_event(window, &event);
    }
}

static void _engine_glfw_scroll_cb(GLFWwindow *window, double sx, double sy)
{
    double x, y;
    glfwGetCursorPos(window, &x, &y);
    imgui_event event = {0};
    event.type = IMGUI_EVENT_SCROLL_WHEEL;
    event.mouseX = (float)x;
    event.mouseY = (float)y;
    event.scrollX = (float)sx;
    event.scrollY = (float)sy;
    _engine_glfw_dispatch_event(window, &event);
}

static void _engine_glfw_key_cb(GLFWwindow *window, int key, int scancode, int action, int mods)
{
    (void)scancode;
    (void)mods;
    if (action == GLFW_REPEAT)
        action = GLFW_PRESS; // basili tutulan tuslar (backspace vb.) tekrar uretir
    double x, y;
    glfwGetCursorPos(window, &x, &y);
    imgui_event event = {0};
    event.type = action == GLFW_PRESS ? IMGUI_EVENT_KEY_DOWN : IMGUI_EVENT_KEY_UP;
    event.mouseX = (float)x;
    event.mouseY = (float)y;
    event.key = key;
    _engine_glfw_fill_mods(window, &event);
    _engine_glfw_dispatch_event(window, &event);
}

static void _engine_glfw_char_cb(GLFWwindow *window, unsigned int codepoint)
{
    double x, y;
    glfwGetCursorPos(window, &x, &y);
    imgui_event event = {0};
    event.type = IMGUI_EVENT_TEXT_INPUT;
    event.mouseX = (float)x;
    event.mouseY = (float)y;
    event.character = codepoint;
    _engine_glfw_dispatch_event(window, &event);
}

static bool _engine_glfw_create_secondary(_engine_glfw_secondary *window, int width, int height,
                                          const char *title, int x, int y)
{
    window->width = width;
    window->height = height;
    window->glfw = glfwCreateWindow(width, height, title, NULL, _engine_glfw.main);
    if (!window->glfw)
        return false;
    glfwSetWindowPos(window->glfw, x, y);

    // sokol'un cizecegi renderbuffer'lar ana context'te olusur.
    glfwMakeContextCurrent(_engine_glfw.main);
    _glGenRenderbuffers(1, &window->colorRb);
    _glBindRenderbuffer(_GL_RENDERBUFFER, window->colorRb);
    _glRenderbufferStorage(_GL_RENDERBUFFER, _GL_RGBA8, width, height);
    _glGenRenderbuffers(1, &window->depthRb);
    _glBindRenderbuffer(_GL_RENDERBUFFER, window->depthRb);
    _glRenderbufferStorage(_GL_RENDERBUFFER, _GL_DEPTH24_STENCIL8, width, height);
    _glGenFramebuffers(1, &window->mainFb);
    _glBindFramebuffer(_GL_FRAMEBUFFER, window->mainFb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_COLOR_ATTACHMENT0, _GL_RENDERBUFFER, window->colorRb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_DEPTH_STENCIL_ATTACHMENT, _GL_RENDERBUFFER, window->depthRb);
    if (_glCheckFramebufferStatus(_GL_FRAMEBUFFER) != _GL_FRAMEBUFFER_COMPLETE)
    {
        fprintf(stderr, "engine_host_glfw: secondary main-fb incomplete\n");
        return false;
    }
    _glBindFramebuffer(_GL_FRAMEBUFFER, 0);

    // Pencere context'i ayni renderbuffer'i blit kaynagi olarak sarar.
    glfwMakeContextCurrent(window->glfw);
    glfwSwapInterval(0);
    _glGenFramebuffers(1, &window->winFb);
    _glBindFramebuffer(_GL_FRAMEBUFFER, window->winFb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_COLOR_ATTACHMENT0, _GL_RENDERBUFFER, window->colorRb);
    if (_glCheckFramebufferStatus(_GL_FRAMEBUFFER) != _GL_FRAMEBUFFER_COMPLETE)
    {
        fprintf(stderr, "engine_host_glfw: secondary win-fb incomplete\n");
        return false;
    }
    _glBindFramebuffer(_GL_FRAMEBUFFER, 0);
    glfwMakeContextCurrent(_engine_glfw.main);
    return true;
}

static void _engine_glfw_destroy_secondary(_engine_glfw_secondary *window)
{
    if (!window->glfw)
        return;
    glfwMakeContextCurrent(window->glfw);
    _glDeleteFramebuffers(1, &window->winFb);
    glfwMakeContextCurrent(_engine_glfw.main);
    _glDeleteFramebuffers(1, &window->mainFb);
    _glDeleteRenderbuffers(1, &window->colorRb);
    _glDeleteRenderbuffers(1, &window->depthRb);
    glfwDestroyWindow(window->glfw);
    window->glfw = NULL;
}

static void _engine_glfw_present_secondary(_engine_glfw_secondary *window)
{
    int dstWidth, dstHeight;
    glfwGetFramebufferSize(window->glfw, &dstWidth, &dstHeight);
    glfwMakeContextCurrent(window->glfw);
    _glBindFramebuffer(_GL_DRAW_FRAMEBUFFER, 0);
    _glBindFramebuffer(_GL_READ_FRAMEBUFFER, window->winFb);
    _glBlitFramebuffer(0, 0, window->width, window->height,
                       0, 0, dstWidth, dstHeight,
                       _GL_COLOR_BUFFER_BIT, _GL_LINEAR);
    glfwSwapBuffers(window->glfw);
    glfwMakeContextCurrent(_engine_glfw.main);
    sg_reset_state_cache();
}

// Pencere boyutu degisince paylasilan renderbuffer'lar yeni boyutta kurulur
// ve iki context'teki FBO'lara yeniden baglanir.
static bool _engine_glfw_resize_secondary(_engine_glfw_secondary *window, int width, int height)
{
    if (width <= 0 || height <= 0)
        return false;
    glfwMakeContextCurrent(_engine_glfw.main);
    _glDeleteRenderbuffers(1, &window->colorRb);
    _glDeleteRenderbuffers(1, &window->depthRb);
    _glGenRenderbuffers(1, &window->colorRb);
    _glBindRenderbuffer(_GL_RENDERBUFFER, window->colorRb);
    _glRenderbufferStorage(_GL_RENDERBUFFER, _GL_RGBA8, width, height);
    _glGenRenderbuffers(1, &window->depthRb);
    _glBindRenderbuffer(_GL_RENDERBUFFER, window->depthRb);
    _glRenderbufferStorage(_GL_RENDERBUFFER, _GL_DEPTH24_STENCIL8, width, height);
    _glBindFramebuffer(_GL_FRAMEBUFFER, window->mainFb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_COLOR_ATTACHMENT0, _GL_RENDERBUFFER, window->colorRb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_DEPTH_STENCIL_ATTACHMENT, _GL_RENDERBUFFER, window->depthRb);
    bool mainOk = _glCheckFramebufferStatus(_GL_FRAMEBUFFER) == _GL_FRAMEBUFFER_COMPLETE;
    _glBindFramebuffer(_GL_FRAMEBUFFER, 0);
    glfwMakeContextCurrent(window->glfw);
    _glBindFramebuffer(_GL_FRAMEBUFFER, window->winFb);
    _glFramebufferRenderbuffer(_GL_FRAMEBUFFER, _GL_COLOR_ATTACHMENT0, _GL_RENDERBUFFER, window->colorRb);
    bool winOk = _glCheckFramebufferStatus(_GL_FRAMEBUFFER) == _GL_FRAMEBUFFER_COMPLETE;
    _glBindFramebuffer(_GL_FRAMEBUFFER, 0);
    glfwMakeContextCurrent(_engine_glfw.main);
    sg_reset_state_cache();
    if (!mainOk || !winOk)
    {
        fprintf(stderr, "engine_host_glfw: resize fbo incomplete (%dx%d)\n", width, height);
        return false;
    }
    window->width = width;
    window->height = height;
    return true;
}

bool engine_host_open_panel_window(uint32_t panelId, const char *title, int x, int y, int width, int height)
{
    for (int i = 0; i < ENGINE_GLFW_MAX_PANEL_WINDOWS; ++i)
    {
        if (_engine_glfw.panels[i].used && _engine_glfw.panels[i].panelId == panelId)
            return true;
    }
    for (int i = 0; i < ENGINE_GLFW_MAX_PANEL_WINDOWS; ++i)
    {
        _engine_glfw_panel_window *slot = &_engine_glfw.panels[i];
        if (slot->used)
            continue;
        if (!_engine_glfw_create_secondary(&slot->window, width, height, title, x, y))
            return false;
        glfwSetMouseButtonCallback(slot->window.glfw, _engine_glfw_mouse_button_cb);
        glfwSetCursorPosCallback(slot->window.glfw, _engine_glfw_cursor_cb);
        glfwSetScrollCallback(slot->window.glfw, _engine_glfw_scroll_cb);
        glfwSetKeyCallback(slot->window.glfw, _engine_glfw_key_cb);
        glfwSetCharCallback(slot->window.glfw, _engine_glfw_char_cb);
        slot->panelId = panelId;
        slot->used = true;
        slot->hasPending = false;
        slot->mouseX = -1000.0f;
        slot->mouseY = -1000.0f;
        return true;
    }
    return false;
}

void engine_host_main_window_pos(int *x, int *y)
{
    int wx = 0, wy = 0;
    if (_engine_glfw.main)
        glfwGetWindowPos(_engine_glfw.main, &wx, &wy);
    if (x)
        *x = wx;
    if (y)
        *y = wy;
}

void engine_host_glfw_run(int width, int height, const char *title)
{
    if (!glfwInit())
    {
        fprintf(stderr, "engine_host_glfw: glfwInit failed\n");
        return;
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 1);
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GLFW_TRUE);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_SCALE_TO_MONITOR, GLFW_TRUE); // 125%/150% ekranlarda mantikli pencere boyutu

    _engine_glfw.main = glfwCreateWindow(width, height, title, NULL, NULL);
    if (!_engine_glfw.main)
    {
        fprintf(stderr, "engine_host_glfw: main window failed\n");
        glfwTerminate();
        return;
    }
    {
        float scaleX = 1.0f, scaleY = 1.0f;
        int winW = 0, winH = 0, fbW = 0, fbH = 0;
        glfwGetWindowContentScale(_engine_glfw.main, &scaleX, &scaleY);
        glfwGetWindowSize(_engine_glfw.main, &winW, &winH);
        glfwGetFramebufferSize(_engine_glfw.main, &fbW, &fbH);
        printf("host: dpi scale=%.2f window=%dx%d framebuffer=%dx%d\n", scaleX, winW, winH, fbW, fbH);
    }
    _engine_glfw.mainWidth = width;
    _engine_glfw.mainHeight = height;
    glfwMakeContextCurrent(_engine_glfw.main);
    glfwSwapInterval(1);
    if (!_engine_glfw_load_gl())
    {
        fprintf(stderr, "engine_host_glfw: GL loader failed\n");
        glfwTerminate();
        return;
    }
    glfwSetMouseButtonCallback(_engine_glfw.main, _engine_glfw_mouse_button_cb);
    glfwSetCursorPosCallback(_engine_glfw.main, _engine_glfw_cursor_cb);
    glfwSetScrollCallback(_engine_glfw.main, _engine_glfw_scroll_cb);
    glfwSetKeyCallback(_engine_glfw.main, _engine_glfw_key_cb);
    glfwSetCharCallback(_engine_glfw.main, _engine_glfw_char_cb);

    sg_desc desc = {0};
    desc.environment.defaults.color_format = SG_PIXELFORMAT_RGBA8;
    desc.environment.defaults.depth_format = SG_PIXELFORMAT_DEPTH_STENCIL;
    desc.environment.defaults.sample_count = 1;
    desc.logger.func = slog_func;
    sg_setup(&desc);
    engine_host_bootstrap();

    while (!glfwWindowShouldClose(_engine_glfw.main))
    {
        glfwPollEvents();
        glfwMakeContextCurrent(_engine_glfw.main);
        sg_reset_state_cache();

        int fbWidth, fbHeight;
        glfwGetFramebufferSize(_engine_glfw.main, &fbWidth, &fbHeight);
        _engine_glfw.mainWidth = fbWidth;
        _engine_glfw.mainHeight = fbHeight;

        _engine_glfw_set_main_swapchain();
        engine_host_frame_tick();

        for (int i = 0; i < ENGINE_GLFW_MAX_PANEL_WINDOWS; ++i)
        {
            _engine_glfw_panel_window *slot = &_engine_glfw.panels[i];
            if (!slot->used)
                continue;
            if (glfwWindowShouldClose(slot->window.glfw))
            {
                uint32_t closedId = slot->panelId;
                _engine_glfw_destroy_secondary(&slot->window);
                slot->used = false;
                engine_editor_panel_closed(closedId);
                continue;
            }
            int panelWidth = 0, panelHeight = 0;
            glfwGetFramebufferSize(slot->window.glfw, &panelWidth, &panelHeight);
            if (panelWidth <= 0 || panelHeight <= 0)
                continue; // minimize: render/blit atla
            if ((panelWidth != slot->window.width || panelHeight != slot->window.height) &&
                !_engine_glfw_resize_secondary(&slot->window, panelWidth, panelHeight))
                continue;
            _engine_glfw_set_secondary_swapchain(&slot->window);
            engine_host_render_panel(slot->panelId, slot->window.width, slot->window.height,
                                     slot->hasPending ? &slot->pendingEvent : NULL,
                                     slot->mouseX, slot->mouseY);
            slot->hasPending = false;
            _engine_glfw_set_main_swapchain();
            _engine_glfw_present_secondary(&slot->window);
        }

        glfwSwapBuffers(_engine_glfw.main);
    }

    for (int i = 0; i < ENGINE_GLFW_MAX_PANEL_WINDOWS; ++i)
    {
        if (_engine_glfw.panels[i].used)
        {
            _engine_glfw_destroy_secondary(&_engine_glfw.panels[i].window);
            _engine_glfw.panels[i].used = false;
        }
    }
    engine_host_shutdown_tick();
    glfwDestroyWindow(_engine_glfw.main);
    glfwTerminate();
}

#endif
