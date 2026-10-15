using System;
using System.Collections.Generic;
using ParityKit;

// 93: Dictionary/HashSet semantigi — resize, siralama (free-list slot yeniden kullanimi), enumeration sirasinda degisiklik, comparer'lar.
namespace Demo93
{
    struct PKey : IEquatable<PKey>
    {
        public int A; public string B;
        public PKey(int a, string b) { A = a; B = b; }
        public bool Equals(PKey o) { return A == o.A && B == o.B; }
        public override bool Equals(object o) { return o is PKey k && Equals(k); }
        public override int GetHashCode() { return A; } // B'yi hash'e katmaz -> carpisma
        public override string ToString() { return A + ":" + B; }
    }

    struct PlainKey { public int X; public int Y; }  // Equals/GetHashCode yok: ValueType varsayilani (alan bazli)

    class RefKey { public int V; public RefKey(int v) { V = v; } } // kimlik esitligi

    class ModComparer : IEqualityComparer<int>
    {
        readonly int m; public ModComparer(int m) { this.m = m; }
        public bool Equals(int a, int b) { return a % m == b % m; }
        public int GetHashCode(int a) { return a % m; }
    }

    class ConstHash : IEqualityComparer<string>
    {
        public bool Equals(string a, string b) { return string.Equals(a, b); }
        public int GetHashCode(string s) { return 42; } // hepsi ayni kovada
    }

    enum Col { R, G, B }

    class App93
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string V(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }
        static string Keys<K, V2>(IEnumerable<KeyValuePair<K, V2>> d) { var sb = new System.Text.StringBuilder(); foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(','); return sb.ToString(); }
        static string Items<T>(IEnumerable<T> e) { var sb = new System.Text.StringBuilder(); foreach (var x in e) sb.Append(x).Append(','); return sb.ToString(); }

        public static int Run() { return Par.Loop(93, 24, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return DictBasics();
                case 2: return DictOrderAfterRemove();
                case 3: return DictResize();
                case 4: return DictModifyDuringEnum();
                case 5: return DictRemoveDuringEnum();
                case 6: return DictComparer();
                case 7: return DictCollisions();
                case 8: return DictStructKey();
                case 9: return DictRefKey();
                case 10: return DictNullKeyValue();
                case 11: return DictKeysValues();
                case 12: return DictTryMethods();
                case 13: return DictEnumKey();
                case 14: return HashSetBasics();
                case 15: return HashSetOrder();
                case 16: return HashSetOps();
                case 17: return HashSetComparer();
                case 18: return HashSetModifyDuringEnum();
                case 19: return DictCopyCtor();
                case 20: return DictEnumerator();
                case 21: return SortedDictionaryBasics();
                case 22: return DictEnsureCapacity();
                case 23: return DictValueTypes();
                case 24: return DictDoubleKeys();
            }
            return "?";
        }

