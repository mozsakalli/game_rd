#include "imgui.h"
#include "imgui_font.h"

#include <float.h>
#include <math.h>
#include <string.h>

extern void engine_draw_ui_rect(float x, float y, float width, float height,
                                unsigned char r, unsigned char g, unsigned char b, unsigned char a, int layer);
extern void engine_draw_ui_rect_rotated(float x, float y, float width, float height, float rotation,
                                        unsigned char r, unsigned char g, unsigned char b, unsigned char a, int layer);
extern void engine_push_scissor(int x, int y, int width, int height);
extern void engine_pop_scissor(void);

#define IMGUI_MAX_CONTROL_STATES 2048
#define IMGUI_CONTROL_EMPTY 0u
#define IMGUI_MAX_LAYOUT_ENTRIES 4096
#define IMGUI_MAX_LAYOUT_GROUPS 256
#define IMGUI_MAX_LAYOUT_DEPTH 64
#define IMGUI_MAX_WINDOWS 128
#define IMGUI_MAX_DOCK_NODES 128
#define IMGUI_MAX_DOCK_TABS 16

typedef struct
{
    imgui_rect rect;
    float minWidth;
    float maxWidth;
    float minHeight;
    float maxHeight;
    bool expandWidth;
    bool expandHeight;
    int ownerGroup;
} _imgui_layout_entry;

typedef struct
{
    imgui_rect rect;
    bool horizontal;
    float cursor;
    float contentWidth;
    float contentHeight;
    int parentGroup;
    int firstEntry;
    int endEntry;
} _imgui_layout_group;

typedef struct
{
    uint32_t id;
    VmString *title;
    imgui_panel_draw_fn draw;
    void *userData;
    imgui_rect rect;
    imgui_rect contentRect;
    float minWidth;
    float minHeight;
    bool floating;
    bool visible;
    imgui_dock_node dockNode;
} _imgui_window;

typedef struct
{
    imgui_dock_node_type type;
    imgui_rect rect;
    int parent;
    int firstChild;
    int secondChild;
    bool vertical;
    float ratio;
    uint32_t tabs[IMGUI_MAX_DOCK_TABS];
    int tabCount;
    int activeTab;
} _imgui_dock_node;

struct imgui_dock_host
{
    _imgui_dock_node nodes[IMGUI_MAX_DOCK_NODES];
    int nodeCount;
    imgui_dock_node root;
    imgui_rect rect;
};

struct imgui_state
{
    imgui_event event;
    bool hasEvent;
    float deltaTime;
    uint32_t hotControl;
    uint32_t keyboardControl;
    bool changed;
    bool enabled;
    imgui_control_state controls[IMGUI_MAX_CONTROL_STATES];
    imgui_skin skin;
    const imgui_skin *activeSkin;
    imgui_rect rootRect;
    _imgui_layout_entry entries[IMGUI_MAX_LAYOUT_ENTRIES];
    int entryCount;
    int replayEntry;
    _imgui_layout_group groups[IMGUI_MAX_LAYOUT_GROUPS];
    int groupCount;
    int replayGroup;
    int groupStack[IMGUI_MAX_LAYOUT_DEPTH];
    int groupDepth;
    float scrollOffsetY;
    int scrollDepth;
    float scrollOffsetStack[64];
    _imgui_window windows[IMGUI_MAX_WINDOWS];
    int windowCount;
    int windowStack[IMGUI_MAX_LAYOUT_DEPTH];
    int windowDepth;
    _imgui_window *activeWindow;
    float contentOriginX;
    float contentOriginY;
    float originStackX[IMGUI_MAX_LAYOUT_DEPTH]; // imgui_begin_group origin yigini
    float originStackY[IMGUI_MAX_LAYOUT_DEPTH];
    bool originClip[IMGUI_MAX_LAYOUT_DEPTH];
    int originDepth;
    const imgui_font *font;
    float fontPixelHeight;
    _imgui_dock_node dockNodes[IMGUI_MAX_DOCK_NODES];
    int dockNodeCount;
    imgui_dock_node dockRoot;
    imgui_dock_host *activeDockHost;
};

static struct imgui_state _imgui;

static float _imgui_metric(float value, float fallback)
{
    return value > 0.0f ? value : fallback;
}

static uint32_t _imgui_hash(uint32_t value)
{
    value ^= value >> 16;
    value *= 0x7feb352dU;
    value ^= value >> 15;
    value *= 0x846ca68bU;
    value ^= value >> 16;
    return value;
}

static void _imgui_resolve_group(int ownerGroup, imgui_rect rect, bool horizontal)
{
    float used = 0.0f;
    int expandCount = 0;
    for (int i = 0; i < _imgui.entryCount; ++i)
    {
        _imgui_layout_entry *entry = &_imgui.entries[i];
        if (entry->ownerGroup != ownerGroup)
            continue;
        float size = horizontal ? entry->rect.width : entry->rect.height;
        float minimum = horizontal ? entry->minWidth : entry->minHeight;
        float maximum = horizontal ? entry->maxWidth : entry->maxHeight;
        if (maximum < minimum)
            maximum = minimum;
        if (size < minimum)
            size = minimum;
        if (size > maximum)
            size = maximum;
        if (horizontal)
            entry->rect.width = size;
        else
            entry->rect.height = size;
        used += size;
        if (horizontal ? entry->expandWidth : entry->expandHeight)
            ++expandCount;
    }

    float available = horizontal ? rect.width : rect.height;
    float extra = available > used ? available - used : 0.0f;
    float cursor = horizontal ? rect.x : rect.y;
    for (int i = 0; i < _imgui.entryCount; ++i)
    {
        _imgui_layout_entry *entry = &_imgui.entries[i];
        if (entry->ownerGroup != ownerGroup)
            continue;
        bool expands = horizontal ? entry->expandWidth : entry->expandHeight;
        if (expands && expandCount > 0 && extra > 0.0f)
        {
            float maximum = horizontal ? entry->maxWidth : entry->maxHeight;
            float current = horizontal ? entry->rect.width : entry->rect.height;
            float addition = extra / (float)expandCount;
            if (maximum < FLT_MAX && current + addition > maximum)
                addition = maximum - current;
            if (addition < 0.0f)
                addition = 0.0f;
            if (horizontal)
                entry->rect.width += addition;
            else
                entry->rect.height += addition;
            extra -= addition;
            --expandCount;
        }
        if (horizontal)
        {
            entry->rect.x = cursor;
            entry->rect.y = rect.y;
            cursor += entry->rect.width;
        }
        else
        {
            entry->rect.x = rect.x;
            entry->rect.y = cursor;
            cursor += entry->rect.height;
        }
    }
}

static void _imgui_resolve_layout(void)
{
    _imgui_resolve_group(-1, _imgui.rootRect, false);
    for (int i = 0; i < _imgui.groupCount; ++i)
    {
        _imgui_layout_group *group = &_imgui.groups[i];
        _imgui_resolve_group(i, group->rect, group->horizontal);
    }
}

void imgui_begin_frame(float deltaTime)
{
    if (_imgui.dockNodeCount == 0)
        _imgui.dockRoot = -1;
    _imgui.deltaTime = deltaTime;
    _imgui.changed = false;
    _imgui.enabled = true;
    _imgui.hasEvent = false;
    if (!_imgui.activeSkin)
    {
        imgui_skin_init_default(&_imgui.skin);
        _imgui.activeSkin = &_imgui.skin;
    }
}

void imgui_end_frame(void)
{
    _imgui.hasEvent = false;
    _imgui.scrollOffsetY = 0.0f;
    _imgui.scrollDepth = 0;
}

