using System;
using UnityEngine;

namespace Digitoy.Box
{
    // Metin katmani: engine LayoutBox.Text.cs portu. Kutu vatandasi MTSDF metin: word wrap (padding icli
    // rect'e), ellipsis, yatay/dikey hizalama, kalinlik (SDF esik kaydirma), kontur, golge. Yerlesim
    // cache'i yalniz font/metin/boyut/hiza/wrap/rect degisince kurulur; cizim ayni EmitQuad yolu ve ayni
    // sheet → kutu + metin tek draw.
    public sealed partial class UIBox
    {
        [Header("Text")]
        [SerializeField] UIFont font;
        [SerializeField, TextArea(1, 6)] string text = "";
        [SerializeField] float textSize = 32f;
        [SerializeField] UIFill textFill = UIFill.Solid(Color.black);   // kutu fill varsayilani beyaz; metin okunsun
        [SerializeField] TextAlign textAlign = TextAlign.Left;
        [SerializeField] LayoutAlign textAlignV = LayoutAlign.Start;
        [SerializeField] bool textWrap = true;
        [SerializeField] bool textEllipsis = true;
        [SerializeField, Range(-5f, 3f)] float textWeight;   // SDF px: + kalin, - ince
        [SerializeField] float textSpacing;                   // harf araligi (px)
        [SerializeField] float textOutlineWidth;
        [SerializeField] UIFill textOutline = UIFill.Solid(Color.black);
        [SerializeField] Color textShadowColor = new Color(0, 0, 0, 0);
        [SerializeField] Vector2 textShadowOffset = new Vector2(0, 4);
        [SerializeField] float textShadowBlur = 8f;
        [Tooltip("Diagnostics: draw glyph quads as the raw SDF channel (Alpha = true SDF via USER.y<0, Median = msdf median).")]
        [SerializeField] TextDebugView textDebug = TextDebugView.Off;

        public enum TextDebugView { Off, Alpha, Median, Diff }

        public UIFont Font { get => font; set { if (!ReferenceEquals(font, value)) { font = value; SetAllDirty(); } } }
        public string Text { get => text; set { value ??= ""; if (text != value) { text = value; SetVerticesDirty(); } } }
        public float TextSize { get => textSize; set { if (textSize != value) { textSize = value; SetVerticesDirty(); } } }
        public UIFill TextFill { get => textFill; set { textFill = value; SetVerticesDirty(); } }
        public TextAlign TextAlign { get => textAlign; set { if (textAlign != value) { textAlign = value; SetVerticesDirty(); } } }
        public LayoutAlign TextAlignV { get => textAlignV; set { if (textAlignV != value) { textAlignV = value; SetVerticesDirty(); } } }
        public bool TextWrap { get => textWrap; set { if (textWrap != value) { textWrap = value; SetVerticesDirty(); } } }
        public bool TextEllipsis { get => textEllipsis; set { if (textEllipsis != value) { textEllipsis = value; SetVerticesDirty(); } } }
        public float TextWeight { get => textWeight; set { textWeight = value; SetVerticesDirty(); } }
        public float TextSpacing { get => textSpacing; set { if (textSpacing != value) { textSpacing = value; SetVerticesDirty(); } } }
        public float TextOutlineWidth { get => textOutlineWidth; set { textOutlineWidth = value; SetVerticesDirty(); } }
        public UIFill TextOutline { get => textOutline; set { textOutline = value; SetVerticesDirty(); } }
        public Color TextShadowColor { get => textShadowColor; set { textShadowColor = value; SetVerticesDirty(); } }
        public Vector2 TextShadowOffset { get => textShadowOffset; set { textShadowOffset = value; SetVerticesDirty(); } }
        public float TextShadowBlur { get => textShadowBlur; set { textShadowBlur = value; SetVerticesDirty(); } }

        const float TextSdfSpread = UIFont.SdfSpread;

        bool HasText => font != null && font.sheet != null && font.sdfSize > 0 && textSize > 0 && !string.IsNullOrEmpty(text);

