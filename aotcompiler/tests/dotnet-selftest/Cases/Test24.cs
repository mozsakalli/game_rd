using System;
namespace Demo24 {
    enum Pri24 { Low = 1, Mid = 10, High = 100 }
    class App24 {
        public static int Run() {
            int acc = 0;
            object o = Pri24.Mid;
            if (o is Pri24) { acc += 1; }
            if (o.ToString() == "Mid") { acc += 2; }
            if ((int)o == 10) { acc += 4; }
            if ((Pri24)(object)100 == Pri24.High) { acc += 8; }
            if (o.GetHashCode() == 10) { acc += 16; }
            if (typeof(Pri24).Name == "Pri24") { acc += 32; }
            if (o.GetType() == typeof(Pri24)) { acc += 64; }
            if (!(o is int)) { acc += 128; }
            if (o.Equals(Pri24.Mid)) { acc += 256; }
            object undef = (Pri24)55;
            if (undef.ToString() == "55") { acc += 512; }
            object a24 = Pri24.Mid;
            object b24 = Pri24.Mid;
            if (a24 == b24) { acc += 1024; }
            return acc;
        }
    }
}