void imgui_begin_event(const imgui_event *event)
{
    if (event)
    {
        _imgui.event = *event;
        _imgui.hasEvent = true;
        if (event->type == IMGUI_EVENT_LAYOUT)
        {
            _imgui.entryCount = 0;
            _imgui.groupCount = 0;
            _imgui.groupDepth = 0;
            _imgui.windowDepth = 0;
        }
        else
        {
            _imgui.replayEntry = 0;
            _imgui.replayGroup = 0;
            _imgui.groupDepth = 0;
            _imgui.windowDepth = 0;
        }
    }
    else
    {
        memset(&_imgui.event, 0, sizeof(_imgui.event));
        _imgui.event.type = IMGUI_EVENT_REPAINT;
        _imgui.hasEvent = true;
    }
}

void imgui_end_event(void)
{
    if (_imgui.hasEvent && _imgui.event.type == IMGUI_EVENT_LAYOUT)
        _imgui_resolve_layout();
    _imgui.hasEvent = false;
}

imgui_event_type imgui_event_type_current(void)
{
    return _imgui.hasEvent ? _imgui.event.type : IMGUI_EVENT_REPAINT;
}

const imgui_event *imgui_current_event(void)
{
    return _imgui.hasEvent ? &_imgui.event : NULL;
}

uint32_t imgui_get_control_id(uint32_t hint)
{
    uint32_t id = _imgui_hash(hint ? hint : 0x811c9dc5U);
    return id ? id : 1U;
}

imgui_control_state *imgui_get_control_state(uint32_t id)
{
    if (!id)
        return NULL;

    uint32_t slot = _imgui_hash(id) % IMGUI_MAX_CONTROL_STATES;
    for (uint32_t i = 0; i < IMGUI_MAX_CONTROL_STATES; ++i)
    {
        imgui_control_state *state = &_imgui.controls[(slot + i) % IMGUI_MAX_CONTROL_STATES];
        if (state->id == id)
            return state;
        if (state->id == IMGUI_CONTROL_EMPTY)
        {
            memset(state, 0, sizeof(*state));
            state->id = id;
            return state;
        }
    }
    return NULL;
}

uint32_t imgui_hot_control(void)
{
    return _imgui.hotControl;
}

void imgui_set_hot_control(uint32_t id)
{
    _imgui.hotControl = id;
}

uint32_t imgui_keyboard_control(void)
{
    return _imgui.keyboardControl;
}

void imgui_set_keyboard_control(uint32_t id)
{
    _imgui.keyboardControl = id;
}

bool imgui_changed(void)
{
    return _imgui.changed;
}

void imgui_set_changed(bool changed)
{
    _imgui.changed = changed;
}

bool imgui_enabled(void)
{
    return _imgui.enabled;
}

void imgui_set_enabled(bool enabled)
{
    _imgui.enabled = enabled;
}

bool imgui_rect_contains(imgui_rect rect, float x, float y)
{
    return x >= rect.x && y >= rect.y && x < rect.x + rect.width && y < rect.y + rect.height;
}

static imgui_style_state _imgui_style_state(imgui_color background, imgui_color text)
{
    imgui_style_state state = {0};
    state.backgroundColor = background;
    state.textColor = text;
    return state;
}

void imgui_skin_init_default(imgui_skin *skin)
{
    if (!skin)
        return;
    memset(skin, 0, sizeof(*skin));

    imgui_color transparent = {0, 0, 0, 0};
    imgui_color text = {216, 221, 228, 255};
    imgui_color textBright = {240, 243, 248, 255};
    imgui_color panel = {40, 44, 52, 255};
    imgui_color box = {33, 36, 43, 255};
    imgui_color button = {58, 64, 75, 255};
    imgui_color hover = {72, 80, 95, 255};
    imgui_color active = {53, 116, 240, 255};
    imgui_color focused = {74, 132, 244, 255};

    skin->label.normal = _imgui_style_state(transparent, text);
    skin->label.paddingLeft = 2;
    skin->label.paddingTop = 2;
    skin->box.normal = _imgui_style_state(box, text);
    skin->button.normal = _imgui_style_state(button, text);
    skin->button.hover = _imgui_style_state(hover, textBright);
    skin->button.active = _imgui_style_state(active, textBright);
    skin->button.focused = _imgui_style_state(focused, textBright);
    skin->button.paddingLeft = 10;
    skin->button.paddingRight = 10;
    skin->button.paddingTop = 3;
    skin->button.paddingBottom = 3;
    skin->toggle = skin->button;
    skin->horizontalSlider = skin->box;
    skin->horizontalSliderThumb = skin->button;
    skin->verticalSlider = skin->box;
    skin->verticalSliderThumb = skin->button;
    skin->window.normal = _imgui_style_state(panel, text);
    skin->scrollView.normal = _imgui_style_state(transparent, text);
    skin->textField = skin->button;
    skin->textArea = skin->button;
}

void imgui_set_skin(const imgui_skin *skin)
{
    if (skin)
        _imgui.activeSkin = skin;
}

const imgui_skin *imgui_skin_current(void)
{
    if (!_imgui.activeSkin)
    {
        imgui_skin_init_default(&_imgui.skin);
        _imgui.activeSkin = &_imgui.skin;
    }
    return _imgui.activeSkin;
}

const imgui_style_state *imgui_style_state_for(const imgui_style *style, uint32_t id, bool on)
{
    if (!style)
        return NULL;
    imgui_control_state *state = imgui_get_control_state(id);
    bool active = _imgui.hotControl == id;
    bool focused = _imgui.keyboardControl == id;
    if (on)
    {
        if (active)
            return &style->onActive;
        if (focused)
            return &style->onFocused;
        if (state && state->hovered)
            return &style->onHover;
        return &style->onNormal;
    }
    if (active)
        return &style->active;
    if (focused)
        return &style->focused;
    if (state && state->hovered)
        return &style->hover;
    return &style->normal;
}

static void _imgui_draw_style(imgui_rect rect, const imgui_style_state *state, int layer)
{
    if (_imgui.event.type != IMGUI_EVENT_REPAINT || !state || state->backgroundColor.a == 0)
        return;
    imgui_color color = state->backgroundColor;
    engine_draw_ui_rect(rect.x + _imgui.contentOriginX, rect.y + _imgui.contentOriginY,
                        rect.width, rect.height,
                        color.r, color.g, color.b, color.a, layer);
}

static void _imgui_draw_style_screen(imgui_rect rect, const imgui_style_state *state, int layer)
{
    if (_imgui.event.type != IMGUI_EVENT_REPAINT || !state || state->backgroundColor.a == 0)
        return;
    imgui_color color = state->backgroundColor;
    engine_draw_ui_rect(rect.x, rect.y, rect.width, rect.height,
                        color.r, color.g, color.b, color.a, layer);
}

static imgui_rect _imgui_screen_rect(imgui_rect rect)
{
    rect.x += _imgui.contentOriginX;
    rect.y += _imgui.contentOriginY;
    return rect;
}

// Unity GUI.BeginGroup: origin kaydirilir, istenirse scissor ile kirpilir.
// rect grup-lokal (mevcut origin'e gore) verilir; widget'lar ve event testleri
// otomatik olarak yeni lokal koordinatta calisir.
void imgui_begin_group(imgui_rect rect, bool clip)
{
    if (_imgui.originDepth >= IMGUI_MAX_LAYOUT_DEPTH)
        return;
    _imgui.originStackX[_imgui.originDepth] = _imgui.contentOriginX;
    _imgui.originStackY[_imgui.originDepth] = _imgui.contentOriginY;
    _imgui.originClip[_imgui.originDepth] = clip;
    _imgui.originDepth++;
    float screenX = _imgui.contentOriginX + rect.x;
    float screenY = _imgui.contentOriginY + rect.y;
    if (clip && _imgui.event.type == IMGUI_EVENT_REPAINT)
        engine_push_scissor((int)screenX, (int)screenY, (int)rect.width, (int)rect.height);
    _imgui.contentOriginX = screenX;
    _imgui.contentOriginY = screenY;
}

