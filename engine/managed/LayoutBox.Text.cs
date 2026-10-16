using System;

namespace DigitoyEngine;

// LayoutBox metin katmani: kutu vatandasi SDF metin. TextSprite ozellik paritesi
// (dikey gradient dolgu, kontur, golge â€” hepsi instance verisinden her frame
// okunur) + kutu ozellikleri: word wrap (padding icli rect'e), ellipsis (sigmayan
// satir/blok "..." ile kirpilir), yatay/dikey hizalama. Yerlesim cache'i yalniz
// font/metin/boyut/hiza/wrap/rect degisince kurulur (glyph quad'lari kutu-icerik
// uzayinda saklanir; stil degisimi rebuild istemez). Cizim ayni EmitQuad yolu â€”
// font atlasi UiPieces bolgesini de tasidigindan kutu + metin tek draw'a merge olur.
public sealed unsafe partial class LayoutBox
{
    // --- serilesen metin verisi ---
    // Font bir AD'dir (FontTable; "" = varsayilan): asset referansi tasinmaz, bagimlilik tablodan yurur.
    // Metin stili alanlari metin girilince gorunur (loc bagli metin de "dolu" sayilir — editor DocVisible).
    [SerializeField, FontName] string font = "";
    [SerializeField] string text = "";
    [SerializeField, ShowIf(nameof(text))] float textSize = 32f;
    [SerializeField, ShowIf(nameof(text))] TextAlign textAlign = TextAlign.Left;
    [SerializeField, ShowIf(nameof(text))] LayoutAlign textAlignV = LayoutAlign.Start;
    [SerializeField, ShowIf(nameof(text))] bool textWrap = true;
    [SerializeField, ShowIf(nameof(text))] bool textEllipsis = true;
    [SerializeField, ShowIf(nameof(text))] float textWeight;
    [SerializeField, ShowIf(nameof(text))] float textSkew;
    [SerializeField, ShowIf(nameof(text))] float textSpacing;
    [SerializeField, ShowIf(nameof(text))] Gradient textFill = new(Color.White);
    [SerializeField, ShowIf(nameof(text))] float textOutlineWidth;
    [SerializeField, ShowIf(nameof(text))] Gradient textOutline = new(Color.Black);
    [SerializeField, ShowIf(nameof(text))] Color textShadowColor;
    [SerializeField, ShowIf(nameof(text))] Vec2 textShadowOffset = new(0, 4);
    [SerializeField, ShowIf(nameof(text))] float textShadowBlur = 8f;

    // Ad -> Font cozumu PUSH modeli: ad degisince (setter/OnValidate/OnEnable) ve tablo degisince (Fonts.Changed ->
    // tum etkin kutular) cozulur ve kutu kirlenir; sicak yolda surum kiyasi yok, getter duz alan okur.
    Font _rf;
    public Font Font => _rf ?? ResolveFont();

    Font ResolveFont()
    {
        var f = Fonts.Resolve(font);
        if (!ReferenceEquals(f, _rf))
        {
            _rf = f;
            MarkDirty();
        }
        return f;
    }

    // Etkin kutular (OnEnable/OnDisable); tablo degisince hepsi yeniden cozer. Motor tipi statigi (ALC reload'dan
    // etkilenmez); kutu basina alloc yok (List buyumesi haric).
    static readonly System.Collections.Generic.List<LayoutBox> _fontUsers = new();
    static bool _fontsHooked;

    static void OnFontsChanged()
    {
        int w = 0;
        for (int i = 0; i < _fontUsers.Count; i++)
        {
            var b = _fontUsers[i];
            if (b._destroyed)
                continue; // OnDisable kacirilmis olu kutu: listeden duser
            _fontUsers[w++] = b;
            b.ResolveFont();
        }
        _fontUsers.RemoveRange(w, _fontUsers.Count - w);
    }

    void HookFonts()
    {
        if (!_fontsHooked) { _fontsHooked = true; Fonts.Changed += OnFontsChanged; }
        _fontUsers.Add(this);
        ResolveFont();
    }

    void UnhookFonts()
    {
        int i = _fontUsers.LastIndexOf(this);
        if (i >= 0)
        {
            int last = _fontUsers.Count - 1;
            _fontUsers[i] = _fontUsers[last];
            _fontUsers.RemoveAt(last);
        }
    }

