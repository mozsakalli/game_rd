using System;
namespace Demo29 {
    class App29 {
        static int log;
        static void L(int d) { log = log * 10 + d; }
        static int Basit() {
            try { L(1); } finally { L(2); }
            return log;
        }
        static int Firlat(int x) {
            try {
                try { if (x > 0) { throw new Exception("boom"); } L(3); }
                finally { L(4); }
            } catch (Exception) { L(5); }
            return log;
        }
        static int Erken() {
            try { L(6); return log; }
            finally { L(7); }
        }
        static int Dongu() {
            int s = 0;
            for (int i = 0; i < 5; i++) {
                try {
                    if (i == 2) { continue; }
                    if (i == 4) { break; }
                    s += i;
                } finally { s += 100; }
            }
            return s;
        }
        static int Karma(int x) {
            try { if (x > 0) { throw new Exception("k"); } L(1); }
            catch (Exception) { L(2); }
            finally { L(3); }
            return log;
        }
        static int Zincir() {
            try {
                try { throw new Exception("z"); }
                finally { L(8); }
            } catch (Exception e) { if (e.Message == "z") { L(9); } }
            return log;
        }
        static int Cift() {
            try {
                try { return 5; }
                finally { L(1); }
            } finally { L(2); }
        }
        public static int Run() {
            int acc = 0;
            log = 0;
            if (Basit() == 12) { acc += 1; }
            log = 0;
            if (Firlat(1) == 45) { acc += 2; }
            log = 0;
            if (Firlat(0) == 34) { acc += 4; }
            log = 0;
            if (Erken() == 6 && log == 67) { acc += 8; }
            if (Dongu() == 504) { acc += 16; }
            log = 0;
            if (Karma(1) == 23) { acc += 32; }
            log = 0;
            if (Karma(0) == 13) { acc += 64; }
            log = 0;
            if (Zincir() == 89) { acc += 128; }
            log = 0;
            if (Cift() == 5 && log == 12) { acc += 256; }
            return acc;
        }
    }
}