void imgui_end_group(void)
{
    if (_imgui.originDepth <= 0)
        return;
    _imgui.originDepth--;
    if (_imgui.originClip[_imgui.originDepth] && _imgui.event.type == IMGUI_EVENT_REPAINT)
        engine_pop_scissor();
    _imgui.contentOriginX = _imgui.originStackX[_imgui.originDepth];
    _imgui.contentOriginY = _imgui.originStackY[_imgui.originDepth];
}

float imgui_local_mouse_x(void)
{
    return _imgui.event.mouseX - _imgui.contentOriginX;
}

float imgui_local_mouse_y(void)
{
    return _imgui.event.mouseY - _imgui.contentOriginY;
}

void imgui_draw_rect(imgui_rect rect, imgui_color color, int layer)
{
    if (_imgui.event.type != IMGUI_EVENT_REPAINT || color.a == 0)
        return;
    engine_draw_ui_rect(rect.x + _imgui.contentOriginX, rect.y + _imgui.contentOriginY,
                        rect.width, rect.height, color.r, color.g, color.b, color.a, layer);
}

void imgui_draw_rect_rotated(imgui_rect rect, float rotation, imgui_color color, int layer)
{
    if (_imgui.event.type != IMGUI_EVENT_REPAINT || color.a == 0)
        return;
    engine_draw_ui_rect_rotated(rect.x + _imgui.contentOriginX, rect.y + _imgui.contentOriginY,
                                rect.width, rect.height, rotation,
                                color.r, color.g, color.b, color.a, layer);
}

extern float engine_host_content_scale(void);
void imgui_set_font(const imgui_font *font, float pixelHeight)
{
    _imgui.font = font;
    _imgui.fontPixelHeight = (pixelHeight > 0.0f ? pixelHeight : 32.0f) * engine_host_content_scale();
}

const imgui_font *imgui_font_current(void)
{
    return _imgui.font;
}

void imgui_label(VmString *text, imgui_rect rect, const imgui_style *style)
{
    const imgui_style *resolved = style ? style : &imgui_skin_current()->label;
    if (_imgui.event.type != IMGUI_EVENT_REPAINT || !_imgui.font || !text)
        return;
    rect = _imgui_screen_rect(rect);
    imgui_color color = resolved->normal.textColor;
    imgui_draw_text_at(_imgui.font, text, _imgui.fontPixelHeight,
                       rect.x + resolved->paddingLeft + resolved->contentOffsetX,
                       rect.y + resolved->paddingTop + resolved->contentOffsetY,
                       color, 1160);
}

bool imgui_button_text(uint32_t hint, VmString *text, imgui_rect rect, const imgui_style *style)
{
    const imgui_style *resolved = style ? style : &imgui_skin_current()->button;
    bool clicked = imgui_button(hint, rect, resolved);
    rect = _imgui_screen_rect(rect);
    if (_imgui.event.type == IMGUI_EVENT_REPAINT && _imgui.font && text)
    {
        imgui_control_state *state = imgui_get_control_state(imgui_get_control_id(hint));
        const imgui_style_state *stateStyle = imgui_style_state_for(resolved, state ? state->id : 0, false);
        imgui_draw_text_at(_imgui.font, text, _imgui.fontPixelHeight,
                           rect.x + resolved->paddingLeft + resolved->contentOffsetX,
                           rect.y + resolved->paddingTop + resolved->contentOffsetY,
                           stateStyle ? stateStyle->textColor : resolved->normal.textColor, 1170);
    }
    return clicked;
}

void imgui_box(uint32_t hint, imgui_rect rect, const imgui_style *style)
{
    uint32_t id = imgui_get_control_id(hint);
    const imgui_style *resolved = style ? style : &imgui_skin_current()->box;
    _imgui_draw_style(rect, imgui_style_state_for(resolved, id, false), 1000);
}

bool imgui_button(uint32_t hint, imgui_rect rect, const imgui_style *style)
{
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    if (!state)
        return false;

    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT &&
                   imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;
    state->active = _imgui.hotControl == id;
    state->focused = _imgui.keyboardControl == id;

    const imgui_style *resolved = style ? style : &imgui_skin_current()->button;
    _imgui_draw_style(rect, imgui_style_state_for(resolved, id, false), 1100);

    if (_imgui.enabled && event)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN && hovered && !event->used)
        {
            _imgui.hotControl = id;
            state->active = true;
            return false;
        }
        if (event->type == IMGUI_EVENT_MOUSE_UP && _imgui.hotControl == id)
        {
            bool clicked = hovered;
            _imgui.hotControl = 0;
            state->active = false;
            if (clicked)
            {
                _imgui.changed = true;
                state->changed = true;
            }
            return clicked;
        }
    }

    return false;
}

bool imgui_toggle(uint32_t hint, imgui_rect rect, bool *value, const imgui_style *style)
{
    if (!value)
        return false;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    bool changedThisEvent = false;
    if (!state)
        return false;

    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT &&
                   imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;
    state->active = _imgui.hotControl == id;
    state->focused = _imgui.keyboardControl == id;

    if (_imgui.enabled && event)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN && hovered && !event->used)
        {
            _imgui.hotControl = id;
            state->active = true;
            state->changed = false;
        }
        else if (event->type == IMGUI_EVENT_MOUSE_UP && _imgui.hotControl == id)
        {
            bool clicked = hovered;
            _imgui.hotControl = 0;
            state->active = false;
            if (clicked)
            {
                *value = !*value;
                state->changed = true;
                changedThisEvent = true;
                _imgui.changed = true;
            }
        }
    }

    const imgui_style *resolved = style ? style : &imgui_skin_current()->toggle;
    _imgui_draw_style(rect, imgui_style_state_for(resolved, id, *value), 1120);
    return changedThisEvent;
}

// Unity GUI.Toggle(text, toolbarButton) karsiligi: on/off stilli text'li toggle.
bool imgui_toggle_text(uint32_t hint, VmString *text, imgui_rect rect, bool *value, const imgui_style *style)
{
    const imgui_style *resolved = style ? style : &imgui_skin_current()->toggle;
    bool changed = imgui_toggle(hint, rect, value, resolved);
    if (_imgui.event.type == IMGUI_EVENT_REPAINT && _imgui.font && text && value)
    {
        imgui_rect screen = _imgui_screen_rect(rect);
        uint32_t id = imgui_get_control_id(hint);
        const imgui_style_state *stateStyle = imgui_style_state_for(resolved, id, *value);
        imgui_draw_text_at(_imgui.font, text, _imgui.fontPixelHeight,
                           screen.x + resolved->paddingLeft + resolved->contentOffsetX,
                           screen.y + resolved->paddingTop + resolved->contentOffsetY,
                           stateStyle ? stateStyle->textColor : resolved->normal.textColor, 1170);
    }
    return changed;
}

static float _imgui_clamp01(float value)
{
    return value < 0.0f ? 0.0f : value > 1.0f ? 1.0f
                                              : value;
}

static float _imgui_slider_value(float position, float start, float end)
{
    if (end == start)
        return start;
    return start + _imgui_clamp01(position) * (end - start);
}

