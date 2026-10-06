namespace DigitoyEngine;

// C '_engine_Instance' ile birebir bellek duzeni (per-instance vertex buffer, slot 1):
//   { float model[16]; float uvRect[4]; Color tint0..3; float user[4]; float fxp[4]; float fxRect[4] }
//   -> 64 + 16 + 16 + 16 + 16 + 16 = 144 bayt
// Alan sirasi/boyutu DEGISTIRILMEMELI; pipeline layout offset'leri buna bagli.
// tint0..3 = quad KOSE renkleri, uv uzayinda: 0=(0,0) 1=(1,0) 2=(1,1) 3=(0,1)
// (eski motorun DrawMultiColorQuad modeli — vertex stage bilinear secer, dort
// renk ayniysa davranis eski tek tint ile birebir). user = CORE shader'a ait
// per-instance parametre (USER makrosu; SDF golge smoothing, UI kenar, progress
// araligi vb — default 0). fxp = .fx zincirine ait AYRI kanal (FXP makrosu;
// Renderer.FxParams) — core'un USER'iyla cakismaz, batch'i bozmaz.
// fxRect = .fx'in gordugu LOCALUV'nin bu quad icindeki penceresi (offset.xy,
// scale.zw): parcali renderer'lar (9-slice kutu, glyph'ler) her parcayi
// renderer-lokal 0..1 uzayina oturtur; tek quad'lar (0,0,1,1) gecer.
public unsafe struct Instance
{
    public fixed float model[16];
    public fixed float uvRect[4];
    public Color tint0;
    public Color tint1;
    public Color tint2;
    public Color tint3;
    public fixed float user[4];
    public fixed float fxp[4];
    public fixed float fxRect[4];
}
