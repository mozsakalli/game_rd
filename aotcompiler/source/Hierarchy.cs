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
            // on-kayitli runtime tipleri PrimitiveOrder'da yok (AllPrimitives gormez) -> acikca kur.
            BuildVTable(Primitive.Object, state);
            BuildVTable(Primitive.String, state);
            foreach (var p in ctx.AllPrimitives.ToList()) // ornekleme yeni tip kaydedebilir -> snapshot
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsGeneric && p.GenericTemplate == null)
                    BuildVTable(p, state);

            foreach (var p in ctx.AllPrimitives.ToList())
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsInterface && !p.IsGeneric && p.GenericTemplate == null)
                    BuildITables(ctx, p);
            return state.instantiated;
        }

        class BuildState { public Context ctx; public HashSet<Primitive> done = new HashSet<Primitive>(); public bool instantiated; }

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

        static void BuildITables(Context ctx, Primitive p)
        {
            p.ITables.Clear();
            foreach (var iface in AllInterfaces(p))
            {
                var table = new List<Code>();
                foreach (var im in iface.VTable)
                    table.Add(FindImpl(ctx, p, iface, im)
                        ?? throw new System.Exception($"{p.Name}, {iface.Name}.{im.DisplayName ?? im.Name} methodunu implement etmiyor"));
                p.ITables[iface] = table;
            }
        }

        // Interface slotu icin impl: once acik impl (bu iface icin), sonra ortuk (public, ayni imza); en tureyenden yukari.
        // Impl parent'tan geliyorsa ve tureyen bunu override ettiyse vtable'daki guncel hedef alinir (sanal zincir korunur).
        static Code FindImpl(Context ctx, Primitive cls, Primitive iface, Code interfaceMethod)
        {
            for (var t = cls; t != null; t = t.Parent)
            {
                foreach (var m in ctx.MembersOf(t))
                    if (m.ExplicitInterface == iface && Signature.Matches(m, interfaceMethod))
                        return Devirtualize(cls, m);
                foreach (var m in ctx.MembersOf(t))
                    if (m.ExplicitInterface == null && !m.IsStatic && Signature.Matches(m, interfaceMethod))
                        return Devirtualize(cls, m);
            }
            return null;
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
                    int slot = SlotOf(p.VTable, m);
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
        public static int SlotOf(List<Code> vtable, Code method)
        {
            for (int i = 0; i < vtable.Count; i++)
                if (vtable[i].MethodTypeArguments == null && vtable[i].ExplicitInterface == null && Signature.Matches(vtable[i], method))
                    return i;
            return -1;
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
