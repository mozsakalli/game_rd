using System.Collections.Generic;

namespace DigitoyEditor;

// Editor secimi: authored nesne kimlikleri (SceneDoc localId). COKLU secim:
// Ids sirali liste, DocId = BIRINCIL (son secilen; gizmo merkezi/inspector bunu
// gosterir). 0 = secim yok. Eski tek-secim kodu DocId get/set ile ayni calisir.
public static class Selection
{
    static readonly List<int> _ids = new();

    public static int DocId
    {
        get => _ids.Count > 0 ? _ids[^1] : 0;
        set
        {
            _assetPath = null;
            _ids.Clear();
            if (value != 0)
                _ids.Add(value);
        }
    }

    public static IReadOnlyList<int> Ids => _ids;
    public static int Count => _ids.Count;

    public static bool Contains(int id) => _ids.Contains(id);

    // Ctrl+tik: varsa cikar, yoksa ekle (eklenen birincil olur).
    public static void Toggle(int id)
    {
        if (id == 0)
            return;
        _assetPath = null;
        if (!_ids.Remove(id))
            _ids.Add(id);
    }

    public static void Add(int id)
    {
        if (id == 0)
            return;
        _assetPath = null;
        if (!_ids.Contains(id))
            _ids.Add(id);
    }

    public static void Remove(int id) => _ids.Remove(id);

    public static void Clear() => _ids.Clear();

    // Asset secimi (Project panelinden tek tik): sahne secimiyle karsilikli dislar
    // — Inspector hangisi doluysa onu gosterir.
    static string _assetPath;
    public static string AssetPath
    {
        get => _assetPath;
        set
        {
            _assetPath = value;
            if (value != null)
                _ids.Clear();
        }
    }
}
