using System;
using System.Collections.Generic;
using System.IO;

namespace DigitoyEngine;

// Prebaked SDF font asset'i (TextMeshPro modeli): TTF EDITORDE bir kez pisirilir
// (FontImporter), runtime yalniz artifact'i okur — pak'a ttf girmez, cihazda
// stb_truetype/bake maliyeti yok. Font DOKU SAHIBI DEGILDIR: glyph'ler bir
// "sayfa"da yasar — ya fontun kendi sheet'i (gruba alinmamis font) ya da bir
// AtlasGroup sayfasi (tasarimci birden fazla fontu/ikonu tek dokuya merge eder).
// Baglamayi AssetDatabase yapar (BindSheet / BindAtlas); metrikler senkron okunur.
public sealed unsafe class Font : IAsset
{
    public struct Glyph
    {
        public int Codepoint;
        public float Advance, XOff, YOff, W, H;
        public float AtlasX, AtlasY; // Page depolama koordinati (satir 0 = ilk satir)
        // Gorunen murekkebin sol/sag kenari (kalem orijinine gore, SDF px, alt-piksel;
        // importer SDF bitmap'inden esik=128 kesisimiyle cikarir). W=0 ise ikisi de 0.
        public float InkL, InkR;
    }

    public string Name { get; internal set; }
    public int GlyphCount { get; private set; }
    public float Ascent { get; private set; }     // SDF piksel uzayinda
    public float Descent { get; private set; }
    public float LineHeight { get; private set; }
    public float KernScale { get; private set; }
    public float SdfSize { get; private set; }

    // Glyph'lerin su an yasadigi doku (sheet veya atlas sayfasi); baglanmadiysa null.
    public Texture Page { get; private set; }

    // Kendi sheet'inin tanimi (importer yazar): gruba alinmamis font bunu yukler.
    public int SheetWidth { get; private set; }
    public int SheetHeight { get; private set; }
    int _sheetPiecesX, _sheetPiecesY;

    Glyph[] _glyphs;
    float[] _sheetX, _sheetY; // sheet koordinatlari (atlas cozulunce geri donus icin)
    short[] _kern;
    int[] _latin;             // cp < 256 hizli yol (-1 = yok)
    Dictionary<int, int> _map; // kalan codepoint'ler
    int _fallbackIndex;        // '?' (yoksa 0)
    Texture _ownedSheet;       // BindSheet'in urettigi doku (atlas gelince yok edilir)

    Font() { }

    // Kutu/golge SDF parcalari: glyph'lerle AYNI sayfadan (sheet ve atlas sayfalari
    // hepsi UiPieces tasir) — kutu + metin tek draw'da. Sayfa yoksa fallback.
    public UiAtlas Pieces => Page != null && Page.HasPieces
        ? new UiAtlas(Page, Page.PiecesX, Page.PiecesY)
        : UiPieces.Fallback;

    public bool IsBound => Page != null;

    // Her sayfa (yeniden) baglanisinda artar: metin quad cache'leri UV'lerini tazeler.
    public int BindVersion { get; private set; }

    public int GlyphIndex(char c) => GlyphIndex((int)c);

    public int GlyphIndex(int codepoint)
    {
        if (codepoint < 256)
        {
            int i = _latin[codepoint];
            return i >= 0 ? i : _fallbackIndex;
        }
        return _map != null && _map.TryGetValue(codepoint, out int gi) ? gi : _fallbackIndex;
    }

    public ref readonly Glyph GlyphAt(int index) => ref _glyphs[index];

    public float Kerning(int left, int right) => _kern[left * GlyphCount + right] * KernScale;

    // Tek satirin genisligi SDF piksel uzayinda (kerning dahil; '\n' icermemeli).
    public float MeasureLine(ReadOnlySpan<char> text)
    {
        float w = 0;
        int prev = -1;
        for (int i = 0; i < text.Length; i++)
        {
            int gi = GlyphIndex(text[i]);
            if (prev >= 0)
                w += Kerning(prev, gi);
            w += _glyphs[gi].Advance;
            prev = gi;
        }
        return w;
    }

    // --- Dunya SDF materyali (UiPieces.UiShader ile AYNI shader/pipeline:
    // ayni sayfadaki kutu parcalari + metin + ikonlar tek instanced draw'a merge olur) ---

    Material _material;

    // TextSprite/LayoutBox'in kullandigi paylasilan materyal (font basina bir, lazy).
    // Sayfa yeniden baglanabildigi icin doku her erisimde esitlenir.
    public Material Material
    {
        get
        {
            _material ??= new Material
            {
                Shader = UiPieces.UiShader,
                SortMode = SortMode.Transparent, // blend = kanon default (One/OneMinusSrcAlpha)
            };
            _material.MainTexture = Page;
            return _material;
        }
    }

    // --- Baglama (AssetDatabase) ---

    // Kendi sheet'ine baglanir (DPIX R8 blobu; importer "sheet" artifact'i).
    internal bool BindSheet(byte[] sheetBlob)
    {
        if (!PixelBlob.TryParse(sheetBlob, out int ch, out int w, out int h) || ch != 1)
            return false;
        ReleaseOwnedSheet();
        fixed (byte* p = &sheetBlob[PixelBlob.HeaderSize])
            _ownedSheet = Texture.FromAlpha(w, h, p);
        _ownedSheet.Name = Name + "#sheet";
        _ownedSheet.Persistent = true; // R8 dokunun CPU kopyasi yok: evict edilirse geri yuklenemez
        _ownedSheet.PiecesX = _sheetPiecesX;
        _ownedSheet.PiecesY = _sheetPiecesY;
        for (int i = 0; i < _glyphs.Length; i++)
        {
            _glyphs[i].AtlasX = _sheetX[i];
            _glyphs[i].AtlasY = _sheetY[i];
        }
        Page = _ownedSheet;
        BindVersion++;
        return true;
    }

