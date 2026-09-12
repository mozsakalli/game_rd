#include "engine_window.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

struct engine_window
{
    HWND handle;
    HDC dc;
    bool closing;
};

static const char *_engine_window_class = "DigiPlayEngineWindow";
static bool _engine_window_class_ready;

static LRESULT CALLBACK _engine_window_proc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    engine_window *window = (engine_window *)GetWindowLongPtrA(hwnd, GWLP_USERDATA);
    switch (message)
    {
    case WM_NCCREATE:
    {
        const CREATESTRUCTA *create = (const CREATESTRUCTA *)lParam;
        window = (engine_window *)create->lpCreateParams;
        SetWindowLongPtrA(hwnd, GWLP_USERDATA, (LONG_PTR)window);
        return TRUE;
    }
    case WM_CLOSE:
        if (window)
            window->closing = true;
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        if (window)
            window->closing = true;
        return 0;
    default:
        return DefWindowProcA(hwnd, message, wParam, lParam);
    }
}

static bool _engine_window_register_class(void)
{
    if (_engine_window_class_ready)
        return true;
    HINSTANCE instance = GetModuleHandleA(NULL);
    WNDCLASSEXA cls = {0};
    cls.cbSize = sizeof(cls);
    cls.lpfnWndProc = _engine_window_proc;
    cls.hInstance = instance;
    cls.hCursor = LoadCursorA(NULL, IDC_ARROW);
    cls.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    cls.lpszClassName = _engine_window_class;
    if (!RegisterClassExA(&cls) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        return false;
    _engine_window_class_ready = true;
    return true;
}

engine_window *engine_window_create(const engine_window_desc *desc)
{
    if (!desc || desc->width <= 0 || desc->height <= 0 || !_engine_window_register_class())
        return NULL;

    engine_window *window = (engine_window *)calloc(1, sizeof(*window));
    if (!window)
        return NULL;

    DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    if (desc->resizable)
        style |= WS_THICKFRAME | WS_MAXIMIZEBOX;
    RECT bounds = {0, 0, desc->width, desc->height};
    AdjustWindowRect(&bounds, style, FALSE);
    int x = desc->x;
    int y = desc->y;
    if (x == 0 && y == 0)
        x = CW_USEDEFAULT, y = CW_USEDEFAULT;

    HWND handle = CreateWindowExA(0, _engine_window_class,
                                  desc->title ? desc->title : "DigiPlay",
                                  style, x, y,
                                  bounds.right - bounds.left,
                                  bounds.bottom - bounds.top,
                                  NULL, NULL, GetModuleHandleA(NULL), window);
    if (!handle)
    {
        free(window);
        return NULL;
    }
    window->handle = handle;
    window->dc = GetDC(handle);
    if (!window->dc)
    {
        if (window->dc)
            ReleaseDC(handle, window->dc);
        DestroyWindow(handle);
        free(window);
        return NULL;
    }
    if (desc->visible)
        ShowWindow(handle, SW_SHOW);
    UpdateWindow(handle);
    fprintf(stderr, "engine_window: created HWND=%p\n", (void *)window->handle);
    return window;
}

void engine_window_destroy(engine_window *window)
{
    if (!window)
        return;
    if (window->handle)
    {
        if (window->dc)
            ReleaseDC(window->handle, window->dc);
        DestroyWindow(window->handle);
    }
    free(window);
}

void engine_window_show(engine_window *window)
{
    if (window && window->handle)
        ShowWindow(window->handle, SW_SHOW);
}

void engine_window_hide(engine_window *window)
{
    if (window && window->handle)
        ShowWindow(window->handle, SW_HIDE);
}

void engine_window_set_title(engine_window *window, const char *title)
{
    if (window && window->handle && title)
        SetWindowTextA(window->handle, title);
}

engine_window_state engine_window_get_state(const engine_window *window)
{
    engine_window_state state = {0};
    if (!window || !window->handle)
        return state;
    RECT rect;
    GetWindowRect(window->handle, &rect);
    state.x = rect.left;
    state.y = rect.top;
    state.width = rect.right - rect.left;
    state.height = rect.bottom - rect.top;
    state.dpiScale = 1.0f;
    state.focused = GetForegroundWindow() == window->handle;
    state.minimized = IsIconic(window->handle) != FALSE;
    state.closing = window->closing;
    return state;
}

void engine_window_poll_events(void)
{
    MSG message;
    while (PeekMessageA(&message, NULL, 0, 0, PM_REMOVE))
    {
        TranslateMessage(&message);
        DispatchMessageA(&message);
    }
}

void *engine_window_native_handle(const engine_window *window)
{
    return window ? (void *)window->handle : NULL;
}

bool engine_window_begin_render(engine_window *window)
{
    (void)window;
    return false;
}

void engine_window_end_render(engine_window *window)
{
    if (!window)
        return;
    InvalidateRect(window->handle, NULL, FALSE);
}

#else

struct engine_window
{
    int unused;
};

engine_window *engine_window_create(const engine_window_desc *desc)
{
    (void)desc;
    return NULL;
}

void engine_window_destroy(engine_window *window) { free(window); }
void engine_window_show(engine_window *window) { (void)window; }
void engine_window_hide(engine_window *window) { (void)window; }
void engine_window_set_title(engine_window *window, const char *title)
{
    (void)window;
    (void)title;
}
engine_window_state engine_window_get_state(const engine_window *window)
{
    (void)window;
    engine_window_state state = {0};
    return state;
}
void engine_window_poll_events(void) {}
void *engine_window_native_handle(const engine_window *window)
{
    (void)window;
    return NULL;
}
bool engine_window_begin_render(engine_window *window)
{
    (void)window;
    return false;
}
void engine_window_end_render(engine_window *window) { (void)window; }

#endif
