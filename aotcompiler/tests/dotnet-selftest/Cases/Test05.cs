namespace Demo5
{
    class Base5 { public virtual int Tag() { return 1; } }
    class Kid5 : Base5 { public override int Tag() { return 2; } public int Extra() { return 30; } }
    class App5
    {
        public static int Run()
        {
            float f = 7.9f;
            int i = (int)f;
            int gi = (int)((float)i * 2.0f);
            Base5 b = new Kid5();
            int acc = i + gi;
            if (b is Kid5) { acc = acc + 100; }
            Kid5 k = (Kid5)b;
            acc = acc + k.Extra();
            Base5 b2 = new Base5();
            Kid5 nk = b2 as Kid5;
            if (nk == null) { acc = acc + 5; }
            acc = acc + b.Tag();
            return acc;
        }
    }
}