    // Yerlesimi etkileyenler kirletir; stil alanlari duz assignment (cache yok).
    public string FontName { get => font; set { value ??= ""; if (font != value) { font = value; ResolveFont(); MarkDirty(); } } }
    public string Text { get => text; set { value ??= ""; if (text != value) { text = value; MarkDirty(); } } }
    // Parametreli ceviri: bagli sablon ("{0} coins") args ile yazilir, dil degisince ayni args'la yeniden.
    // Kod key bilmez. Bagli degilse mevcut Text sablon sayilir (tek seferlik). Bkz. Localize.
    public void Format(params object[] args) => Localize.Format(this, nameof(text), args);
    public float TextSize { get => textSize; set { if (textSize != value) { textSize = value; MarkDirty(); } } }
    public TextAlign TextAlign { get => textAlign; set { if (textAlign != value) { textAlign = value; MarkDirty(); } } }
    public LayoutAlign TextAlignV { get => textAlignV; set { if (textAlignV != value) { textAlignV = value; MarkDirty(); } } }
    public bool TextWrap { get => textWrap; set { if (textWrap != value) { textWrap = value; MarkDirty(); } } }
    public bool TextEllipsis { get => textEllipsis; set { if (textEllipsis != value) { textEllipsis = value; MarkDirty(); } } }
    // SDF kalinlik (kenar esigi kaydirma; stroke her yone kalinlasir/incelir,
    // yerlesim/olcum degismez) ve fake italic (blok merkezli x-shear, 0 = dik;
    // matris-only). TextSprite paritesi. Weight birimi SDF px: 0 = normal, + kalin,
    // - ince (~[-5, +3]); kontur/golge de kayan kenardan hesaplanir. Ikisi de
    // her frame instance verisinden okunur, MarkDirty gerekmez.
    public float TextWeight { get => textWeight; set => textWeight = value; }
    public float TextSkew { get => textSkew; set => textSkew = value; }
    // Harf araligi: advance'e eklenen SABIT px (scale'den bagimsiz; olcum/wrap'a
    // girer -> dirty). Kalem SDF-px uzayinda ilerledigi icin spacing/scale eklenir.
    public float TextSpacing { get => textSpacing; set { if (textSpacing != value) { textSpacing = value; MarkDirty(); } } }
    public Gradient TextFill { get => textFill; set => textFill = value; }
    public float TextOutlineWidth { get => textOutlineWidth; set => textOutlineWidth = value; }
    public Gradient TextOutline { get => textOutline; set => textOutline = value; }
    public Color TextShadowColor { get => textShadowColor; set => textShadowColor = value; }
    public Vec2 TextShadowOffset { get => textShadowOffset; set => textShadowOffset = value; }
    public float TextShadowBlur { get => textShadowBlur; set => textShadowBlur = value; }

    const float TextSdfSpread = 21f; // shim font bake sabiti (TextSprite ile ayni)

    bool HasText
    {
        get
        {
            if (textSize <= 0 || string.IsNullOrEmpty(text))
                return false;
            var f = Font;
            return f != null && f.SdfSize > 0;
        }
    }

    // --- rich text (RichText.cs): tag'ler yerlesim kurulurken tuketilir; kalem uzayi PX (span basina font/boyut
    // degisebildiginden tek bir SDF-px olcegi yok: her glyph kendi scale = Size / Font.EmPx ile px'e gecer). ---

    readonly RichText.Frame[] _rtStack = new RichText.Frame[RichText.MaxDepth]; // ic ice tag yigini (rebuild)

    RichText.Cursor TextStart() => RichText.Cursor.Start(textSize, Font);

    static float ScaleOf(in RichText.State st) => st.Size / st.Font.EmPx;

    // --- yerlesim cache'i (icerik sol-ust orijinli lokal uzay, px) ---

    const byte QfItalic = 1, QfUnderline = 2, QfColor = 4, QfBold = 8;

    struct TextQuad
    {
        public float X0, Y0, X1, Y1; // icerik uzayi (y-down, blok ustu y=0)
        public float U0, V0, U1, V1; // V takasli (CPU atlas satir 0 ustte)
        public float Base, Size;     // satir baseline'i (italik shear merkezi), em px (kontur/golge olcegi)
        public Color Color;          // QfColor: span rengi (dolgu gradient'ini ezer)
        public byte Flags, FontIdx;  // FontIdx: _tbFonts indeksi (materyal gruplama)
    }

    struct LinkRect
    {
        public float X0, Y0, X1, Y1; // icerik uzayi
        public int IdStart, IdLen;   // text icindeki id araligi
    }

    TextQuad[] _tq;
    int _tqCount;
    LinkRect[] _links;
    int _linkCount;
    Font[] _tbFonts = new Font[4];
    int _tbFontCount;
    float _tbBlockH;
    Font _tbFont;
    int _tbBind, _tbFontsVer;
    string _tbText;
    float _tbSize, _tbAvailW, _tbAvailH, _tbSpacing;
    TextAlign _tbAlign;
    LayoutAlign _tbAlignV;
    bool _tbWrap, _tbEllipsis;

