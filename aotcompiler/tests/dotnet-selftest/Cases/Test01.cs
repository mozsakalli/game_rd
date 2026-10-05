namespace Demo
{
    class Counter
    {
        int value;
        public int Add(int amount) { value = value + amount; return value; }
    }
    class Animal2 { public virtual int Speak() { return 1; } }
    class Dog2 : Animal2 { public override int Speak() { return 2; } }
    class App
    {
        static int SumTo(int n)
        {
            int sum = 0;
            int i = 1;
            while (i <= n) { sum = sum + i; i = i + 1; }
            return sum;
        }
        public static int Run()
        {
            Counter c = new Counter();
            c.Add(5);
            c.Add(7);
            Animal2 a = new Dog2();
            return c.Add(0) + SumTo(10) + a.Speak();
        }
    }
}
