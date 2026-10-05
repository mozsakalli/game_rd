using System;
using System.Collections.Generic;
using System.Linq;
namespace Demo31 {
    class App31 {
        public static int Run() {
            int acc = 0;
            IDictionary<int, int> map = new Dictionary<int, int>();
            for (int i = 0; i < 12; i++) { map[i] = i * i; }
            map[3] = 99;
            if (map.Count == 12) { acc += 1; }
            if (map.ContainsKey(3) && !map.ContainsKey(42)) { acc += 2; }
            if (map.TryGetValue(3, out int v) && v == 99) { acc += 4; }
            if (!map.TryGetValue(42, out int missing) && missing == 0) { acc += 8; }
            if (map[11] == 121) { acc += 16; }
            if (map.Remove(3) && !map.ContainsKey(3) && map.ContainsKey(11) && map.Count == 11) { acc += 16384; }
            int keyTotal = 0;
            foreach (var key in map.Keys) { keyTotal += key; }
            if (keyTotal != 63) { return -1; }
            List<int> search = new List<int>();
            search.Add(2); search.Add(3); search.Add(5);
            int needle = 3;
            if (search.FirstOrDefault(item => item == needle) != 3 || search.FirstOrDefault(item => item == 9) != 0) { return -1; }
            List<int> edits = new List<int>();
            edits.Add(1); edits.Add(3); edits.Insert(1, 2);
            if (edits.Count != 3 || edits[1] != 2 || !edits.Remove(2) || edits.Remove(9) || edits[1] != 3) { return -1; }
            List<int> list = new List<int>();
            for (int i = 0; i < 9; i++) { list.Add(i); }
            IList<int> listView = list;
            listView.Add(9);
            list.RemoveAt(4);
            if (list.Count == 9 && list[4] == 5 && listView[8] == 9) { acc += 32; }
            if (list.Contains(8) && !list.Contains(4)) { acc += 64; }
            list.Reverse();
            if (list[0] != 9 || list[8] != 0) { return -1; }
            list.Clear();
            if (list.Count == 0) { acc += 128; }
            if (typeof(int).IsPrimitive) { acc += 256; }
            if (typeof(string).IsPrimitive == false && typeof(App31).IsPrimitive == false) { acc += 512; }
            if (Type.GetTypeCode(typeof(int)) == TypeCode.Int32) { acc += 1024; }
            if (Type.GetTypeCode(typeof(byte)) == TypeCode.Byte) { acc += 2048; }
            if (Type.GetTypeCode(typeof(string)) == TypeCode.String) { acc += 4096; }
            if (Type.GetTypeCode(typeof(App31)) == TypeCode.Empty) { acc += 8192; }
            Dictionary<int, int> pairs = new Dictionary<int, int>();
            pairs[2] = 4;
            pairs[3] = 9;
            var iterator = pairs.GetEnumerator();
            int pairTotal = 0;
            while (iterator.MoveNext()) { pairTotal += iterator.Current.Key + iterator.Current.Value; }
            if (pairTotal == 18) { acc += 32768; }
            return acc;
        }
    }
}
