namespace DigitoyEngine;

// Degerler C 'engine_render.h' enum'lariyla birebir esittir; shim tarafinda
// sokol karsiliklarina cevrilir. Sayilar DEGISTIRILMEMELI.

public enum SamplerType
{
    Linear = 0,
    Nearest = 1,
}

public enum BlendFactor
{
    Zero = 0,
    One = 1,
    SrcAlpha = 2,
    OneMinusSrcAlpha = 3,
    DstAlpha = 4,
    OneMinusDstAlpha = 5,
    SrcColor = 6,
    OneMinusSrcColor = 7,
    DstColor = 8,
    OneMinusDstColor = 9,
}

public enum CullMode
{
    None = 0,
    Front = 1,
    Back = 2,
}

// Kullanici yuzu blend secimi (Renderer.BlendMode) — HER renderer'da ayni alan,
// ayni tablo (sprite/kutu/metin ayrismaz). Faktorler premultiplied cikisa gore
// (shader wrapper cikista rgb*a yazar); ham Src/DstBlend dahili detaydir.
public enum BlendMode
{
    Normal = 0,   // One / OneMinusSrcAlpha
    Additive = 1, // One / One
    Multiply = 2, // DstColor / OneMinusSrcAlpha
    Screen = 3,   // One / OneMinusSrcColor
}

// Unity Material.renderQueue karsiligi: siralama MATERYALIN ozelligi, komutun degil.
// Kovalar numara sirasiyla cizilir (Unity: Geometry 2000 -> Transparent 3000 -> Overlay 4000).
// DrawMesh'teki 'layer' Unity sortingOrder karsiligidir: kova ICINDEKI sira ipucu.
public enum SortMode
{
    None = 0,
    Opaque = 1,      // Unity Geometry: state gruplu (batch dostu)
    Transparent = 2, // Unity Transparent: layer oncelikli arkadan-one
    Ui = 3,          // Unity Overlay
    Painter = 4,     // saf 2D: katman her seyden once
}
