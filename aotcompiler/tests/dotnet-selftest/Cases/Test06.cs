namespace Demo6
{
    interface IShape { int Area(); }
    interface INamed { int Id(); }
    class Rect : IShape, INamed
    {
        int w; int h;
        public Rect(int w0, int h0) { w = w0; h = h0; }
        public int Area() { return w * h; }
        public int Id() { return 1; }
    }
    class Circle : IShape
    {
        int r;
        public Circle(int r0) { r = r0; }
        public int Area() { return 3 * r * r; }
    }
    class App6
    {
        static int SumAreas(IShape a, IShape b) { return a.Area() + b.Area(); }
        public static int Run()
        {
            IShape s1 = new Rect(3, 4);
            IShape s2 = new Circle(2);
            int acc = SumAreas(s1, s2);
            if (s1 is INamed) { acc = acc + 100; }
            INamed n = s1 as INamed;
            if (n != null) { acc = acc + n.Id(); }
            INamed n2 = s2 as INamed;
            if (n2 == null) { acc = acc + 5; }
            Rect rr = (Rect)s1;
            acc = acc + rr.Area();
            return acc;
        }
    }
}