        float TextScale => textSize / font.sdfSize;

        // Sabit px spacing'in SDF-px karsiligi (kalem uzayi).
        float SpacingSdf => textSpacing / TextScale;

        static float WeightEdge(float weight) => 0.5f - Mathf.Clamp(weight, -5f, 3f) / TextSdfSpread;

        // '\n' disindaki kontrol karakterleri (TextArea'nin '\r'si vb.) sifir genislikli, glyph'siz.
        static bool IsSkipped(char c) => c < ' ' && c != '\n';

        // --- yerlesim cache'i (icerik sol-ust orijinli, y-down) ---

        struct TextQuad
        {
            public float X0, Y0, X1, Y1;
            public float U0, V0, U1, V1; // V0 = alt kenar (Unity v)
        }

        TextQuad[] _tq;
        int _tqCount;
        float _tbBlockH;
        UIFont _tbFont;
        string _tbText;
        float _tbSize, _tbAvailW, _tbAvailH, _tbSpacing;
        TextAlign _tbAlign;
        LayoutAlign _tbAlignV;
        bool _tbWrap, _tbEllipsis;

        // --- olcum (adim 3 layout'u icin): dogal genislik / satir toplami ---

        public float TextPreferredWidth(float wrapWidth)
        {
            if (!HasText)
                return 0;
            if (textWrap && wrapWidth > 0)
                return 0;
            float mx = 0;
            int pos = 0;
            while (NextLine(ref pos, float.MaxValue, out _, out _, out _, out float inkL, out float inkR))
                mx = Mathf.Max(mx, inkR - inkL);
            return mx * TextScale + padding.x + padding.z;
        }

        public float TextPreferredHeight(float wrapWidth)
        {
            if (!HasText)
                return 0;
            float scale = TextScale;
            float wrapW = textWrap && wrapWidth > 0
                ? Mathf.Max(1f, wrapWidth - padding.x - padding.z) / scale
                : float.MaxValue;
            int n = 0, pos = 0;
            while (NextLine(ref pos, wrapW, out _, out _, out _, out _, out _))
                n++;
            return n * font.lineHeight * scale + padding.y + padding.w;
        }

        // --- satir kirici (greedy word wrap) ---
        bool NextLine(ref int pos, float availW, out int start, out int end, out float w, out float inkL, out float inkR)
        {
            string s = text;
            int len = s.Length;
            start = pos;
            end = pos;
            w = 0;
            inkL = 0;
            inkR = 0;
            if (pos > len)
                return false;
            int prev = -1, lastSp = -1;
            float wAtSp = 0, inkRAtSp = 0;
            for (int i = start; i < len; i++)
            {
                char c = s[i];
                if (c == '\n')
                {
                    end = i;
                    pos = i + 1;
                    return true;
                }
                if (IsSkipped(c))
                    continue;
                int gi = font.GlyphIndex(c);
                ref readonly var g = ref font.GlyphAt(gi);
                float gap = prev >= 0 ? font.Kerning(prev, gi) + SpacingSdf : 0;
                float add = gap + g.advance;
                bool ink = g.w > 0;
                float l = prev < 0 && ink ? g.inkL : inkL;
                float r = ink ? Mathf.Max(inkR, w + gap + g.inkR) : inkR;
                if (c == ' ')
                {
                    lastSp = i;
                    wAtSp = w;
                    inkRAtSp = inkR;
                }
                else if (r - l > availW && i > start)
                {
                    if (lastSp >= start)
                    {
                        end = lastSp;
                        w = wAtSp;
                        inkR = inkRAtSp;
                        pos = lastSp + 1;
                    }
                    else
                    {
                        end = i;
                        pos = i;
                    }
                    return true;
                }
                inkL = l;
                inkR = r;
                w += add;
                prev = gi;
            }
            end = len;
            pos = len + 1;
            return true;
        }

