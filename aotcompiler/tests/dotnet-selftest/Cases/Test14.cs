using System;
namespace Demo14
{
    class GameError : Exception
    {
        int code;
        public GameError(int c) { code = c; }
        public int Code() { return code; }
    }
    class App14
    {
        int hp;
        static int Boom(int x)
        {
            if (x > 3) { throw new GameError(x * 10); }
            return x;
        }
        public static int Run()
        {
            int acc = 0;
            try
            {
                acc = acc + Boom(2);
                acc = acc + Boom(7);
                acc = acc + 1000;
            }
            catch (GameError e)
            {
                acc = acc + e.Code();
            }
            try
            {
                int[] a = new int[2];
                acc = acc + a[5];
            }
            catch (IndexOutOfRangeException)
            {
                acc = acc + 100;
            }
            try
            {
                try
                {
                    Boom(9);
                }
                catch (IndexOutOfRangeException)
                {
                    acc = acc + 5000;
                }
            }
            catch (GameError g)
            {
                acc = acc + g.Code() / 10;
            }
            try
            {
                App14 n = null;
                acc = acc + n.hp;
            }
            catch (NullReferenceException)
            {
                acc = acc + 200;
            }
            catch { }
            try
            {
                try { Boom(5); } catch (GameError e2) { acc = acc + 1; throw; }
            }
            catch (GameError e3)
            {
                acc = acc + e3.Code() / 50;
            }
            return acc;
        }
        static int PrintDemo()
        {
            try { Boom(6); } catch (GameError e) { Console.Error.WriteLine(e.Code()); }
            return 1;
        }
    }
}
