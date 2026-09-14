using System;

namespace DigitoyEngine;

// Bir atlastaki UI parça bölgesinin tanıtıcısı: doku + bölge orijini (px).
// Parça yerleşimi orijine GÖRELİ ve SABİT (UiPieces sabitleri) — kutu quad'ları
// hangi atlas bağlanırsa bağlansın aynı hesapla UV üretir.
public readonly struct UiAtlas
{
    public readonly Texture Tex;
    public readonly int X, Y; // bölge orijini (atlas px, satır 0 üstte)

    public UiAtlas(Texture tex, int x, int y)
    {
        Tex = tex;
        X = x;
        Y = y;
    }

    // Bölgeye göreli px dikdörtgeni -> normalize UV (CPU satır yönelimi; V takası çağıranda).
    public void Uv(int rx, int ry, int rw, int rh, out float u0, out float v0, out float u1, out float v1)
    {
        float iw = 1f / Tex.Width, ih = 1f / Tex.Height;
        u0 = (X + rx) * iw;
        v0 = (Y + ry) * ih;
        u1 = (X + rx + rw) * iw;
        v1 = (Y + ry + rh) * ih;
    }

    // Düz beyaz örnek noktası (parça ortası — bilinear komşu sızması olmaz).
    public void SolidUv(out float u, out float v)
    {
        u = (X + UiPieces.SolidX + UiPieces.SolidSize * 0.5f) / Tex.Width;
        v = (Y + UiPieces.SolidY + UiPieces.SolidSize * 0.5f) / Tex.Height;
    }
}

// Prosedürel SDF UI parçaları (eski motorun pişmiş FillTexture modelinin SDF hali):
// çeyrek disk köşe (kutu radius'u — fwidth AA ile her ölçekte keskin), geniş-spread
// gölge köşesi (USER.x smoothing ile değişken blur) ve düz beyaz kare. Parçalar
// HER DFNT font atlasının alt bandına (glyph hücreleri 14 satır = y<1792) VE
// font'suz kullanım için lazy fallback atlasına AYNI fonksiyonla pişer — kutu +
// metin aynı doku/shader'ı paylaşır (tek draw). Değer eşlemesi font SDF'iyle aynı:
// 0.5 = kenar, >0.5 içerisi (aynı smoothstep shader'ı ikisini de çizer).
public static unsafe class UiPieces
{
    // Bölgeye göreli parça yerleşimi (px). Aralarda 8px pad (bilinear sızma).
    public const int CornerX = 0, CornerY = 0, CornerSize = 128;
    public const float CornerRadius = 88f;   // px, disk merkezi parçanın (0,0)'ında
    public const float CornerSpread = 64f;   // 0..1 değer bandının px genişliği

    public const int ShadowX = 136, ShadowY = 0, ShadowSize = 128;
    public const float ShadowRadius = 64f;   // R+b haritalamasi 128'e sigsin (b<=r)
    public const float ShadowSpread = 128f;  // genis band: buyuk smoothing'e alan

    public const int SolidX = 272, SolidY = 0, SolidSize = 32;

    public const int RegionW = 312, RegionH = 128;

    // DFNT font atlasındaki bölge orijini: 14 glyph satırı (1792) + 8 pad.
    public const int FontRegionX = 0;
    public const int FontRegionY = 1800;

    // Parçaları R8 atlasa pişirir (satır 0 üstte). Bake tarafı (editör importer)
    // ve runtime fallback aynı fonksiyonu kullanır — çıktı birebir aynı.
    public static void BakeRegion(byte[] atlas, int atlasW, int ox, int oy)
    {
        int atlasH = atlas.Length / atlasW;
        BakeDisc(atlas, atlasW, atlasH, ox + CornerX, oy + CornerY, CornerSize, CornerRadius, CornerSpread);
        BakeDisc(atlas, atlasW, atlasH, ox + ShadowX, oy + ShadowY, ShadowSize, ShadowRadius, ShadowSpread);
        for (int y = 0; y < SolidSize; y++)
            for (int x = 0; x < SolidSize; x++)
                atlas[(oy + SolidY + y) * atlasW + ox + SolidX + x] = 255;
    }

