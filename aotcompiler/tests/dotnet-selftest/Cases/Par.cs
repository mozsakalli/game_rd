using System;

// Parite kiti (Par86+ case'leri): her alt-kontrol "case*1000+k<TAB>deger" satiri basar; harness (ParseResults) bu
// satirlari ayri case sayar -> .NET baseline ile AOT/interp degeri yan yana diff'lenir. Beklenen deger hardcode EDILMEZ:
// olcut "editorde (.NET) ne cikiyorsa o". Adim = ayri static metot (AOT stub metot bazinda; eksik corelib tipi yalniz o
// adimi dusurur). Deterministik olmayan .NET degerleri (string/HashCode.Combine/kimlik hash, stack trace, zaman) yazilmaz.
namespace ParityKit
{
    public static class Par
    {
        public static void Out(int c, int k, string v)
        {
            Console.Write(c * 1000 + k);
            Console.Write("\t");
            Console.WriteLine(v == null ? "<null>" : v.Replace("\n", "\\n").Replace("\r", "\\r"));
        }

        public static string Ex(Exception e) { return "EXC:" + e.GetType().Name; }

        // Degerleri '|' ile birlestirir; null -> "null". ToString'in kendisi de parite konusudur.
        public static string J(params object[] xs)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < xs.Length; i++)
            {
                if (i > 0) sb.Append('|');
                var x = xs[i];
                sb.Append(x == null ? "null" : x.ToString());
            }
            return sb.ToString();
        }

        // SUREC KATILI kontroller (AOT'de C trap/abort: INT_MIN/-1 idiv -> 0xC0000095, dev tahsis -> OOM abort). .NET'te yakalanabilir
        // exception; AOT'te surecin tamami olur ve kalan tum case'ler "cikti yok" olur. Varsayilan KAPALI; acmak icin true yap.
        public const bool Killers = true; // A1 duzeltmesi sonrasi acildi (MIN/-1 -> OverflowException)
        public static string K(Func<string> f) { return Killers ? T(f) : "SKIP(killer)"; }
        public const bool HugeAlloc = false; // int.MaxValue boyutlu tahsisler: AOT'de OOM abort riski
        public static string H(Func<string> f) { return HugeAlloc ? T(f) : "SKIP(huge)"; }

        public static string T(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return Ex(e); }
        }

        // libm'e bagli (Sin/Cos/Exp/Log/Pow) sonuclar: son-bit sapmasi zararsiz, 6 hane.
        public static string R6(double d) { return Math.Round(d, 6).ToString("R"); }

        public static int Loop(int c, int n, Func<int, string> step)
        {
            for (int k = 1; k <= n; k++)
            {
                string v;
                try { v = step(k); }
                catch (Exception e) { v = Ex(e); }
                Out(c, k, v);
            }
            return n;
        }
    }
}