float imgui_horizontal_slider(uint32_t hint, imgui_rect rect, float *value, float leftValue, float rightValue,
                              const imgui_style *style, const imgui_style *thumbStyle)
{
    if (!value)
        return leftValue;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    if (!state)
        return *value;
    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT && imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;

    if (_imgui.enabled && event)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN && hovered && !event->used)
            _imgui.hotControl = id;
        if ((event->type == IMGUI_EVENT_MOUSE_DOWN || event->type == IMGUI_EVENT_MOUSE_DRAG) && _imgui.hotControl == id)
            *value = _imgui_slider_value((event->mouseX - rect.x) / rect.width, leftValue, rightValue);
        if (event->type == IMGUI_EVENT_MOUSE_UP && _imgui.hotControl == id)
        {
            if (hovered)
                *value = _imgui_slider_value((event->mouseX - rect.x) / rect.width, leftValue, rightValue);
            _imgui.hotControl = 0;
        }
    }

    float normalized = (rightValue == leftValue) ? 0.0f : _imgui_clamp01((*value - leftValue) / (rightValue - leftValue));
    const imgui_style *trackStyle = style ? style : &imgui_skin_current()->horizontalSlider;
    const imgui_style *resolvedThumb = thumbStyle ? thumbStyle : &imgui_skin_current()->horizontalSliderThumb;
    _imgui_draw_style(rect, imgui_style_state_for(trackStyle, id, false), 1140);
    imgui_rect thumb = {rect.x + normalized * rect.width - rect.height * 0.5f, rect.y, rect.height, rect.height};
    _imgui_draw_style(thumb, imgui_style_state_for(resolvedThumb, id, false), 1141);
    return *value;
}

float imgui_vertical_slider(uint32_t hint, imgui_rect rect, float *value, float topValue, float bottomValue,
                            const imgui_style *style, const imgui_style *thumbStyle)
{
    if (!value)
        return topValue;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    if (!state)
        return *value;
    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT && imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;
    if (_imgui.enabled && event)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN && hovered && !event->used)
            _imgui.hotControl = id;
        if ((event->type == IMGUI_EVENT_MOUSE_DOWN || event->type == IMGUI_EVENT_MOUSE_DRAG) && _imgui.hotControl == id)
            *value = _imgui_slider_value((event->mouseY - rect.y) / rect.height, topValue, bottomValue);
        if (event->type == IMGUI_EVENT_MOUSE_UP && _imgui.hotControl == id)
        {
            if (hovered)
                *value = _imgui_slider_value((event->mouseY - rect.y) / rect.height, topValue, bottomValue);
            _imgui.hotControl = 0;
        }
    }
    float normalized = (bottomValue == topValue) ? 0.0f : _imgui_clamp01((*value - topValue) / (bottomValue - topValue));
    const imgui_style *trackStyle = style ? style : &imgui_skin_current()->verticalSlider;
    const imgui_style *resolvedThumb = thumbStyle ? thumbStyle : &imgui_skin_current()->verticalSliderThumb;
    _imgui_draw_style(rect, imgui_style_state_for(trackStyle, id, false), 1140);
    imgui_rect thumb = {rect.x, rect.y + normalized * rect.height - rect.width * 0.5f, rect.width, rect.width};
    _imgui_draw_style(thumb, imgui_style_state_for(resolvedThumb, id, false), 1141);
    return *value;
}

// Unity GUI.Horizontal/VerticalScrollbar karsiligi: orantili thumb, surukleme destekli.
static float _imgui_scrollbar(uint32_t hint, imgui_rect rect, float *value, float viewSize, float contentSize,
                              const imgui_style *style, const imgui_style *thumbStyle, bool vertical)
{
    if (!value)
        return 0.0f;
    float maxScroll = contentSize > viewSize ? contentSize - viewSize : 0.0f;
    if (*value < 0.0f)
        *value = 0.0f;
    if (*value > maxScroll)
        *value = maxScroll;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    if (!state)
        return *value;
    float track = vertical ? rect.height : rect.width;
    float minThumb = _imgui_metric(imgui_skin_current()->metrics.minThumbSize, 16.0f);
    float thumbLen = (contentSize > 0.0f && maxScroll > 0.0f) ? track * (viewSize / contentSize) : track;
    if (thumbLen < minThumb)
        thumbLen = minThumb;
    if (thumbLen > track)
        thumbLen = track;
    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT &&
                   imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;

    if (_imgui.enabled && event && maxScroll > 0.0f && track > thumbLen)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN && hovered && !event->used)
            _imgui.hotControl = id;
        if ((event->type == IMGUI_EVENT_MOUSE_DOWN || event->type == IMGUI_EVENT_MOUSE_DRAG) && _imgui.hotControl == id)
        {
            float mouse = vertical ? event->mouseY - rect.y : event->mouseX - rect.x;
            *value = _imgui_clamp01((mouse - thumbLen * 0.5f) / (track - thumbLen)) * maxScroll;
            _imgui.changed = true;
        }
        if (event->type == IMGUI_EVENT_MOUSE_UP && _imgui.hotControl == id)
            _imgui.hotControl = 0;
    }

    float normalized = maxScroll > 0.0f ? *value / maxScroll : 0.0f;
    const imgui_style *trackStyle = style ? style : (vertical ? &imgui_skin_current()->verticalSlider : &imgui_skin_current()->horizontalSlider);
    const imgui_style *resolvedThumb = thumbStyle ? thumbStyle : (vertical ? &imgui_skin_current()->verticalSliderThumb : &imgui_skin_current()->horizontalSliderThumb);
    _imgui_draw_style_screen(rect, imgui_style_state_for(trackStyle, id, false), 1140);
    imgui_rect thumb = vertical
                           ? (imgui_rect){rect.x + 1.0f, rect.y + normalized * (track - thumbLen), rect.width - 2.0f, thumbLen}
                           : (imgui_rect){rect.x + normalized * (track - thumbLen), rect.y + 1.0f, thumbLen, rect.height - 2.0f};
    _imgui_draw_style_screen(thumb, imgui_style_state_for(resolvedThumb, id, false), 1141);
    return *value;
}

float imgui_horizontal_scrollbar(uint32_t hint, imgui_rect rect, float *value, float viewSize, float contentSize,
                                 const imgui_style *style, const imgui_style *thumbStyle)
{
    return _imgui_scrollbar(hint, rect, value, viewSize, contentSize, style, thumbStyle, false);
}

float imgui_vertical_scrollbar(uint32_t hint, imgui_rect rect, float *value, float viewSize, float contentSize,
                               const imgui_style *style, const imgui_style *thumbStyle)
{
    return _imgui_scrollbar(hint, rect, value, viewSize, contentSize, style, thumbStyle, true);
}

// buffer'in ilk `count` kod biriminin piksel genisligi (stack VmString view, alloc yok).
static float _imgui_prefix_width(const unsigned short *buffer, int count)
{
    if (!_imgui.font || count <= 0)
        return 0.0f;
    VmString view = {0};
    view.length = count;
    view.data = buffer;
    float width = 0.0f, height = 0.0f;
    imgui_text_size_at(_imgui.font, &view, _imgui.fontPixelHeight, &width, &height);
    return width;
}

