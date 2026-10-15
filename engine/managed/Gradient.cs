using System;

namespace DigitoyEngine;

public enum GradientType : byte { Solid, Linear, Radial }
// Linear eksen: Vertical ust->alt, Horizontal sol->sag, Diagonal sol-ust->sag-alt,
// DiagonalReverse sag-ust->sol-alt. Capraz yonler kose renkleriyle (tint0..3 bilinear)
// cizilir — ek quad/shader yok.
public enum GradientDirection : byte { Vertical, Horizontal, Diagonal, DiagonalReverse }

// Renk dolgusu: Solid = tek renk, Linear = 2..4 duraklı (color -> [color3 @stop3] ->
// [color4 @stop4] -> color2; ara duraklar yalniz stops>2 iken okunur, konumlari 0..1
// ve sirali olmali — At() siralamayi varsayar, bozuksa clamp'ler). direction eksene
// karar verir. Radial = color(merkez) -> color2(kenar, closest-side elips); yalniz
// LayoutBox dolgusu cizer, diger tuketiciler At() uzerinden duz color2'ye duser.
// Standart serializer'a [Serializable] inline nesne olarak girer (ozel yol yok).
[Serializable]
public struct Gradient
{
    public GradientType type;
    [ShowIf(nameof(type), GradientType.Linear)] public GradientDirection direction;
    public Color color;
    [ShowIf(nameof(type), GradientType.Linear, GradientType.Radial)] public Color color2;
    // Durak sayisi (2..4): 2 = color->color2; 3 = +color3@stop3; 4 = +color4@stop4.
    [ShowIf(nameof(type), GradientType.Linear)] public int stops;
    [ShowIf(nameof(stops), 3, 4)] public Color color3;
    [ShowIf(nameof(stops), 3, 4)] public float stop3;
    [ShowIf(nameof(stops), 4)] public Color color4;
    [ShowIf(nameof(stops), 4)] public float stop4;

    public Gradient(Color solid)
    {
        type = GradientType.Solid;
        direction = GradientDirection.Vertical;
        color = solid;
        color2 = solid;
        stops = 2;
        color3 = color4 = solid;
        stop3 = 0.5f; stop4 = 0.75f;
    }

    public Gradient(Color start, Color end, GradientDirection dir = GradientDirection.Vertical)
    {
        type = GradientType.Linear;
        direction = dir;
        color = start;
        color2 = end;
        stops = 2;
        color3 = color4 = end;
        stop3 = 0.5f; stop4 = 0.75f;
    }

    // Etkin ara durak sayisi (0..2); stops alani 0 (eski/yeni default) ise 2 sayilir.
    // CAPRAZ yonde ara durak YOK: durak cizgileri capraz olur, eksen-hizali quad'larla tam
    // bolunemez (2x2 yaklasimi denendi, parca sinirlarinda 12/255 dikis) — capraz 2 renk.
    public readonly int MidStops
        => type == GradientType.Linear && !IsDiagonal ? Math.Clamp(stops - 2, 0, 2) : 0;

    // Herhangi bir pikselde gorunur katki var mi (a=0 dolgu hic cizilmez).
    public readonly bool Visible
        => color.a > 0 || (type != GradientType.Solid && color2.a > 0)
        || (MidStops >= 1 && color3.a > 0) || (MidStops >= 2 && color4.a > 0);

    // Renderer'lar orneklenecek ekseni bundan secer (Solid'de eksen onemsiz).
    public readonly bool IsHorizontal
        => type == GradientType.Linear && direction == GradientDirection.Horizontal;

    public readonly bool IsDiagonal
        => type == GradientType.Linear
        && (direction == GradientDirection.Diagonal || direction == GradientDirection.DiagonalReverse);

    // i. ara duragin konumu/rengi (i = 0..MidStops-1), konumlar sirali varsayilir.
    public readonly float MidPos(int i) => i == 0 ? stop3 : stop4;
    public readonly Color MidColor(int i) => i == 0 ? color3 : color4;

    // Eksen boyunca ornekleme: t=0 baslangic, t=1 bitis (clamp'li), ara duraklar parcali-lineer.
    public readonly Color At(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Sample(t, t);
    }

    // Clamp'siz: 0..1 disinda komsu segmentin dogrusu uzatilir (kanal clamp'li). Kutu disina
    // AA payi kadar tasan parcalar (kose hucresi, kenar seridi) icin — GPU'nun lineer
    // interpolasyonu kutu icindeki parcalarla ayni dogru uzerinde kalir, dikis olusmaz.
    public readonly Color AtExtrapolated(float t) => Sample(Math.Clamp(t, 0f, 1f), t);

    // tc: segment secimi icin clamp'li konum; t: lerp'te kullanilan gercek konum.
    readonly Color Sample(float tc, float t)
    {
        if (type == GradientType.Solid)
            return color;
        if (type == GradientType.Radial)
            return color2; // taban/dis renk; merkez katmani LayoutBox overlay'i
        int mids = MidStops;
        if (mids == 0)
            return Lerp(color, color2, t);
        float p0 = 0f; Color c0 = color;
        for (int i = 0; i < mids; i++)
        {
            float p = Math.Clamp(MidPos(i), p0, 1f);
            if (tc <= p)
                return p > p0 ? Lerp(c0, MidColor(i), (t - p0) / (p - p0)) : MidColor(i);
            p0 = p; c0 = MidColor(i);
        }
        return p0 < 1f ? Lerp(c0, color2, (t - p0) / (1f - p0)) : color2;
    }

    static Color Lerp(Color a, Color b, float t) => new(
        Ch(a.r + (b.r - a.r) * t),
        Ch(a.g + (b.g - a.g) * t),
        Ch(a.b + (b.b - a.b) * t),
        Ch(a.a + (b.a - a.a) * t));

    static byte Ch(float v) => (byte)Math.Clamp((int)(v + 0.5f), 0, 255);

    public static implicit operator Gradient(Color c) => new(c);
}

