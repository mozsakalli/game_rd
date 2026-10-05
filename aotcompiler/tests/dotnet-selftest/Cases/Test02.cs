namespace Demo2
{
    class Vec
    {
        int x;
        int y;
        public Vec(int ax, int ay) { x = ax; y = ay; }
        public int Sum() { return x + y; }
    }
    class App2
    {
        static int total;
        static int seed = 3;
        static int Counter() { total = total + 1; return total; }
        static System.Collections.Generic.IEnumerable<int> Squares(int n)
        {
            int i = 0;
            while (i < n) { yield return i * i; i = i + 1; }
        }
        public static int Run()
        {
            Vec v = new Vec(3, 4);
            int s = 0;
            foreach (int q in Squares(4)) { s = s + q; }
            return v.Sum() + s + seed + Counter() + Counter();
        }
    }
}