        static string DictBasics()
        {
            var d = new Dictionary<string, int>();
            d.Add("a", 1); d["b"] = 2; d["a"] = 3;
            string e1 = V(() => d.Add("a", 9)), e2 = P(() => d["zz"]), e3 = V(() => d.Add(null, 1)), e4 = P(() => d[null]), e5 = V(() => d[null] = 1), e6 = P(() => d.ContainsKey(null)), e7 = P(() => d.Remove(null)), e8 = P(() => d.TryGetValue(null, out _));
            bool r1 = d.Remove("a"), r2 = d.Remove("a"), r3 = d.Remove("b", out int rv);
            d["x"] = 10; d["y"] = 20;
            return Par.J(Q(() => d.Count), Q(() => Keys(d)), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => r1), Q(() => r2), Q(() => r3), Q(() => rv), Q(() => d.ContainsKey("x")), Q(() => d.ContainsValue(20)), Q(() => d.ContainsValue(99)), d.TryGetValue("x", out int tv), Q(() => tv), d.TryGetValue("q", out int tv2), Q(() => tv2), Q(() => d.GetValueOrDefault("q")), Q(() => d.GetValueOrDefault("q", -1)), Q(() => d.GetValueOrDefault("x", -1)), Q(() => d.TryAdd("x", 99)), Q(() => d.TryAdd("z", 30)), Q(() => d["x"]), Q(() => d.Count), Q(() => d.Keys.Count), Q(() => d.Values.Count), Q(() => Items(d.Keys)), Q(() => Items(d.Values)), Q(() => new Dictionary<int, int>().Count), Q(() => new Dictionary<int, int>(0).Count), Q(() => new Dictionary<int, int>(100).Count), P(() => new Dictionary<int, int>(-1).Count), Q(() => d.Comparer.GetType().Name), Q(() => new Dictionary<int, int>().Comparer.GetType().Name), Q(() => new Dictionary<Col, int>().Comparer.GetType().Name), Q(() => new Dictionary<PKey, int>().Comparer.GetType().Name), Q(() => new Dictionary<object, int>().Comparer.GetType().Name), Q(() => ((System.Collections.IDictionary)d).Contains("x")), Q(() => ((System.Collections.IDictionary)d).Contains(5)), Q(() => ((System.Collections.IDictionary)d)["x"]), Q(() => ((System.Collections.IDictionary)d)["nope"] == null), P(() => ((System.Collections.IDictionary)d)[5]), V(() => ((System.Collections.IDictionary)d)["k"] = "notint"), V(() => ((System.Collections.IDictionary)d)[5] = 1), Q(() => ((System.Collections.ICollection)d).IsSynchronized), Q(() => ((IDictionary<string, int>)d).IsReadOnly), Q(() => ((ICollection<KeyValuePair<string, int>>)d).Contains(new KeyValuePair<string, int>("x", 10))), Q(() => ((ICollection<KeyValuePair<string, int>>)d).Contains(new KeyValuePair<string, int>("x", 11))), Q(() => ((ICollection<KeyValuePair<string, int>>)d).Remove(new KeyValuePair<string, int>("x", 11))), Q(() => ((ICollection<KeyValuePair<string, int>>)d).Remove(new KeyValuePair<string, int>("x", 10))), Q(() => d.Count));
        }

        static string DictOrderAfterRemove()
        {
            // .NET: silinen slot free-list'e girer; sonraki Add o slotu kullanir -> enumeration sirasi ekleme sirasindan sapar
            var d = new Dictionary<int, string>();
            for (int i = 1; i <= 5; i++) d[i] = "v" + i;
            string o1 = Keys(d);
            d.Remove(2); d[6] = "v6";
            string o2 = Keys(d);
            d.Remove(1); d.Remove(4); d[7] = "v7"; d[8] = "v8"; d[9] = "v9";
            string o3 = Keys(d);
            d.Remove(9); d.Remove(7); d[10] = "a"; d[11] = "b";
            string o4 = Keys(d);
            d[3] = "upd"; // mevcut anahtar: yer degismez
            string o5 = Keys(d);
            d.Remove(3); d[3] = "re"; // sil + ekle: sona degil slot'a
            string o6 = Keys(d);
            d.Clear(); d[100] = "x"; d[1] = "y";
            string o7 = Keys(d);
            var s = new Dictionary<string, int>(); s["one"] = 1; s["two"] = 2; s["three"] = 3; s.Remove("two"); s["four"] = 4; s.Remove("one"); s["five"] = 5; s["six"] = 6;
            string o8 = Keys(s);
            var h = new Dictionary<int, int>(); for (int i = 0; i < 10; i++) h[i * 7] = i; h.Remove(14); h.Remove(35); h[100] = 100; h[200] = 200; h[300] = 300;
            string o9 = Keys(h);
            return Par.J(Q(() => o1), Q(() => o2), Q(() => o3), Q(() => o4), Q(() => o5), Q(() => o6), Q(() => o7), Q(() => o8), Q(() => o9), Q(() => Items(d.Keys)), Q(() => Items(d.Values)), Q(() => Items(h.Keys)));
        }

        static string DictResize()
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i < 200; i++) d[i * 3] = i;
            string first = Items(d.Keys).Substring(0, 40), last = Items(d.Keys).Substring(Items(d.Keys).Length - 30);
            for (int i = 0; i < 200; i += 2) d.Remove(i * 3);
            for (int i = 1000; i < 1050; i++) d[i] = i;
            string mixed = Items(d.Keys);
            var neg = new Dictionary<int, int>(); for (int i = -50; i <= 50; i++) neg[i * i * (i < 0 ? -1 : 1)] = i; // negatif anahtarlar, hash = deger
            var big = new Dictionary<int, int>(); for (int i = 0; i < 1000; i++) big[i * 1000003] = i; int sum = 0; foreach (var kv in big) sum += kv.Value;
            var strs = new Dictionary<string, int>(); for (int i = 0; i < 100; i++) strs["k" + i] = i;
            return Par.J(Q(() => d.Count), Q(() => first), Q(() => last), Q(() => mixed.Length), Q(() => mixed.Substring(0, 60)), Q(() => neg.Count), Q(() => Items(neg.Keys).Substring(0, 60)), Q(() => big.Count), Q(() => sum), Q(() => strs.Count), Q(() => Items(strs.Keys).Substring(0, 50)), Q(() => d.ContainsKey(3)), Q(() => d.ContainsKey(6)), Q(() => d[1049]), Q(() => new Dictionary<int, int>(3) { { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 } }.Count), Q(() => Keys(new Dictionary<int, int>(1) { { 5, 1 }, { 1, 2 }, { 9, 3 }, { 3, 4 }, { 7, 5 } })), Q(() => Keys(new Dictionary<int, int>(16) { { 5, 1 }, { 1, 2 }, { 9, 3 }, { 3, 4 }, { 7, 5 } })), Q(() => Keys(new Dictionary<int, int>() { { 17, 1 }, { 1, 2 }, { 33, 3 } })), Q(() => Keys(new Dictionary<int, int>() { { int.MaxValue, 1 }, { int.MinValue, 2 }, { -1, 3 }, { 0, 4 } })));
        }

        static string DictModifyDuringEnum()
        {
            var d = new Dictionary<int, int> { { 1, 1 }, { 2, 2 }, { 3, 3 } };
            string e1 = V(() => { foreach (var kv in d) d[99] = 1; });
            d.Remove(99);
            string e2 = V(() => { foreach (var kv in d) d[kv.Key] = kv.Value * 10; }); // mevcut anahtari guncelleme: .NET Core 3+ izinli
            string vals = Keys(d);
            string e3 = V(() => { foreach (var k in d.Keys) d[k] = 0; });
            string e4 = V(() => { foreach (var k in d.Keys) d.Add(k + 100, 0); });
            string e5 = V(() => { foreach (var k in d.Values) d.Clear(); });
            d[1] = 1; d[2] = 2;
            string e6 = V(() => { var en = d.GetEnumerator(); en.MoveNext(); d[7] = 7; var c = en.Current; });
            string e7 = V(() => { var en = d.GetEnumerator(); en.MoveNext(); d[7] = 8; en.MoveNext(); });
            string e8 = V(() => { var en = d.GetEnumerator(); d.Remove(7); en.MoveNext(); });
            string e9 = V(() => { var en = d.GetEnumerator(); d[1] = 11; en.MoveNext(); });
            string e10 = V(() => { var en = d.GetEnumerator(); while (en.MoveNext()) { } d[50] = 1; en.MoveNext(); });
            string e11 = V(() => { var en = d.GetEnumerator(); while (en.MoveNext()) { } en.Dispose(); en.MoveNext(); });
            string e12 = V(() => { var en = d.GetEnumerator(); d.TryAdd(1, 5); en.MoveNext(); }); // basarisiz TryAdd versiyon degistirir mi?
            string e13 = V(() => { var en = d.GetEnumerator(); d.Remove(12345); en.MoveNext(); }); // basarisiz Remove
            string e14 = V(() => { var en = d.GetEnumerator(); d.ContainsKey(1); d.TryGetValue(1, out _); en.MoveNext(); });
            string e15 = V(() => { var en = d.GetEnumerator(); d.EnsureCapacity(1000); en.MoveNext(); });
            string e16 = V(() => { var en = d.GetEnumerator(); d.TrimExcess(); en.MoveNext(); });
            return Par.J(Q(() => e1), Q(() => e2), Q(() => vals), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => e10), Q(() => e11), Q(() => e12), Q(() => e13), Q(() => e14), Q(() => e15), Q(() => e16), Q(() => d.Count));
        }

        static string DictRemoveDuringEnum()
        {
            // .NET Core 3.0+: enumeration sirasinda Remove/Clear enumerator'i GECERSIZ KILMAZ
            var d = new Dictionary<int, int>(); for (int i = 0; i < 10; i++) d[i] = i;
            string e1 = V(() => { foreach (var kv in d) if (kv.Key % 2 == 0) d.Remove(kv.Key); });
            string k1 = Keys(d);
            string e2 = V(() => { foreach (var kv in d) d.Remove(kv.Key + 1); }); // sonraki elemani sil
            string k2 = Keys(d);
            for (int i = 0; i < 6; i++) d[i] = i;
            var visited = new List<int>();
            string e3 = V(() => { foreach (var kv in d) { visited.Add(kv.Key); if (kv.Key == 1) d.Remove(5); } });
            string e4 = V(() => { foreach (var kv in d) { d.Clear(); } });
            int cnt = d.Count;
            d[1] = 1; d[2] = 2;
            string e5 = V(() => { foreach (var kv in d) { d.Remove(kv.Key); d[kv.Key] = 9; } }); // sil + geri ekle = Add -> versiyon degisir
            string e6 = V(() => { foreach (var k in d.Keys) d.Remove(k); });
            d[1] = 1; d[2] = 2; d[3] = 3;
            string e7 = V(() => { foreach (var v in d.Values) d.Remove(1); });
            string e8 = V(() => { foreach (var kv in d) d.Remove(kv.Key, out _); });
            d[1] = 1;
            var hs = new HashSet<int> { 1, 2, 3, 4 };
            string e9 = V(() => { foreach (var x in hs) if (x % 2 == 0) hs.Remove(x); });
            string e10 = V(() => { foreach (var x in hs) hs.Add(x + 10); });
            return Par.J(Q(() => e1), Q(() => k1), Q(() => e2), Q(() => k2), Q(() => e3), Q(() => Items(visited)), Q(() => e4), Q(() => cnt), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => d.Count), Q(() => e9), Q(() => Items(hs)), Q(() => e10), Q(() => hs.Count));
        }

        static string DictComparer()
        {
            var ci = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ci["Key"] = 1; ci["KEY"] = 2; ci["key"] += 10;
            string e1 = V(() => ci.Add("kEy", 0));
            var mod = new Dictionary<int, string>(new ModComparer(3));
            mod[1] = "a"; mod[4] = "b"; mod[7] += "c"; mod[2] = "x";
            string e2 = V(() => mod.Add(10, "z"));
            var inv = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase); inv["ß"] = 1; inv["SS"] = 2; inv["İ"] = 3; inv["i"] = 4; inv["ı"] = 5; inv["I"] = 6;
            var ord = new Dictionary<string, int>(StringComparer.Ordinal); ord["a"] = 1; ord["A"] = 2;
            var oic = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); oic["ß"] = 1; oic["SS"] = 2; oic["İ"] = 3; oic["i"] = 4; oic["ı"] = 5; oic["I"] = 6; oic["K"] = 7; oic["k"] = 8; oic["ǅ"] = 9; oic["Ǆ"] = 10; oic["ǆ"] = 11;
            var cur = new Dictionary<string, int>(StringComparer.CurrentCulture); cur["a"] = 1; cur["A"] = 2; cur["\u00C5"] = 3; cur["A\u030A"] = 4;
            var nul = new Dictionary<string, int>((IEqualityComparer<string>)null); nul["a"] = 1;
            return Par.J(Q(() => ci.Count), Q(() => ci["key"]), Q(() => Items(ci.Keys)), Q(() => e1), Q(() => ci.ContainsKey("KEY")), Q(() => ci.Remove("kEY")), Q(() => ci.Count), Q(() => mod.Count), Q(() => Keys(mod)), Q(() => e2), Q(() => mod.ContainsKey(100)), Q(() => mod[100]), Q(() => inv.Count), Q(() => Keys(inv)), Q(() => ord.Count), Q(() => oic.Count), Q(() => Keys(oic)), Q(() => cur.Count), Q(() => Keys(cur)), Q(() => nul.Comparer.GetType().Name), Q(() => ci.Comparer.GetType().Name), Q(() => ci.Comparer == StringComparer.OrdinalIgnoreCase), Q(() => mod.Comparer.GetType().Name), Q(() => new Dictionary<string, int>(StringComparer.Ordinal).Comparer.GetType().Name), Q(() => new Dictionary<string, int>(StringComparer.Ordinal).Comparer == StringComparer.Ordinal), Q(() => new Dictionary<string, int>(EqualityComparer<string>.Default).Comparer.GetType().Name), Q(() => new Dictionary<int, int>(EqualityComparer<int>.Default).Comparer.GetType().Name), Q(() => new Dictionary<int, int>(new ModComparer(5)).Comparer.GetType().Name));
        }

        static string DictCollisions()
        {
            var d = new Dictionary<string, int>(new ConstHash());
            for (int i = 0; i < 50; i++) d["k" + i] = i;
            string e1 = V(() => d.Add("k5", 0));
            bool r = d.Remove("k25");
            d["k100"] = 100;
            var pk = new Dictionary<PKey, int>();
            for (int i = 0; i < 20; i++) pk[new PKey(i % 4, "s" + i)] = i; // hash = i%4: 4 kovada 20 anahtar
            pk.Remove(new PKey(1, "s5")); pk[new PKey(1, "s5")] = 500; pk[new PKey(1, "s9")] = 900;
            var collide = new Dictionary<long, int>(); // long hash: (int)(v ^ (v>>32)) -> 0 ve 1<<32|1 gibi carpisanlar
            collide[0L] = 0; collide[(1L << 32) | 1L] = 1; collide[1L] = 2; collide[1L << 32] = 3; collide[-1L] = 4; collide[(-1L) ^ (0xFFFFFFFFL << 32)] = 5;
            var dbl = new Dictionary<double, int>(); dbl[0.0] = 1; dbl[-0.0] = 2; dbl[double.NaN] = 3; dbl[0.0 / 0.0] = 4; dbl[1.0] = 5; dbl[1.0000000000000002] = 6;
            return Par.J(Q(() => d.Count), Q(() => d["k49"]), Q(() => e1), Q(() => r), Q(() => d.ContainsKey("k25")), Q(() => d["k100"]), Q(() => Items(d.Keys).Substring(0, 30)), Q(() => Items(d.Keys).Length), Q(() => pk.Count), Q(() => Keys(pk)), Q(() => pk[new PKey(1, "s5")]), Q(() => pk.ContainsKey(new PKey(1, "zzz"))), Q(() => collide.Count), Q(() => Keys(collide)), Q(() => dbl.Count), Q(() => Keys(dbl)), Q(() => dbl[-0.0]), Q(() => dbl[double.NaN]), Q(() => dbl.ContainsKey(0.0)), Q(() => dbl.ContainsKey(float.NaN)), Q(() => dbl.Remove(double.NaN)), Q(() => dbl.Count));
        }

        static string DictStructKey()
        {
            var d = new Dictionary<PlainKey, string>();
            d[new PlainKey { X = 1, Y = 2 }] = "a"; d[new PlainKey { X = 1, Y = 2 }] = "b"; d[new PlainKey { X = 2, Y = 1 }] = "c"; d[default] = "z";
            var k = new PlainKey { X = 1, Y = 2 }; k.Y = 3; d[k] = "d";
            var t = new Dictionary<(int, string), int>(); t[(1, "a")] = 1; t[(1, "a")] = 2; t[(1, "b")] = 3; t[(2, "a")] = 4; t[(1, null)] = 5;
            var vt = new Dictionary<ValueTuple<int, int>, int>(); vt[(1, 2)] = 1; vt[(2, 1)] = 2; vt[(1, 2)] += 10;
            var kv = new Dictionary<KeyValuePair<int, int>, int>(); kv[new KeyValuePair<int, int>(1, 2)] = 1; kv[new KeyValuePair<int, int>(1, 2)] = 2;
            var nk = new Dictionary<int?, int>(); nk[1] = 1; nk[null] = 2; nk[1] = 3; string e1 = V(() => nk.Add(null, 0));
            var dk = new Dictionary<DateTime, int>(); dk[new DateTime(2024, 1, 1)] = 1; dk[new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)] = 2; dk[new DateTime(2024, 1, 2)] = 3;
            var gk = new Dictionary<Guid, int>(); var g = Guid.NewGuid(); gk[g] = 1; gk[new Guid(g.ToByteArray())] = 2; gk[Guid.Empty] = 3;
            var ck = new Dictionary<char, int>(); ck['a'] = 1; ck['a'] = 2; ck['A'] = 3;
            var bk = new Dictionary<bool, int>(); bk[true] = 1; bk[false] = 2; bk[true] = 3;
            var byk = new Dictionary<byte, int>(); for (int i = 0; i < 256; i++) byk[(byte)i] = i; byk[(byte)I(300)] += 1000;
            return Par.J(Q(() => d.Count), Q(() => Keys2(d)), Q(() => new PlainKey { X = 1, Y = 2 }.Equals(new PlainKey { X = 1, Y = 2 })), Q(() => new PlainKey { X = 1, Y = 2 }.GetHashCode() == new PlainKey { X = 1, Y = 2 }.GetHashCode()), Q(() => new PlainKey { X = 1, Y = 2 }.Equals(new PlainKey { X = 2, Y = 1 })), Q(() => new PlainKey { X = 1, Y = 2 }.GetHashCode() == new PlainKey { X = 2, Y = 1 }.GetHashCode()), Q(() => t.Count), Q(() => t[(1, "a")]), Q(() => t[(1, null)]), Q(() => vt.Count), Q(() => vt[(1, 2)]), Q(() => kv.Count), Q(() => kv[new KeyValuePair<int, int>(1, 2)]), Q(() => nk.Count), Q(() => nk[null]), Q(() => nk[1]), Q(() => e1), Q(() => nk.ContainsKey(null)), Q(() => nk.Remove(null)), Q(() => nk.Count), Q(() => dk.Count), Q(() => dk[new DateTime(2024, 1, 1)]), Q(() => gk.Count), Q(() => gk[g]), Q(() => ck.Count), Q(() => ck['a']), Q(() => bk.Count), Q(() => bk[true]), Q(() => byk.Count), Q(() => byk[44]), Q(() => Items(bk.Keys)), Q(() => Items(ck.Keys)));
        }

        static string Keys2(Dictionary<PlainKey, string> d) { var sb = new System.Text.StringBuilder(); foreach (var kv in d) sb.Append(kv.Key.X).Append('/').Append(kv.Key.Y).Append('=').Append(kv.Value).Append(','); return sb.ToString(); }

        static string DictRefKey()
        {
            var a = new RefKey(1); var b = new RefKey(1);
            var d = new Dictionary<RefKey, int>(); d[a] = 1; d[b] = 2; d[a] = 3;
            var od = new Dictionary<object, int>(); od[1] = 1; od[1L] = 2; od["1"] = 3; od[1.0] = 4; od[(byte)1] = 5; od[I(1)] = 6; od['1'] = 7; od[true] = 8; od[Col.G] = 9; od[(object)1] += 100;
            var boxed = new Dictionary<object, int>(); object k1 = I(5); object k2 = I(5); boxed[k1] = 1; boxed[k2] = 2;
            var str = new Dictionary<object, int>(); string s1 = new string(new[] { 'x' }); string s2 = new string(new[] { 'x' }); str[s1] = 1; str[s2] = 2;
            var arrk = new Dictionary<int[], int>(); var arr1 = new[] { 1 }; var arr2 = new[] { 1 }; arrk[arr1] = 1; arrk[arr2] = 2;
            var dlg = new Dictionary<Action, int>(); Action act1 = DictRefKeyHelper; Action act2 = DictRefKeyHelper; dlg[act1] = 1; dlg[act2] = 2; Action lam1 = () => { }; Action lam2 = () => { }; dlg[lam1] = 3; dlg[lam2] = 4;
            var typ = new Dictionary<Type, int>(); typ[typeof(int)] = 1; typ[I(1).GetType()] = 2; typ[typeof(List<int>)] = 3; typ[new List<int>().GetType()] = 4; typ[typeof(List<>)] = 5;
            return Par.J(Q(() => d.Count), Q(() => d[a]), Q(() => d[b]), Q(() => d.ContainsKey(new RefKey(1))), Q(() => od.Count), Q(() => Items(od.Values)), Q(() => od[1]), Q(() => od[1L]), Q(() => od["1"]), Q(() => od[(byte)1]), Q(() => boxed.Count), Q(() => str.Count), Q(() => str[s1]), Q(() => ReferenceEquals(s1, s2)), Q(() => arrk.Count), Q(() => dlg.Count), Q(() => dlg[act1]), Q(() => typ.Count), Q(() => typ[typeof(int)]), Q(() => typ[typeof(List<int>)]), Q(() => typ[typeof(List<>)]), Q(() => new Dictionary<RefKey, int>().Comparer.Equals(a, b)), Q(() => EqualityComparer<RefKey>.Default.Equals(a, a)), Q(() => EqualityComparer<object>.Default.Equals(1, 1)), Q(() => EqualityComparer<object>.Default.Equals(1, 1L)), Q(() => EqualityComparer<object>.Default.Equals("x", s1)), Q(() => EqualityComparer<object>.Default.GetHashCode(1) == I(1).GetHashCode()));
        }

        static void DictRefKeyHelper() { }

        static string DictNullKeyValue()
        {
            var d = new Dictionary<string, string>(); d["a"] = null; d["b"] = "x";
            var od = new Dictionary<int, object>(); od[1] = null;
            var nd = new Dictionary<int, int?>(); nd[1] = null; nd[2] = 5;
            var kd = new Dictionary<string, int>();
            return Par.J(Q(() => d.Count), Q(() => d["a"] == null), Q(() => d.ContainsValue(null)), d.TryGetValue("a", out string v1), Q(() => v1 == null), d.TryGetValue("zz", out string v2), Q(() => v2 == null), Q(() => od.ContainsValue(null)), Q(() => od[1] == null), Q(() => nd.ContainsValue(null)), Q(() => nd[1].HasValue), Q(() => nd[2].Value), nd.TryGetValue(1, out int? n1), Q(() => n1.HasValue), nd.TryGetValue(9, out int? n2), Q(() => n2.HasValue), V(() => kd[null] = 1), V(() => kd.Add(null, 1)), P(() => kd[null]), P(() => kd.ContainsKey(null)), P(() => kd.Remove(null)), P(() => kd.TryGetValue(null, out _)), P(() => kd.TryAdd(null, 1)), P(() => kd.GetValueOrDefault(null)), V(() => new Dictionary<object, int>()[null] = 1), P(() => ((System.Collections.IDictionary)kd)[null]), V(() => ((System.Collections.IDictionary)kd)[null] = 1), P(() => ((System.Collections.IDictionary)kd).Contains(null)), V(() => ((System.Collections.IDictionary)kd).Remove(null)), V(() => ((System.Collections.IDictionary)kd).Add("k", null)), V(() => ((System.Collections.IDictionary)od).Add(2, null)), Q(() => od.Count), Q(() => ((System.Collections.IDictionary)d)["a"] == null), Q(() => ((System.Collections.IDictionary)d)["nope"] == null), Q(() => string.Join(",", d.Keys)), Q(() => d.Remove("a")), Q(() => d.ContainsValue(null)));
        }

        static string DictKeysValues()
        {
            var d = new Dictionary<int, string> { { 3, "c" }, { 1, "a" }, { 2, "b" } };
            var keys = d.Keys; var vals = d.Values;
            int kc0 = keys.Count; d[4] = "d"; int kc1 = keys.Count; // canli gorunum
            var karr = new int[10]; keys.CopyTo(karr, 2); var varr = new string[5]; vals.CopyTo(varr, 0);
            string e1 = V(() => keys.CopyTo(new int[2], 0)), e2 = V(() => keys.CopyTo(karr, 8)), e3 = V(() => keys.CopyTo(karr, -1)), e4 = V(() => keys.CopyTo(null, 0));
            string e5 = V(() => ((ICollection<int>)keys).Add(5)), e6 = V(() => ((ICollection<int>)keys).Remove(1)), e7 = V(() => ((ICollection<int>)keys).Clear()), e8 = V(() => ((ICollection<string>)vals).Add("x"));
            bool ck = ((ICollection<int>)keys).Contains(3), ro = ((ICollection<int>)keys).IsReadOnly, cv = ((ICollection<string>)vals).Contains("b"), cvn = ((ICollection<string>)vals).Contains(null);
            var ke = keys.GetEnumerator(); ke.MoveNext(); d[5] = "e"; string e9 = V(() => ke.MoveNext()); string e10 = P(() => ke.Current);
            var kl = new List<int>(keys); var vl = new List<string>(vals);
            var karr2 = new int[d.Count]; d.Keys.CopyTo(karr2, 0);
            var kvarr = new KeyValuePair<int, string>[10]; ((ICollection<KeyValuePair<int, string>>)d).CopyTo(kvarr, 1);
            var oarr = new object[10]; string e11 = V(() => ((System.Collections.ICollection)d).CopyTo(oarr, 0)); var dearr = new System.Collections.DictionaryEntry[10]; string e12 = V(() => ((System.Collections.ICollection)d).CopyTo(dearr, 0)); var sarr = new string[10]; string e13 = V(() => ((System.Collections.ICollection)d).CopyTo(sarr, 0)); var marr = new int[2, 5]; string e14 = V(() => ((System.Collections.ICollection)d).CopyTo(marr, 0));
            return Par.J(Q(() => kc0), Q(() => kc1), Q(() => Items(karr)), Q(() => Items(varr)), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => ck), Q(() => ro), Q(() => cv), Q(() => cvn), Q(() => e9), Q(() => e10), Q(() => Items(kl)), Q(() => Items(vl)), Q(() => Items(karr2)), Q(() => kvarr[1].Key), Q(() => kvarr[0].Key), Q(() => kvarr[5].Key), Q(() => kvarr[6].Key), Q(() => e11), Q(() => oarr[0].GetType().Name), Q(() => e12), Q(() => dearr[0].Key), Q(() => e13), Q(() => e14), Q(() => keys.GetType().Name), Q(() => vals.GetType().Name), Q(() => ((System.Collections.IDictionary)d).Keys.GetType().Name), Q(() => ((System.Collections.IDictionary)d).Keys.Count), Q(() => ((IReadOnlyDictionary<int, string>)d).Keys.GetType().Name), Q(() => ReferenceEquals(d.Keys, d.Keys)), Q(() => ReferenceEquals(d.Values, d.Values)));
        }

        static string DictTryMethods()
        {
            var d = new Dictionary<string, int> { { "a", 1 } };
            bool t1 = d.TryAdd("a", 2), t2 = d.TryAdd("b", 2), t3 = d.TryGetValue("a", out int g1), t4 = d.TryGetValue("z", out int g2), t5 = d.Remove("a", out int r1), t6 = d.Remove("a", out int r2), t7 = d.Remove("zz");
            ref int refv = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(d, "c", out bool exists); refv += 5;
            ref int refv2 = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(d, "c", out bool exists2); refv2 += 5;
            ref int refn = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(d, "nope"); bool isNull = System.Runtime.CompilerServices.Unsafe.IsNullRef(ref refn);
            ref int refb = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(d, "b"); refb = 42;
            int cap1 = d.EnsureCapacity(100); int cap2 = d.EnsureCapacity(10); d.TrimExcess(); int cap3 = d.EnsureCapacity(0); d.TrimExcess(50); int cap4 = d.EnsureCapacity(0); string e1 = P(() => d.EnsureCapacity(-1)); string e2 = V(() => d.TrimExcess(1));
            return Par.J(Q(() => t1), Q(() => t2), Q(() => t3), Q(() => g1), Q(() => t4), Q(() => g2), Q(() => t5), Q(() => r1), Q(() => t6), Q(() => r2), Q(() => t7), Q(() => d.Count), Q(() => exists), Q(() => exists2), Q(() => d["c"]), Q(() => isNull), Q(() => d["b"]), Q(() => cap1 >= 100), Q(() => cap2 >= 100), Q(() => cap3 >= 2), Q(() => cap4 >= 50), Q(() => e1), Q(() => e2), Q(() => Keys(d)), Q(() => new Dictionary<int, int>().EnsureCapacity(0)), Q(() => new Dictionary<int, int>().EnsureCapacity(1)), Q(() => new Dictionary<int, int>().EnsureCapacity(3)), Q(() => new Dictionary<int, int>().EnsureCapacity(4)), Q(() => new Dictionary<int, int>().EnsureCapacity(7)), Q(() => new Dictionary<int, int>().EnsureCapacity(8)), Q(() => new Dictionary<int, int>().EnsureCapacity(100)), Q(() => new Dictionary<int, int>(5).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(17).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(0).EnsureCapacity(0)));
        }

        static string DictEnumKey()
        {
            var d = new Dictionary<Col, int>(); d[Col.R] = 1; d[Col.B] = 3; d[Col.G] = 2; d[(Col)I(7)] = 7; d[Col.R] += 10;
            var lb = new Dictionary<LongE, int>(); lb[LongE.A] = 1; lb[LongE.B] = 2; lb[(LongE)I(-1)] = 3;
            var be = new Dictionary<ByteE, int>(); be[ByteE.X] = 1; be[ByteE.Y] = 2; be[(ByteE)I(255)] = 3;
            var hs = new HashSet<Col> { Col.R, Col.G, Col.R, (Col)I(5) };
            return Par.J(Q(() => d.Count), Q(() => Keys(d)), Q(() => d[Col.R]), Q(() => d.ContainsKey((Col)I(1))), Q(() => d.ContainsKey((Col)I(9))), Q(() => d.Comparer.GetType().Name), Q(() => lb.Count), Q(() => Keys(lb)), Q(() => be.Count), Q(() => Keys(be)), Q(() => hs.Count), Q(() => Items(hs)), Q(() => EqualityComparer<Col>.Default.Equals(Col.R, (Col)I(0))), Q(() => EqualityComparer<Col>.Default.GetHashCode(Col.B)), Q(() => EqualityComparer<Col>.Default.GetType().Name), Q(() => EqualityComparer<LongE>.Default.GetHashCode(LongE.B)), Q(() => EqualityComparer<ByteE>.Default.GetHashCode((ByteE)I(200))), Q(() => Col.B.GetHashCode()), Q(() => LongE.B.GetHashCode()), Q(() => ((ByteE)I(200)).GetHashCode()), Q(() => Comparer<Col>.Default.Compare(Col.R, Col.B)), Q(() => Comparer<Col>.Default.Compare((Col)I(9), Col.B)), Q(() => Comparer<LongE>.Default.Compare((LongE)I(-1), LongE.A)), Q(() => Comparer<ByteE>.Default.Compare((ByteE)I(255), ByteE.X)), Q(() => Comparer<Col>.Default.GetType().Name));
        }

        enum LongE : long { A = 1L << 40, B = -5 }
        enum ByteE : byte { X = 1, Y = 200 }

        static string HashSetBasics()
        {
            var h = new HashSet<string>();
            bool a1 = h.Add("a"), a2 = h.Add("a"), a3 = h.Add("b"), a4 = h.Add(null), a5 = h.Add(null);
            bool r1 = h.Remove("a"), r2 = h.Remove("a"), r3 = h.Remove(null), c1 = h.Contains("b"), c2 = h.Contains(null), c3 = h.Contains("zz");
            h.Add("c"); h.Add("d");
            bool tg = h.TryGetValue("c", out string actual), tg2 = h.TryGetValue("q", out string actual2);
            var ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" }; bool ciAdd = ci.Add("a"); bool ciTry = ci.TryGetValue("a", out string ciActual);
            int rw = h.RemoveWhere(s => s == "b" || s == "zz");
            var arr = new string[5]; h.CopyTo(arr, 1); var arr2 = new string[1]; string e1 = V(() => h.CopyTo(arr2, 0)); var arr3 = new string[5]; h.CopyTo(arr3, 0, 1);
            return Par.J(Q(() => a1), Q(() => a2), Q(() => a3), Q(() => a4), Q(() => a5), Q(() => h.Count), Q(() => r1), Q(() => r2), Q(() => r3), Q(() => c1), Q(() => c2), Q(() => c3), Q(() => tg), Q(() => actual), Q(() => tg2), Q(() => actual2 == null), Q(() => ciAdd), Q(() => ciTry), Q(() => ciActual), Q(() => ci.Count), Q(() => rw), Q(() => h.Count), Q(() => Items(h)), Q(() => Items(arr)), Q(() => e1), Q(() => Items(arr3)), Q(() => new HashSet<int>().Count), Q(() => new HashSet<int>(100).Count), P(() => new HashSet<int>(-1).Count), Q(() => new HashSet<int>(new[] { 1, 2, 2, 3 }).Count), Q(() => new HashSet<int>((IEnumerable<int>)null == null ? new int[0] : new int[0]).Count), P(() => new HashSet<int>((IEnumerable<int>)null).Count), Q(() => h.Comparer.GetType().Name), Q(() => ci.Comparer.GetType().Name), Q(() => ((ICollection<string>)h).IsReadOnly), Q(() => ((System.Collections.Generic.ISet<string>)h).Add("c")), Q(() => h.EnsureCapacity(100) >= 100), V(() => h.TrimExcess()), Q(() => h.Count));
        }

        static string HashSetOrder()
        {
            var h = new HashSet<int>(); for (int i = 1; i <= 6; i++) h.Add(i * 10);
            string o1 = Items(h);
            h.Remove(20); h.Add(70); string o2 = Items(h);
            h.Remove(10); h.Remove(40); h.Add(80); h.Add(90); h.Add(100); string o3 = Items(h);
            h.Remove(30); h.Add(30); string o4 = Items(h); // sil + geri ekle: slot
            var s = new HashSet<string> { "one", "two", "three", "four" }; s.Remove("two"); s.Add("five"); s.Remove("one"); s.Add("six"); s.Add("seven");
            string o5 = Items(s);
            var big = new HashSet<int>(); for (int i = 0; i < 100; i++) big.Add(i * 13); for (int i = 0; i < 100; i += 3) big.Remove(i * 13); for (int i = 2000; i < 2020; i++) big.Add(i);
            string o6 = Items(big);
            var fromList = new HashSet<int>(new[] { 5, 3, 9, 1, 3, 5, 7 }); string o7 = Items(fromList);
            var neg = new HashSet<int> { -1, -2, int.MinValue, int.MaxValue, 0, 1 }; string o8 = Items(neg);
            var strs = new HashSet<string>(); for (int i = 0; i < 30; i++) strs.Add("s" + (i * 7 % 30)); string o9 = Items(strs);
            var dbl = new HashSet<double> { 1.5, -0.0, 0.0, double.NaN, 0.0 / 0.0, 2.5 }; string o10 = Items(dbl);
            return Par.J(Q(() => o1), Q(() => o2), Q(() => o3), Q(() => o4), Q(() => o5), Q(() => o6), Q(() => o7), Q(() => o8), Q(() => o9), Q(() => o10), Q(() => dbl.Count), Q(() => dbl.Contains(-0.0)), Q(() => dbl.Contains(double.NaN)));
        }

        static string HashSetOps()
        {
            var a = new HashSet<int> { 1, 2, 3, 4 }; var b = new HashSet<int> { 3, 4, 5 }; var e = new HashSet<int>();
            var u = new HashSet<int>(a); u.UnionWith(b); var i = new HashSet<int>(a); i.IntersectWith(b); var x = new HashSet<int>(a); x.ExceptWith(b); var s = new HashSet<int>(a); s.SymmetricExceptWith(b);
            var u2 = new HashSet<int>(a); u2.UnionWith(new[] { 9, 1, 8, 1 }); var i2 = new HashSet<int>(a); i2.IntersectWith(new[] { 4, 4, 2, 99 }); var x2 = new HashSet<int>(a); x2.ExceptWith(new[] { 1, 1, 100 }); var s2 = new HashSet<int>(a); s2.SymmetricExceptWith(new[] { 1, 1, 7, 7 });
            var self = new HashSet<int>(a); self.IntersectWith(self); var self2 = new HashSet<int>(a); self2.ExceptWith(self2); var self3 = new HashSet<int>(a); self3.SymmetricExceptWith(self3); var self4 = new HashSet<int>(a); self4.UnionWith(self4);
            var ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B" }; var cs = new HashSet<string> { "a", "c" }; var ciu = new HashSet<string>(ci, StringComparer.OrdinalIgnoreCase); ciu.UnionWith(cs); var ciI = new HashSet<string>(ci, StringComparer.OrdinalIgnoreCase); ciI.IntersectWith(cs); bool ciSub = ci.IsSubsetOf(new[] { "a", "b", "c" }), csSup = cs.IsSupersetOf(new[] { "A" }), ciOv = ci.Overlaps(cs), ciSe = ci.SetEquals(new[] { "a", "b" }), csSe = cs.SetEquals(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "C" });
            return Par.J(Q(() => Items(u)), Q(() => Items(i)), Q(() => Items(x)), Q(() => Items(s)), Q(() => Items(u2)), Q(() => Items(i2)), Q(() => Items(x2)), Q(() => Items(s2)), Q(() => Items(self)), Q(() => self2.Count), Q(() => self3.Count), Q(() => Items(self4)), Q(() => a.IsSubsetOf(u)), Q(() => a.IsProperSubsetOf(u)), Q(() => a.IsSubsetOf(a)), Q(() => a.IsProperSubsetOf(a)), Q(() => u.IsSupersetOf(a)), Q(() => u.IsProperSupersetOf(a)), Q(() => a.IsSupersetOf(a)), Q(() => a.IsProperSupersetOf(a)), Q(() => e.IsSubsetOf(a)), Q(() => e.IsProperSubsetOf(a)), Q(() => e.IsSubsetOf(e)), Q(() => e.IsProperSubsetOf(e)), Q(() => a.IsSupersetOf(e)), Q(() => e.IsSupersetOf(e)), Q(() => a.Overlaps(b)), Q(() => a.Overlaps(e)), Q(() => e.Overlaps(e)), Q(() => a.SetEquals(new[] { 4, 3, 2, 1, 1 })), Q(() => a.SetEquals(b)), Q(() => e.SetEquals(new int[0])), Q(() => a.IsSubsetOf(new[] { 1, 2, 3, 4, 4 })), Q(() => a.IsProperSubsetOf(new[] { 1, 2, 3, 4, 4 })), Q(() => a.IsProperSubsetOf(new[] { 1, 2, 3, 4, 5, 5 })), Q(() => a.IsSupersetOf(new[] { 1, 1 })), Q(() => a.IsProperSupersetOf(new[] { 1, 1, 2, 3, 4 })), Q(() => a.Overlaps(new[] { 9, 1 })), P(() => a.IsSubsetOf(null)), P(() => a.SetEquals(null)), V(() => a.UnionWith(null)), Q(() => Items(ciu)), Q(() => Items(ciI)), Q(() => ciSub), Q(() => csSup), Q(() => ciOv), Q(() => ciSe), Q(() => csSe), Q(() => HashSet<int>.CreateSetComparer().Equals(a, new HashSet<int> { 4, 3, 2, 1 })), Q(() => HashSet<int>.CreateSetComparer().Equals(a, b)), Q(() => HashSet<int>.CreateSetComparer().Equals(null, null)), Q(() => HashSet<int>.CreateSetComparer().Equals(a, null)), Q(() => HashSet<int>.CreateSetComparer().GetHashCode(a) == HashSet<int>.CreateSetComparer().GetHashCode(new HashSet<int> { 4, 3, 2, 1 })), Q(() => HashSet<int>.CreateSetComparer().GetHashCode(null)), Q(() => HashSet<string>.CreateSetComparer().Equals(ci, new HashSet<string> { "a", "b" })), Q(() => HashSet<string>.CreateSetComparer().Equals(new HashSet<string> { "a", "b" }, ci)));
        }

        static string HashSetComparer()
        {
            var mod = new HashSet<int>(new ModComparer(3)); bool m1 = mod.Add(1), m2 = mod.Add(4), m3 = mod.Add(2), m4 = mod.Add(7); bool mc = mod.Contains(10); mod.TryGetValue(10, out int mv);
            var ch = new HashSet<string>(new ConstHash()); for (int i = 0; i < 30; i++) ch.Add("x" + i); ch.Remove("x15"); ch.Add("x15"); ch.Add("y");
            var pk = new HashSet<PKey>(); for (int i = 0; i < 12; i++) pk.Add(new PKey(i % 3, "v" + i)); bool pkc = pk.Contains(new PKey(1, "v4")), pkc2 = pk.Contains(new PKey(1, "v5"));
            var oic = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ß", "SS", "İ", "i", "ı", "I", "K", "k", "ǅ", "Ǆ", "ǆ", "ſ", "s" };
            var inv = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) { "ß", "SS", "İ", "i", "ı", "I", "\u00C5", "A\u030A" };
            var cur = new HashSet<string>(StringComparer.CurrentCulture) { "\u00C5", "A\u030A", "a", "A" };
            return Par.J(Q(() => m1), Q(() => m2), Q(() => m3), Q(() => m4), Q(() => mod.Count), Q(() => Items(mod)), Q(() => mc), Q(() => mv), Q(() => ch.Count), Q(() => Items(ch).Substring(0, 20)), Q(() => ch.Contains("x15")), Q(() => pk.Count), Q(() => Items(pk)), Q(() => pkc), Q(() => pkc2), Q(() => oic.Count), Q(() => Items(oic)), Q(() => inv.Count), Q(() => Items(inv)), Q(() => cur.Count), Q(() => Items(cur)), Q(() => mod.Comparer.GetType().Name), Q(() => oic.Comparer == StringComparer.OrdinalIgnoreCase), Q(() => new HashSet<string>((IEqualityComparer<string>)null).Comparer.GetType().Name), Q(() => new HashSet<int>(EqualityComparer<int>.Default).Comparer.GetType().Name), Q(() => new HashSet<string>(EqualityComparer<string>.Default).Comparer.GetType().Name), Q(() => new HashSet<object>().Comparer.GetType().Name), Q(() => new HashSet<int?>().Comparer.GetType().Name), Q(() => new HashSet<Col>().Comparer.GetType().Name), Q(() => new HashSet<PKey>().Comparer.GetType().Name), Q(() => new HashSet<PlainKey>().Comparer.GetType().Name), Q(() => new HashSet<byte>().Comparer.GetType().Name), Q(() => new HashSet<double>().Comparer.GetType().Name), Q(() => new HashSet<(int, int)>().Comparer.GetType().Name), Q(() => new HashSet<int[]>().Comparer.GetType().Name));
        }

        static string HashSetModifyDuringEnum()
        {
            var h = new HashSet<int> { 1, 2, 3 };
            string e1 = V(() => { foreach (var x in h) h.Add(99); });
            h.Remove(99);
            string e2 = V(() => { foreach (var x in h) h.Add(x); }); // basarisiz Add
            string e3 = V(() => { foreach (var x in h) h.Remove(x); }); // .NET Core 3+: izinli
            int c = h.Count; h.Add(1); h.Add(2);
            string e4 = V(() => { foreach (var x in h) h.Clear(); });
            h.Add(1); h.Add(2);
            string e5 = V(() => { foreach (var x in h) h.Remove(42); });
            string e6 = V(() => { foreach (var x in h) h.Contains(1); });
            string e7 = V(() => { foreach (var x in h) h.UnionWith(new[] { 1 }); });
            string e8 = V(() => { foreach (var x in h) h.UnionWith(new[] { 77 }); });
            h.Remove(77);
            string e9 = V(() => { foreach (var x in h) h.IntersectWith(new[] { 1, 2 }); });
            string e10 = V(() => { foreach (var x in h) h.IntersectWith(new[] { 1 }); });
            h.Add(2);
            string e11 = V(() => { foreach (var x in h) h.ExceptWith(new[] { 99 }); });
            string e12 = V(() => { foreach (var x in h) h.RemoveWhere(v => v > 100); });
            string e13 = V(() => { foreach (var x in h) h.RemoveWhere(v => v == 2); });
            h.Add(2);
            string e14 = V(() => { foreach (var x in h) h.TrimExcess(); });
            string e15 = V(() => { foreach (var x in h) h.EnsureCapacity(1000); });
            string e16 = V(() => { var en = h.GetEnumerator(); while (en.MoveNext()) { } h.Add(5); en.MoveNext(); });
            return Par.J(Q(() => e1), Q(() => e2), Q(() => e3), Q(() => c), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => e10), Q(() => e11), Q(() => e12), Q(() => e13), Q(() => e14), Q(() => e15), Q(() => e16), Q(() => Items(h)));
        }

        static string DictCopyCtor()
        {
            var src = new Dictionary<int, string>(); for (int i = 0; i < 10; i++) src[i] = "v" + i; src.Remove(3); src.Remove(7); src[20] = "x"; src[21] = "y";
            var copy = new Dictionary<int, string>(src);
            var fromList = new Dictionary<int, string>(new List<KeyValuePair<int, string>> { new(5, "a"), new(1, "b"), new(9, "c") });
            string e1 = P(() => new Dictionary<int, string>(new List<KeyValuePair<int, string>> { new(5, "a"), new(5, "b") }).Count);
            var ci = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "a", 1 } }; var ciCopy = new Dictionary<string, int>(ci); var ciCopy2 = new Dictionary<string, int>(ci, StringComparer.Ordinal); ciCopy2["A"] = 2;
            var cs = new Dictionary<string, int> { { "a", 1 }, { "A", 2 } }; string e2 = P(() => new Dictionary<string, int>(cs, StringComparer.OrdinalIgnoreCase).Count);
            var ro = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(src); string e3 = V(() => ((IDictionary<int, string>)ro)[1] = "z"); string e4 = V(() => ((IDictionary<int, string>)ro).Add(99, "z"));
            var sorted = new SortedDictionary<int, string>(src); var sl = new SortedList<int, string>(src);
            return Par.J(Q(() => Keys(src)), Q(() => Keys(copy)), Q(() => copy.Count), Q(() => ReferenceEquals(src.Comparer, copy.Comparer)), Q(() => Keys(fromList)), Q(() => e1), Q(() => ciCopy.Comparer.GetType().Name), Q(() => ciCopy.ContainsKey("A")), Q(() => ciCopy2.Count), Q(() => e2), Q(() => ro.Count), Q(() => ro[1]), Q(() => e3), Q(() => e4), Q(() => Keys(sorted)), Q(() => Keys(sl)), Q(() => copy.Remove(0)), Q(() => src.ContainsKey(0)), Q(() => new Dictionary<int, int>(new Dictionary<int, int>()).Count), P(() => new Dictionary<int, int>((IDictionary<int, int>)null).Count), P(() => new Dictionary<int, int>((IEnumerable<KeyValuePair<int, int>>)null).Count), Q(() => Keys(new Dictionary<int, string>(src.Where2(kv => kv.Key > 5)))));
        }

        static string Keys2(SortedDictionary<int, string> d) { var sb = new System.Text.StringBuilder(); foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(','); return sb.ToString(); }
        static string Keys3(SortedList<int, string> d) { var sb = new System.Text.StringBuilder(); foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(','); return sb.ToString(); }

        static string DictEnumerator()
        {
            var d = new Dictionary<int, string> { { 1, "a" }, { 2, "b" } };
            var en = d.GetEnumerator();
            var c0 = en.Current; bool m1 = en.MoveNext(); var c1 = en.Current; bool m2 = en.MoveNext(); var c2 = en.Current; bool m3 = en.MoveNext(); var c3 = en.Current; bool m4 = en.MoveNext();
            var ie = ((IEnumerable<KeyValuePair<int, string>>)d).GetEnumerator(); string e1 = P(() => ie.Current); ie.MoveNext(); while (ie.MoveNext()) { } string e2 = P(() => ie.Current.Key);
            var de = ((System.Collections.IDictionary)d).GetEnumerator(); string e3 = P(() => de.Current); string e4 = P(() => de.Key); de.MoveNext(); var entry = de.Entry; var key = de.Key; var val = de.Value; var cur = de.Current;
            var oe = ((System.Collections.IEnumerable)d).GetEnumerator(); oe.MoveNext(); var ocur = oe.Current;
            ((System.Collections.IEnumerator)en).Reset(); bool mr = en.MoveNext(); var cr = en.Current;
            var ke = d.Keys.GetEnumerator(); var kc0 = ke.Current; ke.MoveNext(); var kc1 = ke.Current; while (ke.MoveNext()) { } var kcEnd = ke.Current;
            var ve = d.Values.GetEnumerator(); var vc0 = ve.Current; ve.MoveNext(); var vc1 = ve.Current;
            var empty = new Dictionary<int, int>().GetEnumerator(); bool em = empty.MoveNext(); var ec = empty.Current;
            return Par.J(Q(() => c0.Key), Q(() => c0.Value == null), Q(() => m1), Q(() => c1.Key), Q(() => c1.Value), Q(() => m2), Q(() => c2.Key), Q(() => m3), Q(() => c3.Key), Q(() => c3.Value == null), Q(() => m4), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => entry.Key), Q(() => entry.Value), Q(() => key), Q(() => val), Q(() => cur.GetType().Name), Q(() => ocur.GetType().Name), Q(() => mr), Q(() => cr.Key), Q(() => kc0), Q(() => kc1), Q(() => kcEnd), Q(() => vc0 == null), Q(() => vc1), Q(() => em), Q(() => ec.Key), Q(() => ec.Value), Q(() => en.GetType().Name), Q(() => ke.GetType().Name), Q(() => c0.ToString()), Q(() => c1.ToString()), Q(() => new KeyValuePair<string, int>(null, 0).ToString()), Q(() => new KeyValuePair<int, string>(1, null).ToString()), Q(() => default(KeyValuePair<int, int>).ToString()), Q(() => new KeyValuePair<int, int>(1, 2).Equals(new KeyValuePair<int, int>(1, 2))), Q(() => new KeyValuePair<int, int>(1, 2).GetHashCode() == new KeyValuePair<int, int>(1, 2).GetHashCode()), Q(() => new KeyValuePair<int, int>(1, 2).Key), Q(() => new KeyValuePair<int, int>(1, 2).Deconstruct2()));
        }

        static string SortedDictionaryBasics()
        {
            var sd = new SortedDictionary<string, int>(); sd["b"] = 2; sd["a"] = 1; sd["B"] = 3; sd["ä"] = 4; sd["10"] = 5; sd["9"] = 6; sd[""] = 0;
            string e1 = V(() => sd[null] = 1);
            var sdi = new SortedDictionary<int, int>(); foreach (var i in new[] { 5, -1, 3, int.MinValue, 0, int.MaxValue }) sdi[i] = i;
            var sdic = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase); sdic["b"] = 1; sdic["A"] = 2; sdic["a"] = 3;
            var sdr = new SortedDictionary<int, int>(Comparer<int>.Create((x, y) => y.CompareTo(x))); foreach (var i in new[] { 1, 3, 2 }) sdr[i] = i;
            var sl = new SortedList<string, int>(); sl["b"] = 2; sl["a"] = 1; sl["c"] = 3; sl.RemoveAt(0); sl["aa"] = 4;
            var ss = new SortedSet<int> { 5, 1, 3, 1, 9 }; var view = ss.GetViewBetween(2, 8);
            var sdv = new SortedDictionary<double, int>(); sdv[1.5] = 1; sdv[-0.0] = 2; sdv[0.0] = 3; string e2 = V(() => sdv[double.NaN] = 4); sdv[double.NegativeInfinity] = 5;
            return Par.J(Q(() => Keys(sd)), Q(() => e1), Q(() => sd.Count), Q(() => Keys(sdi)), Q(() => Keys(sdic)), Q(() => sdic.Count), Q(() => Keys(sdr)), Q(() => Keys(sl)), Q(() => sl.IndexOfKey("c")), Q(() => sl.IndexOfKey("zz")), Q(() => sl.Keys[0]), Q(() => sl.Values[1]), Q(() => Items(ss)), Q(() => ss.Min), Q(() => ss.Max), Q(() => Items(view)), Q(() => view.Count), Q(() => Items(ss.Reverse())), Q(() => ss.GetViewBetween(2, 8).Min), P(() => ss.GetViewBetween(8, 2).Count), Q(() => Keys(sdv)), Q(() => e2), Q(() => sdv.Count), Q(() => sd.Comparer.GetType().Name), Q(() => sdi.Comparer.GetType().Name), Q(() => ss.Comparer.GetType().Name), Q(() => sd.Keys.GetType().Name), Q(() => Items(sd.Keys)), Q(() => Items(sd.Values)), Q(() => sd.ContainsKey("A")), Q(() => sdic.ContainsKey("A")), Q(() => sd.Remove("a")), Q(() => sd.Remove("a")), sd.TryGetValue("b", out int bv), Q(() => bv), ss.TryGetValue(3, out int sv), Q(() => sv), Q(() => ss.Remove(3)), Q(() => ss.Contains(3)), Q(() => new SortedSet<int>(new[] { 3, 1, 2 }).SetEquals(new[] { 1, 2, 3 })), Q(() => Items(new SortedSet<string>(new[] { "b", "B", "a", "A", "ä" }))), Q(() => Items(new SortedSet<string>(new[] { "b", "B", "a", "A", "ä" }, StringComparer.Ordinal))), Q(() => Items(new SortedSet<string>(new[] { "b", "B", "a", "A", "ä" }, StringComparer.OrdinalIgnoreCase))));
        }

        static string DictEnsureCapacity()
        {
            var d = new Dictionary<int, int>();
            var sb = new System.Text.StringBuilder();
            foreach (var n in new[] { 0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 16, 17, 23, 24, 29, 30, 37, 47, 48, 59, 71, 89, 100, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919, 1000, 1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369 })
                sb.Append(new Dictionary<int, int>().EnsureCapacity(n)).Append(',');
            var h = new HashSet<int>(); var sb2 = new System.Text.StringBuilder();
            foreach (var n in new[] { 0, 1, 3, 4, 7, 8, 100, 1000 }) sb2.Append(new HashSet<int>().EnsureCapacity(n)).Append(',');
            var l = new List<int>(); var sb3 = new System.Text.StringBuilder();
            foreach (var n in new[] { 0, 1, 3, 4, 5, 8, 9, 100 }) sb3.Append(new List<int>().EnsureCapacity(n)).Append(',');
            var grow = new Dictionary<int, int>(); var sb4 = new System.Text.StringBuilder(); int last = -1;
            for (int i = 0; i < 200; i++) { grow[i] = i; int cap = grow.EnsureCapacity(0); if (cap != last) { sb4.Append(i).Append(':').Append(cap).Append(','); last = cap; } }
            var growH = new HashSet<int>(); var sb5 = new System.Text.StringBuilder(); last = -1;
            for (int i = 0; i < 200; i++) { growH.Add(i); int cap = growH.EnsureCapacity(0); if (cap != last) { sb5.Append(i).Append(':').Append(cap).Append(','); last = cap; } }
            return Par.J(Q(() => sb.ToString()), Q(() => sb2.ToString()), Q(() => sb3.ToString()), Q(() => sb4.ToString()), Q(() => sb5.ToString()), Q(() => new Dictionary<int, int>(3).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(4).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(10).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(100).EnsureCapacity(0)), Q(() => new HashSet<int>(10).EnsureCapacity(0)), Q(() => new HashSet<int>(new[] { 1, 2, 3 }).EnsureCapacity(0)), Q(() => new HashSet<int>(new List<int> { 1, 2, 3, 4, 5 }).EnsureCapacity(0)), Q(() => new Dictionary<int, int>(new Dictionary<int, int> { { 1, 1 }, { 2, 2 } }).EnsureCapacity(0)));
        }

        static string DictValueTypes()
        {
            var d = new Dictionary<int, PlainKey>(); d[1] = new PlainKey { X = 1, Y = 2 };
            var v = d[1]; v.X = 99; // kopya: dict etkilenmez
            string e1 = V(() => { var tmp = d[1]; tmp.Y = 5; }); // sadece kopya
            d[1] = v;
            ref var rv = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(d, 1); rv.Y = 777;
            var dl = new Dictionary<int, List<int>>(); dl[1] = new List<int>(); dl[1].Add(5); var lst = dl[1]; lst.Add(6);
            var dn = new Dictionary<int, int?>(); dn[1] = null; dn[2] = 3; int sum = 0; foreach (var kv in dn) sum += kv.Value ?? -1;
            var dt = new Dictionary<int, (int a, string b)>(); dt[1] = (1, "x"); var t = dt[1]; t.a = 5; dt[2] = t; var (ta, tb) = dt[2];
            var dd = new Dictionary<int, double>(); dd[1] = 0.1; dd[1] += 0.2; dd[2] = 1.0 / 3; dd[2] *= 3;
            var dc = new Dictionary<int, char>(); dc[1] = 'a'; dc[1]++;
            var db = new Dictionary<int, byte>(); db[1] = 255; db[1]++; db[2] = 0; db[2]--;
            var de = new Dictionary<int, Col>(); de[1] = Col.R; de[1]++; de[2] = Col.B; de[2]++;
            var df = new Dictionary<int, float>(); df[1] = 16777216f; df[1] += 1f;
            var dlg = new Dictionary<int, long>(); dlg[1] = long.MaxValue; dlg[1]++;
            return Par.J(Q(() => d[1].X), Q(() => d[1].Y), Q(() => e1), Q(() => dl[1].Count), Q(() => sum), Q(() => ta), Q(() => tb), Q(() => dt[1].a), Q(() => dd[1]), Q(() => dd[2]), Q(() => dc[1]), Q(() => db[1]), Q(() => db[2]), Q(() => de[1]), Q(() => de[2]), Q(() => (int)de[2]), Q(() => df[1]), Q(() => dlg[1]), Q(() => d.ContainsValue(new PlainKey { X = 99, Y = 777 })), Q(() => d.ContainsValue(new PlainKey { X = 99, Y = 2 })), Q(() => dn.ContainsValue(null)), Q(() => dn.ContainsValue(3)), Q(() => dt.ContainsValue((5, "x"))), Q(() => dt.ContainsValue((5, "y"))), Q(() => dl.ContainsValue(lst)), Q(() => dl.ContainsValue(new List<int> { 5, 6 })), Q(() => dd.ContainsValue(0.30000000000000004)), Q(() => dd.ContainsValue(0.3)), Q(() => df.ContainsValue(16777216f)), Q(() => new Dictionary<int, double> { { 1, double.NaN } }.ContainsValue(double.NaN)), Q(() => new Dictionary<int, double> { { 1, -0.0 } }.ContainsValue(0.0)), Q(() => new Dictionary<int, string> { { 1, "a" } }.ContainsValue(new string('a', 1))), Q(() => new Dictionary<int, object> { { 1, 5 } }.ContainsValue(5)), Q(() => new Dictionary<int, object> { { 1, 5 } }.ContainsValue(5L)), Q(() => new Dictionary<int, object> { { 1, 5 } }.ContainsValue((byte)5)));
        }

        static string DictDoubleKeys()
        {
            var d = new Dictionary<double, string>(); d[0.1 + 0.2] = "a"; d[0.3] = "b"; d[1e-320] = "sub"; d[double.Epsilon] = "eps"; d[double.PositiveInfinity] = "inf"; d[double.MaxValue] = "max"; d[-0.0] = "nz"; d[0.0] = "pz"; d[double.NaN] = "nan"; d[-double.NaN] = "nnan"; d[BitConverter.Int64BitsToDouble(0x7FF0000000000001L)] = "snan";
            var f = new Dictionary<float, string>(); f[0.1f] = "a"; f[(float)0.1] = "b"; f[0.1f + 0.2f] = "c"; f[0.3f] = "d"; f[float.NaN] = "nan"; f[-0f] = "nz"; f[0f] = "pz";
            var l = new Dictionary<long, string>(); l[1L << 32] = "a"; l[1] = "b"; l[(1L << 32) | 1] = "c"; l[-1] = "d"; l[long.MinValue] = "min"; l[long.MaxValue] = "max"; l[0] = "z";
            var dec = new Dictionary<decimal, string>(); dec[1.0m] = "a"; dec[1.00m] = "b"; dec[1m] = "c"; dec[0.1m + 0.2m] = "d"; dec[0.3m] = "e";
            var u = new Dictionary<uint, string>(); u[uint.MaxValue] = "a"; u[0] = "b"; u[0x80000000] = "c";
            var ul = new Dictionary<ulong, string>(); ul[ulong.MaxValue] = "a"; ul[1UL << 63] = "b"; ul[1] = "c";
            var sh = new Dictionary<short, string>(); sh[-1] = "a"; sh[short.MinValue] = "b"; sh[1] = "c"; sh[0] = "d";
            var sb = new Dictionary<sbyte, string>(); sb[-1] = "a"; sb[sbyte.MinValue] = "b"; sb[1] = "c";
            var ch = new Dictionary<char, string>(); ch['\uFFFF'] = "a"; ch['\0'] = "b"; ch['a'] = "c";
            return Par.J(Q(() => d.Count), Q(() => Keys(d)), Q(() => f.Count), Q(() => Keys(f)), Q(() => l.Count), Q(() => Keys(l)), Q(() => dec.Count), Q(() => Keys(dec)), Q(() => u.Count), Q(() => Keys(u)), Q(() => ul.Count), Q(() => Keys(ul)), Q(() => sh.Count), Q(() => Keys(sh)), Q(() => sb.Count), Q(() => Keys(sb)), Q(() => ch.Count), Q(() => Keys(ch).Length), Q(() => d[double.NaN]), Q(() => d[0.0]), Q(() => f[float.NaN]), Q(() => f[0f]), Q(() => dec[1m]), Q(() => l[1]), Q(() => (0.1 + 0.2).GetHashCode() == 0.3.GetHashCode()), Q(() => 1.0m.GetHashCode() == 1.00m.GetHashCode()), Q(() => 1.0m.Equals(1.00m)), Q(() => 1.0m == 1.00m), Q(() => 1.0m.ToString()), Q(() => 1.00m.ToString()), Q(() => (0.1m + 0.2m).ToString()), Q(() => dec[0.3m]), Q(() => 1.0m.CompareTo(1.00m)), Q(() => decimal.Equals(1.0m, 1.00m)));
        }
    }

    static class Ext93
    {
        public static IEnumerable<KeyValuePair<K, V>> Where2<K, V>(this Dictionary<K, V> d, Func<KeyValuePair<K, V>, bool> pred) { foreach (var kv in d) if (pred(kv)) yield return kv; }
        public static string Deconstruct2(this KeyValuePair<int, int> kv) { var (k, v) = kv; return k + "/" + v; }
    }
}
