using System;
namespace Demo38 {
    class Item { public int Value; }
    class App38 {
        public static int Run() {
            Item item = new Item();
            Action<Item> set = value => value.Value = 21;
            Func<Item, int> increment = value => value.Value = value.Value + 1;
            set(item);
            return item.Value + increment(item);
        }
    }
}
