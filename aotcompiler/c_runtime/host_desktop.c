// host_desktop.c — Windows/macOS platform host'u: GLFW pencere + GL (ya da Metal) context + dongu.
// Oyunla yalniz de_app.h uzerinden konusur (docs/platform-hosts.md). Girdi POLL edilmez: GLFW
// callback'leri de_app_event kuyruguna yazar, oyun Frame basinda bosaltir. Zaman dt olarak gecer.
// Swap host'un isi (GL). main() buradadir; aotcompiler artik main uretmez.
#include "de_app.h"
#include <stdio.h>
#include <stdlib.h>
#define GLFW_INCLUDE_NONE
#include "GLFW/glfw3.h"
#ifdef SOKOL_METAL
void de_metal_init_window(GLFWwindow* w); // sokol_shim.c (Metal yolu)
#endif

static GLFWwindow* g_window;
static float g_mouseScale = 1.0f; // pencere px -> mantiksal px
static int g_mouseDown;

static void push(int type, int id, float x, float y, int a, int b)
{
    DeEvent e = { type, id, x, y, a, b };
    de_app_event(&e);
}

static void on_mouse_button(GLFWwindow* w, int button, int action, int mods)
{
    (void)mods;
    if (button != GLFW_MOUSE_BUTTON_LEFT) return;
    double mx, my; glfwGetCursorPos(w, &mx, &my);
    g_mouseDown = action == GLFW_PRESS;
    push(g_mouseDown ? DE_EV_POINTER_DOWN : DE_EV_POINTER_UP, 0, (float)mx / g_mouseScale, (float)my / g_mouseScale, 0, 0);
}

static void on_cursor_pos(GLFWwindow* w, double mx, double my)
{
    (void)w;
    if (g_mouseDown)
        push(DE_EV_POINTER_MOVE, 0, (float)mx / g_mouseScale, (float)my / g_mouseScale, 0, 0);
}

static void on_key(GLFWwindow* w, int key, int scancode, int action, int mods)
{
    (void)w; (void)scancode; (void)mods;
    if (action == GLFW_REPEAT) return;
    push(action == GLFW_PRESS ? DE_EV_KEY_DOWN : DE_EV_KEY_UP, key, 0, 0, 0, 0);
}

static void on_char(GLFWwindow* w, unsigned int cp) { (void)w; push(DE_EV_TEXT, 0, 0, 0, (int)cp, 0); }
static void on_focus(GLFWwindow* w, int focused) { (void)w; push(DE_EV_FOCUS, 0, 0, 0, focused, 0); }

void de_host_set_title(const char* utf8) { if (g_window) glfwSetWindowTitle(g_window, utf8); }
void de_host_log(int level, const char* utf8) { fprintf(level >= 2 ? stderr : stdout, "%s\n", utf8); }

// Framebuffer boyutu + icerik olcegi + fare olcegi (pencere px / mantiksal px; macOS'ta pencere zaten point).
static void measure(int* fbw, int* fbh, float* scale)
{
    glfwGetFramebufferSize(g_window, fbw, fbh);
    float sx, sy; glfwGetWindowContentScale(g_window, &sx, &sy);
    *scale = sx > 0 ? sx : 1.0f;
    int winW; glfwGetWindowSize(g_window, &winW, NULL);
    float lw = *fbw / *scale;
    g_mouseScale = winW > 0 && lw > 0 ? winW / lw : *scale;
}

int main(int argc, char** argv)
{
    const char* root = argc > 1 ? argv[1] : NULL; // NULL: exe Build/ icinde, proje koku ".."
    if (!glfwInit()) { fprintf(stderr, "[host] glfwInit basarisiz\n"); return 1; }
#ifdef SOKOL_METAL
    glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
#else
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 1);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GLFW_TRUE);
#endif
    glfwWindowHint(GLFW_SCALE_TO_MONITOR, GLFW_TRUE);
    g_window = glfwCreateWindow(1280, 720, "Loading...", NULL, NULL);
    if (!g_window) { fprintf(stderr, "[host] pencere acilamadi\n"); return 1; }
#ifdef SOKOL_METAL
    de_metal_init_window(g_window);
#else
    glfwMakeContextCurrent(g_window);
    glfwSwapInterval(1);
#endif
    glfwSetMouseButtonCallback(g_window, on_mouse_button);
    glfwSetCursorPosCallback(g_window, on_cursor_pos);
    glfwSetKeyCallback(g_window, on_key);
    glfwSetCharCallback(g_window, on_char);
    glfwSetWindowFocusCallback(g_window, on_focus);

    int fbw, fbh; float scale;
    measure(&fbw, &fbh, &scale);
    de_app_init(root, ".", fbw, fbh, scale);

    double last = 0;
    while (!glfwWindowShouldClose(g_window))
    {
        glfwPollEvents();
        measure(&fbw, &fbh, &scale);
        double t = glfwGetTime();
        float dt = last > 0 ? (float)(t - last) : 0.0f;
        last = t;
        if (!de_app_frame(dt, fbw, fbh, scale)) break;
#ifndef SOKOL_METAL
        if (fbw > 0 && fbh > 0) glfwSwapBuffers(g_window);
#endif
    }
    de_app_shutdown();
    glfwTerminate();
    fprintf(stderr, "[host] main returned\n");
    return 0;
}
