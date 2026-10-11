VEC4 fx(VEC4 c, VEC2 uv) {
  VEC2 d = uv - VEC2(0.5, 0.5);
  FLOAT v = 1.0 - SATURATE(dot(d, d) * 1.8);
  return VEC4(c.rgb * v, c.a);
}
