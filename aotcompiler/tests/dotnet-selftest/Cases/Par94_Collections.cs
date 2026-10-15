using System;
using System.Collections.Generic;
using ParityKit;

// 94: List/Queue/Stack/Array semantigi — kapasite buyumesi, sinirlar, Sort/BinarySearch, enumeration sirasinda degisiklik,
// Queue wraparound, Stack sirasi, dizi kovaryansi (int[]<->uint[] CLR'de gecerli), cok boyutlu/jagged, Array.* yardimcilari.
namespace Demo94
{
    struct Pt { public int X, Y; public void Mutate() { X += 100; } public override string ToString() { return X + "/" + Y; } }
    class Node { public int V; public Node(int v) { V = v; } public override string ToString() { return "N" + V; } }
    enum Col { R, G, B }
    enum ColByte : byte { R, G, B }

    partial class App94
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string V(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }
        static string Items<T>(IEnumerable<T> e) { var sb = new System.Text.StringBuilder(); foreach (var x in e) sb.Append(x == null ? "null" : x.ToString()).Append(','); return sb.ToString(); }

        public static int Run() { return Par.Loop(94, 24, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return ListBounds();
                case 2: return ListCapacity();
                case 3: return ListSort();
                case 4: return ListSortStability();
                case 5: return ListBinarySearch();
                case 6: return ListModifyDuringEnum();
                case 7: return ListFindRemove();
                case 8: return ListRanges();
                case 9: return ListStructElements();
                case 10: return QueueBasics();
                case 11: return QueueWraparound();
                case 12: return StackBasics();
                case 13: return ArrayCovariance();
                case 14: return ArrayPrimitiveCast();
                case 15: return ArrayMultiDim();
                case 16: return ArrayJagged();
                case 17: return ArrayHelpers();
                case 18: return ArrayCopyOverlap();
                case 19: return ArraySortSearch();
                case 20: return ArrayInterfaces();
                case 21: return ArrayBounds();
                case 22: return ArrayEmptyClone();
                case 23: return LinkedListBasics();
                case 24: return ListEnumeratorStruct();
            }
            return "?";
        }

        static string ListBounds()
        {
            var l = new List<int> { 1, 2, 3 };
            return Par.J(P(() => l[3]), P(() => l[-1]), P(() => l[int.MaxValue]), V(() => l[3] = 1), V(() => l.Insert(4, 1)), V(() => l.Insert(-1, 1)), V(() => l.Insert(3, 9)), Q(() => Items(l)), V(() => l.RemoveAt(4)), V(() => l.RemoveAt(-1)), V(() => l.RemoveRange(2, 5)), V(() => l.RemoveRange(-1, 1)), V(() => l.RemoveRange(0, -1)), V(() => l.RemoveRange(4, 0)), V(() => l.RemoveRange(4, 1)), P(() => l.GetRange(2, 5).Count), P(() => l.GetRange(4, 0).Count), P(() => l.GetRange(-1, 0).Count), V(() => l.InsertRange(5, new[] { 1 })), V(() => l.InsertRange(0, (IEnumerable<int>)null)), V(() => l.AddRange(null)), V(() => l.CopyTo(new int[2])), V(() => l.CopyTo(new int[10], 7)), V(() => l.CopyTo(1, new int[10], 0, 4)), V(() => l.CopyTo(null)), P(() => l.IndexOf(1, 5)), P(() => l.IndexOf(1, 4)), P(() => l.IndexOf(1, 0, 5)), P(() => l.IndexOf(1, -1)), P(() => l.LastIndexOf(1, 4)), P(() => l.LastIndexOf(1, 3)), P(() => l.LastIndexOf(1, 3, 5)), P(() => new List<int>().LastIndexOf(1, 0)), P(() => new List<int>().LastIndexOf(1, 0, 0)), P(() => new List<int>().IndexOf(1, 0)), P(() => new List<int>().IndexOf(1, 1)), Q(() => l.Remove(99)), Q(() => l.Remove(2)), Q(() => Items(l)), P(() => new List<int>(-1).Count), Q(() => new List<int>(0).Count), P(() => new List<int>((IEnumerable<int>)null).Count), P(() => l.FindIndex(4, x => true)), P(() => l.FindIndex(3, x => true)), P(() => l.FindIndex(0, 4, x => true)), P(() => l.FindLastIndex(3, x => true)), P(() => l.FindLastIndex(2, x => true)), P(() => l.FindLastIndex(-1, x => true)), P(() => new List<int>().FindLastIndex(0, x => true)), P(() => new List<int>().FindLastIndex(-1, x => true)), P(() => l.Find(null)), P(() => l.ConvertAll<int>(null)), P(() => l.Exists(null)), V(() => l.Sort((Comparison<int>)null)), V(() => l.Sort(0, 5, null)), V(() => l.Reverse(0, 5)), P(() => l.BinarySearch(0, 5, 1, null)), Q(() => ((System.Collections.IList)l).Add(5)), P(() => ((System.Collections.IList)l).Add("x")), P(() => ((System.Collections.IList)l).Add(null)), V(() => ((System.Collections.IList)l)[0] = "x"), Q(() => ((System.Collections.IList)l).IndexOf("x")), Q(() => ((System.Collections.IList)l).Contains(5)), Q(() => ((System.Collections.IList)l).Contains(5L)), Q(() => ((System.Collections.IList)l).IsFixedSize), Q(() => ((System.Collections.IList)l).IsReadOnly), Q(() => ((IList<int>)l).IsReadOnly));
        }