    // Parca disina SDF devami (apron): koordinat 0 / size sinirlarinda bilinear
    // komsu pad'in sifirlarini karistirip kutu quad dikislerini gorunur yapiyordu.
    const int Apron = 4;

    // Çeyrek disk SDF: merkez parçanın (0,0) pikseli, 0.5 = kenar (piksel merkezi örneklenir).
    // Apron disk formülünün doğal devamı (kare simetri) — sınırda bilinear birebir doğru.
    static void BakeDisc(byte[] atlas, int atlasW, int atlasH, int px, int py, int size, float radius, float spread)
    {
        for (int y = -Apron; y < size + Apron; y++)
        {
            int ay = py + y;
            if (ay < 0 || ay >= atlasH)
                continue;
            int row = ay * atlasW;
            for (int x = -Apron; x < size + Apron; x++)
            {
                int ax = px + x;
                if (ax < 0 || ax >= atlasW)
                    continue;
                float d = MathF.Sqrt((x + 0.5f) * (x + 0.5f) + (y + 0.5f) * (y + 0.5f));
                float v = Math.Clamp(0.5f + (radius - d) / spread, 0f, 1f);
                atlas[row + ax] = (byte)(v * 255f + 0.5f);
            }
        }
    }

    // Font'suz kutular için paylaşılan atlas (lazy; içerik DFNT bölgesiyle birebir).
    static Texture _fallbackTex;

    // Varsayılan UI fontu: ilk yüklenen font otomatik atanır (elle de set edilebilir).
    // Font'suz LayoutBox parçaları bu fontun atlasından örneklenir — sahnedeki
    // metinli kutularla texview aynı kalır, hepsi tek instanced draw'a merge olur.
    // Parça bölgesi her DFNT'de birebir aynı piştiği için görsel fark yoktur.
    public static Font DefaultFont;

    public static UiAtlas Fallback
    {
        get
        {
            if (_fallbackTex == null)
            {
                const int w = 512, h = 128;
                byte[] px = new byte[w * h];
                BakeRegion(px, w, 0, 0);
                fixed (byte* p = px)
                    _fallbackTex = Texture.FromAlpha(w, h, p);
                _fallbackTex.Name = "ui#pieces";
                _fallbackTex.Persistent = true;
            }
            return new UiAtlas(_fallbackTex, 0, 0);
        }
    }

    // Ortak UI SDF shader'i: USER.x = smoothing (0 = fwidth keskin), USER.y =
    // kenar merkezi (0 = 0.5), USER.z > 0 = ic kenar bandi (border RINGI icin;
    // alpha dista 0 - kenarda 1 - USER.z'de tekrar 0). Kutu, golge ve metin AYNI
    // shader/pipeline'i paylasir -> ayni atlasa dusen bitisik draw'lar merge olur.
    public const string SdfFragment =
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  FLOAT d = SAMPLE(tex, uv).r;\n" +
        "  FLOAT s = USER.x > 0.0 ? USER.x : FWIDTH(d);\n" +
        "  FLOAT c = USER.y > 0.0 ? USER.y : 0.5;\n" +
        "  FLOAT a = smoothstep(c - s, c + s, d);\n" +
        "  if (USER.z > 0.0) a *= 1.0 - smoothstep(USER.z - s, USER.z + s, d);\n" +
        "  return VEC4(color.rgb, a * color.a);\n}"; // duz renk; premultiply wrapper'da

    static Shader _shader;
    public static Shader UiShader => _shader ??= Shader.CreateEffect(SdfFragment);

    static Material _material;

    // Fallback atlasli paylasilan materyal (LayoutBox kullanir; blend = kanon default).
    public static Material SharedMaterial => _material ??= new Material
    {
        MainTexture = Fallback.Tex,
        Shader = UiShader,
        SortMode = SortMode.Transparent,
    };
}
