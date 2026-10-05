using System;
namespace Demo25 {
    partial class Cfg {
        int a = 7;
        public int A() { return a + B(); }
    }
    partial class Cfg {
        int b = 35;
        public int B() { return b; }
        public int Total { get { return A() + b; } }
    }
    class App25 {
        public static int Run() {
            var c = new Cfg();
            return c.Total + c.A();
        }
    }
}
