namespace Demo18 {
    enum Renk { Kirmizi, Yesil = 5, Mavi }
    class App18 {
        const int Carpan = 3;
        static readonly int Taban = 40;
        public static int Run() {
            int acc = 0;
            for (int i = 0; i < 5; i++) { acc += i; }
            acc = acc * Carpan + Taban;
            int j = 10;
            j -= 2; j *= 3; j /= 4; j %= 5;
            acc += j;
            int k = 2;
            int post = k++;
            int pre = ++k;
            acc += post + pre + k;
            acc += k > 3 ? 100 : 200;
            int d = 0;
            do { d++; } while (d < 3);
            acc += d;
            int[] arr = new int[] { 4, 5, 6 };
            arr[1] += 10;
            arr[2]++;
            acc += arr[0] + arr[1] + arr[2] + arr.Length;
            uint m = 0xFFu;
            m = (m << 4) | 0xAu;
            acc += (int)(m & 0xFFFu);
            uint big = 0x80000000u;
            if (big > 0x7FFFFFFFu) { acc += 7; }
            long neg = -big;
            if (neg < 0) { acc += 9; }
            ulong ul = 0xFF00FF00FF00FF00ul;
            ul = ul >> 8;
            if ((ul & 0xFFul) == 0xFFul) { acc += 11; }
            Renk r = Renk.Yesil;
            if (r == Renk.Yesil) { acc += (int)r; }
            switch (r) {
                case Renk.Yesil: acc += 20; break;
                default: acc += 1; break;
            }
            r = (Renk)6;
            if (r == Renk.Mavi) { acc += 2; }
            int bits = ~0;
            if (bits == -1) { acc += 3; }
            acc += 1 << 3;
            return acc;
        }
    }
}