    // Atlas sayfasina baglanir: cagiran her glyph icin konum verir (bolge tablosu).
    internal void BeginAtlasBind(Texture page)
    {
        ReleaseOwnedSheet();
        Page = page;
        BindVersion++;
    }

    internal void SetGlyphPosition(int index, int x, int y)
    {
        _glyphs[index].AtlasX = x;
        _glyphs[index].AtlasY = y;
    }

    internal void Unbind()
    {
        ReleaseOwnedSheet();
        Page = null;
        BindVersion++;
    }

    void ReleaseOwnedSheet()
    {
        if (_ownedSheet == null)
            return;
        _ownedSheet.Destroy();
        _ownedSheet = null;
    }

    // --- DFNT artifact bicimi (v3: doku yok) ---
    // [magic][version][glyphCount][5 metrik float][sheetW][sheetH][piecesX][piecesY]
    // [glyph: cp int32 + 7 float * glyphCount][kern int16 * glyphCount^2]

    public const int Magic = 0x544E4644; // "DFNT"
    public const int Version = 4;

    internal static Font FromArtifact(byte[] blob, string name)
    {
        var f = Parse(blob, name);
        // Ilk yuklenen (veya reimport'ta ayni adli) font varsayilan UI atlasi olur.
        if (f != null && (UiPieces.DefaultFont == null || UiPieces.DefaultFont.Name == name))
            UiPieces.DefaultFont = f;
        return f;
    }

    // Reimport: ayni instance YERINDE tazelenir (sahnedeki referanslar bozulmaz);
    // cagiran sonra sayfayi yeniden baglar.
    internal bool ReloadFrom(byte[] blob)
    {
        var n = Parse(blob, Name);
        if (n == null)
            return false;
        Unbind();
        GlyphCount = n.GlyphCount;
        Ascent = n.Ascent; Descent = n.Descent; LineHeight = n.LineHeight;
        KernScale = n.KernScale; SdfSize = n.SdfSize;
        SheetWidth = n.SheetWidth; SheetHeight = n.SheetHeight;
        _sheetPiecesX = n._sheetPiecesX; _sheetPiecesY = n._sheetPiecesY;
        _glyphs = n._glyphs; _sheetX = n._sheetX; _sheetY = n._sheetY;
        _kern = n._kern; _latin = n._latin; _map = n._map; _fallbackIndex = n._fallbackIndex;
        return true;
    }

    static Font Parse(byte[] blob, string name)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(blob));
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
                return null;
            var f = new Font { Name = name, GlyphCount = r.ReadInt32() };
            f.Ascent = r.ReadSingle();
            f.Descent = r.ReadSingle();
            f.LineHeight = r.ReadSingle();
            f.KernScale = r.ReadSingle();
            f.SdfSize = r.ReadSingle();
            f.SheetWidth = r.ReadInt32();
            f.SheetHeight = r.ReadInt32();
            f._sheetPiecesX = r.ReadInt32();
            f._sheetPiecesY = r.ReadInt32();
            int n = f.GlyphCount;
            f._glyphs = new Glyph[n];
            f._sheetX = new float[n];
            f._sheetY = new float[n];
            f._latin = new int[256];
            Array.Fill(f._latin, -1);
            f._fallbackIndex = 0;
            for (int i = 0; i < n; i++)
            {
                var g = new Glyph
                {
                    Codepoint = r.ReadInt32(),
                    Advance = r.ReadSingle(),
                    XOff = r.ReadSingle(),
                    YOff = r.ReadSingle(),
                    W = r.ReadSingle(),
                    H = r.ReadSingle(),
                    AtlasX = r.ReadSingle(),
                    AtlasY = r.ReadSingle(),
                    InkL = r.ReadSingle(),
                    InkR = r.ReadSingle(),
                };
                f._glyphs[i] = g;
                f._sheetX[i] = g.AtlasX;
                f._sheetY[i] = g.AtlasY;
                if (g.Codepoint < 256)
                    f._latin[g.Codepoint] = i;
                else
                    (f._map ??= new Dictionary<int, int>())[g.Codepoint] = i;
                if (g.Codepoint == '?')
                    f._fallbackIndex = i;
            }
            f._kern = new short[n * n];
            for (int i = 0; i < f._kern.Length; i++)
                f._kern[i] = r.ReadInt16();
            return f;
        }
        catch (Exception)
        {
            return null; // bozuk artifact: cagiran loglar
        }
    }

    // Artifact yazici (editor FontImporter): glyph koordinatlari sheet uzayinda.
    public static byte[] WriteArtifact(float ascent, float descent, float lineHeight, float kernScale,
        float sdfSize, int sheetW, int sheetH, int piecesX, int piecesY,
        ReadOnlySpan<Glyph> glyphs, ReadOnlySpan<short> kern)
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(Version);
        w.Write(glyphs.Length);
        w.Write(ascent);
        w.Write(descent);
        w.Write(lineHeight);
        w.Write(kernScale);
        w.Write(sdfSize);
        w.Write(sheetW);
        w.Write(sheetH);
        w.Write(piecesX);
        w.Write(piecesY);
        foreach (ref readonly var g in glyphs)
        {
            w.Write(g.Codepoint);
            w.Write(g.Advance);
            w.Write(g.XOff);
            w.Write(g.YOff);
            w.Write(g.W);
            w.Write(g.H);
            w.Write(g.AtlasX);
            w.Write(g.AtlasY);
            w.Write(g.InkL);
            w.Write(g.InkR);
        }
        foreach (short k in kern)
            w.Write(k);
        w.Flush();
        return ms.ToArray();
    }
}
