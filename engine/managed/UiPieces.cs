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
// HER atlas sayfasına ve her font sheet'ine (paketleyicinin yerleşik üreticisi) VE
// sayfasız kullanım için lazy fallback dokusuna AYNI fonksiyonla pişer — kutu +
// metin + ikon aynı doku/shader'ı paylaşır (tek draw). Sayfadaki orijin Texture
// .PiecesX/Y'de taşınır. Değer eşlemesi font SDF'iyle aynı: 0.5 = kenar.
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

    // Parça içeriği değişince artır: paketleyici hash'ine girer (tüm atlas/sheet'ler yeniden pişer).
    public const int BakeVersion = 1;

    // Parçaları R8 tampona pişirir (satır 0 üstte, tampon genişliği atlasW). Bake
    // tarafı (editör paketleyici) ve runtime fallback aynı fonksiyonu kullanır.
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

    // Sayfasız kutular için paylaşılan doku (lazy; içerik atlas bölgesiyle birebir).
    static Texture _fallbackTex;

    // Varsayılan UI fontu: ilk yüklenen font otomatik atanır (elle de set edilebilir).
    // Font'suz LayoutBox parçaları bu fontun sayfasından örneklenir — sahnedeki
    // metinli kutularla texview aynı kalır, hepsi tek instanced draw'a merge olur.
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
                _fallbackTex.PiecesX = 0;
                _fallbackTex.PiecesY = 0;
            }
            return new UiAtlas(_fallbackTex, 0, 0);
        }
    }

    // Ortak UI shader'i: USER.x = smoothing (0 = fwidth keskin), USER.y =
    // kenar merkezi (0 = 0.5; ISARET = mesafe kaynagi, asagi bak), USER.z > 0 = ic
    // kenar bandi (border RINGI icin; alpha dista 0 - kenarda 1 - USER.z'de tekrar 0),
    // USER.w > 0 = ham rampa (alpha = d dogrudan; radyal gradient), USER.w < 0 =
    // DOKULU gecis (duz sprite: SAMPLE * color — ayni sayfadaki ikon kutu/metinle
    // tek draw'a girer).
    // Keskin yol (USER.x == 0) TMP_SDF ile ayni: kenar etrafinda TAM 1 ekran px
    // LINEER rampa (saturate((d-c)/fwidth + 0.5)). Eski smoothstep(c∓fwidth) 2 px
    // genislik + S-egrisi veriyordu -> metin yumusak/bulanik gorunuyordu.
    // Acik smoothing (golge blur) smoothstep'te kalir — orada yumusaklik amac.
    //
    // IKI VARYANT, sayfa piksel formatina gore (UiPieces.ShaderFor(page)):
    //   R8   (SdfFragment):  d = .r (tek kanal SDF).
    //   RGBA (MsdfFragment): d = median(r,g,b) (MTSDF: keskin kose), USER.y < 0 ise
    //        d = .a (gercek SDF; golge/kontur gibi kenardan uzak okumalar — msdf
    //        median'i kenardan uzakta guvenilir degildir). Kenar merkezi |USER.y|.
    //        Tek kanal SDF iceren bolgeler (UiPieces, R8'den kopyalanan glyph'ler) dort
    //        kanala kopyali -> median == a, her iki okuma da dogru.
    // Instance verisi iki varyantta AYNI anlamda; metin yayicilari golge/konturda -c yazar.
    const string _fragHead =
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  if (USER.w < 0.0) return SAMPLE(tex, uv) * color;\n";
    const string _fragTail =
        "  if (USER.w > 0.0) return VEC4(color.rgb, d * color.a);\n" +
        "  FLOAT c = USER.y != 0.0 ? abs(USER.y) : 0.5;\n" +
        "  FLOAT a;\n" +
        "  if (USER.x > 0.0) {\n" +
        "    FLOAT s = USER.x;\n" +
        "    a = smoothstep(c - s, c + s, d);\n" +
        "    if (USER.z > 0.0) a *= 1.0 - smoothstep(USER.z - s, USER.z + s, d);\n" +
        "  } else {\n" +
        "    FLOAT w = max(FWIDTH(d), 1e-5);\n" +
        "    a = SATURATE((d - c) / w + 0.5);\n" +
        "    if (USER.z > 0.0) a *= 1.0 - SATURATE((d - USER.z) / w + 0.5);\n" +
        "  }\n" +
        "  return VEC4(color.rgb, a * color.a);\n}"; // duz renk; premultiply wrapper'da

    public const string SdfFragment = _fragHead +
        "  FLOAT d = SAMPLE(tex, uv).r;\n" + _fragTail;

    public const string MsdfFragment = _fragHead +
        "  VEC4 t = SAMPLE(tex, uv);\n" +
        "  FLOAT d = USER.y < 0.0 ? t.a : max(min(t.r, t.g), min(max(t.r, t.g), t.b));\n" + _fragTail;

    // Dokulu gecis modunun USER degeri (LayoutBox sprite yolu).
    public static readonly Vec4 TexturedUser = new(0f, 0f, 0f, -1f);

    static Shader _shader, _msdfShader;
    public static Shader UiShader => _shader ??= Shader.CreateEffect(SdfFragment);
    public static Shader UiShaderMsdf => _msdfShader ??= Shader.CreateEffect(MsdfFragment);

    // Sayfanin formatina gore UI shader'i (R8 -> .r, RGBA -> median/alpha).
    public static Shader ShaderFor(Texture page)
        => page != null && page.Channels == 4 ? UiShaderMsdf : UiShader;

    static Material _material;

    // Fallback dokulu paylasilan materyal (LayoutBox kullanir; blend = kanon default).
    public static Material SharedMaterial => _material ??= new Material
    {
        MainTexture = Fallback.Tex,
        Shader = UiShader,
        SortMode = SortMode.Transparent,
    };

    static Material _textured;

    // Dokulu kutu materyali: doku VE shader cizim aninda sayfaya gore atanir
    // (paylasilan materyal deseni — TexView DrawMesh aninda yakalanir).
    public static Material TexturedMaterial => _textured ??= new Material
    {
        MainTexture = Fallback.Tex,
        Shader = UiShader,
        SortMode = SortMode.Transparent,
    };
}
