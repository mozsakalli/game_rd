namespace DigitoyEngine;

// Kenar dortlusu (Unity RectOffset).
public struct RectOffset
{
    public float Left, Right, Top, Bottom;
    public RectOffset(float left, float right, float top, float bottom)
    {
        Left = left; Right = right; Top = top; Bottom = bottom;
    }
    public RectOffset(float all) : this(all, all, all, all) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public Rect Remove(in Rect r)
        => new Rect(r.x + Left, r.y + Top, r.width - Horizontal, r.height - Vertical);
    public Rect Add(in Rect r)
        => new Rect(r.x - Left, r.y - Top, r.width + Horizontal, r.height + Vertical);
}

// Unity GUIStyleState karsiligi (Faz 1: text yok, duz renk + opsiyonel doku).
public struct GuiStyleState
{
    public Color Background;
    public Color Content; // ileride text/icon rengi; simdilik thumb gibi ic ogeler
}

// Unity GUIStyle karsiligi: 8 durum + kenar dortluleri + sabit olcu.
// StateFor secimi Unity ile ayni oncelik: hot->Active, keyboardFocus->Focused,
// hover->Hover, degilse Normal ('on' true ise On* varyantlari).
public sealed class GuiStyle
{
    public string Name = "";
    public GuiStyleState Normal, Hover, Active, Focused;
    public GuiStyleState OnNormal, OnHover, OnActive, OnFocused;
    public RectOffset Margin, Padding, Border;
    public float FixedWidth, FixedHeight;

    public GuiStyle() { }

    public GuiStyle(GuiStyle other)
    {
        Name = other.Name;
        Normal = other.Normal; Hover = other.Hover; Active = other.Active; Focused = other.Focused;
        OnNormal = other.OnNormal; OnHover = other.OnHover; OnActive = other.OnActive; OnFocused = other.OnFocused;
        Margin = other.Margin; Padding = other.Padding; Border = other.Border;
        FixedWidth = other.FixedWidth; FixedHeight = other.FixedHeight;
    }

    public ref GuiStyleState StateFor(int controlId, bool on, bool hovered)
    {
        if (GuiUtility.HotControl == controlId && hovered)
            return ref on ? ref OnActive : ref Active;
        if (GuiUtility.KeyboardControl == controlId)
            return ref on ? ref OnFocused : ref Focused;
        if (hovered && GuiUtility.HotControl == 0)
            return ref on ? ref OnHover : ref Hover;
        return ref on ? ref OnNormal : ref Normal;
    }

    // Repaint'te arka plani cizer (Faz 1: duz renkli quad; 9-slice border sonra).
    public void Draw(in Rect rect, int controlId, bool on = false)
    {
        if (Event.Current == null || Event.Current.Type != EventType.Repaint)
            return;
        bool hovered = rect.Contains(Event.Current.MousePosition);
        ref GuiStyleState s = ref StateFor(controlId, on, hovered);
        if (s.Background.a != 0)
            GuiRenderer.DrawRect(rect, s.Background);
    }
}

// Unity GUISkin karsiligi: adlandirilmis stil seti. Default = koyu editor temasi.
public sealed class GuiSkin
{
    public GuiStyle Box, Button, Toggle, HorizontalSlider, SliderThumb, Window, Label, TextField;

    public static readonly GuiSkin Default = CreateDefault();

    static GuiSkin CreateDefault()
    {
        static GuiStyleState S(byte r, byte g, byte b, byte a = 255)
            => new GuiStyleState { Background = new Color(r, g, b, a) };

        return new GuiSkin
        {
            Box = new GuiStyle
            {
                Name = "box",
                Normal = S(45, 48, 58),
                Hover = S(45, 48, 58),
                Active = S(45, 48, 58),
                Focused = S(45, 48, 58),
                Padding = new RectOffset(4),
                Margin = new RectOffset(4),
            },
            Label = new GuiStyle
            {
                Name = "label",
                Margin = new RectOffset(4),
            },
            Button = new GuiStyle
            {
                Name = "button",
                Normal = S(70, 74, 88),
                Hover = S(88, 94, 112),
                Active = S(50, 110, 200),
                Focused = S(70, 74, 88),
                Margin = new RectOffset(4),
                Padding = new RectOffset(6, 6, 3, 3),
                FixedHeight = 24,
            },
            Toggle = new GuiStyle
            {
                // Off-state = cerceve rengi, On-state = ic isaret rengi (Gui.Toggle cizer).
                Name = "toggle",
                Normal = S(105, 110, 124),
                Hover = S(135, 140, 155),
                Active = S(90, 150, 230),
                Focused = S(105, 110, 124),
                OnNormal = S(70, 140, 235),
                OnHover = S(95, 160, 245),
                OnActive = S(55, 115, 200),
                OnFocused = S(70, 140, 235),
                Margin = new RectOffset(4),
                FixedWidth = 18,
                FixedHeight = 18,
            },
            HorizontalSlider = new GuiStyle
            {
                Name = "hslider",
                Normal = S(40, 42, 50),
                Hover = S(40, 42, 50),
                Active = S(40, 42, 50),
                Focused = S(40, 42, 50),
                Margin = new RectOffset(4),
                FixedHeight = 18,
            },
            SliderThumb = new GuiStyle
            {
                Name = "sliderthumb",
                Normal = S(140, 145, 160),
                Hover = S(170, 175, 190),
                Active = S(90, 150, 230),
                Focused = S(140, 145, 160),
                FixedWidth = 10,
                FixedHeight = 18,
            },
            Window = new GuiStyle
            {
                Name = "window",
                Normal = S(33, 35, 43, 245),
                Hover = S(33, 35, 43, 245),
                Active = S(33, 35, 43, 245),
                Focused = S(33, 35, 43, 245),
                Padding = new RectOffset(8, 8, 28, 8), // ust: baslik cubugu
            },
            TextField = new GuiStyle
            {
                Name = "textfield",
                Normal = S(22, 24, 30),
                Hover = S(26, 28, 36),
                Active = S(22, 24, 30),
                Focused = S(28, 32, 46),
                Margin = new RectOffset(4),
                Padding = new RectOffset(6, 6, 2, 2),
                FixedHeight = 22,
            },
        };
    }
}
