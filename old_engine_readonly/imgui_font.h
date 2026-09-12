#ifndef ENGINE_IMGUI_FONT_H
#define ENGINE_IMGUI_FONT_H

#include "imgui.h"

typedef struct imgui_font imgui_font;

imgui_font *imgui_font_load_file(VmString *path, float pixelHeight);
imgui_font *imgui_font_load_memory(const unsigned char *data, int size, float pixelHeight);
void imgui_font_destroy(imgui_font *font);
float imgui_font_line_height(const imgui_font *font);
void imgui_text_size(const imgui_font *font, VmString *text, float *width, float *height);
void imgui_draw_text(const imgui_font *font, VmString *text, float x, float y, imgui_color color, int layer);
void imgui_text_size_at(const imgui_font *font, VmString *text, float pixelHeight, float *width, float *height);
void imgui_draw_text_at(const imgui_font *font, VmString *text, float pixelHeight,
                        float x, float y, imgui_color color, int layer);
void imgui_debug_draw_text_guides(const imgui_font *font, VmString *text, float pixelHeight,
                                  float x, float y, int layer);
void imgui_debug_draw_atlas(const imgui_font *font, float x, float y, float width, float height, int layer);

#endif
