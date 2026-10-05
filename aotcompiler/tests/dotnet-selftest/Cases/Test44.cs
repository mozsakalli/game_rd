namespace Demo44 {
    class Collector {
        string[] tags;
        object defaultValue;
        public Collector(object value = null, params string[] values) { defaultValue = value; tags = values; }
        public int TagCount() { return tags.Length; }
        public int DefaultInt() { return defaultValue == null ? 0 : (int)defaultValue; }
    }
    class App44 {
        public static int Run() {
            Collector filled = new Collector(7, "a", "b");
            Collector empty = new Collector();
            return filled.TagCount() + filled.DefaultInt() + empty.TagCount() + empty.DefaultInt();
        }
    }
}
