using System;

namespace DigitoyEngine;

// LayoutBox metin katmani: kutu vatandasi SDF metin. TextSprite ozellik paritesi
// (dikey gradient dolgu, kontur, golge — hepsi instance verisinden her frame
// okunur) + kutu ozellikleri: word wrap (padding icli rect'e), ellipsis (sigmayan
// satir/blok "..." ile kirpilir), yatay/dikey hizalama. Yerlesim cache'i yalniz
// font/metin/boyut/hiza/wrap/rect degisince kurulur (glyph quad'lari kutu-icerik
// uzayinda saklanir; stil degisimi rebuild istemez). Cizim ayni EmitQuad yolu —
// font atlasi UiPieces bolgesini de tasidigindan kutu + metin tek draw'a merge olur.
public sealed unsafe partial class LayoutBox
{
    // --- serilesen metin verisi ---
    [SerializeField] Font font;
    [SerializeField, ShowIf(nameof(font))] string text = "";
    [SerializeField, ShowIf(nameof(font))] float textSize = 32f;
    [SerializeField, ShowIf(nameof(font))] TextAlign textAlign = TextAlign.Left;
    [SerializeField, ShowIf(nameof(font))] LayoutAlign textAlignV = LayoutAlign.Start;
    [SerializeField, ShowIf(nameof(font))] bool textWrap = true;
    [SerializeField, ShowIf(nameof(font))] bool textEllipsis = true;
    [SerializeField, ShowIf(nameof(font))] Gradient textFill = new(Color.White);
    [SerializeField, ShowIf(nameof(font))] float textOutlineWidth;
    [SerializeField, ShowIf(nameof(font))] Gradient textOutline = new(Color.Black);
    [SerializeField, ShowIf(nameof(font))] Color textShadowColor;
    [SerializeField, ShowIf(nameof(font))] Vec2 textShadowOffset = new(0, 4);
    [SerializeField, ShowIf(nameof(font))] float textShadowBlur = 8f;

    // Yerlesimi etkileyenler kirletir; stil alanlari duz assignment (cache yok).
    public Font Font { get => font; set { if (!ReferenceEquals(font, value)) { font = value; MarkDirty(); } } }
    public string Text { get => text; set { value ??= ""; if (text != value) { text = value; MarkDirty(); } } }
    public float TextSize { get => textSize; set { if (textSize != value) { textSize = value; MarkDirty(); } } }
    public TextAlign TextAlign { get => textAlign; set { if (textAlign != value) { textAlign = value; MarkDirty(); } } }
    public LayoutAlign TextAlignV { get => textAlignV; set { if (textAlignV != value) { textAlignV = value; MarkDirty(); } } }
    public bool TextWrap { get => textWrap; set { if (textWrap != value) { textWrap = value; MarkDirty(); } } }
    public bool TextEllipsis { get => textEllipsis; set { if (textEllipsis != value) { textEllipsis = value; MarkDirty(); } } }
    public Gradient TextFill { get => textFill; set => textFill = value; }
    public float TextOutlineWidth { get => textOutlineWidth; set => textOutlineWidth = value; }
    public Gradient TextOutline { get => textOutline; set => textOutline = value; }
    public Color TextShadowColor { get => textShadowColor; set => textShadowColor = value; }
    public Vec2 TextShadowOffset { get => textShadowOffset; set => textShadowOffset = value; }
    public float TextShadowBlur { get => textShadowBlur; set => textShadowBlur = value; }

    const float TextSdfSpread = 21f; // shim font bake sabiti (TextSprite ile ayni)

    bool HasText => font != null && font.SdfSize > 0 && textSize > 0 && !string.IsNullOrEmpty(text);

    float TextScale => textSize / font.SdfSize;

    // --- yerlesim cache'i (icerik sol-ust orijinli lokal uzay) ---

    struct TextQuad
    {
        public float X0, Y0, X1, Y1; // icerik uzayi (y-down, blok ustu y=0)
        public float U0, V0, U1, V1; // V takasli (CPU atlas satir 0 ustte)
    }

    TextQuad[] _tq;
    int _tqCount;
    float _tbBlockH;
    Font _tbFont;
    string _tbText;
    float _tbSize, _tbAvailW, _tbAvailH;
    TextAlign _tbAlign;
    LayoutAlign _tbAlignV;
    bool _tbWrap, _tbEllipsis;

    // --- olcum (PreferredW/H'den cagrilir; metin = icerik) ---

    // Dogal (wrap'siz) en genis satir. Wrap + authored genislik varsa metin
    // kutuyu GENISLETMEZ (icine sarilir) — 0 doner, authored width kazanir.
    float TextPreferredW()
    {
        if (!HasText)
            return 0;
        if (textWrap && width > 0)
            return 0;
        float mx = 0;
        int pos = 0;
        while (NextLine(ref pos, float.MaxValue, out _, out _, out float w))
            mx = MathF.Max(mx, w);
        return mx * TextScale + padLeft + padRight;
    }

