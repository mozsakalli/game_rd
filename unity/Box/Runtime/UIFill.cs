using System;
using UnityEngine;

namespace Digitoy.Box
{
    public enum UIFillType { Solid = 0, Gradient = 1, Radial = 2 }

    /// <summary>
    /// Colour source for every painted layer (fill, border, text fill, text outline): a single colour, a
    /// Unity <see cref="Gradient"/> across the layer's band (vertical: time 0 = bottom, 1 = top; horizontal:
    /// 0 = left, 1 = right), or Radial (centre = gradient at 0, edge = gradient at 1; a single linear ramp,
    /// mid-stops ignored; only meaningful for the box fill — other layers use the edge colour).
    /// Vertex colours: exact at quad edges; interior fill quads split at key times.
    /// </summary>
    [Serializable]
    public struct UIFill
    {
        public UIFillType type;
        public Color color;
        public Gradient gradient;
        public bool horizontal;

        public static UIFill Solid(Color c) => new UIFill { type = UIFillType.Solid, color = c, gradient = DefaultGradient() };

        public static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public bool IsGradient => type == UIFillType.Gradient && gradient != null;
        public bool IsRadial => type == UIFillType.Radial && gradient != null;
        public bool IsHorizontal => IsGradient && horizontal;

        /// <summary>Radial centre colour (gradient at 0). Base/edge colour comes from <see cref="Evaluate"/>.</summary>
        public Color CenterColor => IsRadial ? gradient.Evaluate(0f) : color;

        /// <summary>False when nothing would be painted (solid alpha 0 / gradient fully transparent).</summary>
        public bool Visible
        {
            get
            {
                if (!IsGradient && !IsRadial)
                    return color.a > 0f;
                foreach (var k in gradient.alphaKeys)
                    if (k.alpha > 0f)
                        return true;
                return false;
            }
        }

        public Color Evaluate(float t) => IsGradient ? gradient.Evaluate(Mathf.Clamp01(t)) : IsRadial ? gradient.Evaluate(1f) : color;

        /// <summary>Key times strictly inside (0,1), sorted, unique — split positions for interior quads.</summary>
        public int InnerKeyTimes(Span<float> dst)
        {
            if (!IsGradient)
                return 0;
            int n = 0;
            foreach (var k in gradient.colorKeys)
                n = Push(dst, n, k.time);
            foreach (var k in gradient.alphaKeys)
                n = Push(dst, n, k.time);
            for (int i = 1; i < n; i++)
            {
                float v = dst[i];
                int j = i - 1;
                while (j >= 0 && dst[j] > v) { dst[j + 1] = dst[j]; j--; }
                dst[j + 1] = v;
            }
            return n;
        }

        static int Push(Span<float> dst, int n, float t)
        {
            if (t <= 0.0001f || t >= 0.9999f || n >= dst.Length)
                return n;
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(dst[i] - t) < 0.0001f)
                    return n;
            dst[n++] = t;
            return n;
        }
    }
}
