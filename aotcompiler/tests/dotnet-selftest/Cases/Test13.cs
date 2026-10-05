namespace Demo13
{
    class Thing
    {
        int v;
        public Thing(int v0) { v = v0; }
        public int V() { return v; }
    }
    class App13
    {
        public static int Run()
        {
            object o = new Thing(21);
            int acc = 0;
            if (o is Thing) { acc = acc + 1; }
            Thing t = (Thing)o;
            acc = acc + t.V();
            object p = new object();
            if (p is Thing) { acc = acc + 100; }
            if (p is object) { acc = acc + 7; }
            Thing nt = o as Thing;
            if (nt != null) { acc = acc + 3; }
            return acc;
        }
    }
}
