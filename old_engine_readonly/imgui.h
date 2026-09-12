#ifndef ENGINE_IMGUI_H
#define ENGINE_IMGUI_H

#include <stdbool.h>
#include <stdint.h>
#include "../vmrt.h"

typedef enum
{
    IMGUI_EVENT_LAYOUT = 0,
    IMGUI_EVENT_REPAINT,
    IMGUI_EVENT_MOUSE_DOWN,
    IMGUI_EVENT_MOUSE_UP,
    IMGUI_EVENT_MOUSE_DRAG,
    IMGUI_EVENT_KEY_DOWN,
    IMGUI_EVENT_KEY_UP,
    IMGUI_EVENT_SCROLL_WHEEL,
    IMGUI_EVENT_TEXT_INPUT
} imgui_event_type;

typedef struct
{
    imgui_event_type type;
    float mouseX;
    float mouseY;
    float scrollX;
    float scrollY;
    int button;
    int key;
    unsigned int character;
    bool shift;
    bool control;
    bool alt;
    int clickCount; // basma aninda 1, hizli ikinci basmada 2 (double-click)
    bool used;
} imgui_event;

typedef struct
{
    float x;
    float y;
    float width;
    float height;
} imgui_rect;

typedef struct
{
    uint32_t id;
    bool hovered;
    bool active;
    bool focused;
    bool changed;
    int caret; // text field imleç pozisyonu (UTF-16 index)
} imgui_control_state;

typedef struct
{
    unsigned char r;
    unsigned char g;
    unsigned char b;
    unsigned char a;
} imgui_color;

typedef struct
{
    void *background;
    imgui_color textColor;
    imgui_color backgroundColor;
} imgui_style_state;

typedef enum
{
    IMGUI_IMAGE_LEFT,
    IMGUI_IMAGE_ABOVE,
    IMGUI_IMAGE_ONLY,
    IMGUI_TEXT_ONLY
} imgui_image_position;

typedef struct
{
    imgui_style_state normal;
    imgui_style_state hover;
    imgui_style_state active;
    imgui_style_state focused;
    imgui_style_state onNormal;
    imgui_style_state onHover;
    imgui_style_state onActive;
    imgui_style_state onFocused;
    float fixedWidth;
    float fixedHeight;
    float contentOffsetX;
    float contentOffsetY;
    imgui_image_position imagePosition;
    int paddingLeft;
    int paddingRight;
    int paddingTop;
    int paddingBottom;
    int marginLeft;
    int marginRight;
    int marginTop;
    int marginBottom;
    int borderLeft;
    int borderRight;
    int borderTop;
    int borderBottom;
} imgui_style;

typedef void *imgui_render_target;

typedef struct
{
    imgui_rect rect;
    VmString *title;
    float minWidth;
    float minHeight;
    bool floating;
    bool visible;
} imgui_window_desc;

typedef enum
{
    IMGUI_DOCK_LEAF,
    IMGUI_DOCK_SPLIT,
    IMGUI_DOCK_TABS
} imgui_dock_node_type;

typedef int imgui_dock_node;
typedef struct imgui_dock_host imgui_dock_host;

// Chrome metrikleri: 0 birakilan alan aktif font boyundan turetilir (imgui_line_height bazli).
typedef struct
{
    float windowTitleHeight;
    float dockTabHeight;
    float scrollbarSize;
    float minThumbSize;
    float windowBorder;
} imgui_metrics;

typedef struct
{
    imgui_style label;
    imgui_style box;
    imgui_style button;
    imgui_style toggle;
    imgui_style horizontalSlider;
    imgui_style horizontalSliderThumb;
    imgui_style verticalSlider;
    imgui_style verticalSliderThumb;
    imgui_style window;
    imgui_style scrollView;
    imgui_style textField;
    imgui_style textArea;
    imgui_metrics metrics;
} imgui_skin;

typedef struct imgui_font imgui_font;
typedef void (*imgui_panel_draw_fn)(imgui_rect contentRect, void *userData);

typedef enum
{
    IMGUI_LAYOUT_WIDTH,
    IMGUI_LAYOUT_HEIGHT,
    IMGUI_LAYOUT_MIN_WIDTH,
    IMGUI_LAYOUT_MAX_WIDTH,
    IMGUI_LAYOUT_MIN_HEIGHT,
    IMGUI_LAYOUT_MAX_HEIGHT,
    IMGUI_LAYOUT_EXPAND_WIDTH,
    IMGUI_LAYOUT_EXPAND_HEIGHT
} imgui_layout_option_type;

typedef struct
{
    imgui_layout_option_type type;
    float value;
} imgui_layout_option;

void imgui_begin_frame(float deltaTime);
void imgui_end_frame(void);
void imgui_begin_event(const imgui_event *event);
void imgui_end_event(void);

imgui_event_type imgui_event_type_current(void);
const imgui_event *imgui_current_event(void);

uint32_t imgui_get_control_id(uint32_t hint);
imgui_control_state *imgui_get_control_state(uint32_t id);

uint32_t imgui_hot_control(void);
void imgui_set_hot_control(uint32_t id);
uint32_t imgui_keyboard_control(void);
void imgui_set_keyboard_control(uint32_t id);

bool imgui_changed(void);
void imgui_set_changed(bool changed);
bool imgui_enabled(void);
void imgui_set_enabled(bool enabled);

bool imgui_rect_contains(imgui_rect rect, float x, float y);

