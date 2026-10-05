namespace Demo8
{
    class Box<T>
    {
        T item;
        static int made;
        public Box(T v) { item = v; made = made + 1; }
        public T Get() { return item; }
        public void Set(T v) { item = v; }
        public static int Made() { return made; }
    }
    class Pair<A, B>
    {
        A a;
        B b;
        public Pair(A x, B y) { a = x; b = y; }
        public A First() { return a; }
        public B Second() { return b; }
        public Pair<B, A> Swap() { return new Pair<B, A>(b, a); }
    }
    class NumBase { public virtual int Rank() { return 1; } }
    class NumBox<T> : NumBase
    {
        T v;
        public NumBox(T v0) { v = v0; }
        public override int Rank() { return 5; }
        public T Val() { return v; }
    }
    class App8
    {
        static T Pick<T>(T x, T y, bool second) { if (second) { return y; } return x; }
        public static int Run()
        {
            Box<int> bi = new Box<int>(41);
            bi.Set(bi.Get() + 1);
            Box<int> bi2 = new Box<int>(100);
            Box<float> bf = new Box<float>(2.5f);
            Box<Box<int>> nested = new Box<Box<int>>(bi);
            int acc = nested.Get().Get();
            acc = acc + (int)bf.Get();
            acc = acc + Box<int>.Made() * 10;
            acc = acc + Box<float>.Made();
            Pair<int, float> p = new Pair<int, float>(7, 1.5f);
            Pair<float, int> q = p.Swap();
            acc = acc + q.Second() * 100;
            acc = acc + (int)(q.First() * 2.0f);
            NumBase nb = new NumBox<int>(9);
            acc = acc + nb.Rank();
            NumBox<int> nbx = (NumBox<int>)nb;
            acc = acc + nbx.Val();
            acc = acc + Pick(3, 8, true);
            acc = acc + Pick<int>(3, 8, false);
            acc = acc + Pick(bi2, bi, false).Get();
            int[] arr = new int[2];
            arr[0] = 4;
            arr[1] = 6;
            acc = acc + Pick(arr, arr, true).Length;
            return acc;
        }
    }
}
