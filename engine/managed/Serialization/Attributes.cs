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

// Unity ExecuteInEditMode: isaretli component'in yasam dongusu (Awake/OnEnable/Start/
// OnDisable/OnDestroy) EDIT sahnesinde de kosar. Isaretsiz component = oyun mantigi;
// editor projeksiyonunda uyur (Awake'te SetActive(false) gibi yan etkiler sahneyi bozmaz).
// Motorun kendi gorsel/yerlesim component'leri (LayoutBox, Camera, MovieClip...) isaretlidir.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ExecuteInEditModeAttribute : Attribute
{
}

// Unity AddComponentMenu: editor "Add Component" menusundeki yol ("Rendering/Sprite
// Renderer"). Isaretsiz component "Scripts" grubuna duser (oyun kodu); bos yol ("")
// menuden gizler (test/yardimci component). Grup/sira tamamen attribute'tan — editor
// tip-ozel bir sey bilmez.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AddComponentMenuAttribute : Attribute
{
    public readonly string Path;

    public AddComponentMenuAttribute(string path) => Path = path;
}

// AOT trim (docs/engine-notes.md TRIM): isaretli metot lean profilde GOVDESIZ derlenir (void -> hicbir sey, Task ->
// CompletedTask, diger -> default) ve cagirdigi hicbir sey erisilebilir sayilmaz. Dev/test kancalari icin
// (sahne dokumu, autoload modul, net probe): release ikilisine girmez, full profilde oldugu gibi kalir.
// Donus tipi yalniz void / Task / skaler-referans olabilir (Task<T> DESTEKLENMEZ: durum alanla tasinir).
[AttributeUsage(AttributeTargets.Method)]
public sealed class DevOnlyAttribute : Attribute
{
}

// Inspector'da kosullu gorunurluk: AYNI duzeydeki kardes serilesen alanin degerine
// bagli. value verilirse esitlik (kanonik skaler kiyasi: enum adi, bool, sayi...);
// value=null ise "truthy" testi (bool true / asset-ref dolu / sayi != 0 / string dolu).
// Ornek: [ShowIf(nameof(type), GradientType.Linear)] veya [ShowIf(nameof(font))].
[AttributeUsage(AttributeTargets.Field)]
public sealed class ShowIfAttribute : Attribute
{
    public readonly string Field;
    public readonly object Value;

    public ShowIfAttribute(string field, object value = null)
    {
        Field = field;
        Value = value;
    }
}

// Unity [HideInInspector] karsiligi: alan serilesir ama jenerik inspector/ObjectDrawer
// cizmez — sahibi ozel UI ile cizer (PlayerSettings sahne listesi gibi).
[AttributeUsage(AttributeTargets.Field)]
public sealed class HideInInspectorAttribute : Attribute
{
}

// Unity ScriptableObject'in tip-duzeyi karsiligi (miras yerine attribute): isaretli [Serializable]
// sinif DOSYA olarak yasar (.asset ya da importer "main" artifact'i) ve alanlarda REFERANS
// (Kind.Asset, GUID) olarak gecer — paylasilir, pak'a referanslandigi icin girer, ObjectBinary ile
// piser. Isaretsiz [Serializable] = sahibinin icine GOMULU (Kind.Object). TypeCatalog bu tipleri
// de kaydeder (ad -> sema), PakRoots trim koku sayar. [CreateAssetMenu] bunu ima eder.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetAttribute : Attribute
{
}

// Unity [CreateAssetMenu] karsiligi: [Serializable] tipin Project panelindeki
// Create menusunde gorunmesi ACIK OPT-IN'dir — her serializable menuye dusmez.
// MenuName '/' ile alt menu kurar ("Config/Game Settings"). Tanim her yapida (oyun kodu kullanir);
// tuketicisi (ObjectSerializer.IsCreatable) yalniz editor. [Asset] semantigini ima eder.
[AttributeUsage(AttributeTargets.Class)]
public sealed class CreateAssetMenuAttribute : Attribute
{
    public string MenuName;   // bos: tip adi
    public string FileName;   // bos: tip adi
    public int Order;
}