void imgui_skin_init_default(imgui_skin *skin);
void imgui_set_skin(const imgui_skin *skin);
const imgui_skin *imgui_skin_current(void);
const imgui_style_state *imgui_style_state_for(const imgui_style *style, uint32_t id, bool on);
bool imgui_button(uint32_t hint, imgui_rect rect, const imgui_style *style);
void imgui_box(uint32_t hint, imgui_rect rect, const imgui_style *style);
void imgui_set_font(const imgui_font *font, float pixelHeight);
const imgui_font *imgui_font_current(void);
void imgui_label(VmString *text, imgui_rect rect, const imgui_style *style);
bool imgui_button_text(uint32_t hint, VmString *text, imgui_rect rect, const imgui_style *style);
bool imgui_toggle(uint32_t hint, imgui_rect rect, bool *value, const imgui_style *style);
bool imgui_toggle_text(uint32_t hint, VmString *text, imgui_rect rect, bool *value, const imgui_style *style);
float imgui_horizontal_slider(uint32_t hint, imgui_rect rect, float *value, float leftValue, float rightValue,
                              const imgui_style *style, const imgui_style *thumbStyle);
float imgui_vertical_slider(uint32_t hint, imgui_rect rect, float *value, float topValue, float bottomValue,
                            const imgui_style *style, const imgui_style *thumbStyle);
float imgui_horizontal_scrollbar(uint32_t hint, imgui_rect rect, float *value, float viewSize, float contentSize,
                                 const imgui_style *style, const imgui_style *thumbStyle);
float imgui_vertical_scrollbar(uint32_t hint, imgui_rect rect, float *value, float viewSize, float contentSize,
                               const imgui_style *style, const imgui_style *thumbStyle);
bool imgui_text_field(uint32_t hint, imgui_rect rect, unsigned short *buffer, int capacity, int *length,
                      const imgui_style *style);

// Unity GUI.BeginGroup/EndGroup: lokal koordinat sistemi + istege bagli scissor clip.
void imgui_begin_group(imgui_rect rect, bool clip);
void imgui_end_group(void);
float imgui_local_mouse_x(void); // aktif event mouse'u grup-lokal koordinatta
float imgui_local_mouse_y(void);

// EditorGUI.DrawRect karsiligi: origin-aware, sadece repaint'te cizer.
void imgui_draw_rect(imgui_rect rect, imgui_color color, int layer);
void imgui_draw_rect_rotated(imgui_rect rect, float rotation, imgui_color color, int layer);

void imgui_set_layout_rect(imgui_rect rect);
void imgui_begin_horizontal(void);
void imgui_end_horizontal(void);
void imgui_begin_vertical(void);
void imgui_end_vertical(void);
imgui_rect imgui_get_rect(float width, float height, const imgui_layout_option *options, int optionCount);
void imgui_space(float height);

// Unity GUIStyle.CalcSize / EditorGUIUtility.singleLineHeight karsiliklari.
void imgui_style_calc_size(const imgui_style *style, VmString *text, float *outWidth, float *outHeight);
float imgui_line_height(void);
float imgui_window_title_height(void);
float imgui_dock_tab_height(void);
float imgui_scrollbar_size(void);

// GUILayout tarzi icerik-olculu widget'lar: boyut CalcSize'dan gelir, options ezer.
bool imgui_layout_button(uint32_t hint, VmString *text, const imgui_layout_option *options, int optionCount);
void imgui_layout_label(VmString *text, const imgui_layout_option *options, int optionCount);
bool imgui_layout_toggle(uint32_t hint, VmString *text, bool *value, const imgui_layout_option *options, int optionCount);
bool imgui_begin_scroll_view(uint32_t hint, imgui_rect viewRect, float contentHeight, float *scrollY);
void imgui_end_scroll_view(void);
float imgui_scroll_offset_y(void);
bool imgui_begin_window(uint32_t id, const imgui_window_desc *desc);
void imgui_end_window(void);
imgui_rect imgui_window_content_rect(void);
bool imgui_register_panel(uint32_t id, VmString *title);
imgui_rect imgui_window_rect(uint32_t id);
bool imgui_register_panel_content(uint32_t id, imgui_panel_draw_fn draw, void *userData);
bool imgui_draw_panel(uint32_t id);
bool imgui_panel_draw_direct(uint32_t id, imgui_rect contentRect);
VmString *imgui_window_title(uint32_t id);
imgui_dock_node imgui_dock_begin(imgui_rect rect);
bool imgui_dock_split(imgui_dock_node node, bool vertical, float ratio,
                      imgui_dock_node *first, imgui_dock_node *second);
void imgui_dock_window(uint32_t windowId, imgui_dock_node node);
bool imgui_dock_add_tab(imgui_dock_node node, uint32_t windowId);
bool imgui_dock_select_tab(imgui_dock_node node, uint32_t windowId);
imgui_rect imgui_dock_node_rect(imgui_dock_node node);
imgui_dock_host *imgui_dock_host_create(void);
void imgui_dock_host_destroy(imgui_dock_host *host);
bool imgui_dock_host_begin(imgui_dock_host *host, imgui_rect rect);
void imgui_dock_host_end(void);
imgui_dock_node imgui_dock_host_root(imgui_dock_host *host, imgui_rect rect);
bool imgui_dock_host_split(imgui_dock_host *host, imgui_dock_node node, bool vertical, float ratio,
                           imgui_dock_node *first, imgui_dock_node *second);
bool imgui_dock_host_add_tab(imgui_dock_host *host, imgui_dock_node node, uint32_t windowId);
bool imgui_dock_host_select_tab(imgui_dock_host *host, imgui_dock_node node, uint32_t windowId);
void imgui_dock_host_dock_window(imgui_dock_host *host, uint32_t windowId, imgui_dock_node node);

#endif
