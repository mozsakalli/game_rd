using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language.Trim
{
    // Trim analizinin ciktisi: emisyon YALNIZ bunu sorar (CTranspiler/Meta hicbir yerde profile dogrudan bakmaz).
    // Full profilde her sorgu "canli/hepsi" doner -> cikti bugunkuyle bire bir ayni.
    public sealed class TrimResult
    {
        public readonly TrimProfile Profile;
        public readonly TrimRoots Roots;
        internal readonly HashSet<Code> LiveCodes = new HashSet<Code>();
        internal readonly HashSet<Primitive> LiveTypes = new HashSet<Primitive>();
        internal readonly HashSet<Primitive> Instantiated = new HashSet<Primitive>(); // RTA: New/Box/Activator ile dogan tipler
        internal readonly HashSet<Code> Stubbed = new HashSet<Code>();                 // [DevOnly]: govdesiz emit (lean)
        internal Code CompletedTaskGetter;                                              // Task donuslu stub govdesi icin

        // [DevOnly] metot lean'de govdesiz: void -> bos, Task -> CompletedTask, diger -> default.
        public bool IsStubbed(Code c) => Stubbed.Contains(c);
        internal readonly Dictionary<Primitive, MemberKinds> Facets = new Dictionary<Primitive, MemberKinds>();
        internal readonly Dictionary<Code, Code> CodeParent = new Dictionary<Code, Code>();      // rapor: ilk erisim yolu
        internal readonly Dictionary<Primitive, string> FacetWhy = new Dictionary<Primitive, string>(); // rapor: kok nedeni
        internal readonly Dictionary<Primitive, string> TypeWhy = new Dictionary<Primitive, string>();   // rapor: tip canlilik nedeni (ilk isaretleyen)

        public TrimResult(TrimProfile profile, TrimRoots roots) { Profile = profile; Roots = roots; }

        // "Her sey canli": analiz yapilmadan Full profil (selftest / modul derleme yollari).
        public static TrimResult Everything => new TrimResult(TrimProfile.Full, new TrimRoots());

        // Kod govdesi emit edilsin mi (IsEmittableCode'un USTUNE eklenen filtre).
        public bool IsCodeLive(Code c) => !Profile.TrimCode || LiveCodes.Contains(c);
        public bool IsTypeLive(Primitive p) => !Profile.TrimCode || LiveTypes.Contains(p);

        // Tip icin emit edilecek reflection meta kumesi.
        public MemberKinds Reflection(Primitive p)
        {
            if (!Profile.TrimMeta) return MemberKinds.All;
            return Facets.TryGetValue(p, out var k) ? k : MemberKinds.None;
        }
        public bool Has(Primitive p, MemberKinds k) => (Reflection(p) & k) != 0;

        // MethodInfo kaydi: modul host'ta her kod tam kayit; aksi halde yalniz reflection koklerinin ctor/metotlari tam
        // (+ Props facet'li tipin get_/set_ erisimcileri: PropertyInfo.GetValue/SetValue = Invoke, thunk ister).
        public MethodRecordKind MethodRecord(Code c)
        {
            if (Profile.ModuleHost) return MethodRecordKind.Full;
            if (c.Owner != null)
            {
                var k = Reflection(c.Owner);
                bool ctor = c.SimpleName == "ctor";
                if (ctor ? (k & MemberKinds.Ctors) != 0 : (k & MemberKinds.Methods) != 0) return MethodRecordKind.Full;
                if ((k & MemberKinds.Props) != 0 && Accessors.Contains(c)) return MethodRecordKind.Full;
            }
            return MethodRecordKind.Trace;
        }
        internal readonly HashSet<Code> Accessors = new HashSet<Code>(); // Props facet'li tiplerin get_/set_ Code'lari
        public bool NeedsThunk(Code c) => MethodRecord(c) == MethodRecordKind.Full;
        public bool NeedsVTramp(Code c) => Profile.ModuleHost;
        public bool NeedsDTramp(Primitive p) => Profile.ModuleHost;
        // Tip adlari HER ZAMAN yazilir: stack trace/exception metni ("Sahip.Metot", "List<?>") kullanici onceligi; adlar
        // zaten UTF-16 tekil havuz + generic'lerde lazy (A2). Trim'in konusu meta tablolari, ad degil.
        public bool NeedsTypeName(Primitive p) => true;
    }

    // Erisilebilirlik + reflection facet analizi. Kaynak gercegi: ctx (Resolver sonrasi, monomorph bitmis).
    public static class TrimAnalysis
    {
        public static TrimResult Run(Context ctx, TrimProfile profile, TrimRoots roots)
        {
            var r = new TrimResult(profile, roots);
            var all = ctx.AllCodes.ToList();
            CollectAutoRoots(ctx, roots);
            ResolveComponentRoots(ctx, roots, r);
            PropagateFacets(ctx, r);
            foreach (var kv in r.Facets)
                if ((kv.Value & MemberKinds.Props) != 0)
                    foreach (var pr in kv.Key.Properties)
                    {
                        if (pr.Getter != null) r.Accessors.Add(pr.Getter);
                        if (pr.Setter != null) r.Accessors.Add(pr.Setter);
                    }
            Reach(ctx, all, r);
            return r;
        }

        // ---- reflection kokleri ----

        // Koddan otomatik kokler: [Preserve] tasiyan tip/uye, typeof(T) hedefleri (Name), AddComponent<T>/New hedefleri degil
        // (New dogrudan cagri -> kod grafi). Attribute tipleri: emit edilen her attribute'un tipi Ctors + Fields + Props ister.
        static void CollectAutoRoots(Context ctx, TrimRoots roots)
        {
            foreach (var p in ctx.AllPrimitives)
            {
                if (p.Type != PrimitiveType.Model) continue;
                if (HasAttr(p.Attributes, "Preserve")) roots.AddReflection(p, MemberKinds.All);
                foreach (var a in p.Attributes) AttrRoot(roots, a);
                foreach (var f in p.Fields) foreach (var a in f.Attributes) AttrRoot(roots, a);
                foreach (var pr in p.Properties) foreach (var a in pr.Attributes) AttrRoot(roots, a);
                if (p.IsEnum) roots.AddReflection(p, MemberKinds.Name); // Enum.ToString/Parse uye adlari
            }
            foreach (var c in ctx.AllCodes)
            {
                foreach (var a in c.Attributes) AttrRoot(roots, a);
                if (HasAttr(c.Attributes, "Preserve") && c.Owner != null) roots.AddReflection(c.Owner, MemberKinds.Methods | MemberKinds.Ctors);
                List<Primitive> lastTypeOf = null;
                var ops = c.Operations;
                for (int oi = 0; oi < ops.Count; oi++)
                {
                    var op = ops[oi];
                    if ((op.Type == OpType.TypeOf || op.Type == OpType.TypeOfRt) && op.PrimitiveRef != null && op.PrimitiveRef.Type == PrimitiveType.Model)
                    {
                        // CANON: typeof(T) canon govdede -> her exact orneklemenin sozluk hedefi (GetSystem<LayoutSystem> gibi) kok olur
                        int slot = op.Type == OpType.TypeOfRt ? Lower.HiddenSlot(ops, oi, DictSlotKind.TypeHandle) : -1;
                        lastTypeOf = slot >= 0 ? ExactTargets(ctx, c, slot).ToList() : new List<Primitive> { op.PrimitiveRef };
                        foreach (var tp in lastTypeOf) roots.AddReflection(tp, MemberKinds.Name);
                    }
                    // typeof(X) ... Activator.CreateInstance(Type): `new T()` klonu da bu sekle duser -> X'in ctor kaydi gerekir
                    if (op.Type == OpType.Call && op.Code?.Owner?.Name == "System.Activator" && lastTypeOf != null
                        && op.Code.Name.StartsWith("CreateInstance", StringComparison.Ordinal))
                        foreach (var tp in lastTypeOf) roots.AddReflection(tp, MemberKinds.Name | MemberKinds.Ctors);
                }
            }
        }
        // Canon kodun bir sozluk slotunun tum exact orneklemelerdeki hedef tipleri (canon'un ORNEKLEDIGI exact'ler)
        static IEnumerable<Primitive> ExactTargets(Context ctx, Code canonCode, int slot)
        {
            var owner = Context.DictOwnerOf(canonCode);
            IEnumerable<object> exacts = owner is Primitive p ? ctx.ExactsOf(p) : ctx.ExactCodesOf((Code)owner);
            foreach (var e in exacts)
                if (ctx.TryGetDictInstance(e, out var d) && slot < d.Targets.Count && d.Targets[slot] is Primitive tp && !Canon.ContainsCanon(tp))
                    yield return tp;
        }
        static void AttrRoot(TrimRoots roots, PrimitiveAttribute a)
        {
            if (a?.Type == null) return;
            roots.AddReflection(a.Type, MemberKinds.Name | MemberKinds.Ctors | MemberKinds.Fields | MemberKinds.Props);
        }
        static bool HasAttr(List<PrimitiveAttribute> attrs, string shortName)
            => attrs != null && attrs.Any(a => a.Type != null && ShortName(a.Type.Name) == shortName + "Attribute");

        // Kisa tip adi: "DigitoyEngine.SpriteRenderer" -> "SpriteRenderer"; nested "A+B"/"A/B" -> "B".
        public static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return full;
            int lt = full.IndexOf('<'); // generic mangled adda son '.' arg icinde olabilir
            var head = lt >= 0 ? full.Substring(0, lt) : full;
            int cut = Math.Max(head.LastIndexOf('.'), Math.Max(head.LastIndexOf('+'), head.LastIndexOf('/')));
            return cut >= 0 ? full.Substring(cut + 1) : full;
        }

        // Pak tip adlari -> Primitive: TypeCatalog kisa ad eslesmesi. Component turevi (DSCN) tam facet; [Serializable]
        // sinif (DOBJ: Font, SpriteTable, oyun [Asset] siniflari) Inline (ad+alanlar+ctor+attribute: Reflect.IsAssetClass
        // [Asset]'i okur, SerializedType alanlari gezer). Koddan referanssiz DOBJ tipleri (SpriteTable) yalniz buradan yasar.
        static void ResolveComponentRoots(Context ctx, TrimRoots roots, TrimResult r)
        {
            if (roots.ComponentNames.Count == 0) return;
            var byShort = new Dictionary<string, List<Primitive>>(StringComparer.Ordinal);
            foreach (var p in ctx.AllPrimitives)
            {
                if (p.Type != PrimitiveType.Model || p.IsInterface || p.IsGeneric || p.GenericTemplate != null || p.IsGenericParameter) continue;
                var s = ShortName(p.Name);
                if (!byShort.TryGetValue(s, out var l)) byShort[s] = l = new List<Primitive>();
                l.Add(p);
            }
            foreach (var name in roots.ComponentNames)
            {
                if (!byShort.TryGetValue(name, out var cands)) { roots.Notes.Add($"UYARI pak tipi kodda yok: {name}"); continue; }
                var comps = cands.Where(IsComponent).ToList();
                var assets = comps.Count > 0 ? new List<Primitive>() : cands.Where(c => !c.IsStruct && IsSerializable(c)).ToList();
                if (comps.Count == 0 && assets.Count == 0) { roots.Notes.Add($"UYARI pak tipi Component ya da [Serializable] degil: {name} ({cands.Count} aday)"); continue; }
                if (comps.Count + assets.Count > 1) roots.Notes.Add($"UYARI pak tipi belirsiz, hepsi kok: {name} -> {string.Join(", ", comps.Concat(assets).Select(c => c.Name))}");
                foreach (var p in comps) { roots.AddReflection(p, MemberKinds.Component); r.FacetWhy.TryAdd(p, "pak"); }
                foreach (var p in assets) { roots.AddReflection(p, MemberKinds.Inline); r.FacetWhy.TryAdd(p, "pak asset"); }
            }
        }
        static bool IsComponent(Primitive p)
        {
            for (var t = p; t != null; t = t.Parent)
                if (t.Name == "DigitoyEngine.Component") return true;
            return false;
        }

        // Facet yayilimi: kok -> base zinciri (ayni facet: serializer base alanlarini gezer), Fields -> alan tipleri
        // (inline [Serializable]/struct/enum: Inline; List<T>/T[]: eleman; Component/asset referansi: Name).
        static void PropagateFacets(Context ctx, TrimResult r)
        {
            var work = new Queue<Primitive>();
            void Add(Primitive p, MemberKinds k, string why)
            {
                if (p == null || k == MemberKinds.None) return;
                p = Unwrap(p);
                if (p == null || p.Type != PrimitiveType.Model) return;
                r.Facets.TryGetValue(p, out var cur);
                if ((cur | k) == cur) return;
                r.Facets[p] = cur | k;
                r.FacetWhy.TryAdd(p, why);
                work.Enqueue(p);
            }
            foreach (var kv in r.Roots.Reflection) Add(kv.Key, kv.Value, r.FacetWhy.TryGetValue(kv.Key, out var w) ? w : "kok");
            while (work.Count > 0)
            {
                var p = work.Dequeue();
                var k = r.Facets[p];
                if (p.Parent != null && p.Parent.Type == PrimitiveType.Model && p.Parent != Primitive.Object && p.Parent != Primitive.ValueType)
                    Add(p.Parent, k & ~MemberKinds.Ctors, p.Name + " base");
                if ((k & MemberKinds.Fields) == 0) continue;
                foreach (var f in p.Fields)
                {
                    var elem = ElementOf(f.Type);
                    if (elem != null && !f.Type.IsArray) Add(f.Type, MemberKinds.Name | MemberKinds.Ctors, p.Name + "." + f.Name + " List<T>"); // Activator.CreateInstance(List<T>)
                    var ft = Unwrap(elem ?? f.Type);
                    if (ft == null || ft.Type != PrimitiveType.Model) continue;
                    if (ft.IsEnum) Add(ft, MemberKinds.Name, p.Name + "." + f.Name);
                    else if (ft.IsStruct || IsSerializable(ft)) Add(ft, MemberKinds.Inline, p.Name + "." + f.Name);
                    else Add(ft, MemberKinds.Name, p.Name + "." + f.Name); // Component/asset/GameObject referansi
                }
            }
        }
        static bool IsSerializable(Primitive p) => (p.CilAttributes & 0x2000) != 0; // TypeAttributes.Serializable
        static Primitive Unwrap(Primitive p) => p == null ? null : (p.IsGenericParameter ? null : p);
        // List<T> / T[] -> T (serializer eleman tipini gezer)
        static Primitive ElementOf(Primitive t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.ElementType;
            if (t.InstantiatedFrom != null && t.TypeArguments.Count == 1 && ShortName(t.InstantiatedFrom.Name).StartsWith("List", StringComparison.Ordinal))
                return t.TypeArguments[0];
            return null;
        }

        // ---- kod erisilebilirligi (RTA-benzeri, konservatif) ----
        // Kokler: host girisleri + cctor'lar (full: hepsi eager; lean: yalniz dokunulan tipler) + reflection kokü tiplerin ctor/metotlari (facet'e gore)
        // + extern/native govdeler cagrildikca. Sanal: slot tabanli (vtable/itable) + imza eslesen alt tip override'lari (konservatif).
        static void Reach(Context ctx, List<Code> all, TrimResult r)
        {
            var byName = new Dictionary<string, List<Code>>();
            foreach (var c in all)
            {
                if (!byName.TryGetValue(c.SimpleName, out var l)) byName[c.SimpleName] = l = new List<Code>();
                l.Add(c);
            }
            var queue = new Queue<Code>();
            void Visit(Code target, Code from)
            {
                if (target == null || !r.CodeParent.TryAdd(target, from)) return;
                r.LiveCodes.Add(target);
                queue.Enqueue(target);
                if (target.IsSharedExact) Visit(target.Shared, target); // CANON: exact kabuk canli -> kodu tasiyan canon govde canli
            }
            // cctor koku: modul host'ta (full) HEPSI eager (modul kodu host'un her statigine dokunabilir). Kod trim'inde yalniz
            // DOKUNULAN tipin cctor'u canli: uyesi cagrilan / statik alanina erisilen / new'lenen / reflection koku. Referanssiz
            // plugin tipleri boylece hic dogmaz (cctor'lari da kosmaz). Sira yine EmitInit'in StaticDeps'i.
            bool lazyCctor = r.Profile.TrimCode;
            var cctorOf = new Dictionary<Primitive, Code>();
            foreach (var c in all)
                if (!c.IsExternal && c.SimpleName == "cctor" && c.Owner != null) cctorOf[c.Owner] = c;
            void Touch(Primitive p, Code from)
            {
                if (lazyCctor && p != null && cctorOf.TryGetValue(p, out var cc)) Visit(cc, from);
            }
            foreach (var e in r.Roots.Entry) { Visit(e, null); Touch(e?.Owner, null); }
            foreach (var c in all)
            {
                if (c.IsExternal) continue;
                if (c.SimpleName == "cctor") { if (!lazyCctor) Visit(c, null); }      // eager cctor (EmitInit hepsini cagirir)
                else if (c.SimpleName == "finalize") Visit(c, null);                   // digitoyengine_fin_X zinciri (tip canliysa)
            }
            foreach (var kt in ctx.ExceptionKindTypes) // runtime kind -> ctor (EmitInit DIGITOYENGINE_ex_kind_ctor)
                if (kt != null && ctx.FindMember(kt, "ctor", 0) is Code kctor) Visit(kctor, null);
            foreach (var m in Primitive.Object.VTable) Visit(m, null); // vmobject/vmstring vtable'lari runtime'da doldurulur
            foreach (var m in Primitive.String.VTable) Visit(m, null);
            foreach (var kv in r.Facets)
            {
                var k = kv.Value;
                if ((k & (MemberKinds.Ctors | MemberKinds.Methods | MemberKinds.Props | MemberKinds.Fields)) != 0) Touch(kv.Key, null); // reflection statik alana da erisebilir
                foreach (var c in all)
                {
                    if (c.Owner != kv.Key) continue;
                    bool ctor = c.SimpleName == "ctor";
                    if (ctor ? (k & MemberKinds.Ctors) != 0 : (k & (MemberKinds.Methods | MemberKinds.Props)) != 0) Visit(c, null);
                }
            }
            // Sanal dispatch: slot tabanli (vtable/itable) - Hierarchy'nin kurdugu tablolarla birebir; ad tabanli kural da
            // korunur (konservatif birlesim).
            // RTA: sanal hedef yalniz ORNEKLENMIS (New/Box/Activator/runtime) tiplerde cozulur. Ornekleme kaynaklari:
            // New/Box op'lari, Ctors facet'i (Activator), exception kind'lar, tum struct'lar (default ile dogar) ve corelib
            // (System.*: C runtime gc_alloc ile uretir - string/dizi/kutu/reflection wrapper'lari) - konservatif.
            var instantiated = new HashSet<Primitive>();
            var virtualTargets = new HashSet<Code>();
            void ResolveOn(Primitive s, Code target, Code from)
            {
                var owner = target.Owner;
                int slot = owner.VTable.IndexOf(target);
                if (slot < 0) return;
                if (owner.IsInterface)
                {
                    foreach (var kv in s.ITables) // CANON: canon govdedeki IEnumerable<__Canon> hedefi exact IEnumerable<string> itable'ina duser
                        if ((kv.Key == owner || (kv.Key.Canonical != null && kv.Key.Canonical == owner.Canonical)) && slot < kv.Value.Count) Visit(kv.Value[slot], from);
                }
                else if (IsSubtype(s, owner) && slot < s.VTable.Count) Visit(s.VTable[slot], from);
            }
            void Instantiate(Primitive p)
            {
                if (p == null || p.Type != PrimitiveType.Model || p.IsInterface || !instantiated.Add(p)) return;
                foreach (var t in virtualTargets) ResolveOn(p, t, null);
            }
            void VisitVirtual(Code target, Code from)
            {
                var owner = target.Owner;
                if (owner == null || !virtualTargets.Add(target)) return;
                foreach (var s in instantiated) ResolveOn(s, target, from);
                // imza tabanli kural (konservatif birlesim): yalniz ornekli sahiplerde
                if (byName.TryGetValue(target.SimpleName, out var cands))
                    foreach (var cand in cands)
                        if (cand != target && instantiated.Contains(cand.Owner) && IsSubtype(cand.Owner, owner) && Signature.Matches(cand, target)) Visit(cand, from);
            }
            // System.*: C runtime'in gc_alloc ile yarattigi tipler (string/dizi/kutu/reflection wrapper) — yalniz GENERIC OLMAYAN
            // tanimlar. Generic orneklemeleri (List<X>, Dictionary<K,V>) yalniz kodda New/Activator ile dogar; hepsini ornekli saymak
            // canon govdedeki sanal cagriyi (ICollection<__Canon>.Add) her exact'e dusurup olu plugin orneklemelerini canli tutuyordu.
            // Struct'lar: `default` ile dogar ama yalniz CANLI tipler (canli kodun yerel/alan/imza tipleri) — sabit nokta asagida.
            // Olu plugin'in struct enumerator'larini ornekli saymak canon sanal cagrilarini (IEnumerable<__Canon>) onlara dusuruyordu.
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsStruct && p.Name.StartsWith("System.", StringComparison.Ordinal) && p.InstantiatedFrom == null)
                    Instantiate(p);
            foreach (var kt in ctx.ExceptionKindTypes) Instantiate(kt);
            foreach (var kv in r.Facets) if ((kv.Value & MemberKinds.Ctors) != 0) Instantiate(kv.Key);
            Instantiate(Primitive.Object); Instantiate(Primitive.String);
            // CANON: canon govdedeki op'un sozluk hedefi, o canon sahibin TUM exact orneklemelerinde (New -> ornekli tip, FnPtr -> stub kodu)
            // Yalniz CANLI exact'lerin hedefi (olu Dictionary<string,M.X> exact'inin ValueCollection'i dogmamali); exact sonradan
            // canlanabilir -> (op, exact) ciftleri sabit noktada yeniden denenir (DrainDictOps).
            var dictOps = new List<(Code code, int slot, bool fn)>();
            var dictDone = new HashSet<(Code, int, object)>();
            bool DrainDictOps()
            {
                bool any = false;
                foreach (var (code, slot, fn) in dictOps)
                {
                    var owner = Context.DictOwnerOf(code);
                    IEnumerable<object> exacts = owner is Primitive op ? ctx.ExactsOf(op) : ctx.ExactCodesOf((Code)owner);
                    foreach (var e in exacts)
                    {
                        bool live = e is Primitive ep ? r.LiveTypes.Contains(ep) : r.LiveCodes.Contains((Code)e);
                        if (!live || !dictDone.Add((code, slot, e))) continue;
                        if (!ctx.TryGetDictInstance(e, out var d) || slot >= d.Targets.Count) continue;
                        var tgt = d.Targets[slot];
                        if (tgt is Primitive tp) { Instantiate(tp); MarkType(tp, r); }
                        else if (tgt is Code tc) Visit(tc, code);
                        any = true;
                    }
                }
                return any;
            }
            const string TaskName = "System.Threading.Tasks.Task";
            r.CompletedTaskGetter = ctx.TryGetPrimitive(TaskName, out var taskType) ? ctx.FindMember(taskType, "get_CompletedTask", 0) : null;
            while (queue.Count > 0 || InstantiateLiveStructs() || DrainDictOps())
            {
                if (queue.Count == 0) continue; // ornekleme/sozluk adimi yeni kod eklemedi -> kosulu yeniden degerlendir
                var c = queue.Dequeue();
                markWhy = "kod " + c.Owner?.Name + "." + (c.DisplayName ?? c.Name) + " [" + Path(r, c) + "]";
                if (c.Owner != null) MarkType(c.Owner, r);
                // imza/yerel tipleri: MethodTrace param_types/returnType descriptor'lari + C tip bildirimleri (descriptor trim)
                MarkType(c.ReturnType, r);
                foreach (var a in c.Arguments) MarkType(a.Type, r);
                foreach (var l in c.Locals) MarkType(l, r);
                if (r.Profile.TrimCode && HasAttr(c.Attributes, "DevOnly"))
                {
                    // govde gezilmez: cagirdiklari erisilemez; Task donusu icin CompletedTask canli kalir
                    r.Stubbed.Add(c);
                    if (c.ReturnType?.Name == TaskName) Visit(r.CompletedTaskGetter, c);
                    else if (c.ReturnType != null && c.ReturnType.Name.StartsWith(TaskName + "<", StringComparison.Ordinal))
                        throw new Exception($"[DevOnly] {c.Owner?.Name}.{c.DisplayName ?? c.Name}: Task<T> donusu desteklenmiyor (void/Task/default kullan)");
                    continue;
                }
                var ops = c.Operations;
                for (int oi = 0; oi < ops.Count; oi++)
                {
                    var op = ops[oi];
                    if (op.PrimitiveRef != null) MarkType(op.PrimitiveRef, r);
                    if (op.Field?.Owner != null)
                    {
                        MarkType(op.Field.Owner, r);
                        if (op.Field.IsStatic) Touch(op.Field.Owner, c); // statik alan erisimi = tip dokunuldu
                    }
                    bool isNew = op.Type == OpType.New || op.Type == OpType.NewRt, isBox = op.Type == OpType.Box || op.Type == OpType.BoxRt;
                    bool isDelNew = op.Type == OpType.DelegateNew || op.Type == OpType.DelegateNewRt;
                    if ((isNew || isBox) && op.PrimitiveRef != null) Instantiate(op.PrimitiveRef);
                    // CANON LOWERING: sozluk kenari = tuketicinin bitisik LdDictSlot operandi (New/Box: TypeHandle; DelegateNew: FnPtr)
                    if (op.Type == OpType.NewRt || op.Type == OpType.BoxRt) { int s = Lower.HiddenSlot(ops, oi, DictSlotKind.TypeHandle); if (s >= 0) dictOps.Add((c, s, false)); }
                    if (op.Type == OpType.DelegateNewRt) { int s = Lower.HiddenSlot(ops, oi, DictSlotKind.FnPtr); if (s >= 0) dictOps.Add((c, s, true)); }
                    if (isNew && op.PrimitiveRef != null) Touch(op.PrimitiveRef, c);
                    if (op.Code == null) continue;
                    Visit(op.Code, c);
                    Touch(op.Code.Owner, c); // uye cagrisi = tip dokunuldu (.NET: static metot/ctor girisi cctor tetikler)
                    if (isNew && op.Code.Owner != null) Instantiate(op.Code.Owner); // ctor cagrisi = ornekleme
                    if (op.Type == OpType.CallVirtual || (isDelNew && (op.Code.IsVirtual || op.Code.IsOverride)))
                        VisitVirtual(op.Code, c);
                }
            }
            bool InstantiateLiveStructs()
            {
                bool any = false;
                foreach (var p in r.LiveTypes.ToList())
                    if (p.Type == PrimitiveType.Model && p.IsStruct && !instantiated.Contains(p)) { Instantiate(p); any = true; }
                return any;
            }
            r.Instantiated.UnionWith(instantiated);
            // Facet'li tipler de canli (descriptor + GC trace)
            foreach (var p in r.Facets.Keys) MarkType(p, r);
            // Runtime'in dogrudan adresledigi tipler (EmitInit): exception kind'lar, NotImplementedException (AOT stub firlatmasi),
            // reflection wrapper descriptor'lari, System.Array (dizi base'i)
            foreach (var kt in ctx.ExceptionKindTypes) MarkType(kt, r);
            foreach (var n in new[] { "System.NotImplementedException", "System.Exception", "System.Type", "System.Reflection.FieldInfo", "System.Reflection.PropertyInfo",
                                      "System.Reflection.MethodInfo", "System.Reflection.ConstructorInfo", "System.Array" })
                if (ctx.TryGetPrimitive(n, out var rt)) MarkType(rt, r);
            // CANON: canli exact ornegin sozluk hedefleri (typeof/new/cast descriptor'lari, stub sahipleri) canli; sabit nokta (hedef exact yeni sozluk acabilir)
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var d in ctx.AllDictInstances)
                {
                    bool live = d.Exact is Primitive ep ? r.LiveTypes.Contains(ep) : r.LiveCodes.Contains((Code)d.Exact);
                    if (!live) continue;
                    foreach (var tgt in d.Targets)
                    {
                        if (tgt is Primitive tp) { if (!r.LiveTypes.Contains(tp)) { MarkType(tp, r); grew = true; } }
                        else if (tgt is Code tc && tc.Owner != null && !r.LiveTypes.Contains(tc.Owner)) { MarkType(tc.Owner, r); grew = true; }
                    }
                }
            }
        }
        [ThreadStatic] static string markWhy; // rapor: tip canlilik nedeni (ilk isaretleyen)
        static void MarkType(Primitive p, TrimResult r)
        {
            for (; p != null && r.LiveTypes.Add(p); p = p.Parent)
            {
                r.TypeWhy.TryAdd(p, markWhy ?? "?");
                var savedWhy = markWhy; markWhy = p.Name;
                if (p.ElementType != null) MarkType(p.ElementType, r);
                foreach (var i in p.Interfaces) MarkType(i, r);
                foreach (var a in p.TypeArguments) MarkType(a, r);
                if (p.Canonical != null) MarkType(p.Canonical, r); // exact canli -> yerlesim/trace sahibi canon da
                if (p.InstantiatedFrom != null) MarkType(p.InstantiatedFrom, r); // generic_def descriptor'u
                foreach (var f in p.Fields) MarkType(f.Type, r); // yerlesim: alan tipleri (struct degerle gomulu; ref alan descriptor'u reflection/trace)
                foreach (var f in p.StaticFields) MarkType(f.Type, r);
                markWhy = savedWhy;
            }
        }
        // rapor: kodun ilk erisim yolu (kok -> ... -> kod), en fazla 6 adim
        static string Path(TrimResult r, Code c)
        {
            var parts = new List<string>();
            for (var p = c; p != null && parts.Count < 6; p = r.CodeParent.TryGetValue(p, out var q) ? q : null)
                parts.Add($"{p.Owner?.Name}.{p.DisplayName ?? p.Name}");
            return string.Join(" <- ", parts);
        }
        // Alt tip testi (RTA hedef cozumu): monomorph SONRASI tipler exact'tir -> referans esitligi; paylasimli exact'ler canon
        // uzerinden (List<string> ~ List<__Canon>). Tanim esitligi YALNIZ hedef acik sablonsa. (Eski Definition karsilastirmasi
        // Dictionary<Char,Char> ~ Dictionary<int,X> sayip her orneklemeyi canli tutuyordu: Common.dll +900 KB.)
        static bool IsSubtype(Primitive t, Primitive of)
        {
            if (t == null || of == null) return false;
            for (var p = t; p != null; p = p.Parent)
            {
                if (SameType(p, of)) return true;
                foreach (var i in p.Interfaces)
                    if (SameType(i, of)) return true;
            }
            return false;
        }
        static bool SameType(Primitive a, Primitive b)
        {
            if (a == b) return true;
            if (a.Canonical != null && a.Canonical == b.Canonical) return true;
            return b.IsGeneric && a.Definition == b; // hedef sablon (acik): her ornegi kapsar
        }

        // ---- rapor (--trim-report) ----
        public static string Report(Context ctx, TrimResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"# trim raporu — profil {r.Profile.Name} (moduleHost={r.Profile.ModuleHost} trimMeta={r.Profile.TrimMeta} trimCode={r.Profile.TrimCode})\n\n");
            foreach (var n in r.Roots.Notes) sb.Append("- ").Append(n).Append('\n');
            sb.Append($"\n## kokler: {r.Roots.Entry.Count} giris, {r.Roots.ComponentNames.Count} pak component, {r.Roots.Reflection.Count} reflection tipi\n");
            foreach (var n in r.Roots.ComponentNames) sb.Append("  pak: ").Append(n).Append('\n');
            var codes = ctx.AllCodes.Where(c => c.GenericParameters.Count == 0).ToList();
            int live = codes.Count(c => r.LiveCodes.Contains(c));
            sb.Append($"\n## kod: {live} canli / {codes.Count} somut ({codes.Count - live} erisilemez); {r.Instantiated.Count} ornekli tip\n");
            sb.Append("\n## canli kod, sahip basina (en cok 60):\n");
            foreach (var g in codes.Where(c => r.LiveCodes.Contains(c)).GroupBy(c => c.Owner?.Name ?? "?").OrderByDescending(g => g.Count()).Take(60))
            {
                var first = g.First();
                sb.Append($"  {g.Count(),5}  {g.Key}  <- {Path(r, first)}\n");
            }
            foreach (var c in codes.Where(c => !r.LiveCodes.Contains(c) && !c.IsExternal).OrderBy(c => c.Owner?.Name).ThenBy(c => c.Name))
                sb.Append($"  dead: {c.Owner?.Name}.{c.DisplayName ?? c.Name}\n");
            // kodu olmayan canli tipler (yalniz descriptor): neden zinciri
            var codeOwners = new HashSet<Primitive>(codes.Where(c => r.LiveCodes.Contains(c) && c.Owner != null).Select(c => c.Owner));
            var onlyDesc = r.LiveTypes.Where(p => p.Type == PrimitiveType.Model && !codeOwners.Contains(p) && !p.IsGeneric && p.GenericTemplate == null).ToList();
            sb.Append($"\n## kodsuz canli tip (yalniz descriptor): {onlyDesc.Count}\n");
            foreach (var p in onlyDesc.OrderBy(p => p.Name).Take(400))
            {
                var chain = new List<string> { p.Name };
                var cur = p;
                for (int guard = 0; cur != null && guard < 6; guard++)
                {
                    if (!r.TypeWhy.TryGetValue(cur, out var w) || w == cur.Name) break;
                    chain.Add(w);
                    cur = ctx.TryGetPrimitive(w, out var cp) ? cp : null; // "kod X.Y" -> bulunamaz, zincir biter
                }
                sb.Append($"  {string.Join(" <- ", chain)}\n");
            }
            sb.Append($"\n## reflection facet: {r.Facets.Count} tip\n");
            foreach (var kv in r.Facets.OrderBy(kv => kv.Key.Name))
                sb.Append($"  {kv.Key.Name}: {kv.Value}  <- {(r.FacetWhy.TryGetValue(kv.Key, out var w) ? w : "?")}\n");
            return sb.ToString();
        }
    }
}
