namespace Demo7
{
    class A7
    {
        int x;
        int y = 3;
        public A7() { x = 1; }
        public A7(int x0) { x = x0; }
        public int Sum() { return x + y; }
    }
    class B7 : A7
    {
        int z = 5;
        public B7() { z = z + 1000; }
        public B7(int x0) : base(x0 * 2) { z = z + 100; }
        public int Total() { return Sum() + z; }
    }
    class C7 : B7
    {
        int w = 7;
        public C7() { w = w + 1; }
        public C7(int k) : this() { w = w + k; }
        public int W() { return w; }
    }
    class App7
    {
        public static int Run()
        {
            B7 b1 = new B7(4);
            B7 b2 = new B7();
            C7 c = new C7(10);
            return b1.Total() + b2.Total() + c.Total() + c.W();
        }
    }
}
