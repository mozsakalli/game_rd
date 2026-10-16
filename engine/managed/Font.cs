using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Prebaked SDF font asset'i (TextMeshPro modeli): TTF EDITORDE bir kez pisirilir (FontImporter),
// runtime yalniz veriyi okur — pak'a ttf girmez, cihazda bake maliyeti yok.
// SAF VERI ([Asset], ObjectBinary): metrikler + glyph tablosu + kerning. Font DOKU BILMEZ: her glyph bir
// Sprite referansidir ("hero.ttf#U+0041"); sprite'in hangi sayfada (fontun kendi sheet'i ya da bir
// AtlasGroup sayfasi) yasadigina AssetDatabase'in sprite tablosu karar verir ve YERINDE baglar — font'a
// atlas uyeliginden haber gitmez, metin quad cache'leri Sprite.BindVersion ile tazelenir.
// Turetilmis tablolar (codepoint -> indeks, kern short[]) ilk kullanimda kurulur (OnLoaded kancasi yok).
[Serializable, Asset]
public sealed class Font : ISpriteSource
{
    [Serializable]
    public struct Glyph
    {
        public int Codepoint;
        public float Advance, XOff, YOff, W, H;
        // Gorunen murekkebin sol/sag kenari (kalem orijinine gore, SDF px, alt-piksel;
        // importer SDF bitmap'inden esik=128 kesisimiyle cikarir). W=0 ise ikisi de 0.
        public float InkL, InkR;
        public Sprite Sprite; // W>0 glyph'lerde dolu; bolge sayfa uzayinda (depolama satiri 0 ustte)
    }

    public float Ascent;     // SDF piksel uzayinda
    public float Descent;
    public float LineHeight;
    public float KernScale;
    public float SdfSize;    // bake satir kutusu (ascent-descent) SDF px — stbtt_ScaleForPixelHeight tanimi
    public float EmSize;     // 1 em'in SDF px karsiligi (unitsPerEm * olcek); 0 = eski asset (SdfSize kullanilir)
    public Glyph[] Glyphs = Array.Empty<Glyph>();
    public byte[] Kern = Array.Empty<byte>(); // int16 LE, Glyphs.Length^2 (Blob)

    // --- turetilmis (serilesmez) ---
    [NonSerialized] short[] _kern;
    [NonSerialized] int[] _latin;              // cp < 256 hizli yol (-1 = yok)
    [NonSerialized] Dictionary<int, int> _map; // kalan codepoint'ler
    [NonSerialized] int _fallbackIndex;        // '?' (yoksa 0)
    [NonSerialized] int _pageGlyph = -1;       // pikselli ilk glyph (sayfa/BindVersion kaynagi)
    [NonSerialized] Glyph[] _builtFor;         // tablolar hangi Glyphs dizisi icin kuruldu (reload algisi)

    public int GlyphCount => Glyphs.Length;

    // Metin boyutu (textSize/Size) em cinsindendir (TMP fontSize ile 1:1): quad olcegi = size / EmPx.
    // Bake satir kutusu em'den ~%12-20 buyuk oldugu icin SdfSize'a bolmek metni kucuk gosteriyordu.
    public float EmPx => EmSize > 0 ? EmSize : SdfSize;

    void EnsureTables()
    {
        if (ReferenceEquals(_builtFor, Glyphs) && _latin != null)
            return;
        var g = Glyphs;
        _latin = new int[256];
        Array.Fill(_latin, -1);
        _map = null;
        _fallbackIndex = 0;
        _pageGlyph = -1;
        for (int i = 0; i < g.Length; i++)
        {
            int cp = g[i].Codepoint;
            if (cp < 256)
                _latin[cp] = i;
            else
                (_map ??= new Dictionary<int, int>())[cp] = i;
            if (cp == '?')
                _fallbackIndex = i;
            if (_pageGlyph < 0 && g[i].W > 0 && g[i].H > 0 && g[i].Sprite != null)
                _pageGlyph = i;
        }
        int n = g.Length * g.Length;
        _kern = new short[n];
        var k = Kern;
        for (int i = 0; i < n && 2 * i + 1 < k.Length; i++)
            _kern[i] = (short)(k[2 * i] | (k[2 * i + 1] << 8));
        _builtFor = g;
    }

    // Glyph'lerin yasadigi doku (sheet veya atlas sayfasi); baglanmadiysa null.
    public Texture Page
    {
        get
        {
            EnsureTables();
            return _pageGlyph >= 0 ? Glyphs[_pageGlyph].Sprite.Page : null;
        }
    }

    // Kutu/golge SDF parcalari: glyph'lerle AYNI sayfadan (sheet ve atlas sayfalari
    // hepsi UiPieces tasir) — kutu + metin tek draw'da. Sayfa yoksa fallback.
    public UiAtlas Pieces
    {
        get
        {
            var page = Page;
            return page != null && page.HasPieces ? new UiAtlas(page, page.PiecesX, page.PiecesY) : UiPieces.Fallback;
        }
    }

    public bool IsBound => Page != null;

    // Sayfa (yeniden) baglaninca degisir: metin quad cache'leri UV'lerini tazeler.
    public int BindVersion
    {
        get
        {
            EnsureTables();
            return _pageGlyph >= 0 ? Glyphs[_pageGlyph].Sprite.BindVersion : 0;
        }
    }

    public int GlyphIndex(char c) => GlyphIndex((int)c);

    public int GlyphIndex(int codepoint)
    {
        EnsureTables();
        if (codepoint < 256)
        {
            int i = _latin[codepoint];
            return i >= 0 ? i : _fallbackIndex;
        }
        return _map != null && _map.TryGetValue(codepoint, out int gi) ? gi : _fallbackIndex;
    }

    public ref readonly Glyph GlyphAt(int index) => ref Glyphs[index];

    public float Kerning(int left, int right)
    {
        EnsureTables();
        return _kern[left * Glyphs.Length + right] * KernScale;
    }

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
            w += Glyphs[gi].Advance;
            prev = gi;
        }
        return w;
    }

    // --- Dunya SDF materyali (UiPieces.UiShader ile AYNI shader/pipeline:
    // ayni sayfadaki kutu parcalari + metin + ikonlar tek instanced draw'a merge olur) ---

    [NonSerialized] Material _material;

    // TextSprite/LayoutBox'in kullandigi paylasilan materyal (font basina bir, lazy).
    // Sayfa yeniden baglanabildigi icin doku VE shader (sayfa formatina gore R8/MSDF
    // varyanti) her erisimde esitlenir.
    public Material Material
    {
        get
        {
            _material ??= new Material
            {
                SortMode = SortMode.Transparent, // blend = kanon default (One/OneMinusSrcAlpha)
            };
            var page = Page;
            _material.MainTexture = page;
            _material.Shader = UiPieces.UiShader;
            return _material;
        }
    }
}