        static string ListCapacity()
        {
            var l = new List<int>(); var sb = new System.Text.StringBuilder(); int last = -1;
            for (int i = 0; i < 300; i++) { l.Add(i); if (l.Capacity != last) { sb.Append(i).Append(':').Append(l.Capacity).Append(','); last = l.Capacity; } }
            var l2 = new List<int>(3); int c0 = l2.Capacity; l2.AddRange(new[] { 1, 2, 3, 4 }); int c1 = l2.Capacity; l2.AddRange(new int[20]); int c2 = l2.Capacity;
            var l3 = new List<int>(); l3.AddRange(new List<int> { 1, 2, 3, 4, 5 }); int c3 = l3.Capacity; l3.AddRange(Gen(10)); int c4 = l3.Capacity;
            var l4 = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 }; int c5 = l4.Capacity; l4.RemoveRange(0, 9); int c6 = l4.Capacity; l4.TrimExcess(); int c7 = l4.Capacity; l4.Add(1); int c8 = l4.Capacity;
            var l5 = new List<int>(100); l5.Add(1); l5.TrimExcess(); int c9 = l5.Capacity; l5.Capacity = 50; int c10 = l5.Capacity; string e1 = V(() => l5.Capacity = 0); l5.Clear(); l5.Capacity = 0; int c11 = l5.Capacity; l5.Add(1); int c12 = l5.Capacity;
            var l6 = new List<int>(new[] { 1, 2, 3 }); int c13 = l6.Capacity; var l7 = new List<int>(Gen(7)); int c14 = l7.Capacity; var l8 = new List<int>(new List<int> { 1, 2 }); int c15 = l8.Capacity; var l9 = new List<int>(new HashSet<int> { 1, 2, 3 }); int c16 = l9.Capacity;
            var l10 = new List<int>(); l10.Insert(0, 1); int c17 = l10.Capacity; var l11 = new List<int>(); l11.InsertRange(0, new[] { 1, 2, 3, 4, 5 }); int c18 = l11.Capacity; var l12 = new List<int>(); l12.InsertRange(0, Gen(5)); int c19 = l12.Capacity;
            var l13 = new List<int>(4) { 1, 2, 3, 4 }; l13.InsertRange(2, new[] { 9, 9 }); int c20 = l13.Capacity; var l14 = new List<int>(); l14.EnsureCapacity(5); int c21 = l14.Capacity; l14.EnsureCapacity(3); int c22 = l14.Capacity; l14.EnsureCapacity(100); int c23 = l14.Capacity; string e2 = P(() => l14.EnsureCapacity(-1));
            var ls = new List<string>(); var sb2 = new System.Text.StringBuilder(); last = -1; for (int i = 0; i < 40; i++) { ls.Add("x"); if (ls.Capacity != last) { sb2.Append(ls.Capacity).Append(','); last = ls.Capacity; } }
            return Par.J(Q(() => sb.ToString()), Q(() => c0), Q(() => c1), Q(() => c2), Q(() => c3), Q(() => c4), Q(() => c5), Q(() => c6), Q(() => c7), Q(() => c8), Q(() => c9), Q(() => c10), Q(() => e1), Q(() => c11), Q(() => c12), Q(() => c13), Q(() => c14), Q(() => c15), Q(() => c16), Q(() => c17), Q(() => c18), Q(() => c19), Q(() => Items(l13)), Q(() => c20), Q(() => c21), Q(() => c22), Q(() => c23), Q(() => e2), Q(() => sb2.ToString()), Q(() => new List<int>().Capacity), Q(() => new List<int>(0).Capacity), Q(() => new List<int>(7).Capacity), Q(() => new List<int>(new int[0]).Capacity), Q(() => new List<int>(Gen(0)).Capacity), Q(() => new List<int>(new List<int>()).Capacity));
        }

        static string TupItems(List<(int, string)> l) { var sb = new System.Text.StringBuilder(); for (int i = 0; i < l.Count; i++) sb.Append(l[i].Item1).Append('/').Append(l[i].Item2).Append(','); return sb.ToString(); }
        static IEnumerable<int> Gen(int n) { for (int i = 0; i < n; i++) yield return i; }

        static string ListSort()
        {
            var ints = new List<int> { 5, -1, 3, int.MinValue, int.MaxValue, 0, 3, -1 }; ints.Sort();
            var desc = new List<int> { 5, -1, 3, 0 }; desc.Sort((a, b) => b.CompareTo(a));
            var strs = new List<string> { "b", "B", "a", "A", "ä", "Z", "10", "9", "", null, " " }; strs.Sort();
            var strsO = new List<string>(strs); strsO.Sort(StringComparer.Ordinal);
            var strsI = new List<string>(strs); strsI.Sort(StringComparer.OrdinalIgnoreCase);
            var dbl = new List<double> { 1.5, double.NaN, -0.0, 0.0, double.NegativeInfinity, double.PositiveInfinity, -1.5, double.NaN, double.Epsilon }; dbl.Sort();
            var flt = new List<float> { 1.5f, float.NaN, -0f, 0f, float.NegativeInfinity }; flt.Sort();
            var part = new List<int> { 9, 8, 7, 6, 5, 4, 3, 2, 1 }; part.Sort(2, 5, null);
            var partC = new List<int> { 9, 8, 7, 6, 5, 4, 3, 2, 1 }; partC.Sort(2, 5, Comparer<int>.Create((a, b) => b - a));
            var chars = new List<char> { 'b', 'A', 'a', 'B', '\0', '\uFFFF', ' ' }; chars.Sort();
            var bools = new List<bool> { true, false, true, false }; bools.Sort();
            var enums = new List<Col> { Col.B, Col.R, Col.G, (Col)I(-1), (Col)I(9) }; enums.Sort();
            var nulls = new List<int?> { 3, null, 1, null, 2 }; nulls.Sort();
            var nodes = new List<Node> { new Node(2), new Node(1) }; string e1 = V(() => nodes.Sort());
            var nodesC = new List<Node> { new Node(2), new Node(1), new Node(3) }; nodesC.Sort((a, b) => a.V.CompareTo(b.V));
            var bad = new List<int> { 3, 1, 2, 5, 4, 7, 6, 9, 8, 10, 12, 11, 14, 13, 16, 15, 18, 17, 20, 19 }; string e2 = V(() => bad.Sort((a, b) => 1)); // tutarsiz comparer: .NET InvalidOperationException veya sessiz
            var bad2 = new List<int> { 3, 1, 2 }; string e3 = V(() => bad2.Sort((a, b) => { throw new ArgumentException("boom"); }));
            var obj = new List<object> { 3, 1, 2 }; obj.Sort();
            var mixed = new List<object> { 3, "a", 2 }; string e4 = V(() => mixed.Sort());
            var tup = new List<(int, string)> { (2, "b"), (1, "z"), (2, "a"), (1, "a") }; tup.Sort();
            var big = new List<int>(); var rnd = new Random(12345); for (int i = 0; i < 2000; i++) big.Add(rnd.Next(1000)); big.Sort(); int bsum = 0; for (int i = 0; i < big.Count; i++) bsum += big[i] * (i % 7);
            var ulist = new List<uint> { 0x80000000, 1, 0xFFFFFFFF, 0 }; ulist.Sort();
            var llist = new List<long> { long.MinValue, -1, 1, long.MaxValue, 0 }; llist.Sort();
            var blist = new List<byte> { 200, 1, 255, 0 }; blist.Sort();
            var sblist = new List<sbyte> { -128, 127, -1, 0 }; sblist.Sort();
            return Par.J(Q(() => Items(ints)), Q(() => Items(desc)), Q(() => Items(strs)), Q(() => Items(strsO)), Q(() => Items(strsI)), Q(() => Items(dbl)), Q(() => Items(flt)), Q(() => Items(part)), Q(() => Items(partC)), Q(() => Items(chars).Length), Q(() => (int)chars[0]), Q(() => (int)chars[6]), Q(() => Items(bools)), Q(() => Items(enums)), Q(() => Items(nulls)), Q(() => e1), Q(() => Items(nodesC)), Q(() => e2), Q(() => Items(bad)), Q(() => e3), Q(() => Items(bad2)), Q(() => Items(obj)), Q(() => e4), Q(() => Items(tup)), Q(() => big[0]), Q(() => big[1999]), Q(() => big[1000]), Q(() => bsum), Q(() => Items(ulist)), Q(() => Items(llist)), Q(() => Items(blist)), Q(() => Items(sblist)), Q(() => new List<int>().Count + (V(() => new List<int>().Sort()) == "ok" ? 0 : 1)), V(() => new List<int> { 1 }.Sort()));
        }

        static string ListSortStability()
        {
            // List.Sort/Array.Sort introsort: KARARSIZ. Esit anahtarli elemanlarin sirasi .NET implementasyonuna bagli -> parite icin birebir ayni olmali.
            var items = new List<(int key, int idx)>(); for (int i = 0; i < 40; i++) items.Add((i % 4, i));
            items.Sort((a, b) => a.key.CompareTo(b.key));
            var sb = new System.Text.StringBuilder(); foreach (var it in items) sb.Append(it.idx).Append(',');
            var small = new List<(int key, int idx)>(); for (int i = 0; i < 10; i++) small.Add((i % 2, i)); small.Sort((a, b) => a.key.CompareTo(b.key));
            var sb2 = new System.Text.StringBuilder(); foreach (var it in small) sb2.Append(it.idx).Append(',');
            var arr = new (int key, int idx)[25]; for (int i = 0; i < 25; i++) arr[i] = ((i * 7) % 5, i); Array.Sort(arr, (a, b) => a.key.CompareTo(b.key));
            var sb3 = new System.Text.StringBuilder(); foreach (var it in arr) sb3.Append(it.idx).Append(',');
            var keys = new int[12]; var vals = new int[12]; for (int i = 0; i < 12; i++) { keys[i] = i % 3; vals[i] = i; } Array.Sort(keys, vals);
            var strs = new List<string> { "bb", "a", "cc", "d", "ee", "f", "gg", "h", "ii", "j", "kk", "l", "mm", "n", "oo", "p", "qq" }; strs.Sort((x, y) => x.Length.CompareTo(y.Length));
            var big = new List<(int key, int idx)>(); for (int i = 0; i < 500; i++) big.Add(((i * 31) % 7, i)); big.Sort((a, b) => a.key.CompareTo(b.key)); int chk = 0; for (int i = 0; i < big.Count; i++) chk = chk * 31 + big[i].idx;
            var allEq = new List<(int key, int idx)>(); for (int i = 0; i < 20; i++) allEq.Add((0, i)); allEq.Sort((a, b) => 0);
            var sb4 = new System.Text.StringBuilder(); foreach (var it in allEq) sb4.Append(it.idx).Append(',');
            var rev = new List<(int key, int idx)>(); for (int i = 0; i < 20; i++) rev.Add((20 - i, i)); rev.Sort((a, b) => a.key.CompareTo(b.key));
            var sb5 = new System.Text.StringBuilder(); foreach (var it in rev) sb5.Append(it.idx).Append(',');
            var sorted = new List<(int key, int idx)>(); for (int i = 0; i < 20; i++) sorted.Add((i / 2, i)); sorted.Sort((a, b) => a.key.CompareTo(b.key));
            var sb6 = new System.Text.StringBuilder(); foreach (var it in sorted) sb6.Append(it.idx).Append(',');
            return Par.J(Q(() => sb.ToString()), Q(() => sb2.ToString()), Q(() => sb3.ToString()), Q(() => Items(vals)), Q(() => Items(strs)), Q(() => chk), Q(() => sb4.ToString()), Q(() => sb5.ToString()), Q(() => sb6.ToString()));
        }

        static string ListBinarySearch()
        {
            var l = new List<int> { 1, 3, 5, 7, 9 };
            var dup = new List<int> { 1, 3, 3, 3, 5 };
            var strs = new List<string> { "a", "c", "e" };
            var empty = new List<int>();
            return Par.J(Q(() => l.BinarySearch(5)), Q(() => l.BinarySearch(1)), Q(() => l.BinarySearch(9)), Q(() => l.BinarySearch(0)), Q(() => l.BinarySearch(2)), Q(() => l.BinarySearch(4)), Q(() => l.BinarySearch(10)), Q(() => ~l.BinarySearch(4)), Q(() => dup.BinarySearch(3)), Q(() => dup.BinarySearch(2)), Q(() => dup.BinarySearch(4)), Q(() => strs.BinarySearch("c")), Q(() => strs.BinarySearch("b")), Q(() => strs.BinarySearch("C")), Q(() => strs.BinarySearch("C", StringComparer.OrdinalIgnoreCase)), Q(() => strs.BinarySearch(null)), Q(() => strs.BinarySearch("")), Q(() => empty.BinarySearch(5)), Q(() => l.BinarySearch(1, 3, 7, null)), Q(() => l.BinarySearch(1, 3, 1, null)), Q(() => l.BinarySearch(1, 3, 9, null)), Q(() => l.BinarySearch(5, 0, 5, null)), P(() => l.BinarySearch(5, 1, 5, null)), P(() => l.BinarySearch(-1, 1, 5, null)), Q(() => l.BinarySearch(5, Comparer<int>.Create((a, b) => b.CompareTo(a)))), Q(() => new List<int> { 9, 7, 5 }.BinarySearch(7, Comparer<int>.Create((a, b) => b.CompareTo(a)))), Q(() => new List<double> { 1, double.NaN, 3 }.BinarySearch(double.NaN)), Q(() => new List<double> { double.NaN, 1, 3 }.BinarySearch(double.NaN)), Q(() => new List<double> { double.NaN, 1, 3 }.BinarySearch(1.0)), Q(() => new List<int?> { null, 1, 3 }.BinarySearch(null)), Q(() => new List<int?> { null, 1, 3 }.BinarySearch(2)), Q(() => Array.BinarySearch(new[] { 1, 3, 5 }, 3)), Q(() => Array.BinarySearch(new[] { 1, 3, 5 }, 4)), Q(() => Array.BinarySearch(new[] { 1, 3, 5 }, 1, 2, 5)), P(() => Array.BinarySearch(new[] { 1, 3, 5 }, 1, 3, 5)), Q(() => Array.BinarySearch(new object[] { 1, 3, 5 }, 3)), P(() => Array.BinarySearch(new object[] { 1, "x", 5 }, 3)), Q(() => Array.BinarySearch(new string[] { "a", "c" }, "b")), Q(() => Array.BinarySearch(new[] { 1, 3, 5 }, (object)3)), P(() => Array.BinarySearch(new[] { 1, 3, 5 }, (object)"x")), P(() => Array.BinarySearch(new[] { 1, 3, 5 }, (object)3L)), P(() => Array.BinarySearch(new int[2, 2], 1)), Q(() => Array.BinarySearch(new int[0], 1)), Q(() => new List<int> { 5, 3, 1 }.BinarySearch(3) /* sirasiz: tanimsiz ama deterministik */));
        }

        static string ListModifyDuringEnum()
        {
            var l = new List<int> { 1, 2, 3 };
            string e1 = V(() => { foreach (var x in l) l.Add(9); });
            l.RemoveRange(3, l.Count - 3);
            string e2 = V(() => { foreach (var x in l) l.Remove(x); });
            l.Clear(); l.AddRange(new[] { 1, 2, 3 });
            string e3 = V(() => { foreach (var x in l) l[0] = 10; }); // indexer set: versiyon degistirir mi? (.NET: evet, degistirmez -> izinli... parite)
            string e4 = V(() => { foreach (var x in l) l.Sort(); });
            string e5 = V(() => { foreach (var x in l) l.Reverse(); });
            string e6 = V(() => { foreach (var x in l) l.Clear(); });
            l.AddRange(new[] { 1, 2, 3 });
            string e7 = V(() => { foreach (var x in l) l.Insert(0, 0); });
            l.Clear(); l.AddRange(new[] { 1, 2, 3 });
            string e8 = V(() => { foreach (var x in l) l.RemoveAll(v => v > 100); }); // hic silmeyen RemoveAll
            string e9 = V(() => { foreach (var x in l) l.RemoveAll(v => v == 2); });
            l.Clear(); l.AddRange(new[] { 1, 2, 3 });
            string e10 = V(() => { foreach (var x in l) l.TrimExcess(); });
            string e11 = V(() => { foreach (var x in l) l.Capacity = 100; });
            string e12 = V(() => { foreach (var x in l) l.Contains(1); });
            string e13 = V(() => { var en = l.GetEnumerator(); while (en.MoveNext()) { } l.Add(4); en.MoveNext(); });
            string e14 = V(() => { var en = l.GetEnumerator(); en.MoveNext(); l.Add(5); var c = en.Current; });
            string e15 = V(() => { var en = l.GetEnumerator(); en.MoveNext(); l.Add(6); ((System.Collections.IEnumerator)en).Reset(); });
            string e16 = V(() => { var en = l.GetEnumerator(); en.MoveNext(); l.Add(7); en.Dispose(); en.MoveNext(); });
            string e17 = V(() => { foreach (var x in l) l.Remove(999); }); // basarisiz Remove
            string e18 = V(() => { foreach (var x in l) l.AddRange(new int[0]); }); // bos AddRange
            string e19 = V(() => { foreach (var x in l) l.InsertRange(0, new int[0]); });
            string e20 = V(() => { foreach (var x in l) l.RemoveRange(0, 0); });
            string e21 = V(() => { foreach (var x in l) l.EnsureCapacity(1000); });
            string e22 = V(() => { foreach (var x in l) l.Sort(0, 0, null); });
            string e23 = V(() => { foreach (var x in l) l.Reverse(0, 0); });
            var arr = new[] { 1, 2, 3 }; string e24 = V(() => { foreach (var x in arr) arr[0] = 9; });
            var ro = l.AsReadOnly(); string e25 = V(() => { foreach (var x in ro) l.Add(1); });
            return Par.J(Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => e10), Q(() => e11), Q(() => e12), Q(() => e13), Q(() => e14), Q(() => e15), Q(() => e16), Q(() => e17), Q(() => e18), Q(() => e19), Q(() => e20), Q(() => e21), Q(() => e22), Q(() => e23), Q(() => e24), Q(() => e25), Q(() => Items(l)), Q(() => Items(arr)), Q(() => ro.Count), Q(() => ro[0]), V(() => ((IList<int>)ro)[0] = 1), V(() => ((IList<int>)ro).Add(1)), Q(() => ro.GetType().Name));
        }

        static string ListFindRemove()
        {
            var l = new List<int> { 5, 3, 8, 3, 1, 9, 3 };
            int ra = new List<int>(l).RemoveAll(x => x == 3); var ra2 = new List<int>(l); ra2.RemoveAll(x => x == 3);
            int rz = new List<int>(l).RemoveAll(x => false), rall = new List<int>(l).RemoveAll(x => true);
            var order = new List<int>(); var raOrd = new List<int>(l); raOrd.RemoveAll(x => { order.Add(x); return x > 4; });
            var found = new List<int>(); l.Find(x => { found.Add(x); return x == 8; });
            return Par.J(Q(() => ra), Q(() => Items(ra2)), Q(() => rz), Q(() => rall), Q(() => Items(raOrd)), Q(() => Items(order)), Q(() => Items(found)), Q(() => l.Find(x => x == 3)), Q(() => l.Find(x => x == 99)), Q(() => l.FindLast(x => x == 3)), Q(() => l.FindIndex(x => x == 3)), Q(() => l.FindIndex(2, x => x == 3)), Q(() => l.FindIndex(2, 2, x => x == 3)), Q(() => l.FindLastIndex(x => x == 3)), Q(() => l.FindLastIndex(5, x => x == 3)), Q(() => l.FindLastIndex(5, 3, x => x == 3)), Q(() => l.FindIndex(x => x == 99)), Q(() => Items(l.FindAll(x => x == 3))), Q(() => l.FindAll(x => x == 99).Count), Q(() => l.Exists(x => x > 8)), Q(() => l.Exists(x => x > 9)), Q(() => l.TrueForAll(x => x > 0)), Q(() => l.TrueForAll(x => x > 1)), Q(() => l.IndexOf(3)), Q(() => l.IndexOf(3, 2)), Q(() => l.IndexOf(3, 4)), Q(() => l.IndexOf(3, 4, 2)), Q(() => l.IndexOf(3, 4, 3)), Q(() => l.LastIndexOf(3)), Q(() => l.LastIndexOf(3, 5)), Q(() => l.LastIndexOf(3, 5, 2)), Q(() => l.LastIndexOf(3, 5, 3)), Q(() => l.LastIndexOf(99)), Q(() => l.Contains(9)), Q(() => l.FindAll(x => x == 3).Count), Q(() => Items(l.ConvertAll(x => x * 2))), Q(() => Items(l.ConvertAll(x => x.ToString() + "!"))), Q(() => l.Remove(3)), Q(() => Items(l)), Q(() => l.Count), V(() => l.ForEach(x => { if (x == 8) l.Add(0); })), V(() => l.ForEach(x => { })), V(() => l.ForEach(null)), Q(() => Items(l)), Q(() => new List<string> { "a", null, "b" }.IndexOf(null)), Q(() => new List<string> { "a", null, "b" }.Contains(null)), Q(() => new List<string> { "a", null, "b" }.Remove(null)), Q(() => new List<string> { "a", null, "b" }.Find(x => x == null) == null), Q(() => new List<string> { "a", null, "b" }.FindIndex(x => x == null)), Q(() => new List<double> { 1, double.NaN }.IndexOf(double.NaN)), Q(() => new List<double> { 1, double.NaN }.Contains(double.NaN)), Q(() => new List<double> { -0.0 }.IndexOf(0.0)), Q(() => new List<float> { float.NaN }.Contains(float.NaN)), Q(() => new List<object> { 1, "x" }.IndexOf(1)), Q(() => new List<object> { 1, "x" }.IndexOf(1L)), Q(() => new List<object> { 1, "x" }.Contains("x")), Q(() => new List<Node> { new Node(1) }.Contains(new Node(1))), Q(() => new List<Pt> { new Pt { X = 1 } }.Contains(new Pt { X = 1 })), Q(() => new List<Pt> { new Pt { X = 1 } }.IndexOf(new Pt { X = 1, Y = 1 })));
        }

        static string ListRanges()
        {
            var l = new List<int> { 1, 2, 3, 4, 5 };
            var g = l.GetRange(1, 3); g[0] = 99;
            var sl = l.Slice(1, 3); var sl2 = l[1..3]; var sl3 = l[^2..];
            l.InsertRange(2, new[] { 7, 8 }); string s1 = Items(l);
            l.InsertRange(l.Count, Gen(2)); string s2 = Items(l);
            l.InsertRange(0, l); string s3 = Items(l); // kendini ekleme
            l.RemoveRange(0, l.Count / 2); string s4 = Items(l);
            l.AddRange(l); string s5 = Items(l);
            l.Reverse(); string s6 = Items(l); l.Reverse(1, 3); string s7 = Items(l);
            var copy = new int[20]; l.CopyTo(copy); l.CopyTo(copy, 2); l.CopyTo(1, copy, 15, 2);
            var arr = l.ToArray(); arr[0] = -1;
            var ro = l.AsReadOnly();
            var ins = new List<int> { 1, 2 }; ins.InsertRange(1, new List<int> { 9, 9, 9 }); ins.InsertRange(0, new HashSet<int> { 5 }); ins.InsertRange(ins.Count, new int[0]);
            var cm = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l); cm[0] = 1000;
            System.Runtime.InteropServices.CollectionsMarshal.SetCount(l, 3); string s8 = Items(l);
            string e1 = V(() => System.Runtime.InteropServices.CollectionsMarshal.SetCount(l, -1));
            System.Runtime.InteropServices.CollectionsMarshal.SetCount(l, 6); string s9 = Items(l); // yeni elemanlar: eski degerler mi sifir mi?
            return Par.J(Q(() => Items(g)), Q(() => l[1]), Q(() => Items(sl)), Q(() => Items(sl2)), Q(() => Items(sl3)), Q(() => s1), Q(() => s2), Q(() => s3), Q(() => s4), Q(() => s5), Q(() => s6), Q(() => s7), Q(() => Items(copy)), Q(() => arr[0]), Q(() => l[0]), Q(() => ro.Count), Q(() => Items(ins)), Q(() => s8), Q(() => e1), Q(() => s9), Q(() => l.Capacity >= 6), P(() => l[5..3]), P(() => l[..100]), Q(() => Items(l[..0])), Q(() => Items(l[0..0])), P(() => l.Slice(5, 5)), P(() => l.Slice(-1, 1)), Q(() => Items(l.Slice(6, 0))), P(() => l.Slice(7, 0)), Q(() => Items(new List<int>().GetRange(0, 0))), P(() => new List<int>().GetRange(0, 1)), Q(() => Items(l.GetRange(l.Count, 0))));
        }

        static string ListStructElements()
        {
            var l = new List<Pt> { new Pt { X = 1, Y = 2 }, new Pt { X = 3, Y = 4 } };
            l[0].Mutate(); // kopya uzerinde: etkisiz
            var tmp = l[1]; tmp.Mutate(); // kopya
            var arr = new Pt[] { new Pt { X = 1 } }; arr[0].Mutate(); // dizi: yerinde
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l); span[0].Mutate(); // span: yerinde
            ref var r = ref span[1]; r.Y = 500;
            var pt = new Pt { X = 7 }; var box = (object)pt; pt.X = 8; var unbox = (Pt)box; ((Pt)box).Mutate(); // kutu kopyasi
            var ro = new ReadOnlyPt(5); ro.Try(); // readonly alan: kopyada
            var inStruct = new Pt { X = 1 }; InMutate(in inStruct); // in: defensive copy
            var refStruct = new Pt { X = 1 }; RefMutate(ref refStruct);
            var kv = new KeyValuePair<int, Pt>(1, new Pt { X = 2 }); kv.Value.Mutate();
            var arr2 = new Pt[2]; for (int i = 0; i < 2; i++) arr2[i].X = i + 1; foreach (var p in arr2) { var q = p; q.Mutate(); }
            var dict = new Dictionary<int, Pt> { { 1, new Pt { X = 1 } } }; dict[1].Mutate();
            var tuple = (A: new Pt { X = 1 }, B: 2); tuple.A.Mutate(); // tuple alani: yerinde (ValueTuple field)
            Pt[] arr3 = { new Pt { X = 1 } }; ref Pt rp = ref arr3[0]; rp.Mutate(); ref readonly Pt rrp = ref arr3[0];
            var nullable = (Pt?)new Pt { X = 1 }; var nv = nullable.Value; nv.Mutate();
            var prop = new Holder { P = new Pt { X = 1 } }; prop.P.Mutate(); // property getter kopyasi
            var field = new Holder { F = new Pt { X = 1 } }; field.F.Mutate(); // alan: yerinde
            var sfield = Holder.S; Holder.S.Mutate(); // static alan: yerinde
            var ro2 = new Holder(); Holder.RO.Mutate(); // static readonly: kopya
            return Par.J(Q(() => l[0]), Q(() => l[1]), Q(() => tmp), Q(() => arr[0]), span[0].ToString(), Q(() => l[1].Y), Q(() => unbox.X), Q(() => pt.X), Q(() => ((Pt)box).X), Q(() => ro.V.X), Q(() => inStruct.X), Q(() => refStruct.X), Q(() => kv.Value.X), Q(() => Items(arr2)), Q(() => dict[1].X), Q(() => tuple.A.X), Q(() => arr3[0].X), rrp.X.ToString(), Q(() => nullable.Value.X), Q(() => nv.X), Q(() => prop.P.X), Q(() => field.F.X), Q(() => Holder.S.X), Q(() => Holder.RO.X), Q(() => sfield.X));
        }

        static void InMutate(in Pt p) { p.Mutate(); }
        static void RefMutate(ref Pt p) { p.Mutate(); }

        struct ReadOnlyPt { public readonly Pt V; public ReadOnlyPt(int x) { V = new Pt { X = x }; } public void Try() { V.Mutate(); } }
        class Holder { public Pt P { get; set; } public Pt F; public static Pt S = new Pt { X = 1 }; public static readonly Pt RO = new Pt { X = 1 }; }

        static string QueueBasics()
        {
            var q = new Queue<int>();
            string e1 = P(() => q.Dequeue()), e2 = P(() => q.Peek());
            bool t1 = q.TryDequeue(out int d1), t2 = q.TryPeek(out int p1);
            q.Enqueue(1); q.Enqueue(2); q.Enqueue(3);
            int pk = q.Peek(), dq = q.Dequeue(); bool c = q.Contains(2), c2 = q.Contains(1);
            var arr = q.ToArray(); var copy = new int[5]; q.CopyTo(copy, 1); string e3 = V(() => q.CopyTo(new int[1], 0)); string e4 = V(() => q.CopyTo(copy, 5)); string e5 = V(() => q.CopyTo(null, 0));
            q.Enqueue(4); string items = Items(q); q.Clear(); int cnt = q.Count;
            var q2 = new Queue<int>(new[] { 5, 6, 7 }); var q3 = new Queue<int>(10); var q4 = new Queue<string>(); q4.Enqueue(null); bool cn = q4.Contains(null); string e6 = P(() => new Queue<int>(-1).Count); string e7 = P(() => new Queue<int>((IEnumerable<int>)null).Count);
            var en = q2.GetEnumerator(); string e8 = P(() => en.Current); en.MoveNext(); q2.Enqueue(8); string e9 = V(() => en.MoveNext()); string e10 = P(() => en.Current);
            string e11 = V(() => { foreach (var x in q2) q2.Dequeue(); }); string e12 = V(() => { foreach (var x in q2) q2.Peek(); }); string e13 = V(() => { foreach (var x in q2) q2.TryDequeue(out _); }); string e14 = V(() => { foreach (var x in q2) q2.Contains(1); }); string e15 = V(() => { foreach (var x in q2) q2.TrimExcess(); }); string e16 = V(() => { foreach (var x in q2) q2.Clear(); });
            var q5 = new Queue<int>(); q5.Enqueue(1); q5.Dequeue(); string e17 = V(() => { foreach (var x in q5) q5.Enqueue(1); }); // bos kuyruk uzerinden foreach
            return Par.J(Q(() => e1), Q(() => e2), Q(() => t1), Q(() => d1), Q(() => t2), Q(() => p1), Q(() => pk), Q(() => dq), Q(() => c), Q(() => c2), Q(() => Items(arr)), Q(() => Items(copy)), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => items), Q(() => cnt), Q(() => Items(q2)), Q(() => q3.Count), Q(() => cn), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => e10), Q(() => e11), Q(() => e12), Q(() => e13), Q(() => e14), Q(() => e15), Q(() => e16), Q(() => e17), Q(() => q2.Count), Q(() => ((System.Collections.ICollection)q2).Count), Q(() => ((System.Collections.ICollection)q2).IsSynchronized), Q(() => q2.EnsureCapacity(100) >= 100), V(() => q2.TrimExcess()), P(() => q2.EnsureCapacity(-1)), Q(() => new Queue<int>(0).EnsureCapacity(0)), Q(() => new Queue<int>().EnsureCapacity(1)), Q(() => new Queue<int>().EnsureCapacity(5)), Q(() => new Queue<int>(new[] { 1, 2, 3 }).EnsureCapacity(0)));
        }

        static string QueueWraparound()
        {
            var q = new Queue<int>(4);
            for (int i = 1; i <= 4; i++) q.Enqueue(i);
            q.Dequeue(); q.Dequeue(); q.Enqueue(5); q.Enqueue(6); // head=2, wrap
            string s1 = Items(q) + "|" + Items(q.ToArray());
            var copy = new int[6]; q.CopyTo(copy, 1); string s2 = Items(copy);
            q.Enqueue(7); // buyume: wrap'li icerik duzgun kopyalanmali
            string s3 = Items(q);
            for (int i = 0; i < 3; i++) q.Dequeue(); q.Enqueue(8); q.Enqueue(9); q.Enqueue(10); q.Enqueue(11); q.Enqueue(12);
            string s4 = Items(q); int pk = q.Peek(); bool c = q.Contains(6);
            q.TrimExcess(); string s5 = Items(q);
            var big = new Queue<int>(); for (int i = 0; i < 1000; i++) { big.Enqueue(i); if (i % 3 == 0) big.Dequeue(); } int bsum = 0; foreach (var x in big) bsum += x;
            var alt = new Queue<int>(2); var log = new System.Text.StringBuilder(); for (int i = 0; i < 20; i++) { alt.Enqueue(i); if (i % 2 == 1) log.Append(alt.Dequeue()).Append(','); } log.Append('|').Append(Items(alt));
            var q2 = new Queue<string>(); for (int i = 0; i < 5; i++) q2.Enqueue("s" + i); q2.Dequeue(); q2.Dequeue(); q2.Enqueue("s5"); q2.Enqueue("s6"); q2.Enqueue("s7"); q2.Clear(); q2.Enqueue("new"); 
            var q3 = new Queue<int>(3); q3.Enqueue(1); q3.Enqueue(2); q3.Enqueue(3); q3.Dequeue(); q3.Enqueue(4); q3.Dequeue(); q3.Enqueue(5); q3.Dequeue(); q3.Enqueue(6); q3.Enqueue(7); // buyume tam wrap'te
            return Par.J(Q(() => s1), Q(() => s2), Q(() => s3), Q(() => s4), Q(() => pk), Q(() => c), Q(() => s5), Q(() => big.Count), Q(() => bsum), Q(() => big.Peek()), Q(() => log.ToString()), Q(() => Items(q2)), Q(() => q2.Count), Q(() => Items(q3)), Q(() => q3.Count), Q(() => Items(q3.ToArray())), Q(() => q3.Peek()));
        }

        static string StackBasics()
        {
            var s = new Stack<int>();
            string e1 = P(() => s.Pop()), e2 = P(() => s.Peek());
            bool t1 = s.TryPop(out int p0), t2 = s.TryPeek(out int p1);
            s.Push(1); s.Push(2); s.Push(3);
            string fe = Items(s), ta = Items(s.ToArray()); int pk = s.Peek(), pp = s.Pop(); bool c = s.Contains(3), c2 = s.Contains(2);
            var copy = new int[5]; s.CopyTo(copy, 1); string e3 = V(() => s.CopyTo(new int[1], 0)); string e4 = V(() => s.CopyTo(copy, 5));
            var s2 = new Stack<int>(new[] { 1, 2, 3 }); string fe2 = Items(s2); int top = s2.Peek();
            var s3 = new Stack<string>(); s3.Push(null); bool cn = s3.Contains(null); string e5 = P(() => new Stack<int>(-1).Count);
            var en = s2.GetEnumerator(); string e6 = P(() => en.Current); en.MoveNext(); s2.Push(9); string e7 = V(() => en.MoveNext()); string e8 = P(() => en.Current);
            string e9 = V(() => { foreach (var x in s2) s2.Pop(); }); string e10 = V(() => { foreach (var x in s2) s2.Peek(); }); string e11 = V(() => { foreach (var x in s2) s2.TryPop(out _); }); string e12 = V(() => { foreach (var x in s2) s2.Clear(); }); string e13 = V(() => { foreach (var x in s2) s2.TrimExcess(); });
            s2.Push(1); s2.Push(2); var en2 = s2.GetEnumerator(); while (en2.MoveNext()) { } string e14 = P(() => en2.Current); ((System.Collections.IEnumerator)en2).Reset(); en2.MoveNext(); int cur = en2.Current;
            var big = new Stack<int>(); for (int i = 0; i < 1000; i++) big.Push(i); for (int i = 0; i < 500; i++) big.Pop(); int bsum = 0; foreach (var x in big) bsum += x;
            var ob = new Stack<object>(); ob.Push(1); ob.Push("x"); ob.Push(null); ob.Push(2.5);
            return Par.J(Q(() => e1), Q(() => e2), Q(() => t1), Q(() => p0), Q(() => t2), Q(() => p1), Q(() => fe), Q(() => ta), Q(() => pk), Q(() => pp), Q(() => c), Q(() => c2), Q(() => Items(copy)), Q(() => e3), Q(() => e4), Q(() => fe2), Q(() => top), Q(() => cn), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => e10), Q(() => e11), Q(() => e12), Q(() => e13), Q(() => e14), Q(() => cur), Q(() => big.Count), Q(() => bsum), Q(() => big.Peek()), Q(() => Items(ob)), Q(() => ((System.Collections.ICollection)ob).Count), Q(() => s.Count), Q(() => s2.EnsureCapacity(50) >= 50), P(() => s2.EnsureCapacity(-1)), Q(() => new Stack<int>(0).EnsureCapacity(0)), Q(() => new Stack<int>().EnsureCapacity(1)), Q(() => new Stack<int>().EnsureCapacity(5)), V(() => s2.TrimExcess()), Q(() => Items(s2)));
        }
    }
}
