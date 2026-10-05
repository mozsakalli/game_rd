namespace Demo10 {
    delegate object Modifier10(object value);
    class Locale10 {
        public const string English = "en";
    }
    class App10 {
        static int Grade(int x) {
            int r = 0;
            switch (x) {
                case 1:
                case 2: r = 10; break;
                case 3: r = 30; break;
                default: r = 99; break;
            }
            return r;
        }
        static int Kind(int x) {
            switch (x) {
                case 0: return 5;
                default: return 7;
            }
            return 0;
        }
        static int Sel(char c) {
            switch (c) {
                case 'a': return 1;
                default: return 0;
            }
            return 0;
        }
        static int Qualified(string value) {
            switch (value) {
                case Locale10.English: return 17;
                default: return 0;
            }
        }
        static int Nested(int outer, int inner) {
            switch (outer) {
                case 1:
                    switch (inner) {
                        case 2: return 23;
                        default: return 0;
                    }
                default: return 0;
            }
        }
        static int Escaped(App10 @base) {
            @base.Visible = !@base.Hidden;
            return @base.Visible ? @base.Value : 0;
        }
        static string GenericConcat<T>(T value) {
            return "v=" + value;
        }
        static int StringConversions() {
            var model = new App10();
            string interpolated = $"{model}";
            Modifier10 modifier = model == null ? (Modifier10)null : delegate (object value) { return value; };
            return GenericConcat<int>(7) == "v=7" && 3.5 + "x" == "3.5x" && interpolated.Length > 0 && modifier(model) == model ? 31 : 0;
        }
        static int ByteFormat() {
            return string.Format("{0:x2}", (byte)10) == "0a" &&
                string.Format("#{0:X02}{1:X02}{2:X02}{3:X02}", (byte)1, (byte)15, (byte)16, (byte)255) == "#010F10FF" ? 41 : 0;
        }
        int Value = 29;
        bool Hidden;
        bool Visible;
        public static int Run() {
            int acc = Grade(1) + Grade(2) + Grade(3) + Grade(7);
            int s = 0;
            for (int i = 0; i < 10; i = i + 1) {
                if (i == 3) { continue; }
                if (i == 6) { break; }
                s = s + i;
            }
            acc = acc + s;
            int w = 0;
            int j = 0;
            while (j < 100) {
                j = j + 1;
                if (j % 2 == 0) { continue; }
                if (j > 9) { break; }
                w = w + j;
            }
            acc = acc + w;
            switch (j) {
                case 11: acc = acc + 1000; break;
            }
            acc = acc + Kind(0) * 2 + Kind(4);
            acc = acc + Sel('x') + Sel('a') * 3;
            acc = acc + Qualified("en") + Nested(1, 2) + Escaped(new App10()) + StringConversions() + ByteFormat();
            return acc;
        }
    }
}
