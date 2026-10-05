using System;
namespace Demo48
{
    class App48
    {
        int alan = 3;
        // this capture: instance alanina lambda icinden eris
        int ThisCapture()
        {
            Func<int> f = () => alan * 10;
            alan = 4;
            return f(); // 40: alan guncel okunmali (referans, kopya degil)
        }
        static int LocalMutation()
        {
            int sayac = 0;
            Action art = () => { sayac = sayac + 1; };
            art();
            art();
            sayac = sayac + 100;
            return sayac; // 102: lambda ve dis kod AYNI hucreyi gormeli
        }
        static int ParamCapture(int p)
        {
            Func<int, int> f = x => x + p;
            p = p + 1;
            return f(10); // p=6 guncel: 16
        }
        static int SharedState()
        {
            int ortak = 0;
            Action a = () => { ortak = ortak + 1; };
            Action b = () => { ortak = ortak + 10; };
            a(); b(); a();
            return ortak; // 12: iki lambda ayni hucreyi paylasir
        }
        static int LoopCapture()
        {
            Func<int>[] fs = new Func<int>[3];
            for (int i = 0; i < 3; i++)
            {
                int kopya = i; // C# idiyomu: dongu basina yeni hucre
                fs[i] = () => kopya * kopya;
            }
            return fs[0]() + fs[1]() * 10 + fs[2]() * 100; // 0 + 10 + 400 = 410
        }
        static int NestedCapture()
        {
            int dis = 5;
            Func<int, Func<int>> yap = a => () => a + dis; // ic lambda dis lambda parametresini + dis local'i yakalar
            Func<int> f = yap(2);
            dis = 6;
            return f(); // 2 + 6 = 8
        }
        public static int Run()
        {
            int acc = 0;
            if (new App48().ThisCapture() == 40) { acc += 1; }
            if (LocalMutation() == 102) { acc += 2; }
            if (ParamCapture(5) == 16) { acc += 4; }
            if (SharedState() == 12) { acc += 8; }
            if (LoopCapture() == 410) { acc += 16; }
            if (NestedCapture() == 8) { acc += 32; }
            return acc;
        }
    }
}