bool imgui_text_field(uint32_t hint, imgui_rect rect, unsigned short *buffer, int capacity, int *length,
                      const imgui_style *style)
{
    if (!buffer || !length || capacity <= 0)
        return false;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    rect = _imgui_screen_rect(rect);
    if (!state)
        return false;
    if (*length < 0)
        *length = 0;
    if (*length > capacity)
        *length = capacity;
    if (state->caret > *length)
        state->caret = *length;
    bool focused = _imgui.keyboardControl == id;
    bool hovered = event && event->type != IMGUI_EVENT_LAYOUT &&
                   imgui_rect_contains(rect, event->mouseX, event->mouseY);
    state->hovered = hovered;
    state->focused = focused;
    bool changed = false;
    const imgui_style *resolved = style ? style : &imgui_skin_current()->textField;
    float textX = rect.x + resolved->paddingLeft + resolved->contentOffsetX;

    if (_imgui.enabled && event)
    {
        if (event->type == IMGUI_EVENT_MOUSE_DOWN)
        {
            if (hovered)
            {
                _imgui.keyboardControl = id;
                focused = true;
                int caret = *length;
                for (int i = 1; i <= *length; ++i)
                {
                    if (textX + _imgui_prefix_width(buffer, i) > event->mouseX)
                    {
                        caret = i - 1;
                        break;
                    }
                }
                state->caret = caret;
            }
            else if (focused)
            {
                _imgui.keyboardControl = 0;
                focused = false;
            }
        }
        else if (focused && event->type == IMGUI_EVENT_TEXT_INPUT)
        {
            unsigned int ch = event->character;
            if (ch >= 32 && ch != 127 && ch <= 0xFFFF && *length < capacity)
            {
                for (int i = *length; i > state->caret; --i)
                    buffer[i] = buffer[i - 1];
                buffer[state->caret] = (unsigned short)ch;
                ++*length;
                ++state->caret;
                changed = true;
            }
        }
        else if (focused && event->type == IMGUI_EVENT_KEY_DOWN)
        {
            switch (event->key)
            {
            case 259: // backspace
                if (state->caret > 0)
                {
                    for (int i = state->caret - 1; i < *length - 1; ++i)
                        buffer[i] = buffer[i + 1];
                    --*length;
                    --state->caret;
                    changed = true;
                }
                break;
            case 261: // delete
                if (state->caret < *length)
                {
                    for (int i = state->caret; i < *length - 1; ++i)
                        buffer[i] = buffer[i + 1];
                    --*length;
                    changed = true;
                }
                break;
            case 263: // left
                if (state->caret > 0)
                    --state->caret;
                break;
            case 262: // right
                if (state->caret < *length)
                    ++state->caret;
                break;
            case 268: // home
                state->caret = 0;
                break;
            case 269: // end
                state->caret = *length;
                break;
            case 257: // enter
            case 335: // keypad enter
            case 256: // escape
                _imgui.keyboardControl = 0;
                focused = false;
                break;
            }
        }
        if (changed)
        {
            _imgui.changed = true;
            state->changed = true;
        }
    }

    _imgui_draw_style_screen(rect, imgui_style_state_for(resolved, id, false), 1150);
    if (_imgui.event.type == IMGUI_EVENT_REPAINT && _imgui.font)
    {
        const imgui_style_state *stateStyle = imgui_style_state_for(resolved, id, false);
        imgui_color textColor = stateStyle ? stateStyle->textColor : resolved->normal.textColor;
        if (*length > 0)
        {
            VmString view = {0};
            view.length = *length;
            view.data = buffer;
            imgui_draw_text_at(_imgui.font, &view, _imgui.fontPixelHeight,
                               textX, rect.y + resolved->paddingTop + resolved->contentOffsetY,
                               textColor, 1160);
        }
        if (focused)
            engine_draw_ui_rect(textX + _imgui_prefix_width(buffer, state->caret), rect.y + 4.0f,
                                1.5f, rect.height - 8.0f,
                                textColor.r, textColor.g, textColor.b, 255, 1165);
    }
    return changed;
}

void imgui_set_layout_rect(imgui_rect rect)
{
    _imgui.rootRect = rect;
}

static void _imgui_apply_options(_imgui_layout_entry *entry, const imgui_layout_option *options, int optionCount)
{
    for (int i = 0; i < optionCount; ++i)
    {
        const imgui_layout_option *option = &options[i];
        switch (option->type)
        {
        case IMGUI_LAYOUT_WIDTH:
            entry->minWidth = entry->maxWidth = option->value;
            break;
        case IMGUI_LAYOUT_HEIGHT:
            entry->minHeight = entry->maxHeight = option->value;
            break;
        case IMGUI_LAYOUT_MIN_WIDTH:
            entry->minWidth = option->value;
            break;
        case IMGUI_LAYOUT_MAX_WIDTH:
            entry->maxWidth = option->value;
            break;
        case IMGUI_LAYOUT_MIN_HEIGHT:
            entry->minHeight = option->value;
            break;
        case IMGUI_LAYOUT_MAX_HEIGHT:
            entry->maxHeight = option->value;
            break;
        case IMGUI_LAYOUT_EXPAND_WIDTH:
            entry->expandWidth = option->value != 0.0f;
            break;
        case IMGUI_LAYOUT_EXPAND_HEIGHT:
            entry->expandHeight = option->value != 0.0f;
            break;
        }
    }
}

static imgui_rect _imgui_allocate_rect(float width, float height, const imgui_layout_option *options, int optionCount)
{
    _imgui_layout_entry entry = {0};
    entry.minWidth = width;
    entry.maxWidth = FLT_MAX;
    entry.minHeight = height;
    entry.maxHeight = FLT_MAX;
    _imgui_apply_options(&entry, options, optionCount);

    if (_imgui.event.type == IMGUI_EVENT_LAYOUT)
    {
        int groupIndex = _imgui.groupDepth > 0 ? _imgui.groupStack[_imgui.groupDepth - 1] : -1;
        _imgui_layout_group *group = groupIndex >= 0 ? &_imgui.groups[groupIndex] : NULL;
        float x = group ? group->rect.x : _imgui.rootRect.x;
        float y = group ? group->rect.y : _imgui.rootRect.y;
        if (group)
        {
            if (group->horizontal)
                x += group->cursor;
            else
                y += group->cursor;
        }
        entry.rect = (imgui_rect){x, y - _imgui.scrollOffsetY, entry.minWidth, entry.minHeight};
        entry.ownerGroup = groupIndex;
        if (_imgui.entryCount >= IMGUI_MAX_LAYOUT_ENTRIES)
            return entry.rect;
        _imgui.entries[_imgui.entryCount++] = entry;
        if (group)
        {
            if (group->horizontal)
            {
                group->cursor += entry.rect.width;
                group->contentWidth = group->cursor;
                if (entry.rect.height > group->contentHeight)
                    group->contentHeight = entry.rect.height;
            }
            else
            {
                group->cursor += entry.rect.height;
                group->contentHeight = group->cursor;
                if (entry.rect.width > group->contentWidth)
                    group->contentWidth = entry.rect.width;
            }
        }
        return entry.rect;
    }

    if (_imgui.replayEntry >= _imgui.entryCount)
        return (imgui_rect){0, 0, 0, 0};
    return _imgui.entries[_imgui.replayEntry++].rect;
}

static void _imgui_begin_group(bool horizontal)
{
    if (_imgui.groupDepth >= IMGUI_MAX_LAYOUT_DEPTH)
        return;
    if (_imgui.event.type == IMGUI_EVENT_LAYOUT)
    {
        if (_imgui.groupCount >= IMGUI_MAX_LAYOUT_GROUPS)
            return;
        int parentIndex = _imgui.groupDepth > 0 ? _imgui.groupStack[_imgui.groupDepth - 1] : -1;
        _imgui_layout_group group = {0};
        group.horizontal = horizontal;
        group.rect = parentIndex >= 0 ? _imgui.groups[parentIndex].rect : _imgui.rootRect;
        group.cursor = 0.0f;
        group.parentGroup = parentIndex;
        group.firstEntry = _imgui.entryCount;
        group.endEntry = group.firstEntry;
        int index = _imgui.groupCount++;
        _imgui.groups[index] = group;
        _imgui.groupStack[_imgui.groupDepth++] = index;
    }
    else
    {
        if (_imgui.replayGroup >= _imgui.groupCount)
            return;
        _imgui.groupStack[_imgui.groupDepth++] = _imgui.replayGroup++;
    }
}

