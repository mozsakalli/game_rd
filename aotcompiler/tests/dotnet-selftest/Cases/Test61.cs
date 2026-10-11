using System;
using Matrix;

// Matris hucreleri: her case = bir T sekil kategorisi (Cases/Matrix.cs Probe<T>). Kirmizi hucre = o kategoride generic
// indirgeme hatasi; EXCEPTION mesaji hangi op/metot oldugunu soyler.
namespace Demo61 { class App61 { public static int Run() { return (int)Probe<int>.Run(5, 9, "int"); } } }
namespace Demo62 { class App62 { public static int Run() { return (int)Probe<long>.Run(5000000000L, -3L, "long"); } } }
namespace Demo63 { class App63 { public static int Run() { return (int)Probe<Color>.Run(Color.Green, Color.Blue, "enum"); } } }
namespace Demo64 { class App64 { public static int Run() { return (int)Probe<SA>.Run(new SA(1, 2.5f), new SA(3, 4.5f), "struct-atomic"); } } }
namespace Demo65 { class App65 { public static int Run() { return (int)Probe<SR>.Run(new SR("x", 1), new SR("yy", 2), "struct-ref"); } } }
namespace Demo66 { class App66 { public static int Run() { return (int)Probe<string>.Run("alpha", "beta", "string"); } } }
namespace Demo67 { class App67 { public static int Run() { return (int)Probe<C>.Run(new C(1), new D(2), "class"); } } }
namespace Demo68 { class App68 { public static int Run() { return (int)Probe<IFace>.Run(new C(3), new D(4), "iface"); } } }
namespace Demo69
{
    class App69
    {
        static int acc;
        static void F1() { acc += 1; }
        static void F2() { acc += 10; }
        public static int Run()
        {
            Action a = F1, b = F2;
            long h = Probe<Action>.Run(a, b, "delegate");
            a(); b();
            return (int)(h % 1000000007L) + acc;
        }
    }
}
// Fark ayiklama: H.trace = true -> her adim stderr'e ([h] n); .NET dll ve AOT exe ayri kosturulup diff alinir.
namespace Demo70 { class App70 { public static int Run() { return MatrixGate.Array ? (int)Probe<int[]>.Run(new[] { 1, 2 }, new[] { 3 }, "array") : -70; } } }
namespace Demo71 { class App71 { public static int Run() { return MatrixGate.Nullable ? (int)Probe<int?>.Run(7, 8, "nullable") : -71; } } }
// Gate'ler: hucre derlemeyi kirarsa gecici kapatilir (S3 envanteri). S1 sonrasi ikisi de acik.
static class MatrixGate { public const bool Nullable = true; public const bool Array = true; }
namespace Demo72
{
    // Nullable<T> ozel yolu: where T: struct ile HasValue/Value/GetValueOrDefault, lifted operatorler, null kutulama.
    class App72
    {
        static long h;
        static void L(long d) { h = (h * 31 + d) % 1000000007L; }
        static T? Wrap<T>(T v, bool some) where T : struct { return some ? v : (T?)null; }
        static int Probe<T>(T v) where T : struct
        {
            T? n = Wrap(v, true);
            T? m = Wrap(v, false);
            L(n.HasValue ? 1 : 0); L(m.HasValue ? 1 : 0);
            L(n.Value.Equals(v) ? 1 : 0);
            L(m.GetValueOrDefault().Equals(default(T)) ? 1 : 0);
            object o = n; object p = m;
            L(o == null ? 1 : 0); L(p == null ? 1 : 0);
            L(o is T ? 1 : 0); L(o is T? ? 1 : 0);
            T? back = (T?)o;
            L(back.HasValue ? 1 : 0);
            T? fromNull = (T?)p;
            L(fromNull.HasValue ? 1 : 0);
            L(n.Equals(m) ? 1 : 0); L(n.Equals(v) ? 1 : 0);
            return 0;
        }
        public static int Run()
        {
            h = 3;
            Probe(5); Probe(new SA(2, 1f)); Probe(Color.Red); Probe(2.5);
            int? a = 4, b = null;
            L((a + 1) ?? -1); L((b + 1) ?? -1); L(a == 4 ? 1 : 0); L(b == null ? 1 : 0); L((a > b) ? 1 : 0);
            return (int)h;
        }
    }
}