        int FitCount(int st, int en, float maxW, out float w)
        {
            w = 0;
            int prev = -1, n = 0;
            for (int i = st; i < en; i++)
            {
                if (IsSkipped(text[i])) { n++; continue; }
                int gi = font.GlyphIndex(text[i]);
                float add = (prev >= 0 ? font.Kerning(prev, gi) + SpacingSdf : 0) + font.GlyphAt(gi).advance;
                if (w + add > maxW)
                    break;
                w += add;
                prev = gi;
                n++;
            }
            return st + n;
        }

        void EnsureTextLayout(float availW, float availH)
        {
            if (_tq != null && ReferenceEquals(_tbFont, font) && _tbText == text
                && _tbSize == textSize && _tbAlign == textAlign && _tbAlignV == textAlignV
                && _tbWrap == textWrap && _tbEllipsis == textEllipsis
                && _tbSpacing == textSpacing
                && _tbAvailW == availW && _tbAvailH == availH)
                return;
            BuildTextQuads(availW, availH);
            _tbFont = font;
            _tbText = text;
            _tbSize = textSize;
            _tbAlign = textAlign;
            _tbAlignV = textAlignV;
            _tbWrap = textWrap;
            _tbEllipsis = textEllipsis;
            _tbSpacing = textSpacing;
            _tbAvailW = availW;
            _tbAvailH = availH;
        }

        void BuildTextQuads(float availW, float availH)
        {
            float scale = TextScale;
            const float fitEps = 0.5f;
            float wrapW = textWrap ? Mathf.Max(availW, 1f) / scale + fitEps : float.MaxValue;
            float lineH = font.lineHeight * scale;

            int total = 0;
            {
                int pos = 0;
                while (NextLine(ref pos, wrapW, out _, out _, out _, out _, out _))
                    total++;
            }
            int lines = total;
            if (textEllipsis && lineH > 0)
                lines = Mathf.Max(1, Mathf.Min(total, (int)(availH / lineH + 0.001f)));

            _tbBlockH = lines * lineH;
            int cap = text.Length + 3 * lines + 1;
            if (_tq == null || _tq.Length < cap)
                _tq = new TextQuad[cap];
            _tqCount = 0;

            float clipW = availW / scale + fitEps;
            float dotsW = font.MeasureLine("...") + 3 * SpacingSdf;
            int p = 0;
            for (int li = 0; li < lines; li++)
            {
                NextLine(ref p, wrapW, out int st, out int en, out float w, out float inkL, out float inkR);
                bool dots = textEllipsis && ((li == lines - 1 && li < total - 1) || inkR - inkL > clipW);
                if (dots)
                {
                    en = FitCount(st, en, clipW - dotsW, out float wc);
                    inkL = 0;
                    inkR = wc + dotsW;
                }
                float xs;
                switch (textAlign)
                {
                    case TextAlign.Right: xs = availW - inkR * scale; break;
                    case TextAlign.Center: xs = (availW - (inkR - inkL) * scale) * 0.5f - inkL * scale; break;
                    default: xs = -inkL * scale; break;
                }
                float baseY = li * lineH + font.ascent * scale;
                float pen = 0;
                int prev = -1;
                EmitRun(text, st, en - st, xs, baseY, scale, ref pen, ref prev);
                if (dots)
                    EmitRun("...", 0, 3, xs, baseY, scale, ref pen, ref prev);
            }
        }

        void EmitRun(string s, int offset, int count, float xs, float baseY, float scale, ref float pen, ref int prev)
        {
            for (int i = 0; i < count; i++)
            {
                if (IsSkipped(s[offset + i]))
                    continue;
                int gi = font.GlyphIndex(s[offset + i]);
                if (prev >= 0)
                    pen += font.Kerning(prev, gi) + SpacingSdf;
                ref readonly var g = ref font.GlyphAt(gi);
                if (g.w > 0 && g.h > 0)
                {
                    var uv = font.GlyphUv(g);
                    _tq[_tqCount++] = new TextQuad
                    {
                        X0 = xs + (pen + g.xOff) * scale,
                        Y0 = baseY + g.yOff * scale,
                        X1 = xs + (pen + g.xOff + g.w) * scale,
                        Y1 = baseY + (g.yOff + g.h) * scale,
                        U0 = uv.x,
                        V0 = uv.y,
                        U1 = uv.z,
                        V1 = uv.w,
                    };
                }
                pen += g.advance;
                prev = gi;
            }
        }

