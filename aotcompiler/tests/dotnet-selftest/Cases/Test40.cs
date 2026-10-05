namespace Demo40
{
    class Pair
    {
        int left = 2, right = 3;
        static int first = 4, second = 5;
        public int Sum()
        {
            int a = left, b = right;
            return a + b + first + second;
        }
    }
    class App40
    {
        public static int Run() { return new Pair().Sum(); }
    }
}
