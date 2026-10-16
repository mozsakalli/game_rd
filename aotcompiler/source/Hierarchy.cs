using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Kalitim pass'i (Resolver icinden, somutlastirma sabit noktasindan SONRA calisir):
    // 1) Alan duzlestirme: field.Index parent zinciri dahil global slot olur (parent alanlari once).
    //    C'de struct layout ayni sirayi kullanir -> Derived* prefix-uyumlu Base*.
    // 2) VTable kurulumu (.NET MethodTableBuilder kurali): parent vtable kopyalanir, IsVirtual yeni slot acar, IsOverride
    //    parent'ta AD + SUBSTITUTE EDILMIS PARAMETRE IMZASI eslesen ilk slotu ezer (Signature.Matches; isim-mangling YOK).
    //    Generic sanal metot (Pair<U>): sablon slot ACMAZ; her (metot, tip argumanlari) orneklemesi ayri slottur ve tureyen
    //    siniflarda ayni argumanlarla ornekleme bu pass'te uretilir (IL2CPP generic virtual method tablosu karsiligi).
    // 3) ITable: iface slotu -> en tureyenden yukari ilk imza eslesen impl (acik impl oncelikli).
    // Idempotent: ayni Context'e tekrar uygulanabilir. Yeni kod ornekledigini `Build` donus degeri bildirir
    // (Resolver sabit nokta dongusunu yeniden kosar).
    public static class Hierarchy
    {
        public static bool Build(Context ctx)
        {
            // C# kurali: base'siz class'lar Object'e koklenir (DIGITOYENGINE_is zinciri object'e ulasir).
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsInterface && !p.IsGenericParameter
                    && p.Parent == null && p != Primitive.Object)
                    p.Parent = Primitive.Object;

            var doneLayout = new HashSet<Primitive>();
            foreach (var p in ctx.AllPrimitives.ToList())
                if (p.Type == PrimitiveType.Model && !p.IsGeneric)
                    AssignLayout(p, doneLayout);

            var state = new BuildState { ctx = ctx };
            overrideTarget.Clear(); // tanim -> hedef onbellegi bu kurulum icin (Code nesneleri ctx'e ait)
            exactDecls = null;
            // on-kayitli runtime tipleri PrimitiveOrder'da yok (AllPrimitives gormez) -> acikca kur.
            BuildVTable(Primitive.Object, state);
            BuildVTable(Primitive.String, state);
            foreach (var p in ctx.AllPrimitives.ToList()) // ornekleme yeni tip kaydedebilir -> snapshot
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsGeneric && p.GenericTemplate == null)
                    BuildVTable(p, state);

            foreach (var p in ctx.AllPrimitives.ToList())
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsInterface && !p.IsGeneric && p.GenericTemplate == null)
                    BuildITables(ctx, p, state);
            return state.instantiated;
        }

        class BuildState { public Context ctx; public HashSet<Primitive> done = new HashSet<Primitive>(); public bool instantiated; }

        // ---- ITABLE: TANIM DUZEYINDE impl eslesmesi ----
        // Kalitim zincirindeki her ornek (p, Base<string>, ...) tanimina indirilir; iface ornekleri zincirdeki tanimin bildirdigi
        // ACIK iface uygulamasiyla POZISYONEL eslenir (GenericInstantiator iface listesini sablondan sirayla kurar; miras iface'ler de
        // oyle). Her iki taraf da ortak uzaya (en turemis tanim) harita zincirleriyle cozulur -> canon cokmesi (ISer<List<__Canon>> ->
        // ISer<__Canon>) ve tip interning'e bagimlilik yok. GVM slotu (iface metodunun orneklemesi) icin sinifin generic metot sablonu
        // ayni argumanlarla orneklenir (sinif GVM'inde adim 1 ile ayni mekanik).
        sealed class IfaceSite
        {
            public Primitive Instance;                                   // p'nin AllInterfaces'indeki iface ornegi
            public Primitive DefApp;                                     // onu bildiren tanimdaki acik uygulama (ya da iface'in kendisi)
            public List<Dictionary<Primitive, Primitive>> Maps;          // p-tanim uzayina cozen harita zinciri (sinif zinciri + iface args)
        }
        sealed class ChainLink
        {
            public Primitive Instance, Def;
            public List<Dictionary<Primitive, Primitive>> Maps;          // Def parametreleri -> p-tanim uzayi
        }

        static List<ChainLink> ClassChain(Primitive p)
        {
            var chain = new List<ChainLink>();
            var maps = new List<Dictionary<Primitive, Primitive>>();
            for (var t = p; t != null; t = t.Parent)
            {
                // ornegin tanimi; haritayi bir onceki tanimin PARENT uygulamasindan kur (p icin: kendi argumanlari degil, bos uzay)
                var def = Signature.DefinitionOf(t) ?? t;
                var link = new ChainLink { Instance = t, Def = def, Maps = new List<Dictionary<Primitive, Primitive>>(maps) };
                chain.Add(link);
                // bir sonraki halka: def.Parent acik uygulama (Base<List<T>>) -> parent tanimin parametreleri -> argumanlar
                var pnode = def.Parent;
                var pmap = new Dictionary<Primitive, Primitive>();
                if (pnode != null)
                {
                    var pdef = Signature.DefinitionOf(pnode) ?? pnode;
                    for (int i = 0; i < pdef.GenericParameters.Count && i < pnode.TypeArguments.Count; i++)
                        pmap[pdef.GenericParameters[i]] = pnode.TypeArguments[i];
                }
                maps = new List<Dictionary<Primitive, Primitive>>(maps) { pmap };
            }
            return chain;
        }

        static void AddIfaceSites(Primitive inst, Primitive defApp, List<Dictionary<Primitive, Primitive>> maps, List<IfaceSite> sites)
        {
            if (sites.Any(s => s.Instance == inst)) return; // AllInterfaces tekrarsiz: ilk bildirim kazanir
            var idef = Signature.DefinitionOf(defApp) ?? defApp;
            var imap = new Dictionary<Primitive, Primitive>();
            for (int i = 0; i < idef.GenericParameters.Count && i < defApp.TypeArguments.Count; i++)
                imap[idef.GenericParameters[i]] = defApp.TypeArguments[i];
            var imaps = new List<Dictionary<Primitive, Primitive>>(maps) { imap };
            sites.Add(new IfaceSite { Instance = inst, DefApp = defApp, Maps = imaps });
            // miras iface'ler: ornegin listesi tanimin listesiyle pozisyonel
            for (int j = 0; j < inst.Interfaces.Count && j < idef.Interfaces.Count; j++)
                AddIfaceSites(inst.Interfaces[j], idef.Interfaces[j], imaps, sites);
        }

        static void BuildITables(Context ctx, Primitive p, BuildState st)
        {
            p.ITables.Clear();
            var chain = ClassChain(p);
            var sites = new List<IfaceSite>();
            foreach (var link in chain)
                for (int k = 0; k < link.Instance.Interfaces.Count && k < link.Def.Interfaces.Count; k++)
                    AddIfaceSites(link.Instance.Interfaces[k], link.Def.Interfaces[k], link.Maps, sites);
            foreach (var iface in AllInterfaces(p))
            {
                var site = sites.FirstOrDefault(s => s.Instance == iface)
                    ?? new IfaceSite { Instance = iface, DefApp = iface, Maps = new List<Dictionary<Primitive, Primitive>>() };
                var table = new List<Code>();
                foreach (var im in iface.VTable)
                    table.Add(FindImpl(ctx, p, chain, site, im, st)
                        ?? throw new System.Exception($"{p.Name}, {iface.Name}.{im.DisplayName ?? im.Name} methodunu implement etmiyor"));
                p.ITables[iface] = table;
            }
        }

        // Iface slotu icin impl: once acik impl (bu iface icin), sonra ortuk (ayni imza); en tureyenden yukari. Impl parent'tan
        // geliyorsa ve tureyen bunu override ettiyse vtable'daki guncel hedef alinir (sanal zincir korunur).
        static Code FindImpl(Context ctx, Primitive cls, List<ChainLink> chain, IfaceSite site, Code im, BuildState st)
        {
            var imRoot = GenericInstantiator.RootTemplate(im); // iface TANIM metodu (GVM ise sablon)
            for (int pass = 0; pass < 2; pass++) // 0: acik impl, 1: ortuk
                foreach (var link in chain)
                    foreach (var m in ctx.MembersOf(link.Def).ToList())
                    {
                        if (m.IsStatic || m.MethodTypeArguments != null) continue;
                        if (pass == 0)
                        {
                            if (m.ExplicitInterface == null) continue;
                            if (!Signature.TypeEquals2(m.ExplicitInterface, link.Maps, link.Maps.Count - 1, site.DefApp, site.Maps, site.Maps.Count - 2, null, null)) continue;
                        }
                        else if (m.ExplicitInterface != null) continue;
                        if (!Signature.MatchesUnder2(m, link.Maps, imRoot, site.Maps)) continue;
                        return Realize(ctx, cls, link, m, im, st);
                    }
            return null;
        }

        // Tanim uyesini p'nin zincirindeki ornege indir; GVM ise iface slotunun argumanlariyla ornekle; sonra devirtualize.
        static Code Realize(Context ctx, Primitive cls, ChainLink link, Code defMember, Code im, BuildState st)
        {
            var member = link.Instance == link.Def ? defMember : GenericInstantiator.MemberOf(ctx, link.Instance, defMember);
            if (im.MethodTypeArguments != null)
            {
                if (!ctx.TryGetInstance(member.Owner, GenericInstantiator.RootTemplate(member), im.MethodTypeArguments, out var inst))
                {
                    inst = GenericInstantiator.InstantiateCode(ctx, member, im.MethodTypeArguments);
                    st.instantiated = true;
                }
                member = inst;
            }
            return Devirtualize(cls, member);
        }

        // class'in implement ettigi iface'ler DUZLESTIRILMIS (parent zinciri dahil, tekrarsiz) - C itables ile ayni kural
        public static List<Primitive> AllInterfaces(Primitive p)
        {
            var result = new List<Primitive>();
            void Add(Primitive iface)
            {
                if (result.Contains(iface)) return;
                result.Add(iface);
                foreach (var inherited in iface.Interfaces) Add(inherited);
            }
            for (var t = p; t != null; t = t.Parent)
                foreach (var i in t.Interfaces)
                    Add(i);
            return result;
        }

        // m sanal ve cls'in vtable'inda ezilmisse ezen hedef
        static Code Devirtualize(Primitive cls, Code m)
        {
            if (!m.IsVirtual && !m.IsOverride) return m;
            int slot = m.Owner.VTable.IndexOf(m);
            return slot >= 0 && slot < cls.VTable.Count ? cls.VTable[slot] : m;
        }

        // parent alanlari 0..N-1, kendi alanlari N.. (rekursif, parent once)
        static int AssignLayout(Primitive p, HashSet<Primitive> done)
        {
            int baseOffset = p.Parent != null ? AssignLayout(p.Parent, done) : 0;
            if (!done.Add(p))
                return baseOffset + p.Fields.Count;
            for (int i = 0; i < p.Fields.Count; i++)
                p.Fields[i].Index = baseOffset + i;
            return baseOffset + p.Fields.Count;
        }

        static void BuildVTable(Primitive p, BuildState st)
        {
            if (!st.done.Add(p)) return;
            p.VTable.Clear();
            if (p.Parent != null)
            {
                BuildVTable(p.Parent, st);
                p.VTable.AddRange(p.Parent.VTable);
            }
            // CANON (K5): paylasimli generic class'in sozluk slotu — parent slotlarindan hemen sonra, canon ve tum exact'lerde ayni indeks;
            // tureyen siniflar kopyayla miras alir (Foo : List<string> uzerinden gelen this dogru sozlugu bulur).
            if (!p.IsStruct && !p.IsInterface && p.Canonical != null && (p.IsCanonForm || p.IsSharedExact))
                p.VTable.Add(st.ctx.DictSentinel(p.Canonical));
            var members = st.ctx.MembersOf(p).ToList(); // ornekleme listeyi degistirir -> snapshot
            // 1) parent'tan gelen GENERIC SANAL METOT orneklemeleri: tureyen ayni imzali sablonla ezmisse ayni argumanlarla
            //    ornekle ve slota koy (tureyen sinifin kendi ornekleri henuz yoksa burada dogar).
            for (int slot = 0; slot < p.VTable.Count; slot++)
            {
                var inherited = p.VTable[slot];
                if (inherited.MethodTypeArguments == null || inherited.Owner == p) continue;
                var shell = inherited.Template; // generic metot kabugu (U acik)
                foreach (var m in members)
                    if (m.IsOverride && m.GenericParameters.Count > 0 && m.MethodTypeArguments == null && Signature.Matches(m, shell))
                    {
                        if (!st.ctx.TryGetInstance(p, m, inherited.MethodTypeArguments, out var inst))
                        {
                            inst = GenericInstantiator.InstantiateCode(st.ctx, m, inherited.MethodTypeArguments);
                            st.instantiated = true;
                        }
                        p.VTable[slot] = inst;
                        break;
                    }
            }
            // 2) kendi uyeleri
            foreach (var m in members)
            {
                if (m.IsStatic) continue;
                if (m.GenericParameters.Count > 0) continue; // generic metot SABLONU slot acmaz; ornekleri acar (asagida)
                if (m.MethodTypeArguments != null)
                {
                    // generic metot orneklemesi: zaten bir slota yerlesmis olabilir (adim 1 ya da parent kopyasi)
                    if (p.VTable.Contains(m)) continue;
                    var shell = m.Template;
                    if (!(shell.IsVirtual || shell.IsOverride)) continue;
                    if (shell.IsOverride)
                    {
                        int s = SlotOfGenericOverride(p.VTable, m);
                        if (s >= 0) { p.VTable[s] = m; continue; }
                        // parent zincirinde sablon var ama bu argumanlarla ornegi yok (tureyen tip uzerinden dogrudan cagrilmis):
                        // slotu acan kok sablonu ayni argumanlarla ornekle; slot yerlesimi yeniden kurulumda (Build tekrar kosar)
                        var opener = FindSlotOpener(st.ctx, p.Parent, shell)
                            ?? throw new System.Exception($"override edilecek generic virtual yok: {p.Name}.{m.DisplayName ?? m.Name}");
                        if (!st.ctx.TryGetInstance(opener.Owner, opener, m.MethodTypeArguments, out _))
                        {
                            GenericInstantiator.InstantiateCode(st.ctx, opener, m.MethodTypeArguments);
                            st.instantiated = true;
                        }
                        continue;
                    }
                    p.VTable.Add(m);
                    continue;
                }
                if (m.IsOverride)
                {
                    // Override slotu TANIM duzeyinde cozulur (OverrideTargetOf: sablon hiyerarsisinde ikameli imza) ve slot koku
                    // (SlotRoot) ile vtable'da bulunur: canon/exact/generic-disi tum ornekler ayni yol; tip interning'e ya da
                    // canon cokmesine (Serializer<List<__Canon>> -> Serializer<__Canon>) bagimlilik yok.
                    int slot = SlotOf(st.ctx, p.VTable, m);
                    if (slot < 0)
                    {
                        var par = p.Parent;
                        string pinfo = par == null ? "parent=null" :
                            $"parent={par.Name} vtable=[{string.Join(",", par.VTable.Select(v => v.DisplayName ?? v.Name))}]";
                        throw new System.Exception($"override edilecek virtual yok: {p.Name}.{m.DisplayName ?? m.Name} ({pinfo})");
                    }
                    p.VTable[slot] = m;
                }
                else if (m.IsVirtual)
                    p.VTable.Add(m); // newslot: metadata kesin; Ov<int>.Foo(T)/Foo(int) gibi imzasi cakisan iki slot .NET'te de gecerlidir
            }
        }

        // Override hedefi: vtable'da (parent sirasiyla) AD + IMZA eslesen ILK slot (.NET: ayni imzali birden fazla slot
        // varsa -- Base<int>.Foo(T)/Foo(int) -- ilk slot; C# bunu CS0462 ile reddeder, IL duzeyinde ilk kazanir).
        // ---- TANIM DUZEYINDE OVERRIDE COZUMU (CoreCLR MethodTableBuilder modeli) ----
        // Override'in hedefi, IL sahibi tanim (RootTemplate) uzerinde, sahibin SABLON parent zincirinde ikameli imzayla bir kez
        // bulunur ve onbelleklenir. Slot kimligi = zincirin kokundeki `virtual` tanim (SlotRoot). Orneklerde (exact/canon) slot,
        // vtable girisinin kok tanimi ayni SlotRoot'a cikan giristir. Tip esitligi yalniz nesne grafi (Signature.TypeEqualsUnder).

        static readonly Dictionary<Code, Code> overrideTarget = new Dictionary<Code, Code>();

        // Vtable'da `method`'un (override ornegi ya da tanimi) ezecegi slot; -1 = yok. GVM ornekleri/explicit girisler atlanir.
        public static int SlotOf(Context ctx, List<Code> vtable, Code method)
        {
            var root = SlotRoot(ctx, GenericInstantiator.RootTemplate(method));
            if (root == null) return -1;
            for (int i = 0; i < vtable.Count; i++)
            {
                var v = vtable[i];
                if (v.MethodTypeArguments != null || v.ExplicitInterface != null) continue;
                if (SlotRoot(ctx, GenericInstantiator.RootTemplate(v)) == root) return i;
            }
            return -1;
        }

        // Tanimin slot koku: override zinciri boyunca ilk `virtual` (newslot) tanim; sanal degilse null.
        static Code SlotRoot(Context ctx, Code def)
        {
            for (int guard = 0; def != null && guard < 64; guard++)
            {
                if (!def.IsOverride) return def.IsVirtual ? def : null;
                def = OverrideTargetOf(ctx, def);
            }
            return null;
        }
        // Herhangi bir kod (ornek/kabuk/tanim) icin slot koku (emitter: GVM dispatcher).
        public static Code SlotRootOf(Context ctx, Code code) => SlotRoot(ctx, GenericInstantiator.RootTemplate(code));

        // Slot kokunun (GVM) verilen EXACT metot argumanlariyla bildirilen tum exact orneklemeleri (Base.M<X>, Derived.M<X>, IFoo.M<X>...):
        // sozlugu olan (HasMethodDict canon'lu) kabuklar. Dispatcher gelen sozlugu bunlarin sembolleriyle karsilastirir. Yapisal: kok
        // nesnesi + arguman referans esitligi.
        static Dictionary<Code, List<Code>> exactDecls;
        public static IEnumerable<Code> ExactDeclarationsOf(Context ctx, Code root, List<Primitive> args)
        {
            if (exactDecls == null)
            {
                exactDecls = new Dictionary<Code, List<Code>>();
                foreach (var c in ctx.AllCodes)
                {
                    if (c.MethodTypeArguments == null || Canon.ContainsCanon(c.MethodTypeArguments) || !c.IsSharedExact || !c.Shared.HasMethodDict) continue;
                    var r = SlotRootOf(ctx, c);
                    if (r == null) continue;
                    if (!exactDecls.TryGetValue(r, out var l)) exactDecls[r] = l = new List<Code>();
                    l.Add(c);
                }
            }
            if (!exactDecls.TryGetValue(root, out var list)) yield break;
            foreach (var d in list)
                if (SameArgs(d.MethodTypeArguments, args)) yield return d;
        }

        // Override TANIMININ hedefi: sahibinin parent zincirinde (tanim duzeyinde, bilesik ikame) ad + ikameli imza eslesen
        // ilk sanal/override uye (.NET: parent sirasiyla ilk slot). Yoksa null.
        static Code OverrideTargetOf(Context ctx, Code def)
        {
            if (overrideTarget.TryGetValue(def, out var cached)) return cached;
            Code found = null;
            var maps = new List<Dictionary<Primitive, Primitive>>(); // kalitim zinciri ikame haritalari (dis -> ic)
            var node = def.Owner.Parent; // RootTemplate uyesi: sahibi TANIM (sablon ya da generic-disi tip)
            for (int guard = 0; node != null && found == null && guard < 64; guard++)
            {
                var defn = Signature.DefinitionOf(node) ?? node;
                var pmap = new Dictionary<Primitive, Primitive>();
                if (defn != node) // uygulama dugumu/ornek: tanimin parametreleri -> argumanlar (bir ust haritayla cozulur)
                    for (int i = 0; i < defn.GenericParameters.Count && i < node.TypeArguments.Count; i++)
                        pmap[defn.GenericParameters[i]] = node.TypeArguments[i];
                maps.Add(pmap);
                foreach (var v in ctx.MembersOf(defn))
                {
                    if (v.IsStatic || !(v.IsVirtual || v.IsOverride) || v.GenericParameters.Count > 0
                        || v.MethodTypeArguments != null || v.ExplicitInterface != null) continue;
                    if (Signature.MatchesUnder(def, v, maps)) { found = v; break; }
                }
                node = defn.Parent;
            }
            overrideTarget[def] = found;
            return found;
        }

        // generic metot orneklemesi (Pair<string>) icin slot: ayni argumanlarla ve kabuk imzasi eslesen slot
        static int SlotOfGenericOverride(List<Code> vtable, Code inst)
        {
            for (int i = 0; i < vtable.Count; i++)
            {
                var v = vtable[i];
                if (v.MethodTypeArguments == null || !SameArgs(v.MethodTypeArguments, inst.MethodTypeArguments)) continue;
                if (Signature.Matches(v.Template, inst.Template)) return i;
            }
            return -1;
        }

        static bool SameArgs(List<Primitive> a, List<Primitive> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // parent zincirinde generic sanal metodun slotunu ACAN (IsVirtual) kok sablon kabugu
        static Code FindSlotOpener(Context ctx, Primitive from, Code shell)
        {
            Code opener = null;
            for (var t = from; t != null; t = t.Parent)
                foreach (var m in ctx.MembersOf(t))
                    if (m.IsVirtual && m.GenericParameters.Count > 0 && m.MethodTypeArguments == null && Signature.Matches(m, shell)) opener = m;
            return opener;
        }

        // Instance icin toplam alan sayisi (parent zinciri dahil) ve duz alan listesi
        public static int TotalFieldCount(Primitive p)
        {
            int n = 0;
            for (var t = p; t != null; t = t.Parent)
                n += t.Fields.Count;
            return n;
        }

        public static IEnumerable<PrimitiveField> AllFields(Primitive p)
        {
            var chain = new List<Primitive>();
            for (var t = p; t != null; t = t.Parent)
                chain.Add(t);
            for (int i = chain.Count - 1; i >= 0; i--)
                foreach (var f in chain[i].Fields)
                    yield return f;
        }
    }
}
