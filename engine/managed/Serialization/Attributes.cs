using System;

namespace DigitoyEngine;

// Unity serilestirme attribute'leri. Kurallar:
//   public alan            -> serilesir ([NonSerialized] ile haric tutulur)
//   private/protected alan -> serilesmez ([SerializeField] ile dahil edilir)
//   [FormerlySerializedAs] -> eski adla kaydedilmis veri yeni alana yuklenir
// [SerializeReference] (polimorfik graf) BILEREK yok: tuketicisi binding/action
// sistemi olacak; tip-etiketli node + rid kaydi ister, ihtiyac dogunca eklenir.

[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class FormerlySerializedAsAttribute : Attribute
{
    public readonly string OldName;

    public FormerlySerializedAsAttribute(string oldName) => OldName = oldName;
}

// Tip yeniden adlandirma migrasyonu (FormerlySerializedAs'in tip duzeyi karsiligi).
// Ayrica editor, typemap uzerinden tek-class dosya rename'lerini OTOMATIK yakalar;
// bu attribute cok-class'li dosyalar / elle tasima icin guvence.
[AttributeUsage(AttributeTargets.Class)]
public sealed class MovedFromAttribute : Attribute
{
    public readonly string OldName;

    public MovedFromAttribute(string oldName) => OldName = oldName;
}

// Isaretli component Inspector'da jenerik preview kontrolu alir (PreviewSession).
// Component preview icin HICBIR SEY yazmaz: doc=snapshot, bitis=reload.
[AttributeUsage(AttributeTargets.Class)]
public sealed class PreviewableAttribute : Attribute
{
}