    // Satir sayisi * satir yuksekligi. Wrap genisligi = authored width (varsa);
    // grow/stretch'te nihai _rw olcum aninda bilinmez — bilinccli yaklasiklik.
    float TextPreferredH()
    {
        if (!HasText)
            return 0;
        float scale = TextScale;
        float wrapW = textWrap && width > 0
            ? MathF.Max(1f, width - padLeft - padRight) / scale
            : float.MaxValue;
        int n = 0, pos = 0;
        while (NextLine(ref pos, wrapW, out _, out _, out _))
            n++;
        return n * font.LineHeight * scale + padTop + padBottom;
    }

    // --- satir kirici (greedy word wrap; olcum + kurulum ayni yolu kullanir) ---
    // pos'tan bir satir tuketir; genislikler SDF-px uzayinda. Kirilim tercihen
    // son bosluktan (bosluk yutulur, genislige girmez); tek kelime sigmazsa
    // karakter kirilimi (satir basina en az 1 glyph — sonsuz dongu olmaz).
    bool NextLine(ref int pos, float availW, out int start, out int end, out float w)
    {
        string s = text;
        int len = s.Length;
        start = pos;
        end = pos;
        w = 0;
        if (pos > len)
            return false;
        int prev = -1, lastSp = -1;
        float wAtSp = 0;
        for (int i = start; i < len; i++)
        {
            char c = s[i];
            if (c == '\n')
            {
                end = i;
                pos = i + 1;
                return true;
            }
            int gi = font.GlyphIndex(c);
            float add = (prev >= 0 ? font.Kerning(prev, gi) : 0) + font.GlyphAt(gi).Advance;
            if (c == ' ')
            {
                lastSp = i;
                wAtSp = w;
            }
            else if (w + add > availW && i > start)
            {
                if (lastSp >= start)
                {
                    end = lastSp;
                    w = wAtSp;
                    pos = lastSp + 1;
                }
                else
                {
                    end = i;
                    pos = i;
                }
                return true;
            }
            w += add;
            prev = gi;
        }
        end = len;
        pos = len + 1; // sentinel: metin bitti
        return true;
    }