    public bool HasLinks => _linkCount > 0;

    // Test kancalari (editor testleri; davranis degil gozlem).
    internal int TextQuadCount => _tqCount;
    internal int TextFontCount => _tbFontCount;
    internal int LinkCount => _linkCount;

    // --- olcum (PreferredW/H'den cagrilir; metin = icerik) ---

    struct Line
    {
        public RichText.Cursor Start; // satir basi (konum + durum): kurulum buradan yeniden tarar
        public int End;               // [Start.Pos, End) — kirilan bosluk / '\n' haric
        public float W, InkL, InkR;   // advance toplami, murekkep sol/sag kenari (px, kalem orijinine gore)
        public float Asc, H;          // satirdaki span'larin maks ascent / satir yuksekligi (px)
    }

    // Dogal (wrap'siz) en genis satir. Wrap + authored genislik varsa metin
    // kutuyu GENISLETMEZ (icine sarilir) — 0 doner, authored width kazanir.
    float TextPreferredW()
    {
        if (!HasText)
            return 0;
        if (textWrap && width > 0)
            return 0;
        float mx = 0;
        var cur = TextStart();
        // Yatayda murekkep kutusu: ilk glyph'in sol boslugu ve son glyph'in
        // advance'i sayilmaz (kutu metne tam sarilir). Dikey metrik kalir.
        while (NextLine(ref cur, float.MaxValue, out var ln))
            mx = MathF.Max(mx, ln.InkR - ln.InkL);
        return mx + padLeft + padRight;
    }

    // Satir yukseklikleri toplami (karisik boyutta satir basina maks). Wrap genisligi = authored width (varsa);
    // grow/stretch'te nihai _rw olcum aninda bilinmez — bilincli yaklasiklik.
    float TextPreferredH()
    {
        if (!HasText)
            return 0;
        float wrapW = textWrap && width > 0
            ? MathF.Max(1f, width - padLeft - padRight)
            : float.MaxValue;
        float h = 0;
        var cur = TextStart();
        while (NextLine(ref cur, wrapW, out var ln))
            h += ln.H;
        return h + padTop + padBottom;
    }

    // --- satir kirici (greedy word wrap; olcum + kurulum ayni yolu kullanir) ---
    // cur'dan bir satir tuketir; genislikler px. Kirilim tercihen son bosluktan (bosluk yutulur, genislige
    // girmez; durum o noktaya YENIDEN TARAMAYLA geri sarilir — yigin kopyasi gerekmez), tek kelime sigmazsa
    // karakter kirilimi (satir basina en az 1 glyph — sonsuz dongu olmaz). Sigma testi murekkep genisligiyle
    // (inkR-inkL <= availW) — Grow'da availW = murekkep olcusu, advance ile karsilastirmak sahte wrap uretirdi.
    // Kerning yalniz ayni font+boyut ardisik glyph'ler arasinda; textSpacing her glyph arasina px.
    bool NextLine(ref RichText.Cursor cur, float availW, out Line ln)
    {
        string s = text;
        int len = s.Length;
        ln = default;
        ln.Start = cur;
        if (cur.Pos > len)
            return false;
        int i = cur.Pos;
        float asc = cur.St.Font.Ascent * ScaleOf(in cur.St);
        float lh = cur.St.Font.LineHeight * ScaleOf(in cur.St);
        float w = 0, inkL = 0, inkR = 0;
        int n = 0, prevGi = -1, lastSp = -1;
        Font prevFont = null;
        float prevScale = 0, wAtSp = 0, inkRAtSp = 0, ascAtSp = 0, lhAtSp = 0;
        int end, pos;
        while (i < len)
        {
            char c = s[i];
            if (c == '<' && RichText.TryTag(s, ref i, ref cur.St, _rtStack, ref cur.Depth, textSize))
                continue;
            if (c == '\n')
            {
                end = i;
                pos = i + 1;
                goto Done;
            }
            var font = cur.St.Font;
            float scale = ScaleOf(in cur.St);
            int gi = font.GlyphIndex(c);
            ref readonly var g = ref font.GlyphAt(gi);
            float kern = prevGi >= 0 && ReferenceEquals(prevFont, font) && prevScale == scale ? font.Kerning(prevGi, gi) * scale : 0;
            float gap = prevGi >= 0 ? kern + textSpacing : 0;
            float add = gap + g.Advance * scale;
            bool ink = g.W > 0;
            float l = prevGi < 0 && ink ? g.InkL * scale : inkL;
            float r = ink ? MathF.Max(inkR, w + gap + g.InkR * scale) : inkR;
            if (c == ' ')
            {
                lastSp = i;
                wAtSp = w;
                inkRAtSp = inkR;
                ascAtSp = asc;
                lhAtSp = lh;
            }
            else if (r - l > availW && n > 0)
            {
                if (lastSp >= ln.Start.Pos)
                {
                    // Bosluktan sonra yerlesmis glyph'ler (ve metrikleri) bu satira ait degil: snapshot'a don.
                    end = lastSp;
                    w = wAtSp;
                    inkR = inkRAtSp;
                    asc = ascAtSp;
                    lh = lhAtSp;
                    pos = lastSp + 1;
                    cur = ln.Start;
                    Advance(ref cur, pos); // durum = bosluktan sonraki durum
                }
                else
                {
                    end = i;
                    pos = i;
                }
                goto Done;
            }
            asc = MathF.Max(asc, font.Ascent * scale);
            lh = MathF.Max(lh, font.LineHeight * scale);
            inkL = l;
            inkR = r;
            w += add;
            prevGi = gi;
            prevFont = font;
            prevScale = scale;
            n++;
            i++;
        }
        end = len;
        pos = len + 1; // sentinel: metin bitti
    Done:
        cur.Pos = pos;
        ln.End = end;
        ln.W = w;
        ln.InkL = inkL;
        ln.InkR = inkR;
        ln.Asc = asc;
        ln.H = lh;
        return true;
    }

