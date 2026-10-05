namespace Demo42 {
    class Node { }
    class App42 {
        static int fallbackCalls;
        static Node Fallback() { fallbackCalls++; return new Node(); }
        public static int Run() {
            Node present = new Node();
            Node first = present ?? Fallback();
            Node missing = null;
            Node second = missing ?? Fallback();
            int result = 0;
            if (first == present) { result += 1; }
            if (second != null) { result += 2; }
            return result + fallbackCalls * 10;
        }
    }
}
