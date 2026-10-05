namespace Demo35 {
    class App35 {
        static int calls;
        static int Next() { calls++; return 7; }
        public static int Run() {
            int a = 0;
            int b = 0;
            a = b = Next();
            return a + b + calls * 10;
        }
    }
}