    // cur'u to konumuna ilerletir (tag'leri tuketerek; olcum yok).
    void Advance(ref RichText.Cursor cur, int to)
    {
        string s = text;
        int i = cur.Pos;
        while (i < to)
        {
            if (s[i] == '<' && RichText.TryTag(s, ref i, ref cur.St, _rtStack, ref cur.Depth, textSize))
                continue;
            i++;
        }
        cur.Pos = i;
    }

    // Satirin basindan maxW'ye sigan glyph'lerin bitis indeksi (ellipsis kirpma noktasi); cut = o noktadaki durum.
    int FitCount(in Line ln, float maxW, out float w, out RichText.Cursor cut)
    {
        string s = text;
        var cur = ln.Start;
        int i = cur.Pos;
        w = 0;
        int prevGi = -1;
        Font prevFont = null;
        float prevScale = 0;
        while (i < ln.End)
        {
            if (s[i] == '<' && RichText.TryTag(s, ref i, ref cur.St, _rtStack, ref cur.Depth, textSize))
                continue;
            var font = cur.St.Font;
            float scale = ScaleOf(in cur.St);
            int gi = font.GlyphIndex(s[i]);
            float kern = prevGi >= 0 && ReferenceEquals(prevFont, font) && prevScale == scale ? font.Kerning(prevGi, gi) * scale : 0;
            float add = (prevGi >= 0 ? kern + textSpacing : 0) + font.GlyphAt(gi).Advance * scale;
            if (w + add > maxW)
                break;
            w += add;
            prevGi = gi;
            prevFont = font;
            prevScale = scale;
            i++;
        }
        cur.Pos = i;
        cut = cur;
        return i;
    }

    // "..." genisligi (px): oncesinde metin varken her noktanin onune spacing girer.
    float DotsWidth(in RichText.State st)
        => st.Font.MeasureLine("...".AsSpan()) * ScaleOf(in st) + 3 * textSpacing;

    int BindSum()
    {
        int v = 0;
        for (int i = 0; i < _tbFontCount; i++)
            v += _tbFonts[i].BindVersion;
        return v;
    }

