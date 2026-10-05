namespace DigitoyEngine;

// Runtime prefab: ayri dosyada yasayan alt agac. Editor/loose: SceneDoc (YAML);
// release/pak: pismis baytlar (SceneBinary) — DocNode kurulmaz. Spawn, sahne
// yuklemesiyle AYNI sozlesmeden gecer (referans cozumu + gec aktivasyon).
// Override/variant sistemi bilerek sonraya birakildi.
public sealed class Prefab
{
    public string Key;
#if DE_EDITOR
    internal SceneDoc Doc;      // YAML kaynak (editor); baked'de null
#endif
    internal byte[] Baked;      // SceneBinary (release); YAML'da null
    internal AssetDatabase Assets;

    public GameObject Instantiate(Transform parent = null)
    {
#if DE_EDITOR
        if (Baked == null)
            return Doc.Spawn(parent, Scene.Active.Catalog, Assets);
#endif
        return SceneBinary.Spawn(Baked, parent, Scene.Active.Catalog, Assets);
    }
}
