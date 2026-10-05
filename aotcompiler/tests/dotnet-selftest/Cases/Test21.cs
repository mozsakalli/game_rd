using System;
namespace Demo21 {
    class ResA {
        public static int aFin;
        public static int chain;
        public static int chainDone;
        ~ResA() {
            aFin++;
            if (chain == 5) { chainDone++; chain = 0; }
        }
    }
    class ResB : ResA {
        public static int bFin;
        int big;
        ~ResB() {
            bFin++;
            chain = 5;
            if (big == 0) { return; }
            bFin += 100;
        }
    }
    class App21 {
        static void MakeGarbage() {
            ResB b = new ResB();
            ResA a = new ResA();
        }
        public static int Run() {
            MakeGarbage();
            GC.Collect();
            return ResA.aFin * 100 + ResB.bFin * 10 + ResA.chainDone;
        }
    }
}
