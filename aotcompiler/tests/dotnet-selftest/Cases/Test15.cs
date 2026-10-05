namespace Demo15
{
    class Pt
    {
        int x;
        int y;
        public Pt(int ax, int ay) { x = ax; y = ay; }
        public override int GetHashCode() { return x * 31 + y; }
        public override bool Equals(object o)
        {
            Pt p = o as Pt;
            if (p == null) { return false; }
            return p.x == x && p.y == y;
        }
    }
    class App15
    {
        public static int Run()
        {
            Pt a = new Pt(2, 3);
            Pt b = new Pt(2, 3);
            Pt c = new Pt(9, 9);
            int acc = a.GetHashCode();
            object oa = a;
            if (oa.Equals(b)) { acc = acc + 100; }
            if (a.Equals(c)) { acc = acc + 1000; }
            object p = new object();
            object q = new object();
            int h1 = p.GetHashCode();
            int h2 = q.GetHashCode();
            int h1b = p.GetHashCode();
            if (h1 == h1b && h1 != h2) { acc = acc + 10; }
            if (p.Equals(p)) { acc = acc + 5; }
            if (p.Equals(q)) { acc = acc + 5000; }
            return acc + h2;
        }
    }
}
