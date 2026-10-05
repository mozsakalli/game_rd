using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Kalitim pass'i (Resolver icinden, isim cozumunden SONRA calisir):
    // 1) Alan duzlestirme: field.Index parent zinciri dahil global slot olur (parent alanlari once).
    //    VM'de Instance.Fields ve C'de struct layout ayni sirayi kullanir -> Derived* prefix-uyumlu Base*.
    // 2) VTable kurulumu: parent vtable kopyalanir, IsVirtual yeni slot acar, IsOverride ayni
    //    isimli slotu ezer. Slot eslesmesi ISIMLE yapilir (overload'lu sanal method kapsam disi).
    // Idempotent: ayni Context'e tekrar uygulanabilir (her seferinde bastan hesaplar).
    public static class Hierarchy
    {
        public static void Build(Context ctx)
        {
            // C# kurali: base'siz class'lar Object'e koklenir (DIGITOYENGINE_is zinciri object'e ulasir).
            // Front-end kendi tiplerini declare'da kokler; bu satir elle kurulan IR'i da kapsar.
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsInterface && !p.IsGenericParameter
                    && p.Parent == null && p != Primitive.Object)
                    p.Parent = Primitive.Object;

            var doneLayout = new HashSet<Primitive>();
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsGeneric)
                    AssignLayout(p, doneLayout);

            // Owner'i olan methodlari tipe grupla (kayit sirasi korunur -> deterministik vtable)
            var methodsByOwner = new Dictionary<Primitive, List<Code>>();
            foreach (var c in ctx.AllCodes)
            {
                if (c.Owner == null || c.Owner.Unresolved) continue;
                if (!methodsByOwner.TryGetValue(c.Owner, out var list))
                    methodsByOwner[c.Owner] = list = new List<Code>();
                list.Add(c);
            }

            var doneVt = new HashSet<Primitive>();
            // on-kayitli runtime tipleri PrimitiveOrder'da yok (AllPrimitives gormez) -> acikca kur.
            // Object cocuklarinin recursion'iyla da kurulurdu ama String'in cocugu olmayabilir.
            BuildVTable(Primitive.Object, methodsByOwner, doneVt);
            BuildVTable(Primitive.String, methodsByOwner, doneVt);
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsGeneric)
                    BuildVTable(p, methodsByOwner, doneVt);

            // Interface tablolari: her class icin (zincirdeki tum) iface'lerin slot sirali impl listesi.
            // Impl eslesmesi MANGLED isimle (tam imza), en tureyenden yukari ilk bulunan (override'lar kazanir).
            foreach (var p in ctx.AllPrimitives)
                if (p.Type == PrimitiveType.Model && !p.IsStruct && !p.IsInterface && !p.IsGeneric)
                    BuildITables(p, methodsByOwner);
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
            for (var t = p; t != null && !t.Unresolved; t = t.Parent)
                foreach (var i in t.Interfaces)
                    Add(i);
            return result;
        }

        static void BuildITables(Primitive p, Dictionary<Primitive, List<Code>> methodsByOwner)
        {
            p.ITables.Clear();
            foreach (var iface in AllInterfaces(p))
            {
                var table = new List<Code>();
                foreach (var im in iface.VTable)
                    table.Add(FindImpl(p, iface, im, methodsByOwner)
                        ?? throw new System.Exception($"{p.Name}, {iface.Name}.{im.Name} methodunu implement etmiyor"));
                p.ITables[iface] = table;
            }
        }

        static Code FindImpl(Primitive cls, Primitive iface, Code interfaceMethod, Dictionary<Primitive, List<Code>> methodsByOwner)
        {
            for (var t = cls; t != null && !t.Unresolved; t = t.Parent)
                if (methodsByOwner.TryGetValue(t, out var ms))
                {
                    foreach (var m in ms)
                        if (m.ExplicitInterface == iface && SameInterfaceSignature(m, interfaceMethod))
                            return m;
                    foreach (var m in ms)
                        if (m.ExplicitInterface == null && SameInterfaceSignature(m, interfaceMethod))
                            return m;
                }
            return null;
        }

        static string Simple(Code c) => (c.DisplayName ?? c.Name ?? "").Split('(')[0];
        // Tip esitligi: Primitive kimligi VEYA ad (array/pointer/Apply node'lari intern edilmez;
        // Name kimlige dahildir: "[Byte" == "[Byte").
        static bool SameType(Primitive a, Primitive b) => a == b || (a != null && b != null && a.Name == b.Name);
        static bool SameInterfaceSignature(Code implementation, Code contract)
        {
            if (implementation.IsStatic || !SameType(implementation.ReturnType, contract.ReturnType) ||
                implementation.Arguments.Count != contract.Arguments.Count)
                return false;
            var implementationName = implementation.DisplayName?.Split('(')[0];
            var contractName = contract.DisplayName?.Split('(')[0];
            if (implementationName != null && contractName != null && implementationName != contractName)
                return false;
            for (int i = 1; i < implementation.Arguments.Count; i++)
                if (!SameType(implementation.Arguments[i].Type, contract.Arguments[i].Type) ||
                    implementation.Arguments[i].IsRef != contract.Arguments[i].IsRef ||
                    implementation.Arguments[i].IsOut != contract.Arguments[i].IsOut)
                    return false;
            return true;
        }

        // parent alanlari 0..N-1, kendi alanlari N.. (rekursif, parent once)
        static int AssignLayout(Primitive p, HashSet<Primitive> done)
        {
            int baseOffset = p.Parent != null && !p.Parent.Unresolved ? AssignLayout(p.Parent, done) : 0;
            if (!done.Add(p))
                return baseOffset + p.Fields.Count;
            for (int i = 0; i < p.Fields.Count; i++)
                p.Fields[i].Index = baseOffset + i;
            return baseOffset + p.Fields.Count;
        }

        static void BuildVTable(Primitive p, Dictionary<Primitive, List<Code>> methodsByOwner, HashSet<Primitive> done)
        {
            if (!done.Add(p)) return;
            p.VTable.Clear();
            if (p.Parent != null && !p.Parent.Unresolved)
            {
                BuildVTable(p.Parent, methodsByOwner, done);
                p.VTable.AddRange(p.Parent.VTable);
            }
            if (!methodsByOwner.TryGetValue(p, out var methods)) return;
            foreach (var m in methods)
            {
                if (m.IsOverride)
                {
                    int slot = SlotOf(p.VTable, m.Name);
                    if (slot < 0) throw new System.Exception($"override edilecek virtual yok: {p.Name}.{m.Name}");
                    p.VTable[slot] = m;
                }
                else if (m.IsVirtual)
                {
                    if (SlotOf(p.VTable, m.Name) >= 0) throw new System.Exception($"virtual slot zaten var (override mi demek istedin?): {p.Name}.{m.Name}");
                    p.VTable.Add(m);
                }
            }
        }

        // CallVirtual'in hedef slotu: bildiren methodun ismiyle vtable'da arama
        public static int SlotOf(List<Code> vtable, string methodName)
        {
            for (int i = 0; i < vtable.Count; i++)
                if (vtable[i].Name == methodName)
                    return i;
            return -1;
        }

        // Instance icin toplam alan sayisi (parent zinciri dahil) ve duz alan listesi
        public static int TotalFieldCount(Primitive p)
        {
            int n = 0;
            for (var t = p; t != null && !t.Unresolved; t = t.Parent)
                n += t.Fields.Count;
            return n;
        }

        public static IEnumerable<PrimitiveField> AllFields(Primitive p)
        {
            var chain = new List<Primitive>();
            for (var t = p; t != null && !t.Unresolved; t = t.Parent)
                chain.Add(t);
            for (int i = chain.Count - 1; i >= 0; i--)
                foreach (var f in chain[i].Fields)
                    yield return f;
        }
    }
}
