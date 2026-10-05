namespace Demo12 {
    class App12 {
        int f;
        static int Deep(int[] a, int i) {
            return a[i] * 2;
        }
        static int Mid(int[] a) {
            return Deep(a, 5) + 1;
        }
        static int CrashBounds() {
            int[] a = new int[3];
            return Mid(a);
        }
        static int Poke(App12 x) {
            return x.f;
        }
        static int CrashNull() {
            App12 x = null;
            return Poke(x);
        }
        public static int Run() {
            int[] a = new int[4];
            a[2] = 21;
            return Deep(a, 2);
        }
    }
}
