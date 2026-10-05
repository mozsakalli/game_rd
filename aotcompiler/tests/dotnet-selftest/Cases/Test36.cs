namespace Demo36 {
    class Sample {
        public int Field;
        public int Property { get; set; }
        public Sample() { Field = 1; }
        public Sample(int value) { Field = value; }
    }
    class App36 {
        public static int Run() {
            Sample first = new Sample(3) { Field = 4, Property = 5 };
            Sample second = new Sample { Field = 6, Property = 7 };
            return first.Field + first.Property + second.Field + second.Property;
        }
    }
}