static void _imgui_end_group(void)
{
    if (_imgui.groupDepth <= 0)
        return;
    int groupIndex = _imgui.groupStack[--_imgui.groupDepth];
    if (_imgui.event.type == IMGUI_EVENT_LAYOUT)
    {
        _imgui_layout_group *group = &_imgui.groups[groupIndex];
        group->endEntry = _imgui.entryCount;
        if (_imgui.groupDepth == 0)
            return;
        int parentIndex = _imgui.groupStack[_imgui.groupDepth - 1];
        _imgui_layout_group *parent = &_imgui.groups[parentIndex];
        float extent = group->horizontal ? group->contentWidth : group->contentHeight;
        parent->cursor += extent;
    }
}

void imgui_begin_horizontal(void) { _imgui_begin_group(true); }
void imgui_end_horizontal(void) { _imgui_end_group(); }
void imgui_begin_vertical(void) { _imgui_begin_group(false); }
void imgui_end_vertical(void) { _imgui_end_group(); }

// GUIStyle.CalcSize: text olcusu + padding; fixedWidth/fixedHeight ezer.
void imgui_style_calc_size(const imgui_style *style, VmString *text, float *outWidth, float *outHeight)
{
    const imgui_style *resolved = style ? style : &imgui_skin_current()->label;
    float width = 0.0f, height = _imgui.fontPixelHeight;
    if (_imgui.font && text)
    {
        imgui_text_size_at(_imgui.font, text, _imgui.fontPixelHeight, &width, &height);
        if (height < _imgui.fontPixelHeight)
            height = _imgui.fontPixelHeight;
    }
    width += (float)(resolved->paddingLeft + resolved->paddingRight);
    height += (float)(resolved->paddingTop + resolved->paddingBottom);
    if (resolved->fixedWidth > 0.0f)
        width = resolved->fixedWidth;
    if (resolved->fixedHeight > 0.0f)
        height = resolved->fixedHeight;
    if (outWidth)
        *outWidth = width;
    if (outHeight)
        *outHeight = height;
}

// EditorGUIUtility.singleLineHeight karsiligi: aktif font boyutundan turetilir.
float imgui_line_height(void)
{
    return _imgui.fontPixelHeight + 6.0f;
}

float imgui_window_title_height(void)
{
    return _imgui_metric(imgui_skin_current()->metrics.windowTitleHeight, imgui_line_height());
}

float imgui_dock_tab_height(void)
{
    return _imgui_metric(imgui_skin_current()->metrics.dockTabHeight, imgui_window_title_height() - 2.0f);
}

float imgui_scrollbar_size(void)
{
    return _imgui_metric(imgui_skin_current()->metrics.scrollbarSize, floorf(imgui_line_height() * 0.6f));
}

bool imgui_layout_button(uint32_t hint, VmString *text, const imgui_layout_option *options, int optionCount)
{
    float width = 0.0f, height = 0.0f;
    imgui_style_calc_size(&imgui_skin_current()->button, text, &width, &height);
    imgui_rect rect = imgui_get_rect(width, height, options, optionCount);
    return imgui_button_text(hint, text, rect, NULL);
}

void imgui_layout_label(VmString *text, const imgui_layout_option *options, int optionCount)
{
    float width = 0.0f, height = 0.0f;
    imgui_style_calc_size(&imgui_skin_current()->label, text, &width, &height);
    imgui_rect rect = imgui_get_rect(width, height, options, optionCount);
    imgui_label(text, rect, NULL);
}

bool imgui_layout_toggle(uint32_t hint, VmString *text, bool *value, const imgui_layout_option *options, int optionCount)
{
    float width = 0.0f, height = 0.0f;
    imgui_style_calc_size(&imgui_skin_current()->toggle, text, &width, &height);
    imgui_rect rect = imgui_get_rect(width, height, options, optionCount);
    return imgui_toggle_text(hint, text, rect, value, NULL);
}

imgui_rect imgui_get_rect(float width, float height, const imgui_layout_option *options, int optionCount)
{
    if (optionCount < 0)
        optionCount = 0;
    if (optionCount > 0 && !options)
        optionCount = 0;
    return _imgui_allocate_rect(width, height, options, optionCount);
}

void imgui_space(float height)
{
    (void)imgui_get_rect(0.0f, height, NULL, 0);
}

bool imgui_begin_scroll_view(uint32_t hint, imgui_rect viewRect, float contentHeight, float *scrollY)
{
    if (!scrollY || _imgui.scrollDepth >= 64)
        return false;
    uint32_t id = imgui_get_control_id(hint);
    imgui_control_state *state = imgui_get_control_state(id);
    const imgui_event *event = imgui_current_event();
    imgui_rect screenViewRect = _imgui_screen_rect(viewRect);
    if (!state)
        return false;

    float maxScroll = contentHeight > viewRect.height ? contentHeight - viewRect.height : 0.0f;
    if (_imgui.enabled && event && event->type == IMGUI_EVENT_SCROLL_WHEEL &&
        imgui_rect_contains(screenViewRect, event->mouseX, event->mouseY))
    {
        *scrollY -= event->scrollY * 10.0f;
        if (*scrollY < 0.0f)
            *scrollY = 0.0f;
        if (*scrollY > maxScroll)
            *scrollY = maxScroll;
    }

    if (_imgui.scrollDepth < 64)
        _imgui.scrollOffsetStack[_imgui.scrollDepth] = _imgui.scrollOffsetY;
    _imgui.scrollOffsetY += *scrollY;
    _imgui.scrollDepth++;
    if (event && event->type == IMGUI_EVENT_REPAINT)
        engine_push_scissor((int)screenViewRect.x, (int)screenViewRect.y,
                            (int)screenViewRect.width, (int)screenViewRect.height);
    return true;
}

void imgui_end_scroll_view(void)
{
    if (_imgui.scrollDepth <= 0)
        return;
    const imgui_event *event = imgui_current_event();
    if (event && event->type == IMGUI_EVENT_REPAINT)
        engine_pop_scissor();
    _imgui.scrollOffsetY = _imgui.scrollDepth > 0 ? _imgui.scrollOffsetStack[_imgui.scrollDepth - 1] : 0.0f;
    _imgui.scrollDepth--;
}

float imgui_scroll_offset_y(void)
{
    return _imgui.scrollOffsetY;
}

static _imgui_window *_imgui_find_window(uint32_t id, const imgui_window_desc *desc)
{
    for (int i = 0; i < _imgui.windowCount; ++i)
    {
        if (_imgui.windows[i].id == id)
            return &_imgui.windows[i];
    }
    if (_imgui.windowCount >= IMGUI_MAX_WINDOWS)
        return NULL;
    _imgui_window *window = &_imgui.windows[_imgui.windowCount++];
    memset(window, 0, sizeof(*window));
    window->id = id;
    window->dockNode = -1;
    if (desc)
    {
        window->title = desc->title;
        window->rect = desc->rect;
        window->minWidth = desc->minWidth;
        window->minHeight = desc->minHeight;
        window->floating = desc->floating;
        window->visible = desc->visible;
    }
    else
    {
        window->rect = (imgui_rect){0, 0, 320, 240};
        window->visible = true;
    }
    return window;
}

bool imgui_register_panel(uint32_t id, VmString *title)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    if (!window)
        return false;
    if (title)
        window->title = title;
    return true;
}

bool imgui_register_panel_content(uint32_t id, imgui_panel_draw_fn draw, void *userData)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    if (!window)
        return false;
    window->draw = draw;
    window->userData = userData;
    return true;
}

