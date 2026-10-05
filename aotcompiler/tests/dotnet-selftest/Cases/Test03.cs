namespace Demo3 {
    class App3 {
        public static int Run() {
            int[] a = new int[6];
            for (int i = 0; i < a.Length; i = i + 1) { a[i] = i * i; }
            int s = 0;
            for (int j = 0; j < 6; j = j + 1) { s = s + a[j]; }
            return s + a.Length;
        }
    }
}
