using System;

namespace DigitoyEngine;

// Gui.ComboBox: tikla-ac acilir liste (Unity EditorGUI.Popup benzeri).
// Acikken HotControl'u tutar (modal): tum mouse event'leri combo'ya akar,
// dis tik kapatir + yutar. Popup aktif clip'in DISINA tasabilir — ekran
// uzayinda, yuksek layer'da cizilir (submit sirasi ne olursa olsun ustte).
public static partial class Gui
{
    static readonly int _comboHash = "Gui.Combo".GetHashCode();
    static int _comboOpenId;   // acik popup'un kontrol id'si (frame'ler arasi kalici)
    static float _comboScroll;

    const float ComboItemH = 20f;
    const float ComboMaxH = 302f;
    const int ComboLayer = 64; // panel widget'lari 0-4 kullanir; popup hepsinin ustunde

    // Secili indeksi dondurur; kullanici yeni oge secince yeni indeks doner.
    // items acilinca cizilir; selectedIndex kapsam disiysa "(none)" gosterilir.
    public static int ComboBox(in Rect rect, int selectedIndex, string[] items, GuiStyle style = null)
    {
        style ??= Skin.Button;
        int id = GuiUtility.GetControlID(_comboHash, FocusType.Passive);
        Event ev = Event.Current;
        bool open = _comboOpenId == id;
        // Guvenlik: acikken hot bizde olmali; baskasi devraldiysa popup olmus say.
        if (open && GuiUtility.HotControl != id)
        {
            _comboOpenId = 0;
            open = false;
        }

        int count = items?.Length ?? 0;
        Rect popup = default;
        float maxScroll = 0;
        if (open)
            popup = ComboPopupRect(rect, count, out maxScroll);

        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (open)
                {
                    ev.Use();
                    if (popup.Contains(ev.MousePosition))
                    {
                        int idx = (int)((ev.MousePosition.y - popup.y + _comboScroll) / ComboItemH);
                        CloseCombo(id);
                        if (idx >= 0 && idx < count)
                            return idx;
                    }
                    else
                    {
                        CloseCombo(id); // dis tik: kapat + yut (alttaki kontrole gecmez)
                    }
                }
                else if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                    ev.Use();
                break;

            case EventType.MouseUp:
                if (open)
                {
                    ev.Use(); // popup acikken up'i yut (modal)
                }
                else if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    if (rect.Contains(ev.MousePosition) && count > 0)
                    {
                        _comboOpenId = id; // AC — hot tutulur (modal)
                        popup = ComboPopupRect(rect, count, out maxScroll);
                        // Secili oge gorunur olacak sekilde kaydir.
                        _comboScroll = Math.Clamp(
                            selectedIndex * ComboItemH - (popup.height - ComboItemH) * 0.5f,
                            0, maxScroll);
                    }
                    else
                    {
                        GuiUtility.HotControl = 0;
                    }
                }
                break;

            case EventType.ScrollWheel:
                if (open && popup.Contains(ev.MousePosition))
                {
                    _comboScroll = Math.Clamp(_comboScroll - ev.Delta.y * ComboItemH * 2, 0, maxScroll);
                    ev.Use();
                }
                break;

            case EventType.Repaint:
                {
                    style.Draw(rect, id);
                    float ts = Math.Min(FontSize, rect.height - 4);
                    string label = (uint)selectedIndex < (uint)count ? items[selectedIndex] : "(none)";
                    GuiRenderer.DrawTextIn(new Rect(rect.x + 6, rect.y, Math.Max(0, rect.width - 22), rect.height),
                        label, ts, _textColor);
                    GuiRenderer.DrawTextIn(new Rect(rect.xMax - 16, rect.y, 12, rect.height),
                        "\u25BE", ts, new Color(170, 174, 186, 255));
                    if (open)
                        DrawComboPopup(popup, items, selectedIndex);
                    break;
                }
        }
        return selectedIndex;
    }

    static void CloseCombo(int id)
    {
        _comboOpenId = 0;
        if (GuiUtility.HotControl == id)
            GuiUtility.HotControl = 0;
    }

    // Popup rect'i buton LOKAL uzayinda; ekrana sigmayan popup yukari acilir /
    // yatayda iceri itilir, icerik sigmazsa scroll devreye girer.
    static Rect ComboPopupRect(in Rect rect, int count, out float maxScroll)
    {
        float contentH = count * ComboItemH;
        float h = Math.Min(contentH, ComboMaxH);
        Vec2 origin = GuiClip.Unclip(new Vec2(rect.x, rect.y));
        Rect screen = GuiClip.ScreenRect;
        float below = screen.yMax - (origin.y + rect.height);
        float above = origin.y - screen.y;
        float y;
        if (h <= below || below >= above)
        {
            h = Math.Min(h, Math.Max(ComboItemH, below));
            y = rect.y + rect.height;
        }
        else
        {
            h = Math.Min(h, Math.Max(ComboItemH, above));
            y = rect.y - h;
        }
        float gx = Math.Clamp(origin.x, screen.x, Math.Max(screen.x, screen.xMax - rect.width));
        maxScroll = Math.Max(0, contentH - h);
        return new Rect(rect.x + (gx - origin.x), y, rect.width, h);
    }

    static void DrawComboPopup(in Rect popup, string[] items, int selectedIndex)
    {
        Rect global = GuiClip.Unclip(popup);
        GuiClip.PushScreen();
        GuiRenderer.DrawRect(new Rect(global.x - 1, global.y - 1, global.width + 2, global.height + 2),
            new Color(18, 19, 23, 255), ComboLayer); // ince cerceve
        GuiRenderer.DrawRect(global, new Color(38, 40, 48, 255), ComboLayer + 1);
        GuiClip.Push(global);
        float ts = Math.Min(FontSize, ComboItemH - 4);
        int first = Math.Max(0, (int)(_comboScroll / ComboItemH));
        int last = Math.Min(items.Length - 1, (int)((_comboScroll + popup.height) / ComboItemH));
        for (int i = first; i <= last; i++)
        {
            var row = new Rect(0, i * ComboItemH - _comboScroll, popup.width, ComboItemH);
            bool hover = row.Contains(Event.Current.MousePosition);
            if (hover)
                GuiRenderer.DrawRect(row, new Color(50, 110, 200, 255), ComboLayer + 2);
            else if (i == selectedIndex)
                GuiRenderer.DrawRect(row, new Color(62, 66, 80, 255), ComboLayer + 2);
            GuiRenderer.DrawTextIn(new Rect(row.x + 6, row.y, row.width - 12, row.height),
                items[i], ts, _textColor, false, ComboLayer + 3);
        }
        GuiClip.Pop();
        GuiClip.Pop();
    }
}