bool imgui_draw_panel(uint32_t id)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    if (!window || !window->draw || !window->visible)
        return false;
    if (_imgui.activeWindow != window)
        return false;
    window->draw(window->contentRect, window->userData);
    return true;
}

// Detached pencereler: aktif window guard'i olmadan panel icerigini cizer.
bool imgui_panel_draw_direct(uint32_t id, imgui_rect contentRect)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    if (!window || !window->draw)
        return false;
    window->draw(contentRect, window->userData);
    return true;
}

static void _imgui_resolve_dock_node(imgui_dock_node node, imgui_rect rect)
{
    if (node < 0 || node >= _imgui.dockNodeCount)
        return;
    _imgui_dock_node *dock = &_imgui.dockNodes[node];
    dock->rect = rect;
    if (dock->type != IMGUI_DOCK_SPLIT)
        return;

    float ratio = dock->ratio < 0.1f ? 0.1f : dock->ratio > 0.9f ? 0.9f
                                                                 : dock->ratio;
    imgui_rect first = rect;
    imgui_rect second = rect;
    if (dock->vertical)
    {
        first.width = rect.width * ratio;
        second.x = first.x + first.width;
        second.width = rect.width - first.width;
    }
    else
    {
        first.height = rect.height * ratio;
        second.y = first.y + first.height;
        second.height = rect.height - first.height;
    }
    _imgui_resolve_dock_node(dock->firstChild, first);
    _imgui_resolve_dock_node(dock->secondChild, second);
}

imgui_dock_node imgui_dock_begin(imgui_rect rect)
{
    if (_imgui.dockRoot < 0)
    {
        if (_imgui.dockNodeCount >= IMGUI_MAX_DOCK_NODES)
            return -1;
        _imgui.dockRoot = _imgui.dockNodeCount++;
        _imgui.dockNodes[_imgui.dockRoot] = (_imgui_dock_node){0};
        _imgui.dockNodes[_imgui.dockRoot].type = IMGUI_DOCK_LEAF;
        _imgui.dockNodes[_imgui.dockRoot].rect = rect;
        _imgui.dockNodes[_imgui.dockRoot].parent = -1;
        _imgui.dockNodes[_imgui.dockRoot].firstChild = -1;
        _imgui.dockNodes[_imgui.dockRoot].secondChild = -1;
        _imgui.dockNodes[_imgui.dockRoot].ratio = 0.5f;
    }
    _imgui_resolve_dock_node(_imgui.dockRoot, rect);
    return _imgui.dockRoot;
}

bool imgui_dock_split(imgui_dock_node node, bool vertical, float ratio,
                      imgui_dock_node *first, imgui_dock_node *second)
{
    if (node < 0 || node >= _imgui.dockNodeCount || _imgui.dockNodeCount + 2 > IMGUI_MAX_DOCK_NODES)
        return false;
    _imgui_dock_node *parent = &_imgui.dockNodes[node];
    if (parent->type == IMGUI_DOCK_SPLIT)
        return false;
    int firstIndex = _imgui.dockNodeCount++;
    int secondIndex = _imgui.dockNodeCount++;
    _imgui.dockNodes[firstIndex] = (_imgui_dock_node){0};
    _imgui.dockNodes[firstIndex].type = IMGUI_DOCK_LEAF;
    _imgui.dockNodes[firstIndex].rect = parent->rect;
    _imgui.dockNodes[firstIndex].parent = node;
    _imgui.dockNodes[firstIndex].firstChild = -1;
    _imgui.dockNodes[firstIndex].secondChild = -1;
    _imgui.dockNodes[firstIndex].ratio = 0.5f;
    _imgui.dockNodes[secondIndex] = (_imgui_dock_node){0};
    _imgui.dockNodes[secondIndex].type = IMGUI_DOCK_LEAF;
    _imgui.dockNodes[secondIndex].rect = parent->rect;
    _imgui.dockNodes[secondIndex].parent = node;
    _imgui.dockNodes[secondIndex].firstChild = -1;
    _imgui.dockNodes[secondIndex].secondChild = -1;
    _imgui.dockNodes[secondIndex].ratio = 0.5f;
    parent->type = IMGUI_DOCK_SPLIT;
    parent->vertical = vertical;
    parent->ratio = ratio;
    parent->firstChild = firstIndex;
    parent->secondChild = secondIndex;
    for (int i = 0; i < _imgui.windowCount; ++i)
    {
        if (_imgui.windows[i].dockNode == node)
            _imgui.windows[i].dockNode = firstIndex;
    }
    _imgui_resolve_dock_node(_imgui.dockRoot, _imgui.dockNodes[_imgui.dockRoot].rect);
    if (first)
        *first = firstIndex;
    if (second)
        *second = secondIndex;
    return true;
}

void imgui_dock_window(uint32_t windowId, imgui_dock_node node)
{
    _imgui_window *window = _imgui_find_window(windowId, NULL);
    if (!window || node < 0 || node >= _imgui.dockNodeCount)
        return;
    window->dockNode = node;
    window->floating = false;
}

bool imgui_dock_add_tab(imgui_dock_node node, uint32_t windowId)
{
    if (node < 0 || node >= _imgui.dockNodeCount)
        return false;
    _imgui_dock_node *dock = &_imgui.dockNodes[node];
    if (dock->type == IMGUI_DOCK_SPLIT || dock->tabCount >= IMGUI_MAX_DOCK_TABS)
        return false;
    for (int i = 0; i < dock->tabCount; ++i)
    {
        if (dock->tabs[i] == windowId)
            return true;
    }
    _imgui_window *window = _imgui_find_window(windowId, NULL);
    if (!window)
        return false;
    dock->type = IMGUI_DOCK_TABS;
    dock->tabs[dock->tabCount++] = windowId;
    if (dock->tabCount == 1)
        dock->activeTab = 0;
    window->dockNode = node;
    window->floating = false;
    return true;
}

bool imgui_dock_select_tab(imgui_dock_node node, uint32_t windowId)
{
    if (node < 0 || node >= _imgui.dockNodeCount)
        return false;
    _imgui_dock_node *dock = &_imgui.dockNodes[node];
    for (int i = 0; i < dock->tabCount; ++i)
    {
        if (dock->tabs[i] == windowId)
        {
            dock->activeTab = i;
            return true;
        }
    }
    return false;
}

imgui_rect imgui_dock_node_rect(imgui_dock_node node)
{
    if (node < 0 || node >= _imgui.dockNodeCount)
        return (imgui_rect){0, 0, 0, 0};
    return _imgui.dockNodes[node].rect;
}

imgui_dock_host *imgui_dock_host_create(void)
{
    imgui_dock_host *host = (imgui_dock_host *)calloc(1, sizeof(*host));
    if (host)
        host->root = -1;
    return host;
}

void imgui_dock_host_destroy(imgui_dock_host *host)
{
    free(host);
}

bool imgui_dock_host_begin(imgui_dock_host *host, imgui_rect rect)
{
    if (!host)
        return false;
    if (_imgui.activeDockHost)
        imgui_dock_host_end();
    memcpy(_imgui.dockNodes, host->nodes, sizeof(host->nodes));
    _imgui.dockNodeCount = host->nodeCount;
    _imgui.dockRoot = host->root;
    if (rect.width > 0.0f && rect.height > 0.0f)
        host->rect = rect;
    else
        rect = host->rect;
    _imgui.activeDockHost = host;
    if (_imgui.dockRoot >= 0)
        _imgui_resolve_dock_node(_imgui.dockRoot, rect);
    return true;
}

