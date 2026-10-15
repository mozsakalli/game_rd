using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // GC REFMAP (docs/engine-notes.md): bir tipin yerlesimindeki GC referanslarinin TEK hesabi (.NET GCDesc / IL2CPP gc_desc karsiligi).
    // Kural R1: hicbir tuketici (descriptor trace, dizi elemani, kutu, write barrier, statik kok, vmint) alanlari gezerek ref aramaz;
    // hepsi buradan cikan tabloyu okur. Tablo descriptor'a `X_refmap[] = { n, offsetof(...)... }` olarak yazilir (vmrt.h Type.refmap).
    //  - Class: Hierarchy.AllFields (kalitim duzlestirilmis), ofsetler nesne basindan (GCHeader dahil).
    //  - Struct: alanlar, ofsetler struct basindan; ic ice struct ozyinelemeli duzlestirilir (Nullable<T>, generic struct dahil).
    //  - FixedArray / Pointer / skaler: ref yok. __Canon ve marker'lar: ref (TypeShape.IsGcRef).
    // Canon: paylasimli exact (List<string>) ve canon formu (List<__Canon>) AYNI C struct'i kullanir -> ayni yol listesi;
    // emitter sembolu StructTag uzerinden canon'a baglar (R5).
    public sealed class RefMap
    {
        // Her yol: dis alandan ic alana PrimitiveField zinciri (a.b.c). Emitter CName uygular.
        public readonly IReadOnlyList<PrimitiveField[]> Paths;
        public bool IsEmpty => Paths.Count == 0;
        public int Count => Paths.Count;

        RefMap(List<PrimitiveField[]> paths) { Paths = paths; }

        static readonly Dictionary<Primitive, RefMap> cache = new Dictionary<Primitive, RefMap>();
        static readonly RefMap Empty = new RefMap(new List<PrimitiveField[]>());

        public static void Reset() => cache.Clear();

        // Yerlesimi olan model (class/struct). Diger sekiller (skaler, dizi, pointer, iface, delegate) icin bos.
        public static RefMap Of(Primitive type)
        {
            if (type == null) return Empty;
            var shape = TypeShape.Of(type);
            if (shape != Shape.Struct && shape != Shape.Class) return Empty;
            if (type.Type != PrimitiveType.Model) return Empty; // Object/String gibi runtime kokleri: tablo runtime'da
            if (cache.TryGetValue(type, out var m)) return m;
            var paths = new List<PrimitiveField[]>();
            Collect(type, new List<PrimitiveField>(), paths, 0);
            m = paths.Count == 0 ? Empty : new RefMap(paths);
            cache[type] = m;
            return m;
        }

        // Bir SLOT tipi (alan/eleman/statik) icin: ref ise tek bos yol (slotun kendisi), struct ise struct'in tablosu, degilse bos.
        public static bool SlotHasRefs(Primitive slotType) => TypeShape.IsGcRef(slotType) || !Of(slotType).IsEmpty;

        static void Collect(Primitive owner, List<PrimitiveField> prefix, List<PrimitiveField[]> paths, int depth)
        {
            if (depth > 32) throw new System.Exception($"refmap: struct yerlesimi cok derin / dongusel: {owner.Name}");
            foreach (var f in Hierarchy.AllFields(owner))
            {
                if (f.IsStatic) continue;
                prefix.Add(f);
                if (TypeShape.IsGcRef(f.Type)) paths.Add(prefix.ToArray());
                else if (TypeShape.IsStructModel(f.Type)) Collect(f.Type, prefix, paths, depth + 1);
                prefix.RemoveAt(prefix.Count - 1);
            }
        }
    }
}
