using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// GameObject panosu (Unity gibi SUREC ICI; dosyaya/sistem panosuna yazilmaz). Copy aninda
// alt agaclarin derin klon snapshot'i alinir (orijinal sonradan degisse/silinse de paste ayni
// icerigi verir); paste her seferinde snapshot'tan YENIDEN klonlar → coklu yapistirma, sahneler
// arasi tasima (asset GUID'leri aynen; disa bakan GO ref'leri hedefte yoksa null cozulur).
public static class GoClipboard
{
    static readonly List<List<SceneDoc.GoDoc>> _trees = new();

    public static bool HasContent => _trees.Count > 0;
    public static IReadOnlyList<List<SceneDoc.GoDoc>> Trees => _trees;

    // Secimdeki GO'lardan atasi secimde olmayanlar kopyalanir (alt agac butun gelir), doc sirasiyla.
    public static void Copy(EditorScene es, IEnumerable<int> ids)
    {
        var roots = new List<SceneDoc.GoDoc>();
        foreach (int id in ids)
        {
            var g = es.FindGo(id);
            if (g != null && !es.AncestorIn(g, ids))
                roots.Add(g);
        }
        if (roots.Count == 0)
            return;
        roots.Sort((a, b) => es.Doc.Objects.IndexOf(a).CompareTo(es.Doc.Objects.IndexOf(b)));
        _trees.Clear();
        int nextId = 1; // snapshot kendi id uzayinda; paste'te yeniden remap edilir
        foreach (var g in roots)
            _trees.Add(es.CloneSubtree(es.Doc.Objects, g, ref nextId));
    }
}
