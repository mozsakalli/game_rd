VEC4 fx(VEC4 c, VEC2 uv) {
  FLOAT g = dot(c.rgb, VEC3(0.299, 0.587, 0.114));
  return VEC4(g, g, g, c.a);
}
