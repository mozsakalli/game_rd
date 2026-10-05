using System;
namespace Demo41 {
    class Holder { public Action<int> OnValue; }
    class App41 {
        static int calls;
        static int Next() { calls++; return 4; }
        static void Add(int value) { calls += value * 10; }
        public static int Run() {
            Holder holder = new Holder();
            holder.OnValue?.Invoke(Next());
            holder.OnValue = Add;
            holder.OnValue?.Invoke(Next());
            return calls;
        }
    }
}
