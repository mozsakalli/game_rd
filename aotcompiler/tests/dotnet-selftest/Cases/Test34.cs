namespace Demo34 {
    class Outer {
        public class Item {
            int value;
            public Item(int initial) { value = initial; }
            public int Read() { return value; }
        }
        public struct Stamp {
            public int value;
        }
        public Item Make(int value) { return new Item(value); }
    }
    class App34 {
        public static int Run() {
            Outer outer = new Outer();
            Outer.Item item = outer.Make(21);
            Outer.Stamp stamp = new Outer.Stamp();
            stamp.value = item.Read();
            return item.Read() + stamp.value;
        }
    }
}
