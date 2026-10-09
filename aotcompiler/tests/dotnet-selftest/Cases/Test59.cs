using System;

namespace Demo59
{
    // Dar tabanli enum'lar (enum E : byte/short) ref/out uzerinden: IL ldind.u1/stind.i1 uretir; AOT enum'u 4 bayt int
    // tutar -> okuma/yazma tam int olmali (ust baytlar cop kalmasin). Ayrica alan/dizi/kutulama yollari.
    enum Kind59 : byte { Float, Int, Bool, Str, Enum, Vec2 }
    enum Small59 : short { A = -5, B = 300 }

    class Holder59
    {
        public Kind59 Kind, Element;
        public Small59 S;
    }

    static class App59
    {
        static bool TryKind(Type t, out Kind59 k)
        {
            if (t == typeof(float)) { k = Kind59.Float; return true; }
            if (t == typeof(int)) { k = Kind59.Int; return true; }
            if (t == typeof(bool)) { k = Kind59.Bool; return true; }
            if (t == typeof(string)) { k = Kind59.Str; return true; }
            k = Kind59.Vec2;
            return false;
        }
        static void Bump(ref Kind59 k) { k = (Kind59)((int)k + 1); }
        static void SetSmall(out Small59 s) { s = Small59.B; }
        static int ReadRef(ref Kind59 k) => (int)k;

        public static int Run()
        {
            int sum = 0;
            Kind59 k;
            TryKind(typeof(int), out k); sum += (int)k * 1;          // 1
            TryKind(typeof(bool), out k); sum += (int)k * 10;        // 20
            TryKind(typeof(string), out k); sum += (int)k * 100;     // 300
            Bump(ref k); sum += (int)k * 1000;                       // 4000
            sum += ReadRef(ref k) == 4 ? 10000 : 0;                  // 10000
            Small59 s; SetSmall(out s); sum += s == Small59.B ? 100000 : 0; // 100000
            var h = new Holder59 { Kind = Kind59.Enum, Element = Kind59.Bool };
            Bump(ref h.Kind); sum += h.Kind == Kind59.Vec2 && h.Element == Kind59.Bool ? 1000000 : 0; // 1000000
            var arr = new Kind59[3]; arr[1] = Kind59.Str; Bump(ref arr[1]);
            sum += arr[1] == Kind59.Enum && arr[0] == Kind59.Float ? 10000000 : 0; // 10000000
            object boxed = k; sum += boxed is Kind59 bk && bk == Kind59.Enum ? 100000000 : 0; // 100000000
            return sum; // 111114321
        }
    }
}
