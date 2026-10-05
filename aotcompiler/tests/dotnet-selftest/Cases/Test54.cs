namespace Demo54
{
    // Minor GC kok testi (bit maskeli): hangi kok/edge turu genc nesneyi kaybediyor?
    class Box { public string s; public Box next; }
    class App54
    {
        static string sField;
        static Box box;
        static System.Collections.Generic.List<string> list;
        static System.Collections.Generic.Dictionary<string, int> dict;
        static string[] arr;
        static string Mk(int i) { return "v" + i; } // genc string (op_add)
        public static int Run()
        {
            int ok = 0;
            sField = Mk(1); System.GC.Collect(0); if (sField.Length == 2) ok |= 1;
            box = new Box(); box.s = Mk(2); System.GC.Collect(0); if (box.s.Length == 2) ok |= 2;
            box.next = new Box { s = Mk(3) }; System.GC.Collect(0); if (box.next.s.Length == 2) ok |= 4;
            arr = new string[4]; arr[1] = Mk(4); System.GC.Collect(0); if (arr[1].Length == 2) ok |= 8;
            list = new System.Collections.Generic.List<string>(); list.Add(Mk(5)); System.GC.Collect(0); if (list[0].Length == 2) ok |= 16;
            dict = new System.Collections.Generic.Dictionary<string, int>(); dict[Mk(6)] = 6; System.GC.Collect(0); if (dict.ContainsKey("v6")) ok |= 32;
            // eski sahip -> genc deger (3 minor sonra sahipler eski)
            for (int g = 0; g < 4; g++) System.GC.Collect(0);
            box.s = Mk(7); System.GC.Collect(0); if (box.s.Length == 2) ok |= 64;
            arr[2] = Mk(8); System.GC.Collect(0); if (arr[2].Length == 2) ok |= 128;
            list.Add(Mk(9)); System.GC.Collect(0); if (list[1].Length == 2) ok |= 256;
            dict[Mk(10)] = 10; System.GC.Collect(0); if (dict.ContainsKey("v10")) ok |= 512;
            return ok;
        }
    }
}