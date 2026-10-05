namespace DigitoyEngine;

// Runtime prefab: ayri dosyada yasayan alt agac. Editor/loose: SceneDoc (YAML);
// release/pak: pismis baytlar (SceneBinary) — DocNode kurulmaz. Spawn, sahne
// yuklemesiyle AYNI sozlesmeden gecer (referans cozumu + gec aktivasyon).
// Override/variant sistemi bilerek sonraya birakildi.
public sealed class Prefab
{
    public string Key;
    internal SceneDoc Doc;      // YAML kaynak (editor); baked'de null
    internal byte[] Baked;      // SceneBinary (release); YAML'da null
    internal AssetDatabase Assets;

    public GameObject Instantiate(Transform parent = null)
        => Baked != null
            ? SceneBinary.Spawn(Baked, parent, Scene.Active.Catalog, Assets)
            : Doc.Spawn(parent, Scene.Active.Catalog, Assets);
}
