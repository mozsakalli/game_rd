using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Runtime prefab: ayri dosyada yasayan alt agac. Editor/loose: SceneDoc (YAML);
// release/pak: pismis baytlar (SceneBinary) — DocNode kurulmaz. Spawn, sahne
// yuklemesiyle AYNI sozlesmeden gecer (referans cozumu + gec aktivasyon).
// Editor Doc'u EXPANDED tutulur (icindeki instance kayitlari acik) — nested prefab bilesimi.
public sealed class Prefab
{
    public string Key;
#if DE_EDITOR
    internal SceneDoc Doc;      // YAML kaynak (editor), expanded; baked'de null
    // Icerdigi prefab'lar (gecisli, yol anahtari): dongu reddi + bagimlilik sorgusu.
    internal readonly HashSet<string> Deps = new(StringComparer.OrdinalIgnoreCase);
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
