// Diyagonal parlama bandi. Batch'i bozmaz: tum parametreler per-instance (FXP)
// veya global (TIME); shader/pipeline degismez.
//   FXP.x = faz (-0.5..1.5; 0 ise TIME ile otomatik tarar)
//   FXP.y = bant yari genisligi (0 = 0.15)
//   FXP.z = yogunluk (0 = kapali -> no-op)
//   FXP.w = egim (uv.y'nin x'e katkisi; 0 = dikey bant)
VEC4 fx(VEC4 c, VEC2 uv) {
  FLOAT phase = FXP.x != 0.0 ? FXP.x : fract(TIME.x * 0.5) * 2.0 - 0.5;
  FLOAT halfW = FXP.y > 0.0 ? FXP.y : 0.15;
  FLOAT d = (uv.x + uv.y * FXP.w) / (1.0 + abs(FXP.w)) - phase;
  FLOAT band = 1.0 - SATURATE(abs(d) / halfW);
  band = band * band * FXP.z;
  return VEC4(c.rgb + band * c.a, c.a);
}