    // [st,en) icinden maxW'ye sigan glyph sayisi (ellipsis kirpma noktasi).
    int FitCount(int st, int en, float maxW, out float w)
    {
        w = 0;
        int prev = -1, n = 0;
        for (int i = st; i < en; i++)
        {
            int gi = font.GlyphIndex(text[i]);
            float add = (prev >= 0 ? font.Kerning(prev, gi) : 0) + font.GlyphAt(gi).Advance;
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
        _tbAvailW = availW;
        _tbAvailH = availH;
    }

    void BuildTextQuads(float availW, float availH)
    {
        float scale = TextScale;
        float wrapW = textWrap ? MathF.Max(availW, 1f) / scale : float.MaxValue;
        float lineH = font.LineHeight * scale;

        int total = 0;
        {
            int pos = 0;
            while (NextLine(ref pos, wrapW, out _, out _, out _))
                total++;
        }
        int lines = total;
        if (textEllipsis && lineH > 0)
            lines = Math.Max(1, Math.Min(total, (int)(availH / lineH + 0.001f)));

        _tbBlockH = lines * lineH;
        int cap = text.Length + 3 * lines + 1; // her satira olasi "..." payi
        if (_tq == null || _tq.Length < cap)
            _tq = new TextQuad[cap];
        _tqCount = 0;

        float clipW = availW / scale;
        float dotsW = font.MeasureLine("...".AsSpan());
        int p = 0;
        for (int li = 0; li < lines; li++)
        {
            NextLine(ref p, wrapW, out int st, out int en, out float w);
            // Ellipsis: blogun kirpilan son satiri VEYA availW'yi asan satir.
            bool dots = textEllipsis && ((li == lines - 1 && li < total - 1) || w > clipW);
            float lineW = w;
            if (dots)
            {
                en = FitCount(st, en, clipW - dotsW, out float wc);
                lineW = wc + dotsW;
            }
            float xs = textAlign switch
            {
                TextAlign.Right => availW - lineW * scale,
                TextAlign.Center => (availW - lineW * scale) * 0.5f,
                _ => 0f,
            };
            float baseY = li * lineH + font.Ascent * scale;
            float pen = 0;
            int prev = -1;
            EmitRun(text.AsSpan(st, en - st), xs, baseY, scale, ref pen, ref prev);
            if (dots)
                EmitRun("...".AsSpan(), xs, baseY, scale, ref pen, ref prev);
        }
    }

    // Glyph run'ini quad'lara doker; pen/prev SDF-px kalem durumu satir icinde surer.
    void EmitRun(ReadOnlySpan<char> s, float xs, float baseY, float scale, ref float pen, ref int prev)
    {
        float inv = 1f / font.AtlasSize;
        for (int i = 0; i < s.Length; i++)
        {
            int gi = font.GlyphIndex(s[i]);
            if (prev >= 0)
                pen += font.Kerning(prev, gi);
            ref readonly var g = ref font.GlyphAt(gi);
            if (g.W > 0 && g.H > 0)
            {
                _tq[_tqCount++] = new TextQuad
                {
                    X0 = xs + (pen + g.XOff) * scale,
                    Y0 = baseY + g.YOff * scale,
                    X1 = xs + (pen + g.XOff + g.W) * scale,
                    Y1 = baseY + (g.YOff + g.H) * scale,
                    U0 = g.AtlasX * inv,
                    V0 = (g.AtlasY + g.H) * inv,
                    U1 = (g.AtlasX + g.W) * inv,
                    V1 = g.AtlasY * inv,
                };
            }
            pen += g.Advance;
            prev = gi;
        }
    }

    // --- cizim (Encode sonunda, kutu quad'larindan SONRA = ayni layer'da ustte) ---

    void EncodeText(RenderQueue queue, in Mat4 wm, float x0, float y0, int layer)
    {
        float availW = _rw - padLeft - padRight;
        float availH = _rh - padTop - padBottom;
        if (availW <= 0 || availH <= 0)
            return;
        // Kendi Hidden eksenimiz metni kutu rect'inde kirpar (ata klibiyle kesisir).
        // Encode'un SON emisyonu oldugundan klip alanlarini mutasyonlamak guvenli.
        if (overflowX == OverflowMode.Hidden)
        {
            EnsureClip();
            _clX0 = MathF.Max(_clX0, x0);
            _clX1 = MathF.Min(_clX1, x0 + _rw);
        }
        if (overflowY == OverflowMode.Hidden)
        {
            EnsureClip();
            _clY0 = MathF.Max(_clY0, y0);
            _clY1 = MathF.Min(_clY1, y0 + _rh);
        }
        EnsureTextLayout(availW, availH);
        if (_tqCount == 0)
            return;

        float ox = x0 + padLeft;
        float oy = y0 + padTop + textAlignV switch
        {
            LayoutAlign.Center => (availH - _tbBlockH) * 0.5f,
            LayoutAlign.End => availH - _tbBlockH,
            _ => 0f,
        };
        var mat = font.Material;
        float scale = TextScale;

        // Kontur kenar merkezi 0.5'ten disari kayar (SDF deger uzayi; TextSprite ile ayni).
        float cOut = 0f;
        if (textOutlineWidth > 0 && scale > 0)
            cOut = MathF.Max(0.5f - textOutlineWidth / scale / TextSdfSpread, 0.05f);

        // Painter sirasi ayni layer'da submit sirasi: golge -> kontur -> dolgu.
        if (textShadowColor.a > 0)
        {
            float s = textShadowBlur > 0
                ? textShadowBlur * 0.5f / scale / TextSdfSpread : 0f;
            var user = new Vec4(s, cOut, 0f, 0f); // kontur varsa golge silueti kontur kenarindan
            float sx = ox + textShadowOffset.x, sy = oy + textShadowOffset.y;
            for (int i = 0; i < _tqCount; i++)
            {
                ref readonly var g = ref _tq[i];
                EmitQuad(queue, mat, in wm, sx + g.X0, sy + g.Y0, sx + g.X1, sy + g.Y1,
                    g.U0, g.V0, g.U1, g.V1, textShadowColor, textShadowColor, user, layer);
            }
        }
        if (cOut > 0)
        {
            var user = new Vec4(0f, cOut, 0f, 0f);
            for (int i = 0; i < _tqCount; i++)
            {
                ref readonly var g = ref _tq[i];
                EmitQuad(queue, mat, in wm, ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1,
                    g.U0, g.V0, g.U1, g.V1,
                    TextGradAt(in textOutline, g.Y0),
                    TextGradAt(in textOutline, g.Y1),
                    user, layer);
            }
        }
        for (int i = 0; i < _tqCount; i++)
        {
            ref readonly var g = ref _tq[i];
            EmitQuad(queue, mat, in wm, ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1,
                g.U0, g.V0, g.U1, g.V1,
                TextGradAt(in textFill, g.Y0),
                TextGradAt(in textFill, g.Y1),
                default, layer);
        }
    }

    // Dikey lineer gradient BLOK bandina gore (icerik uzayi: blok ustu y=0).
    Color TextGradAt(in Gradient g, float y)
        => g.At(_tbBlockH > 0 ? y / _tbBlockH : 0f);
}
