using System.Collections.Generic;
namespace Demo39 {
    class App39 {
        public static int Run() {
            List<int> values = new List<int> { 1, 2, 3, 4 };
            int sum = 0;
            foreach (var value in values) {
                if (value == 3) { continue; }
                sum += value;
            }
            return sum;
        }
    }
}
