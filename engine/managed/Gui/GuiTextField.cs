#if DE_EDITOR
using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine.Editor;

// Tek satirli text editleme (Unity TextEditor karsiligi, zero-alloc cekirdek).
// Metin CAGIRANIN char[] tamponundadir (length ref); editor state (caret/
// secim/scroll) control id ile GuiState havuzunda yasar.
//
// Davranis seti (hepsi):
//   tik = caret konumu, surukle = secim, cift-tik = kelime, uc-tik = tumu
//   ok tuslari (+Shift secim, +Ctrl kelime atlama), Home/End (+Shift)
//   Backspace/Delete secim-duyarli (+Ctrl kelime silme)
//   Ctrl+A/C/X/V (GLFW panosu), tasan metin yatay kayar, caret yanip soner
public static partial class Gui
{
    static readonly int _textFieldHash = "Gui.TextField".GetHashCode();

    struct TextEditState
    {
        public int Caret;
        public int Anchor;  // secim baslangici (== Caret ise secim yok)
        public float ScrollX;
    }

    public static void TextField(in Rect rect, char[] buffer, ref int length, GuiStyle style = null)
    {
        var fixedBuffer = buffer;
        TextFieldCore(rect, ref fixedBuffer, ref length, style, grow: false);
    }

    public static void TextField(in Rect rect, ref char[] buffer, ref int length, GuiStyle style = null)
        => TextFieldCore(rect, ref buffer, ref length, style, grow: true);

