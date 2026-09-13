using System;

namespace DigitoyEngine;

public enum GradientType : byte { Solid, Linear }

// Renk dolgusu: Solid = tek renk, Linear = dikey color(ust) -> color2(alt).
// Standart serializer'a [Serializable] inline nesne olarak girer (ozel yol yok).
[Serializable]
public struct Gradient
{
    public GradientType type;
    public Color color;
    [ShowIf(nameof(type), GradientType.Linear)] public Color color2;

    public Gradient(Color solid)
    {
        type = GradientType.Solid;
        color = solid;
        color2 = solid;
    }

    public Gradient(Color top, Color bottom)
    {
        type = GradientType.Linear;
        color = top;
        color2 = bottom;
    }

    // Herhangi bir pikselde gorunur katki var mi (a=0 dolgu hic cizilmez).
    public readonly bool Visible
        => color.a > 0 || (type == GradientType.Linear && color2.a > 0);

    // Dikey ornekleme: t=0 ust, t=1 alt (clamp'li).
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
