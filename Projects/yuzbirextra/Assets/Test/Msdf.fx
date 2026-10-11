// MSDF decode denemesi (msdfgen PNG, 3 kanal). c.rgb = doku kanallari (sprite Color beyaz olmali).
// Kenar: TMP/msdfgen modeli - 1 ekran px lineer rampa. FXP.x > 0 ise ek keskinlik carpani.
VEC4 fx(VEC4 c, VEC2 uv) {
  FLOAT sd = max(min(c.r, c.g), min(max(c.r, c.g), c.b));
  FLOAT w = max(FWIDTH(sd), 1e-5);
  FLOAT k = FXP.x > 0.0 ? FXP.x : 1.0;
  FLOAT a = SATURATE((sd - 0.5) / w * k + 0.5);
  return VEC4(1.0, 1.0, 1.0, a);
}