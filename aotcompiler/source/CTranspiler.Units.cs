using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitoyEngine.Language
{
    // Birim tabanli emisyon (docs/registry-removal.md, B adimi): her tip bir .h/.c cifti, paylasimli parcalar
    // gen_shared.h / gen_misc.c / gen_strings.c / gen_meta.c. Dosya icerigi yalniz o birime bagli (global sira yok)
    // -> degismeyen dosya yeniden yazilmaz, ninja yalniz degiseni derler. Tek dosya modu = ayni parcalarin
    // birlestirilmesi (selftest / wasm).
    public static partial class CTranspiler
    {
        sealed class CUnit
        {
            public string Key;                               // CName(tip) ya da gen_* adi
            public Primitive Prim;                           // tip birimi; paylasimlilarda null
            public readonly StringBuilder H = new StringBuilder();   // header: layout + extern'ler + prototipler
            public readonly StringBuilder Pre = new StringBuilder(); // .c on-bolumu: statik tanimlar, tentative bildirimler, trampoline'ler
            public readonly StringBuilder C = new StringBuilder();   // .c govde: descriptor'lar, fonksiyonlar, tablolar
            public readonly HashSet<string> ValueDeps = new HashSet<string>(); // header'in #include etmesi gereken birimler (deger olarak gomulen struct'lar)
            public string FileBase => Key.StartsWith("gen_", StringComparison.Ordinal) ? Key : "T_" + (Key.Length <= 100 ? Key : Key.Substring(0, 80) + "_" + Fnv64(Key).ToString("x16"));
        }

        static readonly Dictionary<string, CUnit> units = new Dictionary<string, CUnit>();
        static CUnit sharedUnit, metaUnit;
        const string SharedKey = "gen_shared", MetaKey = "gen_meta";

        static bool HasUnit(Primitive p) => p != null && (IsEmittableModel(p) || (p.IsEnum && typeIndex.ContainsKey(p)));
        static CUnit UnitOf(Primitive p)
        {
            if (!HasUnit(p)) return sharedUnit;
            var key = CName(p.Name);
            if (!units.TryGetValue(key, out var u)) units[key] = u = new CUnit { Key = key, Prim = p };
            return u;
        }
        static CUnit UnitOfCode(Code c) => UnitOf(c.Owner);

        // ---- emisyon: eski tek-dosya sirasiyla ayni parcalar, birimlere dagitilmis ----
        static void BuildUnits(Context ctx)
        {
            EmitCtx = ctx;
            units.Clear();
            sharedUnit = new CUnit { Key = SharedKey };
            metaUnit = new CUnit { Key = MetaKey };
            CollectStrings(ctx);

            foreach (var c in ctx.AllCodes) // NativeHeader'lar (extra include/typedef vb.) paylasimli header'a
                if (IsEmittableCode(c) && !string.IsNullOrEmpty(c.NativeHeader))
                    sharedUnit.H.Append(c.NativeHeader + "\n");
            sharedUnit.H.Append("void vmint_enter_virtual(GCHeader *self, unsigned long long rootHash, DeSlot *a, DeSlot *r);\n");
            sharedUnit.H.Append("void vmint_enter_delegate(GCHeader *closure, DeSlot *a, DeSlot *r);\n");
            sharedUnit.H.Append($"extern MethodInfo {MiscMethodsSym}[];\n");

            // yerlesimler (+ header deger bagimliliklari: degerle gomulen struct alanlari)
            foreach (var p in ctx.AllPrimitives)
            {
                if (!IsEmittableModel(p) || p.IsDelegate) continue;
                var u = UnitOf(p);
                u.H.Append(EmitStruct(p));
                foreach (var f in Hierarchy.AllFields(p))
                    if (f.Type.Type == PrimitiveType.Model && f.Type.IsStruct && HasUnit(f.Type) && f.Type != p)
                        u.ValueDeps.Add(CName(f.Type.Name));
            }
            // statik alanlar: tanim sahibinin .c'sinde, extern header'da (C sifir-init)
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                    foreach (var f in p.StaticFields)
                    {
                        var u = UnitOf(p);
                        u.H.Append($"extern {CType(f.Type)} {StaticSym(f)};\n");
                        u.Pre.Append($"{CType(f.Type)} {StaticSym(f)};\n");
                    }
            // prototipler (vtable/itable fonksiyon adresi icerir -> descriptor'lardan once)
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c))
                {
                    var u = UnitOfCode(c);
                    if (c.IsExternal && NeedsPInvokeMarshal(c))
                        u.H.Append(EmitPInvokeWrapper(c)); // string <-> const char* (LPUTF8Str) sarmalayici (static inline)
                    else
                    {
                        // Host'un hic cagirmadigi extern'ler ZAYIF: metot tablosu adresini alir, tanimi yoksa 0 (modul load'da
                        // "uygulanmamis"); host'un cagirdiklari guclu kalir -> eksik tanim hala link hatasi.
                        var linkage = !c.IsExternal ? "" : !hostReferenced.Contains(c) ? "extern DIGITOYENGINE_WEAK " : "extern ";
                        u.H.Append(linkage + Prototype(c) + ";\n");
                    }
                }
            // tentative bildirimler (.c ici: descriptor tanimdan once adres alir)
            foreach (var p in ctx.AllPrimitives)
                if (HasReflectionMembers(p))
                    UnitOf(p).Pre.Append($"static DigitoyEngineMember {CName(p.Name)}_members[{ReflectionMemberCount(p)}];\n");
            foreach (var kv in typeMethods)
                UnitOf(kv.Key).H.Append($"extern MethodInfo {kv.Value.sym}[{kv.Value.count}];\n");
            foreach (var p in ctx.AllPrimitives)
                if ((IsEmittableModel(p) || p.IsEnum) && typeIndex.ContainsKey(p) && EmittableAttrs(p.Attributes).Count > 0)
                    UnitOf(p).Pre.Append($"static DeAttr {CName(p.Name)}_tattrs[{EmittableAttrs(p.Attributes).Count}];\n");
            // trampoline'ler: .c on-bolumu (descriptor / metot tablosu adres alir)
            EmitTrampolinesInto(ctx, c => (methodTable.TryGetValue(c, out var ts) && ts == MiscMethodsSym ? metaUnit : UnitOfCode(c)).Pre, p => UnitOf(p).Pre);

            // ---- descriptor'lar ----
            // Acik generic tanimlar (boyutsuz) ve typeof(T[]) dizi descriptor'lari: paylasimli (nadiren degisir).
            foreach (var tmpl in reflectedGenericDefs.Values)
            {
                int gflags = 64 | (tmpl.IsStruct ? 1 : 0) | (tmpl.IsInterface ? 2 : 0) | (tmpl.IsDelegate ? 4 : 0) | 16;
                sharedUnit.H.Append($"extern Type {TypeSym(tmpl)};\n");
                sharedUnit.C.Append($"Type {TypeSym(tmpl)} = {{ 0, 0, 0, 1, &vmobject_type, &{strPool[tmpl.Display]}, 0, 0, 0, 0, 0, .hash = {Fnv64(tmpl.Name)}ull, .flags = {gflags}, .cilattrs = {tmpl.CilAttributes}u }};\n");
            }
            foreach (var valueStruct in reflectedValueStructs.Values)
                UnitOf(valueStruct).H.Append($"extern Type {CName(valueStruct.Name)}_type;\n");
            foreach (var kv in structArrayTypes)
                UnitOf(kv.Value).H.Append($"extern Type arr_{kv.Key}_type;\n");
            foreach (var p in ctx.AllPrimitives)
            {
                if (!IsEmittableModel(p) || p.IsStruct || p == Primitive.Object || p == Primitive.String) continue;
                var u = UnitOf(p);
                u.H.Append($"extern Type {CName(p.Name)}_type;\n");
                if (!p.IsDelegate && !p.IsInterface && FindFinalizerDeclarer(ctx, p) == p)
                    u.H.Append($"void digitoyengine_fin_{CName(p.Name)}(GCHeader* o);\n"); // turemis tiplerin fin zinciri cagirir
                try { u.C.Append(p.IsDelegate ? EmitDelegateType(p) : EmitTraceAndType(p)); }
                catch (Exception e) { throw new Exception($"tip emisyonu basarisiz: {p.Name} (parent {p.Parent?.Name}, iface [{string.Join(",", p.Interfaces.Select(i => i.Name))}], args [{string.Join(",", p.TypeArguments.Select(a => a.Name))}]): {e.Message}", e); }
            }
            foreach (var p in ctx.AllPrimitives) // enum descriptor'lari (boxing/typeof; C tarafinda deger duz int kalir)
                if (p.IsEnum && typeIndex.ContainsKey(p))
                {
                    var u = UnitOf(p);
                    u.H.Append($"extern Type {CName(p.Name)}_type;\n");
                    u.H.Append(EnumBoxInline(p));
                    u.C.Append(EmitEnumReflection(p));
                }
            foreach (var valueStruct in reflectedValueStructs.Values)
                UnitOf(valueStruct).C.Append(EmitValueStructType(valueStruct));
            foreach (var elem in structArrayTypes.Values) // ref tasiyan struct eleman dizileri (GC eleman tarama)
                UnitOf(elem).C.Append(EmitStructArrayType(elem));
            foreach (var array in reflectedArrays.Values)
            {
                sharedUnit.H.Append($"extern Type {TypeSym(array)};\n");
                sharedUnit.C.Append($"Type {TypeSym(array)} = {{ 0, 0, sizeof(VmArray), 0, {ArrayBaseSym()}, &{strPool[array.Display]}, 0, 0, 0, 0, 0{TypeMetaTail(array)}, .flags = DIGITOYENGINE_TYPE_ARRAY, .elem_type = {ExportTypeSym(array.ElementType)}, .rank = {Math.Max(1, array.ArrayRank)} }};\n");
            }
            // tahsis yardimcilari: header'da inline (extern X_type'tan sonra)
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p) && !p.IsStruct && !p.IsInterface && !p.IsDelegate)
                    UnitOf(p).H.Append(EmitConstructor(p));

            // ---- fonksiyonlar ----
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c) && !c.IsExternal) // IsExternal: govdesi baska yerde (runtime/el yazimi C) -> sadece bildirim
                {
                    var sb = UnitOfCode(c).C;
                    try { sb.Append(EmitFunction(c) + "\n"); }
                    catch (Exception ex)
                    {
                        // Transpile sirasinda cozulemeyen govde: tum pipeline'i dusurme, fonksiyonu stub'la.
                        c.UntranslatableReason = $"transpile: {ex.Message}";
                        Console.Error.WriteLine($"[transpile-stub] {c.EncodeName()}: {ex.Message}");
                        if (Environment.GetEnvironmentVariable("STUBTRACE") != null)
                            Console.Error.WriteLine(ex.StackTrace);
                        c.Operations.Clear();
                        try { sb.Append(EmitFunction(c) + "\n"); }
                        catch (Exception ex2) { throw new Exception($"EmitFunction {c.EncodeName()}: {ex2.Message}", ex2); }
                    }
                }
            foreach (var p in ctx.AllPrimitives)
                if (HasReflectionMembers(p))
                    UnitOf(p).C.Append(EmitReflectionMembers(p));
            foreach (var p in ctx.AllPrimitives) // tip attribute tablolarinin tanimi (fonksiyonlardan sonra: ctor cagrilari)
                if ((IsEmittableModel(p) || p.IsEnum) && typeIndex.ContainsKey(p))
                    UnitOf(p).C.Append(EmitAttrTable($"{CName(p.Name)}_tattrs", p.Attributes));
            EmitMethodTablesInto(c => UnitOfCode(c).C, metaUnit.C);
            metaUnit.C.Append(EmitMetaTables());
            metaUnit.C.Append(EmitInit(ctx));
        }

        // Header'da `struct X` olarak gecen tum adlar icin ileri bildirim (pointer kullanimi tam tanim istemez;
        // deger bagimliliklari ValueDeps ile #include edilir).
        static readonly Regex StructRefRx = new Regex(@"\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        static string ForwardDecls(string headerText, string self)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in StructRefRx.Matches(headerText))
                if (m.Groups[1].Value != self) names.Add(m.Groups[1].Value); // runtime struct'lari icin yinelenen ileri bildirim zararsiz
            return string.Concat(names.Select(n => $"struct {n};\n"));
        }

        // Tek dosya: paylasimli header + birim header'lari (deger bagimliligi sirasinda) + string havuzu + govdeler + meta.
        public static string TranspileProgram(Context ctx)
        {
            BuildUnits(ctx);
            var sb = new StringBuilder();
            sb.Append("#include \"vmrt.h\"\n\n");
            sb.Append(sharedUnit.H);
            var ordered = OrderedTypeUnits();
            foreach (var u in ordered) sb.Append(ForwardDecls(u.H.ToString(), u.Key));
            sb.Append(ForwardDecls(sharedUnit.H.ToString(), SharedKey));
            foreach (var u in ordered) sb.Append(u.H).Append('\n');
            foreach (var smp in shapeSamples) sb.Append($"void {ThunkSym(ShapeKey(smp))}(const void *fn, DeSlot *a, DeSlot *r);\n"); // metot tablolari meta'dan once adres alir
            sb.Append(EmitStringPool()).Append('\n');
            foreach (var u in ordered) sb.Append(u.Pre).Append(u.C).Append('\n');
            sb.Append(sharedUnit.Pre).Append(sharedUnit.C).Append('\n');
            sb.Append(metaUnit.Pre).Append(metaUnit.C);
            return sb.ToString();
        }

        static List<CUnit> OrderedTypeUnits()
        {
            var result = new List<CUnit>();
            var done = new HashSet<string>();
            void Visit(CUnit u)
            {
                if (!done.Add(u.Key)) return;
                foreach (var d in u.ValueDeps.OrderBy(x => x, StringComparer.Ordinal))
                    if (units.TryGetValue(d, out var du)) Visit(du);
                result.Add(u);
            }
            foreach (var u in units.Values.OrderBy(x => x.Key, StringComparer.Ordinal)) Visit(u);
            return result;
        }

        // ---- cok dosya: her birim .h/.c; .c kendi extern'lerini (string havuzu, thunk) ve include'larini yazar ----
        public sealed class GeneratedFile { public string Name; public string Content; public bool Compile; }

        static readonly Regex IdentRx = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
        static readonly Regex StrSymRx = new Regex(@"_strpool_[0-9a-f]{16}(?:_[0-9]+)?", RegexOptions.Compiled);
        static readonly Regex ThunkSymRx = new Regex(@"de_thunk_[0-9a-f]{16}", RegexOptions.Compiled);

        // Govdede gecen birim adlari: tanimlayici icindeki her `_` sinirli alt dizi (arr_X_type, New_X, X_methods, X ...).
        // Fazla eslesme zararsiz (gereksiz include); eksik eslesme derleme hatasi -> kapsayici tarama.
        static HashSet<string> ReferencedUnits(string text, HashSet<string> keys, string self)
        {
            var found = new HashSet<string>();
            var lookup = keys.GetAlternateLookup<ReadOnlySpan<char>>();
            var span = text.AsSpan();
            foreach (var m in IdentRx.EnumerateMatches(span))
            {
                var id = span.Slice(m.Index, m.Length);
                int n = id.Length;
                for (int s = 0; s < n; s++)
                {
                    if (s != 0 && id[s - 1] != '_') continue;
                    for (int e = s + 1; e <= n; e++)
                    {
                        if (e != n && id[e] != '_') continue;
                        if (lookup.TryGetValue(id.Slice(s, e - s), out var key) && key != self) found.Add(key);
                    }
                }
            }
            return found;
        }

        public static List<GeneratedFile> TranspileFiles(Context ctx, string extraMainSource = null)
        {
            BuildUnits(ctx);
            var files = new List<GeneratedFile>();
            var keys = new HashSet<string>(units.Keys, StringComparer.Ordinal);
            var fileOf = units.Values.ToDictionary(u => u.Key, u => u.FileBase);

            // gen_shared.h
            var sh = new StringBuilder();
            sh.Append("#pragma once\n#include \"vmrt.h\"\n");
            sh.Append(ForwardDecls(sharedUnit.H.ToString(), SharedKey));
            sh.Append(sharedUnit.H);
            files.Add(new GeneratedFile { Name = "gen_shared.h", Content = sh.ToString() });

            foreach (var u in units.Values)
            {
                var h = new StringBuilder();
                h.Append("#pragma once\n#include \"gen_shared.h\"\n");
                foreach (var d in u.ValueDeps.OrderBy(x => x, StringComparer.Ordinal))
                    if (fileOf.TryGetValue(d, out var df)) h.Append($"#include \"{df}.h\"\n");
                var htext = u.H.ToString();
                h.Append(ForwardDecls(htext, u.Key));
                h.Append(htext);
                files.Add(new GeneratedFile { Name = u.FileBase + ".h", Content = h.ToString() });
                files.Add(new GeneratedFile { Name = u.FileBase + ".c", Content = BodyFile(u, keys, fileOf, u.FileBase + ".h"), Compile = true });
            }
            files.Add(new GeneratedFile { Name = "gen_misc.c", Content = BodyFile(sharedUnit, keys, fileOf, "gen_shared.h"), Compile = true });
            files.Add(new GeneratedFile { Name = "gen_strings.c", Content = "#include \"gen_shared.h\"\n" + EmitStringPool(), Compile = true });
            // gen_meta: tum header'lar (tip tablosu her tipi adresler) + metot/thunk/init
            var meta = new StringBuilder();
            meta.Append("#include \"gen_shared.h\"\n");
            foreach (var kv in fileOf.OrderBy(kv => kv.Key, StringComparer.Ordinal)) meta.Append($"#include \"{kv.Value}.h\"\n");
            meta.Append(ExternLines(metaUnit.Pre.ToString() + metaUnit.C.ToString()));
            meta.Append(metaUnit.Pre).Append(metaUnit.C);
            if (extraMainSource != null) meta.Append(extraMainSource);
            files.Add(new GeneratedFile { Name = "gen_meta.c", Content = meta.ToString(), Compile = true });
            return files;
        }

        static string ExternLines(string body)
        {
            var sb = new StringBuilder();
            var strs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in StrSymRx.Matches(body)) strs.Add(m.Value);
            foreach (var s in strs) sb.Append($"extern VmString {s};\n");
            var thunks = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in ThunkSymRx.Matches(body)) thunks.Add(m.Value);
            foreach (var t in thunks) sb.Append($"void {t}(const void *fn, DeSlot *a, DeSlot *r);\n");
            return sb.ToString();
        }

        static string BodyFile(CUnit u, HashSet<string> keys, Dictionary<string, string> fileOf, string ownHeader)
        {
            var body = u.Pre.ToString() + u.C.ToString();
            var sb = new StringBuilder();
            sb.Append($"#include \"{ownHeader}\"\n");
            foreach (var k in ReferencedUnits(body, keys, u.Key).OrderBy(x => x, StringComparer.Ordinal))
                sb.Append($"#include \"{fileOf[k]}.h\"\n");
            sb.Append(ExternLines(body));
            sb.Append(body);
            return sb.ToString();
        }

        // Dizine yaz: icerik ayniysa dokunma (mtime korunur -> ninja derlemez); artik uretilmeyen T_*/gen_* dosyalarini sil.
        // Donus: derlenecek .c dosyalarinin tam yollari.
        public static List<string> WriteFiles(string dir, List<GeneratedFile> files, out int written, out int unchanged)
        {
            Directory.CreateDirectory(dir);
            written = unchanged = 0;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<string>();
            foreach (var f in files)
            {
                var path = Path.Combine(dir, f.Name);
                keep.Add(f.Name);
                if (f.Compile) sources.Add(path);
                if (File.Exists(path) && File.ReadAllText(path) == f.Content) { unchanged++; continue; }
                File.WriteAllText(path, f.Content);
                written++;
            }
            foreach (var old in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(old);
                if ((name.StartsWith("T_", StringComparison.Ordinal) || name.StartsWith("gen_", StringComparison.Ordinal)) && !keep.Contains(name)
                    && (name.EndsWith(".c") || name.EndsWith(".h")))
                    File.Delete(old);
            }
            return sources;
        }
    }
}
