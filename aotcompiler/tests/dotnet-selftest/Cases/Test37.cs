using System.Collections.Generic;
namespace Demo37 {
    class App37 {
        public static int Run() {
            List<int> list = new List<int> { 3, 4, 5 };
            Dictionary<int, int> map = new Dictionary<int, int> { [3] = 9, [4] = 16 };
            return list.Count + list[2] + map.Count + map[4];
        }
    }
}
