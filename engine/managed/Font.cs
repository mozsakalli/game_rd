using System;
using System.IO;

namespace DigitoyEngine;

// Prebaked SDF font asset'i (TextMeshPro modeli): TTF EDITORDE bir kez pisirilir
// (FontImporter -> DFNT artifact), runtime yalniz artifact'i okur — pak'a ttf
// girmez, cihazda stb_truetype/bake maliyeti yok. Tek atlas her puntoda keskin
// (SDF); metrikler senkron okunur (layout girdisi senkron kurali).
public sealed unsafe class Font : IAsset
{
    public struct Glyph
    {
        public float Advance, XOff, YOff, W, H, AtlasX, AtlasY;
    }

    public string Name { get; internal set; }
    public int FirstChar { get; private set; }
    public int CharCount { get; private set; }
    public int AtlasSize { get; private set; }
    public float Ascent { get; private set; }     // SDF piksel uzayinda
    public float Descent { get; private set; }
    public float LineHeight { get; private set; }
    public float KernScale { get; private set; }
    public float SdfSize { get; private set; }
    public Texture Atlas { get; private set; }

    Glyph[] _glyphs;
    short[] _kern;

    Font() { }

    // Kutu/golge SDF parcalari (UiPieces) bu atlasin sabit bolgesinde — kutu +
    // bu fontun metni ayni doku/shader'la tek draw'da cizilebilir.
    public UiAtlas Pieces => new UiAtlas(Atlas, UiPieces.FontRegionX, UiPieces.FontRegionY);

    public int GlyphIndex(char c)
    {
        int cp = c;
        if (cp < FirstChar || cp >= FirstChar + CharCount)
            cp = '?';
        return cp - FirstChar;
    }

    public ref readonly Glyph GlyphAt(int index) => ref _glyphs[index];

    public float Kerning(int left, int right) => _kern[left * CharCount + right] * KernScale;

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
    // ayni atlastaki kutu parcalari + metin tek instanced draw'a merge olur) ---

    Material _material;

    // TextSprite'in kullandigi paylasilan materyal (font basina bir, lazy).
    public Material Material
    {
        get
        {
            _material ??= new Material
            {
                MainTexture = Atlas,
                Shader = UiPieces.UiShader,
                SrcBlend = BlendFactor.One, // premultiplied (shader rgb*alpha yazar)
                DstBlend = BlendFactor.OneMinusSrcAlpha,
                SortMode = SortMode.Transparent,
            };
            return _material;
        }
    }

    // --- DFNT artifact bicimi ---
    // [magic][version][firstChar][charCount][atlasSize][5 metrik float]
    // [glyph 7 float * charCount][kern int16 * charCount^2][atlas R8 bayt atlasSize^2]

    public const int Magic = 0x544E4644; // "DFNT"
    public const int Version = 2; // v2: atlas alt bandinda UiPieces utility bolgesi

    internal static Font FromArtifact(byte[] blob, string name)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(blob));
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
                return null;
            var f = new Font
            {
                Name = name,
                FirstChar = r.ReadInt32(),
                CharCount = r.ReadInt32(),
                AtlasSize = r.ReadInt32(),
                Ascent = r.ReadSingle(),
                Descent = r.ReadSingle(),
                LineHeight = r.ReadSingle(),
                KernScale = r.ReadSingle(),
                SdfSize = r.ReadSingle(),
            };
            f._glyphs = new Glyph[f.CharCount];
            for (int i = 0; i < f.CharCount; i++)
            {
                f._glyphs[i] = new Glyph
                {
                    Advance = r.ReadSingle(),
                    XOff = r.ReadSingle(),
                    YOff = r.ReadSingle(),
                    W = r.ReadSingle(),
                    H = r.ReadSingle(),
                    AtlasX = r.ReadSingle(),
                    AtlasY = r.ReadSingle(),
                };
            }
            f._kern = new short[f.CharCount * f.CharCount];
            for (int i = 0; i < f._kern.Length; i++)
                f._kern[i] = r.ReadInt16();
            byte[] atlas = r.ReadBytes(f.AtlasSize * f.AtlasSize);
            fixed (byte* p = atlas)
                f.Atlas = Texture.FromAlpha(f.AtlasSize, f.AtlasSize, p);
            f.Atlas.Name = name + "#atlas";
            f.Atlas.Persistent = true; // atlas evict edilirse geri yukleyecek tarif yok
            // Ilk yuklenen (veya reimport'ta ayni adli) font varsayilan UI atlasi olur.
            if (UiPieces.DefaultFont == null || UiPieces.DefaultFont.Name == name)
                UiPieces.DefaultFont = f;
            return f;
        }
        catch (Exception)
        {
            return null; // bozuk artifact: cagiran loglar
        }
    }

    // EDITOR-TIME bake: ttf -> DFNT blobu (native stb_truetype; GPU'ya dokunmaz).
    // Parametreler shim'de sabit: 2048^2 atlas, 224 glyph (cp 32..255), 32px SDF.
    internal static byte[] Bake(byte[] ttf)
    {
        const int atlasSize = 2048, charCount = 224, firstChar = 32;
        byte[] atlas = new byte[atlasSize * atlasSize];
        float[] glyphs = new float[charCount * 9];
        short[] kern = new short[charCount * charCount];
        float[] metrics = new float[5];
        int baked;
        fixed (byte* pTtf = ttf)
        fixed (byte* pAtlas = atlas)
        fixed (float* pGlyphs = glyphs)
        fixed (short* pKern = kern)
        fixed (float* pMetrics = metrics)
            baked = Sokol.FontBake(pTtf, pAtlas, atlasSize, pGlyphs, pKern, pMetrics);
        if (baked <= 0)
            return null;
        // Glyph hucreleri 14 satir (y<1792); alt banda UI parcalari piser.
        UiPieces.BakeRegion(atlas, atlasSize, UiPieces.FontRegionX, UiPieces.FontRegionY);

        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(Version);
        w.Write(firstChar);
        w.Write(charCount);
        w.Write(atlasSize);
        w.Write(metrics[0]); // ascent
        w.Write(metrics[1]); // descent
        w.Write(metrics[2]); // lineHeight
        w.Write(metrics[3]); // kernScale
        w.Write(metrics[4]); // sdfSize
        for (int i = 0; i < charCount; i++)
        {
            int o = i * 9; // bake 9 float yazar; son 2 (atlas w/h tekrar) kullanilmiyor
            w.Write(glyphs[o + 0]);
            w.Write(glyphs[o + 1]);
            w.Write(glyphs[o + 2]);
            w.Write(glyphs[o + 3]);
            w.Write(glyphs[o + 4]);
            w.Write(glyphs[o + 5]);
            w.Write(glyphs[o + 6]);
        }
        for (int i = 0; i < kern.Length; i++)
            w.Write(kern[i]);
        w.Write(atlas);
        w.Flush();
        return ms.ToArray();
    }
}
