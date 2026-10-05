using static Demo33.Parser33;
namespace Demo33 {
    static class Parser33 {
        public static int parseValue(int value) { return value * 2; }
        public static int Over(int value) { return value + 1; }
        public static int Over(int left, int right) { return left + right; }
    }
    class App33 {
        static int parseValue(int value) { return value * 50; }
        public static int Run() {
            return Parser33.parseValue(21) + Over(3) + Over(4, 5) + parseValue(2);
        }
    }
}