        // --- cizim (kutu katmanlarindan SONRA = ustte) ---
        void EmitText(float x0, float y0)
        {
            float availW = _rw - padding.x - padding.z;
            float availH = _rh - padding.y - padding.w;
            if (availW <= 0 || availH <= 0)
                return;
            EnsureTextLayout(availW, availH);
            if (_tqCount == 0)
                return;

            float ox = x0 + padding.x;
            float oy = y0 + padding.y;
            switch (textAlignV)
            {
                case LayoutAlign.Center: oy += (availH - _tbBlockH) * 0.5f; break;
                case LayoutAlign.End: oy += availH - _tbBlockH; break;
            }
            float scale = TextScale;

            if (textDebug != TextDebugView.Off)
            {
                // Ham rampa (USER.w > 0): alpha = d. Alpha gorunumu y<0 ile .a secer; Median y>0; Diff w=2 -> |median-a|*4.
                var dbg = textDebug == TextDebugView.Diff
                    ? new Vector4(0f, 0.5f, 0f, 2f)
                    : new Vector4(0f, textDebug == TextDebugView.Alpha ? -0.5f : 0.5f, 0f, 1f);
                var wf = UIFill.Solid(Color.white);
                EmitTextLayer(ox, oy, wf, dbg);
                return;
            }

            float cFill = WeightEdge(textWeight);
            float cOut = 0f;
            if (textOutlineWidth > 0 && scale > 0)
                cOut = Mathf.Max(cFill - textOutlineWidth / scale / TextSdfSpread, 0.05f);

            // Painter: golge -> kontur -> dolgu. Golge/kontur USER.y NEGATIF = gercek SDF (.a).
            if (textShadowColor.a > 0)
            {
                float s = textShadowBlur > 0 ? textShadowBlur * 0.5f / scale / TextSdfSpread : 0f;
                var user = new Vector4(s, -(cOut > 0 ? cOut : cFill), 0f, 0f);
                float sx = ox + textShadowOffset.x, sy = oy + textShadowOffset.y;
                var sc = textShadowColor * _tint;
                for (int i = 0; i < _tqCount; i++)
                {
                    ref readonly var g = ref _tq[i];
                    EmitQuad(sx + g.X0, sy + g.Y0, sx + g.X1, sy + g.Y1, g.U0, g.V0, g.U1, g.V1, sc, sc, false, user);
                }
            }
            if (cOut > 0 && textOutline.Visible)
                EmitTextLayer(ox, oy, textOutline, new Vector4(0f, -cOut, 0f, 0f));
            if (textFill.Visible)
                EmitTextLayer(ox, oy, textFill, new Vector4(0f, cFill, 0f, 0f));
        }

        // Glyph quad'lari bir UIFill ile: gradient BLOK bandina gore (dikey: blok yuksekligi, yatay: kullanilabilir genislik).
        void EmitTextLayer(float ox, float oy, in UIFill f, Vector4 user)
        {
            bool gh = f.IsHorizontal;
            float invBand = gh ? (_tbAvailW > 0 ? 1f / _tbAvailW : 0f) : (_tbBlockH > 0 ? 1f / _tbBlockH : 0f);
            for (int i = 0; i < _tqCount; i++)
            {
                ref readonly var g = ref _tq[i];
                Color c0 = f.Evaluate((gh ? g.X0 : g.Y0) * invBand) * _tint;
                Color c1 = f.Evaluate((gh ? g.X1 : g.Y1) * invBand) * _tint;
                EmitQuad(ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1, g.U0, g.V0, g.U1, g.V1, c0, c1, gh, user);
            }
        }
    }
}