    void EnsureTextLayout(float availW, float availH)
    {
        var font = Font;
        if (_tq != null && ReferenceEquals(_tbFont, font) && _tbBind == BindSum() && _tbFontsVer == Fonts.Version
            && _tbText == text
            && _tbSize == textSize && _tbAlign == textAlign && _tbAlignV == textAlignV
            && _tbWrap == textWrap && _tbEllipsis == textEllipsis
            && _tbSpacing == textSpacing
            && _tbAvailW == availW && _tbAvailH == availH)
            return;
        BuildTextQuads(availW, availH);
        _tbFont = font;
        _tbBind = BindSum();
        _tbFontsVer = Fonts.Version;
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

    // Satir ici kalem durumu: konum + kerning oncesi + alt cizgi / link run'lari (satir sonunda flush).
    struct Pen
    {
        public float X;
        public int PrevGi;
        public Font PrevFont;
        public float PrevScale;
        public bool UlActive, UlHasColor;
        public float UlX0, UlX1, UlBase, UlSize;
        public Font UlFont;
        public Color UlColor;
        public bool LinkActive;
        public float LinkX0, LinkX1, LinkTop, LinkH;
        public int LinkStart, LinkLen;
    }

    void BuildTextQuads(float availW, float availH)
    {
        // FP toleransi (px): Grow'da availW = TextPreferredW'nin olcumu; birkac ULP kucuk cikarsa tam sigan
        // satir sahte wrap/ellipsis'e duserdi. Ceyrek px gorsel olarak farkedilmez.
        const float fitEps = 0.25f;
        float wrapW = textWrap ? MathF.Max(availW, 1f) + fitEps : float.MaxValue;

        // 1. gecis: satir sayisi + (ellipsis) yukseklige sigan satir sayisi.
        int total = 0, fit = 0;
        {
            float hAcc = 0;
            var cur = TextStart();
            while (NextLine(ref cur, wrapW, out var ln))
            {
                total++;
                hAcc += ln.H;
                if (hAcc <= availH + 0.001f)
                    fit++;
            }
        }
        int lines = textEllipsis ? Math.Max(1, Math.Min(total, fit)) : total;

        int cap = text.Length + 3 * lines + 1; // her satira olasi "..." payi (alt cizgi quad'lari gerekirse buyutur)
        if (_tq == null || _tq.Length < cap)
            _tq = new TextQuad[cap];
        _tqCount = 0;
        _linkCount = 0;
        _tbFontCount = 0;
        FontIndex(Font);

        float clipW = availW + fitEps;
        float y = 0;
        var c2 = TextStart();
        for (int li = 0; li < lines; li++)
        {
            NextLine(ref c2, wrapW, out var ln);
            // Ellipsis: blogun kirpilan son satiri VEYA availW'yi asan satir.
            bool dots = textEllipsis && ((li == lines - 1 && li < total - 1) || ln.InkR - ln.InkL > clipW);
            int en = ln.End;
            var cut = ln.Start;
            if (dots)
            {
                float dotsW = DotsWidth(in ln.Start.St);
                en = FitCount(in ln, clipW - dotsW, out float wc, out cut);
                // "..." ile biten satirda advance tabanli olcu yeterli.
                ln.InkL = 0;
                ln.InkR = wc + dotsW;
            }
            // Murekkep kutusuna gore hizalama: sol kenar = ilk glyph'in murekkebi.
            float xs = textAlign switch
            {
                TextAlign.Right => availW - ln.InkR,
                TextAlign.Center => (availW - (ln.InkR - ln.InkL)) * 0.5f - ln.InkL,
                _ => -ln.InkL,
            };
            float baseY = y + ln.Asc;
            var pen = new Pen { PrevGi = -1 };
            var cur = ln.Start;
            EmitRun(ref cur, en, xs, baseY, y, ln.H, ref pen);
            if (dots)
                EmitChars("...".AsSpan(), in cut.St, xs, baseY, y, ln.H, ref pen);
            FlushUnderline(ref pen);
            FlushLink(ref pen);
            y += ln.H;
        }
        _tbBlockH = y;
    }

    // [cur.Pos, end) araligini quad'lara doker (tag'ler tuketilir); pen satir icinde surer.
    void EmitRun(ref RichText.Cursor cur, int end, float xs, float baseY, float lineTop, float lineH, ref Pen pen)
    {
        string s = text;
        int i = cur.Pos;
        while (i < end)
        {
            if (s[i] == '<' && RichText.TryTag(s, ref i, ref cur.St, _rtStack, ref cur.Depth, textSize))
                continue;
            EmitGlyph(s[i], in cur.St, xs, baseY, lineTop, lineH, ref pen);
            i++;
        }
        cur.Pos = i;
    }

    void EmitChars(ReadOnlySpan<char> s, in RichText.State st, float xs, float baseY, float lineTop, float lineH, ref Pen pen)
    {
        for (int i = 0; i < s.Length; i++)
            EmitGlyph(s[i], in st, xs, baseY, lineTop, lineH, ref pen);
    }

    void EmitGlyph(char c, in RichText.State st, float xs, float baseY, float lineTop, float lineH, ref Pen pen)
    {
        var font = st.Font;
        float scale = ScaleOf(in st);
        int gi = font.GlyphIndex(c);
        if (pen.PrevGi >= 0)
            pen.X += (ReferenceEquals(pen.PrevFont, font) && pen.PrevScale == scale ? font.Kerning(pen.PrevGi, gi) * scale : 0) + textSpacing;

        // Alt cizgi run'i: ayni font/boyut/renk surdukce tek quad; degisince ya da span bitince flush.
        if (st.Underline)
        {
            if (!pen.UlActive || !ReferenceEquals(pen.UlFont, font) || pen.UlSize != st.Size
                || pen.UlHasColor != st.HasColor || (st.HasColor && !SameColor(pen.UlColor, st.Color)))
            {
                FlushUnderline(ref pen);
                pen.UlActive = true;
                pen.UlX0 = xs + pen.X;
                pen.UlBase = baseY;
                pen.UlSize = st.Size;
                pen.UlFont = font;
                pen.UlColor = st.Color;
                pen.UlHasColor = st.HasColor;
            }
        }
        else if (pen.UlActive)
            FlushUnderline(ref pen);

        // Link run'i: ayni id surdukce tek rect (satir basina).
        if (st.InLink)
        {
            if (!pen.LinkActive || pen.LinkStart != st.LinkStart || pen.LinkLen != st.LinkLen)
            {
                FlushLink(ref pen);
                pen.LinkActive = true;
                pen.LinkX0 = xs + pen.X;
                pen.LinkTop = lineTop;
                pen.LinkH = lineH;
                pen.LinkStart = st.LinkStart;
                pen.LinkLen = st.LinkLen;
            }
        }
        else if (pen.LinkActive)
            FlushLink(ref pen);

        ref readonly var g = ref font.GlyphAt(gi);
        if (g.W > 0 && g.H > 0 && g.Sprite != null)
        {
            // V takasi: sayfada satir 0 ustte, quad V0 = alt kenar.
            var sp = g.Sprite;
            byte flags = 0;
            if (st.Italic) flags |= QfItalic;
            if (st.Bold) flags |= QfBold;
            if (st.HasColor) flags |= QfColor;
            PushQuad(new TextQuad
            {
                X0 = xs + pen.X + g.XOff * scale,
                Y0 = baseY + g.YOff * scale,
                X1 = xs + pen.X + (g.XOff + g.W) * scale,
                Y1 = baseY + (g.YOff + g.H) * scale,
                U0 = sp.U0,
                V0 = sp.V1,
                U1 = sp.U1,
                V1 = sp.V0,
                Base = baseY,
                Size = st.Size,
                Color = st.Color,
                Flags = flags,
                FontIdx = FontIndex(font),
            });
        }
        pen.X += g.Advance * scale;
        pen.UlX1 = pen.LinkX1 = xs + pen.X;
        pen.PrevGi = gi;
        pen.PrevFont = font;
        pen.PrevScale = scale;
    }

    // Alt cizgi = fontun sayfasindaki UiPieces solid parcasindan duz quad (ayni materyal, tek draw);
    // konum/kalinlik em oranindan (TTF post tablosu okunmuyor). Kontur gecisine girmez (EncodeText).
    void FlushUnderline(ref Pen pen)
    {
        if (!pen.UlActive)
            return;
        pen.UlActive = false;
        var page = pen.UlFont.Page;
        if (page == null || !page.HasPieces || pen.UlX1 <= pen.UlX0)
            return;
        // Nokta UV OLMAZ: SDF shader kenar rampasini UV turevinden alir (turev 0 -> NaN, gorunmez). Kutu
        // dolgusu gibi solid parcanin IC 16 px'i (8 px pad: bilinear komsu sizmasi yok), V takasli.
        pen.UlFont.Pieces.Uv(UiPieces.SolidX + 8, UiPieces.SolidY + 8, 16, 16, out float u0, out float v0, out float u1, out float v1);
        float y0 = pen.UlBase + pen.UlSize * RichText.UnderlineOffset;
        float th = MathF.Max(1f, pen.UlSize * RichText.UnderlineThickness);
        PushQuad(new TextQuad
        {
            X0 = pen.UlX0, Y0 = y0, X1 = pen.UlX1, Y1 = y0 + th,
            U0 = u0, V0 = v1, U1 = u1, V1 = v0,
            Base = pen.UlBase, Size = pen.UlSize, Color = pen.UlColor,
            Flags = (byte)(QfUnderline | (pen.UlHasColor ? QfColor : 0)),
            FontIdx = FontIndex(pen.UlFont),
        });
    }

    void FlushLink(ref Pen pen)
    {
        if (!pen.LinkActive)
            return;
        pen.LinkActive = false;
        if (pen.LinkX1 <= pen.LinkX0)
            return;
        if (_links == null)
            _links = new LinkRect[4];
        else if (_linkCount == _links.Length)
            Array.Resize(ref _links, _links.Length * 2);
        _links[_linkCount++] = new LinkRect
        {
            X0 = pen.LinkX0, Y0 = pen.LinkTop, X1 = pen.LinkX1, Y1 = pen.LinkTop + pen.LinkH,
            IdStart = pen.LinkStart, IdLen = pen.LinkLen,
        };
    }

    void PushQuad(in TextQuad q)
    {
        if (_tqCount == _tq.Length)
            Array.Resize(ref _tq, Math.Max(16, _tq.Length * 2));
        _tq[_tqCount++] = q;
    }

    byte FontIndex(Font f)
    {
        for (int i = 0; i < _tbFontCount; i++)
            if (ReferenceEquals(_tbFonts[i], f))
                return (byte)i;
        if (_tbFontCount == _tbFonts.Length)
            Array.Resize(ref _tbFonts, _tbFonts.Length * 2);
        _tbFonts[_tbFontCount] = f;
        return (byte)_tbFontCount++;
    }

    static bool SameColor(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    // --- link vurusu (<link=id>): pointer handler'i olan component sorar; LayoutBox'in kendi handler'i yok ---

    // e.hit.Point (down anindaki dunya noktasi) -> icerik uzayi -> link id; yoksa null. Eslesmede tek Substring.
    public string LinkAt(PointerEvent e) => e == null ? null : LinkAtWorld(e.hit.Point);

    public string LinkAtWorld(Vec3 world)
    {
        if (_linkCount == 0)
            return null;
        ref var wm = ref transform._getWorldMatrix();
        if (!Mat4.Inverse(ref wm, out var inv))
            return null;
        var lp = Mat4.TransformPoint(ref inv, world);
        return LinkAtLocal(new Vec2(lp.x, lp.y));
    }

    // Kutu lokal noktasi (pivot orijinli, y-down).
    public string LinkAtLocal(Vec2 local)
    {
        if (_linkCount == 0)
            return null;
        float availH = _rh - padTop - padBottom;
        float cx = local.x - (-pivot.x * _rw + padLeft);
        float cy = local.y - (-pivot.y * _rh + padTop + TextVAlignOffset(availH));
        for (int i = 0; i < _linkCount; i++)
        {
            ref readonly var l = ref _links[i];
            if (cx >= l.X0 && cx <= l.X1 && cy >= l.Y0 && cy <= l.Y1)
                return text.Substring(l.IdStart, l.IdLen);
        }
        return null;
    }

    float TextVAlignOffset(float availH) => textAlignV switch
    {
        LayoutAlign.Center => (availH - _tbBlockH) * 0.5f,
        LayoutAlign.End => availH - _tbBlockH,
        _ => 0f,
    };

    // --- cizim (Encode sonunda, kutu quad'larindan SONRA = ayni layer'da ustte) ---

    void EncodeText(RenderQueue queue, in Mat4 wm, float x0, float y0, int layer)
    {
        float availW = _rw - padLeft - padRight;
        float availH = _rh - padTop - padBottom;
        if (availW <= 0 || availH <= 0)
            return;
        // Kendi Hidden eksenimiz metni kutu rect'inde kirpar (ata klibiyle kesisir).
        // Encode'un SON emisyonu oldugundan klip alanlarini mutasyonlamak guvenli.
        if (overflowX == OverflowMode.Hidden || overflowX == OverflowMode.Scroll)
        {
            EnsureClip();
            _clX0 = MathF.Max(_clX0, x0);
            _clX1 = MathF.Min(_clX1, x0 + _rw);
        }
        if (overflowY == OverflowMode.Hidden || overflowY == OverflowMode.Scroll)
        {
            EnsureClip();
            _clY0 = MathF.Max(_clY0, y0);
            _clY1 = MathF.Min(_clY1, y0 + _rh);
        }
        EnsureTextLayout(availW, availH);
        if (_tqCount == 0)
            return;

        float ox = x0 + padLeft;
        float oy = y0 + padTop + TextVAlignOffset(availH);

        // Italik shear (SADECE geometri; TextSprite paritesi): blok dikey merkezi
        // etrafinda x' = x - skew*(y - cy). Matrisin y kolonuna x kolonu karisir,
        // merkez sabit kalsin diye translation geri alinir. Rebuild/klip etkisi yok
        // (klip quad kirpmasi icerik uzayinda, shear world matrisinde).
        Mat4 twm = wm;
        if (textSkew != 0f)
            Shear(ref twm, -textSkew, oy + _tbBlockH * 0.5f); // y-down: ust saga yatar

        // Font (sayfa) basina grup: materyal degisir; ayni sayfadaki gruplar RenderQueue'da merge olur.
        // Painter sirasi ayni layer'da submit sirasi: golge -> kontur -> dolgu.
        // Golge/kontur USER.y NEGATIF: MSDF sayfada gercek SDF (.a) okunur — kenardan
        // uzak/kaydirilmis esikler median'da guvenilir degil; R8 sayfada isaret yok sayilir.
        bool oh = textOutline.IsHorizontal, fh = textFill.IsHorizontal;
        for (int fi = 0; fi < _tbFontCount; fi++)
        {
            var font = _tbFonts[fi];
            var mat = font.Material.ForBlend(BlendMode).ForEffects(Effects);
            float emPx = font.EmPx;
            if (textShadowColor.a > 0)
            {
                float sx = ox + textShadowOffset.x, sy = oy + textShadowOffset.y;
                for (int i = 0; i < _tqCount; i++)
                {
                    ref readonly var g = ref _tq[i];
                    if (g.FontIdx != fi) continue;
                    float scale = g.Size / emPx;
                    Edges(in g, scale, out float cFill, out float cOut);
                    float s = textShadowBlur > 0 ? textShadowBlur * 0.5f / scale / TextSdfSpread : 0f;
                    var user = new Vec4(s, -(cOut > 0 ? cOut : cFill), 0f, 0f); // kontur varsa golge silueti kontur kenarindan
                    QuadMatrix(in twm, in g, oy, out Mat4 qm);
                    EmitQuad(queue, mat, in qm, sx + g.X0, sy + g.Y0, sx + g.X1, sy + g.Y1,
                        g.U0, g.V0, g.U1, g.V1, textShadowColor, textShadowColor, false, user, layer);
                }
            }
            if (textOutlineWidth > 0)
            {
                for (int i = 0; i < _tqCount; i++)
                {
                    ref readonly var g = ref _tq[i];
                    if (g.FontIdx != fi || (g.Flags & QfUnderline) != 0) continue;
                    Edges(in g, g.Size / emPx, out _, out float cOut);
                    if (cOut <= 0) continue;
                    var user = new Vec4(0f, -cOut, 0f, 0f);
                    QuadMatrix(in twm, in g, oy, out Mat4 qm);
                    EmitQuad(queue, mat, in qm, ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1,
                        g.U0, g.V0, g.U1, g.V1,
                        TextGradAt(in textOutline, oh ? g.X0 : g.Y0),
                        TextGradAt(in textOutline, oh ? g.X1 : g.Y1),
                        oh, user, layer);
                }
            }
            for (int i = 0; i < _tqCount; i++)
            {
                ref readonly var g = ref _tq[i];
                if (g.FontIdx != fi) continue;
                Edges(in g, g.Size / emPx, out float cFill, out _);
                var user = new Vec4(0f, cFill, 0f, 0f);
                QuadMatrix(in twm, in g, oy, out Mat4 qm);
                if ((g.Flags & QfColor) != 0)
                    EmitQuad(queue, mat, in qm, ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1,
                        g.U0, g.V0, g.U1, g.V1, g.Color, g.Color, false, user, layer);
                else
                    EmitQuad(queue, mat, in qm, ox + g.X0, oy + g.Y0, ox + g.X1, oy + g.Y1,
                        g.U0, g.V0, g.U1, g.V1,
                        TextGradAt(in textFill, fh ? g.X0 : g.Y0),
                        TextGradAt(in textFill, fh ? g.X1 : g.Y1),
                        fh, user, layer);
            }
        }
    }

    // Dolgu kenari weight ile kayar (<b>: +BoldWeight); kontur kenar merkezi oradan disari kayar
    // (SDF deger uzayi; TextSprite ile ayni). Her frame instance verisinden (textWeight rebuild istemez).
    void Edges(in TextQuad g, float scale, out float cFill, out float cOut)
    {
        cFill = TextSprite.WeightEdge(textWeight + ((g.Flags & QfBold) != 0 ? RichText.BoldWeight : 0f));
        cOut = textOutlineWidth > 0 && scale > 0
            ? MathF.Max(cFill - textOutlineWidth / scale / TextSdfSpread, 0.05f)
            : 0f;
    }

    // Italik span: kendi baseline'i etrafinda ek shear (kutu textSkew'i twm'de zaten var).
    static void QuadMatrix(in Mat4 twm, in TextQuad g, float oy, out Mat4 qm)
    {
        qm = twm;
        if ((g.Flags & QfItalic) != 0)
            Shear(ref qm, -RichText.ItalicSkew, oy + g.Base);
    }

    // x' = x + k*(y - cy): y kolonuna x kolonu karisir, cy sabit kalsin diye translation geri alinir.
    static void Shear(ref Mat4 m, float k, float cy)
    {
        float mx = m.m[0], my = m.m[1], mz = m.m[2];
        m.m[4] += mx * k;
        m.m[5] += my * k;
        m.m[6] += mz * k;
        m.m[12] -= mx * k * cy;
        m.m[13] -= my * k * cy;
        m.m[14] -= mz * k * cy;
    }

    // Lineer gradient BLOK bandina gore (icerik uzayi: blok ustu y=0, sol x=0).
    // c = dikeyde y (blok yuksekligi), yatayda x (kullanilabilir genislik).
    Color TextGradAt(in Gradient g, float c)
        => g.IsHorizontal
            ? g.At(_tbAvailW > 0 ? c / _tbAvailW : 0f)
            : g.At(_tbBlockH > 0 ? c / _tbBlockH : 0f);
}
