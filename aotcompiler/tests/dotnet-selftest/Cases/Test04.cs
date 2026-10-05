namespace Demo4
{
    unsafe struct Mat2 { public fixed float m[4]; }
    class Vec2i
    {
        int x;
        int y;
        public Vec2i(int ax, int ay) { x = ax; y = ay; }
        public static Vec2i operator +(Vec2i a, Vec2i b) { return new Vec2i(a.x + b.x, a.y + b.y); }
        public int Sum() { return x + y; }
    }
    unsafe class App4
    {
        static void Scale(ref int v, int f) { v = v * f; }
        static void FillDiag(ref Mat2 m, float d) { m.m[0] = d; m.m[3] = d; }
        static int AddN(int x, int y = 40) { return x + y; }
        static int AddN(int x, int y, int z) { return x + y + z; }
        public static int Run()
        {
            int v = 5;
            Scale(ref v, 3);
            Vec2i a = new Vec2i(1, 2);
            Vec2i b = new Vec2i(3, 4);
            Vec2i cc = a + b;
            Mat2 m = new Mat2();
            FillDiag(ref m, 2.0f);
            int diag = 0;
            if (m.m[0] + m.m[3] == 4.0f) { diag = 7; }
            return v + cc.Sum() + AddN(2) + AddN(1, 2, 3) + diag;
        }
    }
}
