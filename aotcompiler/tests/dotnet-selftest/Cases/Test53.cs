namespace Demo53
{
    // Kusakli GC + Dictionary<string, struct{string[]}> (PakSource.Entry deseni): eski sozluk,
    // struct deger icindeki dizi/string referanslari; out-struct ile geri okuma; minor GC arasi.
    struct Entry { public string Guid; public long Offset; public string[] Deps; }
    class App53
    {
        static System.Collections.Generic.Dictionary<string, Entry> map;
        static string[] GetDeps(string key) { return map.TryGetValue(key, out var e) ? e.Deps : System.Array.Empty<string>(); }
        public static int Run()
        {
            map = new System.Collections.Generic.Dictionary<string, Entry>();
            for (int g = 0; g < 4; g++) System.GC.Collect(0); // artimli dilimler: dongu mutator ile ic ice
            int acc = 0;
            for (int f = 0; f < 30; f++)
            {
                string key = "k" + (f % 10);
                var e = new Entry { Guid = "g" + f, Offset = f };
                int n = f % 4;
                e.Deps = n == 0 ? System.Array.Empty<string>() : new string[n];
                for (int d = 0; d < n; d++) e.Deps[d] = "dep" + f + "_" + d;
                map[key] = e;
                System.GC.Collect(0);
                var deps = GetDeps(key);
                acc += deps.Length * 100 + (deps.Length > 0 ? deps[deps.Length - 1].Length : 0);
                if (map.TryGetValue(key, out var back)) acc += back.Guid.Length + (int)back.Offset;
            }
            foreach (var kv in map) acc += kv.Key.Length + kv.Value.Deps.Length;
            return acc;
        }
    }
}