using System;
using System.Collections.Generic;

namespace Demo56
{
    // Dizi reflection'i (docs/registry-removal.md Faz 4a): VmArray.elem -> `is T[]`, GetType, Type.IsArray/GetElementType,
    // Array.CreateInstance/GetValue/SetValue/Length (Array-tipli referans). Sonuc .NET baseline ile birebir (bit maskesi).
    struct Pt56 { public int x, y; }
    struct Ref56 { public string s; public int k; } // ref tasiyan struct: dizi elemanlari GC taranmali
    enum Renk56 { Kirmizi, Yesil = 7 }
    class Base56 { }
    class Derived56 : Base56 { }
    class App56
    {
        public static int Run()
        {
            long acc = 0;
            object fa = new float[3];
            object ia = new int[2];
            object sa = new string[] { "a", "b" };
            object da = new Derived56[1];
            object pa = new Pt56[2];
            object ea = new Renk56[2];

            // is T[]: exact deger tipleri, ref kovaryans
            if (fa is float[]) acc |= 1;
            if (!(fa is int[])) acc |= 2;
            if (sa is object[]) acc |= 4;            // kovaryans
            if (da is Base56[]) acc |= 8;            // kovaryans (turev -> base)
            if (!(ia is object[])) acc |= 16;        // deger tipi dizisi object[] degil
            if (!(fa is Array == false)) acc |= 32;  // Array sinifi

            // GetType / IsArray / GetElementType
            Type ft = fa.GetType();
            if (ft.IsArray) acc |= 64;
            if (ft.GetElementType() == typeof(float)) acc |= 128;
            if (ft == typeof(float[])) acc |= 256;                       // kimlik: typeof(T[]) ile ayni
            if (sa.GetType().GetElementType() == typeof(string)) acc |= 512;
            if (pa.GetType().GetElementType() == typeof(Pt56)) acc |= 1024;
            if (!typeof(int).IsArray && typeof(int).GetElementType() == null) acc |= 2048;
            if (ea.GetType().GetElementType() == typeof(Renk56)) acc |= 4096;

            // Array-tipli referans: Length / GetValue / SetValue
            Array arr = (Array)fa;
            if (arr.Length == 3) acc |= 8192;
            arr.SetValue(2.5f, 1);
            if ((float)arr.GetValue(1) == 2.5f && ((float[])fa)[1] == 2.5f) acc |= 16384;
            Array sarr = (Array)sa;
            sarr.SetValue("z", 0);
            if ((string)sarr.GetValue(0) == "z" && ((string[])sa)[0] == "z") acc |= 32768;
            Array parr = (Array)pa;
            parr.SetValue(new Pt56 { x = 3, y = 4 }, 1);
            Pt56 p1 = (Pt56)parr.GetValue(1);
            if (p1.x == 3 && p1.y == 4 && ((Pt56[])pa)[1].y == 4) acc |= 65536;
            Array earr = (Array)ea;
            earr.SetValue(Renk56.Yesil, 0);
            if ((Renk56)earr.GetValue(0) == Renk56.Yesil && ((Renk56[])ea)[0] == Renk56.Yesil) acc |= 131072;
            bool threw = false;
            try { sarr.SetValue(new Derived56(), 1); } catch (Exception) { threw = true; }
            if (threw) acc |= 262144; // tip uyusmazligi: InvalidCast

            // Array.CreateInstance: deger, referans, struct (ref tasiyan)
            Array ca = Array.CreateInstance(typeof(int), 4);
            if (ca is int[] && ca.Length == 4 && ca.GetType() == typeof(int[])) acc |= 524288;
            ((int[])ca)[2] = 9;
            if ((int)ca.GetValue(2) == 9) acc |= 1048576;
            Array cs = Array.CreateInstance(typeof(string), 2);
            cs.SetValue("q", 1);
            if (cs is string[] && ((string[])cs)[1] == "q") acc |= 2097152;
            // Ref tasiyan struct dizisi: CreateInstance tahsis tipi arr_X_type (eleman eleman trace) + SetValue/GetValue kopyasi.
            // NOT: "GC.Collect sonrasi local'den oku" senaryosu bu runtime'da GECERSIZ (gc_major kok seti = statikler + acik
            // root'lar; managed frame local'leri kok degil, GC yalniz host frame safepoint'inde kosar) -> burada GC baskisi yok.
            Array cr = Array.CreateInstance(typeof(Ref56), 64);
            for (int i = 0; i < 64; i++)
                cr.SetValue(new Ref56 { s = "s" + i, k = i }, i);
            bool ok = true;
            for (int i = 0; i < 64; i++)
            {
                Ref56 r = (Ref56)cr.GetValue(i);
                if (r.k != i || r.s != "s" + i) ok = false;
            }
            if (ok) acc |= 4194304;
            Ref56[] typed = (Ref56[])cr;
            if (typed[7].k == 7 && typed[63].s == "s63") acc |= 134217728; // tipli gorunum ayni veri
            Array cf = Array.CreateInstance(typeof(float), 1);
            if (cf.GetType().GetElementType() == typeof(float)) acc |= 8388608;

            // bos dizi + GetType
            object empty = new Pt56[0];
            if (empty.GetType().IsArray && empty.GetType().GetElementType() == typeof(Pt56)) acc |= 16777216;

            // as T[]
            object[] oa = sa as object[];
            if (oa != null && oa.Length == 2) acc |= 33554432;
            int[] notInts = fa as int[];
            if (notInts == null) acc |= 67108864;

            return (int)acc;
        }
    }
}
