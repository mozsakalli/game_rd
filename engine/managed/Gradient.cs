using System;

namespace DigitoyEngine;

public enum GradientType : byte { Solid, Linear }
public enum GradientDirection : byte { Vertical, Horizontal }

// Renk dolgusu: Solid = tek renk, Linear = color(baslangic) -> color2(bitis);
// direction eksene karar verir (Vertical: ust->alt, Horizontal: sol->sag).
// Standart serializer'a [Serializable] inline nesne olarak girer (ozel yol yok).
[Serializable]
public struct Gradient
{
    public GradientType type;
    [ShowIf(nameof(type), GradientType.Linear)] public GradientDirection direction;
    public Color color;
    [ShowIf(nameof(type), GradientType.Linear)] public Color color2;

    public Gradient(Color solid)
    {
        type = GradientType.Solid;
        direction = GradientDirection.Vertical;
        color = solid;
        color2 = solid;
    }

    public Gradient(Color start, Color end, GradientDirection dir = GradientDirection.Vertical)
    {
        type = GradientType.Linear;
        direction = dir;
        color = start;
        color2 = end;
    }

    // Herhangi bir pikselde gorunur katki var mi (a=0 dolgu hic cizilmez).
    public readonly bool Visible
        => color.a > 0 || (type == GradientType.Linear && color2.a > 0);

    // Renderer'lar orneklenecek ekseni bundan secer (Solid'de eksen onemsiz).
    public readonly bool IsHorizontal
        => type == GradientType.Linear && direction == GradientDirection.Horizontal;

    // Eksen boyunca ornekleme: t=0 baslangic (ust/sol), t=1 bitis (clamp'li).
    public readonly Color At(float t)
    {
        if (type == GradientType.Solid)
            return color;
        t = Math.Clamp(t, 0f, 1f);
        return new(
            (byte)(color.r + (color2.r - color.r) * t),
            (byte)(color.g + (color2.g - color.g) * t),
            (byte)(color.b + (color2.b - color.b) * t),
            (byte)(color.a + (color2.a - color.a) * t));
    }

    public static implicit operator Gradient(Color c) => new(c);
}
