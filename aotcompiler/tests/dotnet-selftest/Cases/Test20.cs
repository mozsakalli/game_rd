namespace Demo20
{
    class Pair20
    {
        int a;
        int b;
        public Pair20(int x, int y) { a = x; b = y; }
        public bool TryGet(int k, out int v)
        {
            if (k > 0) { v = k * 10; return true; }
            v = -1;
            return false;
        }
    }
    class Shape20 { public virtual int Tag() { return 1; } }
    class Circle20 : Shape20 { public override int Tag() { return 2; } public int R() { return 30; } }
    class App20
    {
        public static int Run()
        {
            var acc = 0;
            var s = "abc";
            acc += s.Length;
            var p = new Pair20(1, 2);
            if (p.TryGet(5, out var got)) { acc += got; }
            if (!p.TryGet(-1, out int miss)) { acc += miss + 2; }
            object o = new Circle20();
            if (o is Circle20 c) { acc += c.R(); }
            if (o is Shape20 sh) { acc += sh.Tag() * 100; }
            var f = 1.5f;
            acc += (int)(f * 2);
            for (var i = 0; i < 3; i++) { acc += i; }
            return acc;
        }
    }
}
