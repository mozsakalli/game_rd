using System;
namespace Demo23 {
    class App23 {
        public static int Run() {
            int acc = 0;
            object o = 42;
            if (o is int) { acc += 1; }
            if (o is int v && v == 42) { acc += 2; }
            if (!(o is long)) { acc += 4; }
            if ((int)o == 42) { acc += 8; }
            if (o.GetType() == typeof(int)) { acc += 16; }
            if (o.ToString() == "42") { acc += 32; }
            object b = true;
            if (b.ToString() == "True") { acc += 64; }
            object d = 2.5;
            if (d.ToString() == "2.5") { acc += 128; }
            if (o.GetHashCode() == 42) { acc += 256; }
            object small1 = 7;
            object small2 = 7;
            if (small1 == small2) { acc += 512; }
            object big1 = 100000;
            object big2 = 100000;
            if (big1 != big2) { acc += 1024; }
            if (o.Equals(42)) { acc += 2048; }
            try { long bad = (long)o; acc += (int)bad; }
            catch (InvalidCastException) { acc += 4096; }
            object n = null;
            try { int z = (int)n; acc += z; }
            catch (NullReferenceException) { acc += 8192; }
            long l = 5000000000;
            object lo = l;
            if (lo.ToString() == "5000000000") { acc += 16384; }
            return acc;
        }
    }
}
