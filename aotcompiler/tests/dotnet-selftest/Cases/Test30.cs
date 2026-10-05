#pragma warning disable
using System;
namespace Demo30 {
    static class Ext30 {
        public static int ToInt(this object o) {
            if (o != null) {
                if (o is int) { return (int)o; }
                if (o is uint) { return (int)(uint)o; }
                if (o is bool) { return (bool)o ? 1 : 0; }
                double d = 0;
                double.TryParse(o.ToString(), out d);
                return (int)d;
            }
            return 0;
        }
        public static int Kati(this object o, int k) { return o.ToInt() * k; }
    }
    class GenericExt30<T> {
        public static int Test(T value) { return value.Kati(2); }
    }
    class App30 {
        public static int Run() {
            int acc = 0;
            object oi = 42;
            if (oi.ToInt() == 42) { acc += 1; }
            object ou = 7u;
            if (ou.ToInt() == 7) { acc += 2; }
            object ob = true;
            if (ob.ToInt() == 1) { acc += 4; }
            object os = "7.9";
            if (os.ToInt() == 7) { acc += 8; }
            object on = null;
            if (on.ToInt() == 0) { acc += 16; }
            if (oi.Kati(3) == 126) { acc += 32; }
            if (double.TryParse("2.5", out double dv) && dv == 2.5) { acc += 64; }
            if (float.TryParse(" 1.25 ", out float fv) && fv == 1.25f) { acc += 128; }
            if (long.TryParse("5000000000", out long lv) && lv == 5000000000) { acc += 256; }
            if (uint.TryParse("4000000000", out uint uv) && uv == 4000000000u) { acc += 512; }
            if (!int.TryParse("abc", out int bad) && bad == 0) { acc += 1024; }
            int ham = 5;
            if (ham.ToInt() == 5) { acc += 2048; }
            if (GenericExt30<int>.Test(3) == 6) { acc += 4096; }
            if (GenericExt30<string>.Test("4") == 8) { acc += 8192; }
            return acc;
        }
    }
}