void imgui_dock_host_end(void)
{
    if (!_imgui.activeDockHost)
        return;
    memcpy(_imgui.activeDockHost->nodes, _imgui.dockNodes, sizeof(_imgui.dockNodes));
    _imgui.activeDockHost->nodeCount = _imgui.dockNodeCount;
    _imgui.activeDockHost->root = _imgui.dockRoot;
    _imgui.activeDockHost = NULL;
}

imgui_dock_node imgui_dock_host_root(imgui_dock_host *host, imgui_rect rect)
{
    if (!host || !imgui_dock_host_begin(host, rect))
        return -1;
    imgui_dock_node root = imgui_dock_begin(rect);
    imgui_dock_host_end();
    return root;
}

bool imgui_dock_host_split(imgui_dock_host *host, imgui_dock_node node, bool vertical, float ratio,
                           imgui_dock_node *first, imgui_dock_node *second)
{
    if (!host || !imgui_dock_host_begin(host, host->rect))
        return false;
    bool result = imgui_dock_split(node, vertical, ratio, first, second);
    imgui_dock_host_end();
    return result;
}

bool imgui_dock_host_add_tab(imgui_dock_host *host, imgui_dock_node node, uint32_t windowId)
{
    if (!host || !imgui_dock_host_begin(host, host->rect))
        return false;
    bool result = imgui_dock_add_tab(node, windowId);
    imgui_dock_host_end();
    return result;
}

bool imgui_dock_host_select_tab(imgui_dock_host *host, imgui_dock_node node, uint32_t windowId)
{
    if (!host || !imgui_dock_host_begin(host, host->rect))
        return false;
    bool result = imgui_dock_select_tab(node, windowId);
    imgui_dock_host_end();
    return result;
}

void imgui_dock_host_dock_window(imgui_dock_host *host, uint32_t windowId, imgui_dock_node node)
{
    if (!host || !imgui_dock_host_begin(host, host->rect))
        return;
    imgui_dock_window(windowId, node);
    imgui_dock_host_end();
}

bool imgui_begin_window(uint32_t id, const imgui_window_desc *desc)
{
    if (_imgui.windowDepth >= IMGUI_MAX_LAYOUT_DEPTH)
        return false;
    _imgui_window *window = _imgui_find_window(id, desc);
    if (!window || !window->visible)
        return false;
    if (desc)
    {
        window->rect = desc->rect;
        window->floating = desc->floating;
        window->visible = desc->visible;
    }
    if (!window->floating && window->dockNode >= 0)
        window->rect = imgui_dock_node_rect(window->dockNode);
    if (!window->floating && window->dockNode >= 0)
    {
        _imgui_dock_node *dock = &_imgui.dockNodes[window->dockNode];
        if (dock->type == IMGUI_DOCK_TABS)
        {
            const imgui_event *event = imgui_current_event();
            if (event && event->type == IMGUI_EVENT_MOUSE_DOWN &&
                imgui_rect_contains((imgui_rect){window->rect.x, window->rect.y, window->rect.width, imgui_window_title_height()},
                                    event->mouseX, event->mouseY))
            {
                float tabWidth = dock->tabCount > 0 ? window->rect.width / (float)dock->tabCount : window->rect.width;
                int tab = (int)((event->mouseX - window->rect.x) / tabWidth);
                if (tab >= 0 && tab < dock->tabCount)
                    imgui_dock_select_tab(window->dockNode, dock->tabs[tab]);
            }
        }
    }
    if (!window->floating && window->dockNode >= 0)
    {
        _imgui_dock_node *dock = &_imgui.dockNodes[window->dockNode];
        if (dock->type == IMGUI_DOCK_TABS &&
            (dock->activeTab < 0 || dock->activeTab >= dock->tabCount || dock->tabs[dock->activeTab] != id))
            return false;
        window->rect = imgui_dock_node_rect(window->dockNode);
    }
    float titleHeight = imgui_window_title_height();
    window->contentRect = (imgui_rect){0, 0, window->rect.width,
                                       window->rect.height > titleHeight ? window->rect.height - titleHeight : 0};
    _imgui.windowStack[_imgui.windowDepth++] = (int)(window - _imgui.windows);
    _imgui.activeWindow = window;
    _imgui.contentOriginX = window->rect.x;
    _imgui.contentOriginY = window->rect.y + titleHeight;
    imgui_set_layout_rect(window->contentRect);
    return true;
}

void imgui_end_window(void)
{
    if (_imgui.windowDepth <= 0)
        return;
    int index = _imgui.windowStack[--_imgui.windowDepth];
    _imgui_window *window = &_imgui.windows[index];
    if (_imgui.event.type == IMGUI_EVENT_REPAINT)
    {
        float titleHeight = imgui_window_title_height();
        float tabHeight = imgui_dock_tab_height();
        float border = _imgui_metric(imgui_skin_current()->metrics.windowBorder, 4.0f);
        imgui_rect titleRect = {window->rect.x, window->rect.y, window->rect.width, titleHeight};
        _imgui_draw_style_screen(titleRect, imgui_style_state_for(&imgui_skin_current()->button, window->id, false), 880);
        if (_imgui.font && window->title)
            imgui_draw_text_at(_imgui.font, window->title, _imgui.fontPixelHeight,
                               titleRect.x + 8.0f, titleRect.y + 3.0f,
                               imgui_skin_current()->button.normal.textColor, 882);
        _imgui_dock_node *dock = window->dockNode >= 0 ? &_imgui.dockNodes[window->dockNode] : NULL;
        if (dock && dock->type == IMGUI_DOCK_TABS && dock->tabCount > 0)
        {
            float tabWidth = window->rect.width / (float)dock->tabCount;
            for (int i = 0; i < dock->tabCount; ++i)
            {
                imgui_rect tabRect = {window->rect.x + i * tabWidth, window->rect.y, tabWidth - 1.0f, tabHeight};
                const imgui_style_state *tabState = i == dock->activeTab
                                                        ? &imgui_skin_current()->button.active
                                                        : &imgui_skin_current()->button.normal;
                _imgui_draw_style_screen(tabRect, tabState, 881);
                VmString *tabTitle = imgui_window_title(dock->tabs[i]);
                if (_imgui.font && tabTitle)
                    imgui_draw_text_at(_imgui.font, tabTitle, _imgui.fontPixelHeight,
                                       tabRect.x + 6.0f, tabRect.y + 2.0f,
                                       tabState->textColor, 883);
            }
        }
        _imgui_draw_style_screen((imgui_rect){window->rect.x, window->rect.y, border, window->rect.height},
                                 &imgui_skin_current()->window.normal, 881);
        _imgui_draw_style_screen((imgui_rect){window->rect.x + window->rect.width - border, window->rect.y, border, window->rect.height},
                                 &imgui_skin_current()->window.normal, 881);
        _imgui_draw_style_screen((imgui_rect){window->rect.x, window->rect.y + window->rect.height - border,
                                              window->rect.width, border},
                                 &imgui_skin_current()->window.normal, 881);
    }
    _imgui.contentOriginX = 0.0f;
    _imgui.contentOriginY = 0.0f;
    _imgui.activeWindow = _imgui.windowDepth > 0 ? &_imgui.windows[_imgui.windowStack[_imgui.windowDepth - 1]] : NULL;
}

VmString *imgui_window_title(uint32_t id)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    return window ? window->title : NULL;
}

imgui_rect imgui_window_rect(uint32_t id)
{
    _imgui_window *window = _imgui_find_window(id, NULL);
    if (!window)
        return (imgui_rect){0, 0, 0, 0};
    return window->rect;
}

imgui_rect imgui_window_content_rect(void)
{
    return _imgui.activeWindow ? _imgui.activeWindow->contentRect : (imgui_rect){0, 0, 0, 0};
}
