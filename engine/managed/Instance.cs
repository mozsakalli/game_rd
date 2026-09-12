namespace DigitoyEngine;

// C '_engine_Instance' ile birebir bellek duzeni (per-instance vertex buffer, slot 1):
//   { float model[16]; float uvRect[4]; Color tint0..3; float user[4] }
//   -> 64 + 16 + 16 + 16 = 112 bayt
// Alan sirasi/boyutu DEGISTIRILMEMELI; pipeline layout offset'leri buna bagli.
// tint0..3 = quad KOSE renkleri, uv uzayinda: 0=(0,0) 1=(1,0) 2=(1,1) 3=(0,1)
// (eski motorun DrawMultiColorQuad modeli — vertex stage bilinear secer, dort
// renk ayniysa davranis eski tek tint ile birebir). user = shader'a serbest
// per-instance parametre (USER makrosu; SDF golge smoothing vb — default 0).
public unsafe struct Instance
{
    public fixed float model[16];
    public fixed float uvRect[4];
    public Color tint0;
    public Color tint1;
    public Color tint2;
    public Color tint3;
    public fixed float user[4];
}
