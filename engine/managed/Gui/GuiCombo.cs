#if DE_EDITOR
using System;

namespace DigitoyEngine.Editor;

// Gui.ComboBox: tikla-ac acilir liste (Unity EditorGUI.Popup benzeri).
// Liste NATIVE popup penceresinde acilir (GuiPopup): ana pencerenin clip'i/kenari
// sinirlamaz. Acikken sahip pencerede HotControl'u tutar (modal): ana penceredeki
// MouseDown popup'i kapatir + yutulur. Secim sonucu bir sonraki GUI turunda alinir.
public static partial class Gui
{
    static readonly int _comboHash = "Gui.Combo".GetHashCode();

    // Secili indeksi dondurur; kullanici yeni oge secince yeni indeks doner.
    // selectedIndex kapsam disiysa "(none)" gosterilir.
    public static int ComboBox(in Rect rect, int selectedIndex, string[] items, GuiStyle style = null)
    {
        style ??= Skin.Button;
        int id = GuiUtility.GetControlID(_comboHash, FocusType.Passive);
        Event ev = Event.Current;
        int count = items?.Length ?? 0;

        // Popup'tan gelen sonuc (herhangi bir pass'te bir kez tuketilir).
        if (GuiPopup.TryTakeResult(id, out int picked))
        {
            if (GuiUtility.HotControl == id)
                GuiUtility.HotControl = 0;
            return picked >= 0 && picked < count ? picked : selectedIndex;
        }
        bool open = GuiPopup.IsOpen(id);

        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (open)
                {
                    ev.Use();
                    GuiPopup.Close(); // dis tik: kapat + yut (alttaki kontrole gecmez)
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
                        GuiPopup.OpenList(id, items, selectedIndex, GuiHost.CurrentWindow, GuiClip.Unclip(rect)); // AC — hot tutulur (modal)
                    else
                        GuiUtility.HotControl = 0;
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
                    break;
                }
        }
        return selectedIndex;
    }
}
#endif
