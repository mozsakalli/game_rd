using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// CORE easing altyapisi — motorun her yerinden kullanilir (tween, MovieClip,
// particle, kullanici kodu). SICAK YOL LUT'TUR: tum egriler acilista tek duz
// tabloya pisirilir (31 x 257 float ~= 32KB); Evaluate = indeks + lineer ara
// deger, ~2-3ns, dallanmasiz, HER egri ayni maliyet (elastic/bounce dahil).
// Analitik EvaluateExact referans/pisirici olarak durur.

public enum Ease
{
    Linear = 0,
    InSine, OutSine, InOutSine,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InQuint, OutQuint, InOutQuint,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InElastic, OutElastic, InOutElastic,
    InBack, OutBack, InOutBack,
    InBounce, OutBounce, InOutBounce,
}

public static class Easing
{
    public const int EaseCount = 31;
    const int Table = 257; // 256 aralik + tam uc noktalar

    static readonly float[] _lut = BakeAll();

    public static float Evaluate(Ease e, float t)
    {
        if (t <= 0f)
            return 0f;
        int baseIdx = (int)e * Table;
        if (t >= 1f)
            return _lut[baseIdx + Table - 1];
        float f = t * (Table - 1);
        int i = (int)f;
        f -= i;
        float a = _lut[baseIdx + i];
        return a + (_lut[baseIdx + i + 1] - a) * f;
    }

    static float[] BakeAll()
    {
        var lut = new float[EaseCount * Table];
        for (int e = 0; e < EaseCount; e++)
            for (int i = 0; i < Table; i++)
                lut[e * Table + i] = EvaluateExact((Ease)e, i / (float)(Table - 1));
        return lut;
    }

    // Penner seti — analitik referans (pisirme + hassasiyet isteyen cagiran icin).
    public static float EvaluateExact(Ease e, float t)
    {
        const float PI = MathF.PI;
        const float C1 = 1.70158f;        // back
        const float C2 = C1 * 1.525f;
        const float C3 = C1 + 1f;
        const float C4 = 2f * PI / 3f;    // elastic
        const float C5 = 2f * PI / 4.5f;

        switch (e)
        {
            default:
            case Ease.Linear: return t;

            case Ease.InSine: return 1f - MathF.Cos(t * PI * 0.5f);
            case Ease.OutSine: return MathF.Sin(t * PI * 0.5f);
            case Ease.InOutSine: return -(MathF.Cos(PI * t) - 1f) * 0.5f;

            case Ease.InQuad: return t * t;
            case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
            case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - 0.5f * Sq(-2f * t + 2f);

            case Ease.InCubic: return t * t * t;
            case Ease.OutCubic: return 1f - Cube(1f - t);
            case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - 0.5f * Cube(-2f * t + 2f);

            case Ease.InQuart: return t * t * t * t;
            case Ease.OutQuart: return 1f - Sq(Sq(1f - t));
            case Ease.InOutQuart: return t < 0.5f ? 8f * t * t * t * t : 1f - 0.5f * Sq(Sq(-2f * t + 2f));

            case Ease.InQuint: return t * t * t * t * t;
            case Ease.OutQuint: return 1f - Cube(1f - t) * Sq(1f - t);
            case Ease.InOutQuint: return t < 0.5f ? 16f * t * t * t * t * t : 1f - 0.5f * Cube(-2f * t + 2f) * Sq(-2f * t + 2f);

            case Ease.InExpo: return t <= 0f ? 0f : MathF.Pow(2f, 10f * t - 10f);
            case Ease.OutExpo: return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
            case Ease.InOutExpo:
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return t < 0.5f ? MathF.Pow(2f, 20f * t - 10f) * 0.5f
                                : (2f - MathF.Pow(2f, -20f * t + 10f)) * 0.5f;

            case Ease.InCirc: return 1f - MathF.Sqrt(1f - t * t);
            case Ease.OutCirc: return MathF.Sqrt(1f - Sq(t - 1f));
            case Ease.InOutCirc:
                return t < 0.5f ? (1f - MathF.Sqrt(1f - Sq(2f * t))) * 0.5f
                                : (MathF.Sqrt(1f - Sq(-2f * t + 2f)) + 1f) * 0.5f;

            case Ease.InElastic:
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((10f * t - 10.75f) * C4);
            case Ease.OutElastic:
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return MathF.Pow(2f, -10f * t) * MathF.Sin((10f * t - 0.75f) * C4) + 1f;
            case Ease.InOutElastic:
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return t < 0.5f
                    ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * C5)) * 0.5f
                    : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * C5) * 0.5f + 1f;

            case Ease.InBack: return C3 * t * t * t - C1 * t * t;
            case Ease.OutBack: return 1f + C3 * Cube(t - 1f) + C1 * Sq(t - 1f);
            case Ease.InOutBack:
                return t < 0.5f
                    ? Sq(2f * t) * ((C2 + 1f) * 2f * t - C2) * 0.5f
                    : (Sq(2f * t - 2f) * ((C2 + 1f) * (2f * t - 2f) + C2) + 2f) * 0.5f;

            case Ease.InBounce: return 1f - OutBounce(1f - t);
            case Ease.OutBounce: return OutBounce(t);
            case Ease.InOutBounce:
                return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) * 0.5f
                                : (1f + OutBounce(2f * t - 1f)) * 0.5f;
        }
    }

    static float Sq(float x) => x * x;
    static float Cube(float x) => x * x * x;

    static float OutBounce(float t)
    {
        const float n1 = 7.5625f, d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }
}

// Authored egri (AnimationCurve muadili): Hermite key'ler serilesir; calisma
// zamaninda LUT'a pisirilir — degerlendirme standart ease ile AYNI maliyet.
// Editor key degistirince Version arttirir -> tembel yeniden pisirme.
[Serializable]
public sealed class CurveKey
{
    public float Time;
    public float Value;
    public float InTangent;
    public float OutTangent;
}

[Serializable]
public sealed class Curve
{
    public List<CurveKey> Keys = new();

    [NonSerialized] public int Version;
    [NonSerialized] float[] _lut;
    [NonSerialized] int _bakedVersion = -1;

    const int Table = 129;

    public float Evaluate(float t)
    {
        if (_lut == null || _bakedVersion != Version)
            Bake();
        if (t <= 0f)
            return _lut[0];
        if (t >= 1f)
            return _lut[Table - 1];
        float f = t * (Table - 1);
        int i = (int)f;
        f -= i;
        return _lut[i] + (_lut[i + 1] - _lut[i]) * f;
    }

    // Hermite (referans + pisirici). Key'ler zaman sirali varsayilir.
    public float EvaluateExact(float t)
    {
        var keys = Keys;
        int n = keys.Count;
        if (n == 0)
            return 0f;
        if (n == 1 || t <= keys[0].Time)
            return keys[0].Value;
        if (t >= keys[n - 1].Time)
            return keys[n - 1].Value;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid].Time <= t) lo = mid;
            else hi = mid;
        }
        var a = keys[lo];
        var b = keys[hi];
        float span = b.Time - a.Time;
        if (span <= 1e-6f)
            return b.Value;
        float u = (t - a.Time) / span;
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * a.Value
             + (u3 - 2f * u2 + u) * span * a.OutTangent
             + (-2f * u3 + 3f * u2) * b.Value
             + (u3 - u2) * span * b.InTangent;
    }

    void Bake()
    {
        _lut ??= new float[Table]; // tek alloc, veriyle yasar
        for (int i = 0; i < Table; i++)
            _lut[i] = EvaluateExact(i / (float)(Table - 1));
        _bakedVersion = Version;
    }
}