    static void TextFieldCore(in Rect rect, ref char[] buffer, ref int length,
        GuiStyle style, bool grow)
    {
        style ??= Skin.TextField;
        int id = GuiUtility.GetControlID(_textFieldHash, FocusType.Keyboard);
        ref TextEditState st = ref GuiUtility.GetState<TextEditState>(id);
        Event ev = Event.Current;
        GuiFont font = Font;

        if (length > buffer.Length) length = buffer.Length;
        if (st.Caret > length) st.Caret = length;
        if (st.Anchor > length) st.Anchor = length;

        // Tab ile odak geldi: tumu secili baslar (Unity davranisi).
        if (GuiUtility.ConsumeTabFocus(id))
        {
            st.Anchor = 0;
            st.Caret = length;
            st.ScrollX = 0;
        }

        float pad = style.Padding.Left;
        var inner = new Rect(rect.x + pad, rect.y, rect.width - pad * 2, rect.height);
        var text = new ReadOnlySpan<char>(buffer, 0, length);

        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    GuiUtility.KeyboardControl = id;
                    int idx = IndexFromX(font, text, ev.MousePosition.x - inner.x + st.ScrollX);
                    if (ev.ClickCount >= 3)
                    {
                        st.Anchor = 0;
                        st.Caret = length;
                    }
                    else if (ev.ClickCount == 2)
                    {
                        st.Anchor = WordStart(text, idx);
                        st.Caret = WordEnd(text, idx);
                    }
                    else
                    {
                        st.Caret = idx;
                        if ((ev.Modifiers & EventModifiers.Shift) == 0)
                            st.Anchor = idx;
                    }
                    ev.Use();
                }
                else if (GuiUtility.KeyboardControl == id)
                {
                    GuiUtility.KeyboardControl = 0; // disari tik: focus birak
                }
                break;

            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    st.Caret = IndexFromX(font, text, ev.MousePosition.x - inner.x + st.ScrollX);
                    ev.Use();
                }
                break;

            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;

            case EventType.KeyDown:
                if (ev.KeyCode == GLFWConst.KEY_TAB)
                {
                    GuiUtility.MoveFocus(id, (ev.Modifiers & EventModifiers.Shift) != 0);
                    ev.Use();
                    break;
                }
                if (HandleKey(ev, ref buffer, ref length, ref st, grow))
                    ev.Use();
                break;

            case EventType.TextInput:
                if (ev.Character >= ' ')
                {
                    if (grow && length >= buffer.Length)
                        Array.Resize(ref buffer, Math.Max(16, buffer.Length * 2));
                    DeleteSelection(buffer, ref length, ref st);
                    Insert(buffer, ref length, ref st, ev.Character);
                    ev.Use();
                }
                break;

            case EventType.Repaint:
                if (rect.Contains(ev.MousePosition) || GuiUtility.HotControl == id)
                    GuiCursorManager.Request(GuiCursor.IBeam);
                DrawTextField(rect, inner, style, id, buffer, length, ref st, font);
                break;
        }
    }

    public static void LayoutTextField(char[] buffer, ref int length, ReadOnlySpan<LayoutOption> options = default)
    {
        GuiStyle style = Skin.TextField;
        float h = style.FixedHeight > 0 ? style.FixedHeight : 22;
        Rect r = GuiLayoutUtility.GetRect(60, 60, h, h, true, false, options);
        TextField(r, buffer, ref length, style);
    }

    // --- Cizim ---

    static void DrawTextField(in Rect rect, in Rect inner, GuiStyle style, int id,
        char[] buffer, int length, ref TextEditState st, GuiFont font)
    {
        style.Draw(rect, id);
        bool focused = GuiUtility.KeyboardControl == id;
        if (font == null)
            return;

        var text = new ReadOnlySpan<char>(buffer, 0, length);
        float caretX = PrefixWidth(font, text, st.Caret);

        // Caret gorunur kalacak sekilde yatay kaydirma.
        if (caretX - st.ScrollX > inner.width - 4)
            st.ScrollX = caretX - inner.width + 4;
        if (caretX - st.ScrollX < 0)
            st.ScrollX = caretX;
        if (st.ScrollX < 0)
            st.ScrollX = 0;

        // Tasan metni alana kirp (koordinatlar clip lokaline gecer).
        GuiClip.Push(new Rect(inner.x, rect.y, inner.width, rect.height));
        float textX = -st.ScrollX;
        float lineH = font.LineHeightAt(FontSize);
        float textY = (rect.height - lineH) * 0.5f;

        if (focused && st.Caret != st.Anchor)
        {
            int a = Math.Min(st.Caret, st.Anchor), b = Math.Max(st.Caret, st.Anchor);
            float x0 = PrefixWidth(font, text, a), x1 = PrefixWidth(font, text, b);
            GuiRenderer.DrawRect(new Rect(textX + x0, 2, x1 - x0, rect.height - 4),
                new Color(60, 100, 180, 170), 1);
        }

        GuiRenderer.DrawText(new Vec2(textX, textY), text, FontSize, _textColor, 2);

        if (focused && (GLFW.GetTime() % 1.0) < 0.55)
            GuiRenderer.DrawRect(new Rect(textX + caretX, 3, 1.5f, rect.height - 6),
                new Color(240, 242, 250, 255), 3);
        GuiClip.Pop();
    }

    // --- Klavye ---

    static bool HandleKey(Event ev, ref char[] buffer, ref int length,
        ref TextEditState st, bool grow)
    {
        bool shift = (ev.Modifiers & EventModifiers.Shift) != 0;
        bool ctrl = (ev.Modifiers & EventModifiers.Control) != 0;
        var text = new ReadOnlySpan<char>(buffer, 0, length);
        bool hasSel = st.Caret != st.Anchor;
        int selMin = Math.Min(st.Caret, st.Anchor), selMax = Math.Max(st.Caret, st.Anchor);

        switch (ev.KeyCode)
        {
            case GLFWConst.KEY_LEFT:
                if (!shift && hasSel)
                    st.Caret = selMin;
                else
                    st.Caret = ctrl ? WordStart(text, Math.Max(0, st.Caret - 1)) : Math.Max(0, st.Caret - 1);
                if (!shift) st.Anchor = st.Caret;
                return true;

            case GLFWConst.KEY_RIGHT:
                if (!shift && hasSel)
                    st.Caret = selMax;
                else
                    st.Caret = ctrl ? WordEnd(text, st.Caret) : Math.Min(length, st.Caret + 1);
                if (!shift) st.Anchor = st.Caret;
                return true;

            case GLFWConst.KEY_HOME:
                st.Caret = 0;
                if (!shift) st.Anchor = 0;
                return true;

            case GLFWConst.KEY_END:
                st.Caret = length;
                if (!shift) st.Anchor = length;
                return true;

            case GLFWConst.KEY_BACKSPACE:
                if (hasSel)
                    DeleteSelection(buffer, ref length, ref st);
                else if (st.Caret > 0)
                {
                    int from = ctrl ? WordStart(text, st.Caret - 1) : st.Caret - 1;
                    DeleteRange(buffer, ref length, from, st.Caret);
                    st.Caret = st.Anchor = from;
                }
                return true;

            case GLFWConst.KEY_DELETE:
                if (hasSel)
                    DeleteSelection(buffer, ref length, ref st);
                else if (st.Caret < length)
                {
                    int to = ctrl ? WordEnd(text, st.Caret) : st.Caret + 1;
                    DeleteRange(buffer, ref length, st.Caret, to);
                    st.Anchor = st.Caret;
                }
                return true;

            case GLFWConst.KEY_A when ctrl:
                st.Anchor = 0;
                st.Caret = length;
                return true;

            case GLFWConst.KEY_C when ctrl:
                if (hasSel)
                    GLFW.SetClipboardString(NativeWindow.MainWindow, new string(buffer, selMin, selMax - selMin));
                return true;

            case GLFWConst.KEY_X when ctrl:
                if (hasSel)
                {
                    GLFW.SetClipboardString(NativeWindow.MainWindow, new string(buffer, selMin, selMax - selMin));
                    DeleteSelection(buffer, ref length, ref st);
                }
                return true;

            case GLFWConst.KEY_V when ctrl:
                {
                    IntPtr p = GLFW.GetClipboardStringPtr(NativeWindow.MainWindow);
                    string s = p != IntPtr.Zero ? Marshal.PtrToStringUTF8(p) : null;
                    if (!string.IsNullOrEmpty(s))
                    {
                        if (grow)
                        {
                            int printable = 0;
                            for (int i = 0; i < s.Length; i++)
                                if (s[i] >= ' ')
                                    printable++;
                            int selected = Math.Abs(st.Caret - st.Anchor);
                            EnsureCapacity(ref buffer, length - selected + printable);
                        }
                        DeleteSelection(buffer, ref length, ref st);
                        for (int i = 0; i < s.Length; i++)
                            if (s[i] >= ' ') // tek satir: kontrol karakterleri atlanir
                                Insert(buffer, ref length, ref st, s[i]);
                    }
                    return true;
                }
        }
        return false;
    }

    static void EnsureCapacity(ref char[] buffer, int required)
    {
        if (required <= buffer.Length)
            return;
        int capacity = Math.Max(16, buffer.Length);
        while (capacity < required)
            capacity *= 2;
        Array.Resize(ref buffer, capacity);
    }

    // --- Metin manipulasyonu (char[] uzerinde, alloc yok) ---

    static void Insert(char[] buffer, ref int length, ref TextEditState st, char c)
    {
        if (length >= buffer.Length)
            return;
        Array.Copy(buffer, st.Caret, buffer, st.Caret + 1, length - st.Caret);
        buffer[st.Caret] = c;
        length++;
        st.Caret++;
        st.Anchor = st.Caret;
    }

    static void DeleteRange(char[] buffer, ref int length, int from, int to)
    {
        Array.Copy(buffer, to, buffer, from, length - to);
        length -= to - from;
    }

    static void DeleteSelection(char[] buffer, ref int length, ref TextEditState st)
    {
        if (st.Caret == st.Anchor)
            return;
        int a = Math.Min(st.Caret, st.Anchor), b = Math.Max(st.Caret, st.Anchor);
        DeleteRange(buffer, ref length, a, b);
        st.Caret = st.Anchor = a;
    }

    // --- Olcum / kelime sinirlari ---

    static float PrefixWidth(GuiFont font, ReadOnlySpan<char> text, int count)
        => count <= 0 ? 0 : font.TextSize(text.Slice(0, Math.Min(count, text.Length)), FontSize).x;

    // Mouse x'inden caret indeksi: glyph ortasini gecince sonraki indeks.
    // Raster advencelar (cizimle birebir: tamsayi fiziksel ilerleme).
    static int IndexFromX(GuiFont font, ReadOnlySpan<char> text, float x)
    {
        if (font == null)
            return text.Length;
        int px = GuiFont.PhysPx(FontSize);
        float invS = 1f / (Gui.Scale > 0 ? Gui.Scale : 1f);
        float acc = 0;
        for (int i = 0; i < text.Length; i++)
        {
            float adv = MathF.Round(font.GetRasterGlyph(px, text[i]).Advance) * invS;
            if (x < acc + adv * 0.5f)
                return i;
            acc += adv;
        }
        return text.Length;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    static int WordStart(ReadOnlySpan<char> text, int i)
    {
        if (i >= text.Length) i = text.Length - 1;
        if (i < 0) return 0;
        while (i > 0 && !IsWordChar(text[i]) && !IsWordChar(text[i - 1])) i--;
        while (i > 0 && IsWordChar(text[i - 1])) i--;
        return i;
    }

    static int WordEnd(ReadOnlySpan<char> text, int i)
    {
        while (i < text.Length && !IsWordChar(text[i])) i++;
        while (i < text.Length && IsWordChar(text[i])) i++;
        return i;
    }
}
#endif
