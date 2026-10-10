namespace DigitoyEngine;

// Unity Screen karsiligi (minimal, mantiksal/point birim). Host her frame gunceller.
public static class Screen
{
    public static float width { get; internal set; }
    public static float height { get; internal set; }
    // Fiziksel piksel / mantiksal birim (HiDPI: 1.5, 2...). Pencere ozelligi, host basar.
    public static float pixelRatio { get; internal set; } = 1f;
}
