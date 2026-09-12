#ifndef ENGINE_WINDOW_H
#define ENGINE_WINDOW_H

#include <stdbool.h>

typedef struct engine_window engine_window;

typedef struct
{
    const char *title;
    int x;
    int y;
    int width;
    int height;
    bool visible;
    bool resizable;
} engine_window_desc;

typedef struct
{
    int x;
    int y;
    int width;
    int height;
    float dpiScale;
    bool focused;
    bool minimized;
    bool closing;
} engine_window_state;

engine_window *engine_window_create(const engine_window_desc *desc);
void engine_window_destroy(engine_window *window);
void engine_window_show(engine_window *window);
void engine_window_hide(engine_window *window);
void engine_window_set_title(engine_window *window, const char *title);
engine_window_state engine_window_get_state(const engine_window *window);
void engine_window_poll_events(void);
void *engine_window_native_handle(const engine_window *window);
bool engine_window_begin_render(engine_window *window);
void engine_window_end_render(engine_window *window);

#endif
