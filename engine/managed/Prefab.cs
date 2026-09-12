namespace DigitoyEngine;

// Runtime prefab: ayri dosyada yasayan alt agac SceneDoc'u. Spawn, sahne
// yuklemesiyle AYNI yoldan gecer (referans cozumu + gec aktivasyon) — iki
// ayri dogum yolu yok. Override/variant sistemi bilerek sonraya birakildi.
public sealed class Prefab
{
    public string Key;
    internal SceneDoc Doc;
    internal AssetDatabase Assets;

    public GameObject Instantiate(Transform parent = null)
        => Doc.Spawn(parent, Scene.Active.Catalog, Assets);
}
