using System;
using System.Collections.Generic;
using System.Globalization;
using DigitoyEngine;

namespace DigitoyEditor;

// DOC-KAYNAKLI Inspector: authored degerleri (SceneDoc) gosterir/duzenler —
// runtime akisi goruntuyu kirletmez. Her duzenleme doc'a yazilir + canliya
// patch basilir (EditorScene.Apply*). Kaydet doc'u yazar. Alan editorleri
// property modelinden (SerializedType semasi) uretilir; ref/liste v1 salt-okunur.
[MenuItem("Window/Inspector", 1)]
public sealed partial class InspectorPanel : EditorWindow
{
    Vec2 _scroll;
    Vec2 _assetScroll;
    string _inspectedTarget;

    const float RowH = 22f;
    const float LabelW = 92f;

    public InspectorPanel() => Title = "Inspector";

    protected override void OnGui()
    {
        var es = App.EditScene;
        var vis = GuiClip.VisibleRect;
        string inspectedTarget = Selection.AssetPath != null
            ? "asset:" + Selection.AssetPath
            : "go:" + Selection.DocId;
        if (_inspectedTarget != inspectedTarget)
        {
            _inspectedTarget = inspectedTarget;
            Gui.CancelNumericEdit();
            GuiUtility.KeyboardControl = 0;
        }
        if (DrawAssetInspector(vis))
            return;
        var g = es.FindGo(Selection.DocId);
        if (g == null)
        {
            Gui.Label(new Rect(4, 4, vis.width - 8, 20), "No selection");
            return;
        }

        _scroll = Gui.BeginScrollView(new Rect(0, 0, vis.width, vis.height), _scroll,
            new Rect(0, 0, vis.width - 20, MeasureHeight(g)));
        float w = vis.width - 24;
        float y = 2;

        bool active = Gui.Toggle(new Rect(4, y, 18, 18), g.Active);
        if (active != g.Active)
            es.SetActive(g, active);
        // GO adi: yazildikca doc'a (NameOp coalesce'li). Secim degisince buffer tazelenir.
        if (_nameForId != g.Id)
        {
            _nameForId = g.Id;
            _nameLen = Math.Min(g.Name.Length, _nameBuf.Length);
            g.Name.CopyTo(0, _nameBuf, 0, _nameLen);
        }
        Gui.TextField(new Rect(28, y, w - 28, 20), _nameBuf, ref _nameLen);
        if (!BufferEquals(g.Name))
            es.SetName(g, new string(_nameBuf, 0, _nameLen));
        y += RowH + 4;

        // Layer: kamera cullingMask yonlendirme biti (8 layer, cocuklara miras yok).
        Gui.Label(new Rect(4, y, 44, 18), "Layer");
        int layer = Gui.ComboBox(new Rect(48, y, 140, 18), g.Layer, _layerNames);
        if (layer != g.Layer)
            es.SetLayer(g, layer);
        y += RowH;

        // Prefab kaynagi (GO duzeyi): baslik + Overrides paneli + serit gostergeleri.
        SceneDoc.GoDoc prefabSrc = null;
        bool prefabChild = false;
        var instRoot = es.Doc.PrefabRootOf(g);
        if (es.Doc.TryGetPrefabSource(g, App.Assets, out _, out var srcNode0, out var rootRec0))
        {
            prefabSrc = srcNode0;
            prefabChild = g != rootRec0; // kok transform/ad instance'a aittir
        }
        if (instRoot != null)
            DrawPrefabHeader(es, instRoot, ref y, w);

        Header(ref y, w, "Transform");
        // Driven pozisyon (ITransformDriver, orn. layout): soluk salt-okunur satir,
        // canli (hesaplanmis) deger gosterilir — Unity driven-property gorunumu.
        var liveGo = es.Live(g.Id);
        bool posDriven = liveGo != null
            && (TransformDriver.Driven(liveGo, out _) & DrivenTransformProperties.Position) != 0;
        Vec3 pos;
        if (posDriven)
        {
            Vec3RowDisabled(ref y, w, "Position", liveGo.transform.localPosition);
            pos = g.Pos;
        }
        else
        {
            pos = Vec3RowPrefab(ref y, w, "Position", g.Pos, prefabChild ? prefabSrc.Pos : null);
        }
        var rot = Vec3RowPrefab(ref y, w, "Rotation", g.Rot, prefabChild ? prefabSrc.Rot : null);
        var scale = Vec3RowPrefab(ref y, w, "Scale", g.Scale, prefabChild ? prefabSrc.Scale : null);
        if (!Same(pos, g.Pos) || !Same(rot, g.Rot) || !Same(scale, g.Scale))
            es.SetTransform(g, pos, rot, scale);
        y += 4;

        int compIndex = 0;
        foreach (var cd in g.Components)
        {
            long ckey = CompKey(g.Id, compIndex);
            bool open = !_collapsed.Contains(ckey);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawRect(new Rect(0, y - 1, w + 12, 21), new Color(50, 54, 64, 255), 1);
            bool en = Gui.Toggle(new Rect(24, y, 18, 18), cd.Enabled);
            if (en != cd.Enabled)
                es.SetEnabled(g, cd, en);
            if (Event.Current.Type == EventType.Repaint)
            {
                GuiRenderer.DrawTextIn(new Rect(6, y, 18, 20), open ? "\u25be" : "\u25b8",
                    Gui.FontSize - 1f, new Color(190, 194, 204, 255), false, 2);
                GuiRenderer.DrawTextIn(new Rect(46, y, w - 68, 20), cd.Type, Gui.FontSize - 1f,
                    new Color(230, 234, 244, 255), false, 2);
            }
            if (Gui.Button(new Rect(w - 20, y, 18, 18), "x"))
            {
                es.RemoveComponent(g, cd);
                break; // koleksiyon degisti: bu frame'i kes, sonraki pass taze cizer
            }
            // Baslik tiklamasi katlar/acar (toggle ve x kendi event'ini once yutar).
            if (HeaderClick(new Rect(4, y, w - 26, 20)))
            {
                if (open) _collapsed.Add(ckey);
                else _collapsed.Remove(ckey);
            }
            y += RowH;
            if (!open)
            {
                y += 2;
                compIndex++;
                continue;
            }

            var entry = App.Catalog?.Find(cd.Type);
            if (entry == null)
            {
                Gui.Label(new Rect(12, y, w - 12, 18), "(missing script)");
                y += RowH;
                continue;
            }
            // Prefab kaynagi (varsa): alan override gostergesi + Revert/Apply icin.
            SceneDoc.CompDoc prefabComp = null;
            Dictionary<int, int> prefabMap = null;
            if (es.Doc.TryGetPrefabSource(g, App.Assets, out _, out var srcNode, out var rootRec))
            {
                prefabComp = SceneDoc.FindCompByAddress(srcNode, SceneDoc.CompAddressOf(g, cd));
                prefabMap = rootRec.PrefabIds;
            }
            if (entry.Previewable)
            {
                // Jenerik preview: Begin/Stop + Restart — component hicbir sey bilmez,
                // bitis doc'tan reload (izler olur), duzenlemeler (Commit->doc) yasar.
                int ci = g.Components.IndexOf(cd);
                bool owner = PreviewSession.IsOwner(g.Id, ci);
                if (Gui.Button(new Rect(12, y, 80, 18), owner ? "Stop" : "Preview"))
                {
                    if (owner) PreviewSession.End();
                    else PreviewSession.Begin(g.Id, ci);
                }
                if (owner && Gui.Button(new Rect(96, y, 80, 18), "Restart"))
                    PreviewSession.Restart();
                y += RowH;
            }
            foreach (var f in entry.Schema)
            {
                if (!DocVisible(f, cd))
                    continue;
                DrawField(es, g, cd, f, ref y, w, prefabComp, prefabMap);
            }
            y += 6;
            compIndex++;
        }

        // Add Component: acilir tip listesi (katalogtan — editor tipleri isimle bilir).
        if (Gui.Button(new Rect(12, y, w - 24, 20), _addingComp ? "(close)" : "Add Component"))
            _addingComp = !_addingComp;
        y += RowH;
        if (_addingComp)
        {
            foreach (var entry in App.Catalog.Entries)
            {
                if (Gui.Button(new Rect(24, y, w - 36, 19), entry.Name))
                {
                    es.AddComponent(g, entry.Name);
                    _addingComp = false;
                    break;
                }
                y += RowH - 2;
            }
        }
        Gui.EndScrollView();
    }

    bool _addingComp;
    bool _showOverrides;
    // Katlanmis component'ler: key = (goId << 32) | compIndex. Default: acik.
    readonly HashSet<long> _collapsed = new();
    static readonly int _hdrHash = "InspectorCompHeader".GetHashCode();

    static long CompKey(int goId, int compIndex) => ((long)goId << 32) | (uint)compIndex;

    // Cizimsiz tiklama kontrolu (baslik arka planini kendimiz cizeriz).
    static bool HeaderClick(in Rect rect)
    {
        int id = GuiUtility.GetControlID(_hdrHash, FocusType.Passive);
        var ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                    ev.Use();
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    return rect.Contains(ev.MousePosition);
                }
                break;
        }
        return false;
    }

    // --- .asset dosyasi duzenleme: sema uzerinden dogrudan nesneye, her degisiklik
    // dosyaya yazilir (v1 undo'suz). Editor tip BILMEZ: tip adi dosyadan, sinif
    // [Serializable] taramasindan (App.AssetTypes) gelir. ---
    string _aPath;
    object _aObj;
    int _aSeenChange = -1;
    SerializedType.FieldSchema[] _aSchema;
    readonly Dictionary<string, char[]> _aStrBufs = new();
    readonly Dictionary<string, int> _aStrLens = new();

    bool DrawAssetInspector(in Rect vis)
    {
        string path = Selection.AssetPath;
        if (path == null || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            return false;
        // Dosya diskte degistiyse (watcher) cache dusur: taze degerler gorunsun.
        if (_aSeenChange != AssetWatcher.AssetChangeVersion)
        {
            _aSeenChange = AssetWatcher.AssetChangeVersion;
            if (_aPath != null && string.Equals(System.IO.Path.GetFullPath(_aPath),
                    AssetWatcher.LastChangedAsset, StringComparison.OrdinalIgnoreCase))
                _aPath = null;
        }
        if (_aPath != path)
        {
            _aPath = path;
            _aObj = null;
            _aSchema = null;
            _aStrBufs.Clear();
            _aStrLens.Clear();
            string tn = ObjectSerializer.TypeNameOf(path);
            var t = App.AssetTypes.Find(x => x.Name == tn);
            if (t != null)
            {
                _aObj = Activator.CreateInstance(t);
                ObjectSerializer.LoadInto(_aObj, path, App.Assets);
                _aSchema = SerializedType.Build(t);
            }
        }
        float contentHeight = MeasureAssetContentHeight();
        _assetScroll = Gui.BeginScrollView(new Rect(0, 0, vis.width, vis.height), _assetScroll,
            new Rect(0, 0, vis.width - 20, contentHeight));
        float w = vis.width - 24;
        float y = 2;
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(4, y, w, 20),
                System.IO.Path.GetFileName(path), Gui.FontSize - 1f, new Color(230, 234, 244, 255), false, 2);
        y += RowH + 2;
        if (_aObj == null)
        {
            Gui.Label(new Rect(4, y, vis.width - 8, 20), "(unknown asset type)");
            Gui.EndScrollView();
            return true;
        }
        bool changed = false;
        foreach (var f in _aSchema)
            DrawAssetField(f, ref y, w, ref changed);
        if (changed)
        {
            AssetWatcher.NoteSelfWrite(_aPath);
            ObjectSerializer.Save(_aObj, _aPath, App.Assets);
        }
        Gui.EndScrollView();
        return true;
    }

    void DrawAssetField(SerializedType.FieldSchema f, ref float y, float w, ref bool changed)
        => DrawAssetSchemaField(_aObj, f, "a:" + f.Name, 0, ref y, w, ref changed, SaveAssetNow);

    DocNode _ovCache;
    int _ovFrame = -1;
    int _ovRootId;

    // Instance'in override listesi (frame basina bir kez diff'lenir; yaml diff'iyle ayni yol).
    // KRITIK: bir kontrol basiliyken (drag) liste DONDURULUR — satir sayisi degisirse
    // sirali control-ID'ler kayar ve drag baska alana akar (pos/rot hayalet override'i).
    DocNode Overrides(EditorScene es, SceneDoc.GoDoc instRoot)
    {
        bool frozen = (GuiUtility.HotControl != 0 || GuiUtility.KeyboardControl != 0)
            && _ovRootId == instRoot.Id && _ovFrame >= 0;
        if (frozen || (_ovFrame == Time.frameCount && _ovRootId == instRoot.Id))
            return _ovCache;
        _ovFrame = Time.frameCount;
        _ovRootId = instRoot.Id;
        _ovCache = es.Doc.BuildOverrides(instRoot, App.Catalog, App.Assets);
        return _ovCache;
    }

    // Unity modeli: instance basligi + acilir Overrides listesi (Revert/Apply burada).
    void DrawPrefabHeader(EditorScene es, SceneDoc.GoDoc instRoot, ref float y, float w)
    {
        var ovs = Overrides(es, instRoot);
        int n = ovs?.Items.Count ?? 0;
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(new Rect(2, y, w, 20), new Color(38, 46, 66, 255), 0);
            string ppath = App.Assets?.ResolvePath(instRoot.PrefabGuid) ?? "?";
            int slash = ppath.LastIndexOf('/');
            if (slash >= 0) ppath = ppath[(slash + 1)..];
            int dot = ppath.IndexOf('.');
            if (dot > 0) ppath = ppath[..dot];
            GuiRenderer.DrawTextIn(new Rect(8, y, w - 130, 20), "Prefab: " + ppath, Gui.FontSize - 3f,
                new Color(150, 180, 235, 255), false, 2);
        }
        if (Gui.Button(new Rect(w - 120, y + 1, 118, 18),
                _showOverrides ? $"Overrides ({n}) -" : $"Overrides ({n}) +"))
            _showOverrides = !_showOverrides;
        y += RowH + 2;
        if (!_showOverrides || ovs == null)
            return;

        foreach (var o in ovs.Items)
        {
            int local = int.TryParse(o.GetScalar("node"), out int l) ? l : 0;
            string comp = o.GetScalar("comp", null);
            string prop = o.GetScalar("prop");
            instRoot.PrefabIds.TryGetValue(local, out int sceneId);
            var tg = es.FindGo(sceneId);
            if (tg == null)
                continue;

            bool canApply = TryDescribe(es, tg, comp, prop, out string label, out var apply, out var revert);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(12, y, w - 130, 18), label, Gui.FontSize - 3f,
                    new Color(200, 205, 215, 255), false, 2);
            if (Gui.Button(new Rect(w - 118, y, 56, 18), "Revert"))
            {
                revert?.Invoke();
                break; // liste degisti: bu frame'i kes
            }
            if (canApply && Gui.Button(new Rect(w - 58, y, 56, 18), "Apply"))
            {
                apply?.Invoke();
                break;
            }
            y += RowH - 2;
        }
        y += 4;
    }

    // Override girdisini cozer: okunur etiket + revert/apply eylemleri.
    // canApply=false: yalniz Revert (removeComp/addComp/enabled apply'i v2).
    bool TryDescribe(EditorScene es, SceneDoc.GoDoc tg, string comp, string prop,
        out string label, out Action apply, out Action revert)
    {
        apply = null;
        revert = null;
        if (string.IsNullOrEmpty(comp))
        {
            if (prop == "addComp")
            {
                label = tg.Name + ": + component (instance)";
                var last = tg.Components.Count > 0 ? tg.Components[^1] : null;
                revert = () => { if (last != null) es.RemoveComponent(tg, last); };
                return false;
            }
            label = tg.Name + "." + prop;
            bool ok = es.Doc.TryGetPrefabSource(tg, App.Assets, out _, out var src, out var root2);
            revert = () =>
            {
                if (!ok)
                    return;
                switch (prop)
                {
                    case "pos": es.SetTransform(tg, src.Pos, tg.Rot, tg.Scale); break;
                    case "rot": es.SetTransform(tg, tg.Pos, src.Rot, tg.Scale); break;
                    case "scale": es.SetTransform(tg, tg.Pos, tg.Rot, src.Scale); break;
                    case "name": es.SetName(tg, src.Name); break;
                    case "active": es.SetActive(tg, src.Active); break;
                }
            };
            apply = () => es.ApplyNodeFieldToPrefab(tg, prop);
            return prop is "pos" or "rot" or "scale" or "name" or "active";
        }
        var cd = SceneDoc.FindCompByAddress(tg, comp);
        if (prop == "removeComp")
        {
            label = tg.Name + "." + comp + " removed";
            bool ok2 = es.Doc.TryGetPrefabSource(tg, App.Assets, out _, out var src2, out var root3);
            revert = () =>
            {
                var pcd = ok2 ? SceneDoc.FindCompByAddress(src2, comp) : null;
                if (pcd != null)
                    es.RestoreComponent(tg, pcd, root3.PrefabIds);
            };
            return false;
        }
        label = tg.Name + "." + comp + "." + prop;
        if (cd == null)
            return false;
        if (prop == "enabled")
        {
            bool ok3 = es.Doc.TryGetPrefabSource(tg, App.Assets, out _, out var src3, out _);
            var pcd3 = ok3 ? SceneDoc.FindCompByAddress(src3, comp) : null;
            revert = () => { if (pcd3 != null) es.SetEnabled(tg, cd, pcd3.Enabled); };
            return false;
        }
        var entry = App.Catalog?.Find(cd.Type);
        var f = entry != null ? SerializedType.Find(entry.Schema, prop) : null;
        if (f == null)
            return false;
        bool ok4 = es.Doc.TryGetPrefabSource(tg, App.Assets, out _, out var src4, out var root4);
        var pcd4 = ok4 ? SceneDoc.FindCompByAddress(src4, comp) : null;
        revert = () =>
        {
            if (pcd4 != null)
                es.RevertField(tg, cd, f, SceneDoc.RemappedPrefabValue(pcd4, f, root4.PrefabIds));
        };
        apply = () => es.ApplyFieldToPrefab(tg, cd, f);
        return true;
    }
    readonly char[] _nameBuf = new char[64];
    int _nameLen;
    int _nameForId;

    static readonly string[] _layerNames =
        { "Default", "Layer 1", "Layer 2", "Layer 3", "Layer 4", "Layer 5", "Layer 6", "UI" };

    bool BufferEquals(string s)
    {
        if (s.Length != _nameLen)
            return false;
        for (int i = 0; i < _nameLen; i++)
            if (s[i] != _nameBuf[i])
                return false;
        return true;
    }

    void DrawField(EditorScene es, SceneDoc.GoDoc g, SceneDoc.CompDoc cd,
        SerializedType.FieldSchema f, ref float y, float w,
        SceneDoc.CompDoc prefabComp = null, Dictionary<int, int> prefabMap = null)
    {
        var node = FindProp(cd, f);

        // Prefab override tespiti: prefab'in remap'li degeriyle yapisal kiyas.
        // Yalniz GOSTERGE (mavi serit) — Revert/Apply tepedeki Overrides panelinden (Unity modeli).
        DocNode prefabVal = prefabComp != null ? SceneDoc.RemappedPrefabValue(prefabComp, f, prefabMap) : null;
        bool overridden = prefabComp != null && !(node == null && prefabVal == null)
            && !DocNode.Equal(node, prefabVal);
        if (overridden && Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(4, y + 1, 3, 16), new Color(95, 155, 245, 255), 1);

        DrawDocRootField(es, g, cd, f, node, ref y, w);
    }

    static DocNode FindProp(SceneDoc.CompDoc cd, SerializedType.FieldSchema f)
        => EditorScene.FindProp(cd, f);

    // Ref alaninin okunur gosterimi: Texture=yol, GoRef=ad, CompRef=ad:Tip.
    static string RefDisplay(in SerializedType.FieldSchema f, string s)
    {
        if (string.IsNullOrEmpty(s))
            return "(none)";
        switch (f.Kind)
        {
            case SerializedType.Kind.Asset:
                return App.Assets?.ResolvePath(s) ?? s;
            case SerializedType.Kind.GoRef:
                return int.TryParse(s, out int goId)
                    ? App.EditScene.FindGo(goId)?.Name ?? s : s;
            default: // CompRef "goId:Tip:idx"
                {
                    int c1 = s.IndexOf(':');
                    if (c1 > 0 && int.TryParse(s.Substring(0, c1), out int cid))
                    {
                        var target = App.EditScene.FindGo(cid);
                        if (target != null)
                            return target.Name + " (" + s.Substring(c1 + 1) + ")";
                    }
                    return s;
                }
        }
    }

    static string Summary(DocNode node, string s)
    {
        if (node == null)
            return "(none)";
        if (node.IsSeq)
            return node.Items.Count + " items";
        if (node.IsMap)
            return "{...}";
        return s.Length > 0 ? s : "(none)";
    }

    static float SafeFloat(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    static bool Same(Vec3 a, Vec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    static string V3(Vec3 v, bool three)
        => three
            ? v.x.ToString("R", CultureInfo.InvariantCulture) + " " + v.y.ToString("R", CultureInfo.InvariantCulture) + " " + v.z.ToString("R", CultureInfo.InvariantCulture)
            : v.x.ToString("R", CultureInfo.InvariantCulture) + " " + v.y.ToString("R", CultureInfo.InvariantCulture);

    static Vec3 Vec3Drags(in Rect rect, Vec3 v, bool three)
    {
        int parts = three ? 3 : 2;
        float pw = rect.width / parts - 3;
        float x = AxisDrag(new Rect(rect.x, rect.y, pw, 18), "X", v.x);
        float yv = AxisDrag(new Rect(rect.x + pw + 4, rect.y, pw, 18), "Y", v.y);
        float z = three ? AxisDrag(new Rect(rect.x + (pw + 4) * 2, rect.y, pw, 18), "Z", v.z) : v.z;
        return new Vec3(x, yv, z);
    }

    // Unity paritesi: eksen etiketi drag tutamaci, alan tikla-yaz.
    static float AxisDrag(in Rect cell, ReadOnlySpan<char> axis, float v)
    {
        const float lw = 12;
        var labelRect = new Rect(cell.x, cell.y, lw, cell.height);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(labelRect, axis, Gui.FontSize - 4f,
                new Color(150, 153, 163, 255), false, 2);
        v = Gui.DragZone(labelRect, v, 0.05f);
        return Gui.DragFloat(new Rect(cell.x + lw, cell.y, cell.width - lw, cell.height), v, 0.05f);
    }

    Vec3 Vec3Row(ref float y, float w, ReadOnlySpan<char> label, Vec3 v)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(12, y, LabelW, 18), label, Gui.FontSize - 3f,
                new Color(165, 168, 178, 255), false, 2);
        var nv = Vec3Drags(new Rect(12 + LabelW, y, w - LabelW - 12, 18), v, true);
        y += RowH;
        return nv;
    }

    // Driven (hesaplanan) transform satiri: kontrol yok, soluk salt-okunur degerler.
    void Vec3RowDisabled(ref float y, float w, ReadOnlySpan<char> label, Vec3 v)
    {
        if (Event.Current.Type == EventType.Repaint)
        {
            var dim = new Color(120, 123, 132, 255);
            GuiRenderer.DrawTextIn(new Rect(12, y, LabelW, 18), label, Gui.FontSize - 3f, dim, false, 2);
            var rect = new Rect(12 + LabelW, y, w - LabelW - 12, 18);
            float pw = rect.width / 3 - 3;
            Span<char> buf = stackalloc char[32];
            for (int i = 0; i < 3; i++)
            {
                float val = i == 0 ? v.x : i == 1 ? v.y : v.z;
                var cell = new Rect(rect.x + (pw + 4) * i, y, pw, 18);
                GuiRenderer.DrawRect(cell, new Color(38, 40, 46, 255), 1);
                val.TryFormat(buf, out int len, "0.00", CultureInfo.InvariantCulture);
                GuiRenderer.DrawTextIn(cell, buf.Slice(0, len), Gui.FontSize - 3f, dim, false, 4);
            }
        }
        y += RowH;
    }

    // Prefab-farkindalikli transform satiri: override'da yalniz mavi serit
    // (Revert/Apply tepedeki Overrides panelinden).
    Vec3 Vec3RowPrefab(ref float y, float w, ReadOnlySpan<char> label, Vec3 v, Vec3? src)
    {
        bool overridden = src.HasValue && !Same(v, src.Value);
        if (overridden && Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(4, y + 1, 3, 16), new Color(95, 155, 245, 255), 1);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(12, y, LabelW, 18), label, Gui.FontSize - 3f,
                new Color(165, 168, 178, 255), false, 2);
        var nv = Vec3Drags(new Rect(12 + LabelW, y, w - LabelW - 12, 18), v, true);
        y += RowH;
        return nv;
    }

    static void Header(ref float y, float w, ReadOnlySpan<char> title)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(4, y, w, 20), title, Gui.FontSize - 1f, new Color(230, 234, 244, 255), false, 2);
        y += RowH;
    }

    float MeasureHeight(SceneDoc.GoDoc g)
    {
        float h = RowH + 4 + RowH + RowH * 4 + 8;
        var instRoot = App.EditScene.Doc.PrefabRootOf(g);
        if (instRoot != null)
        {
            h += RowH + 2; // prefab basligi
            if (_showOverrides)
                h += (Overrides(App.EditScene, instRoot)?.Items.Count ?? 0) * (RowH - 2) + 4;
        }
        int componentIndex = 0;
        foreach (var cd in g.Components)
        {
            if (_collapsed.Contains(CompKey(g.Id, componentIndex)))
            {
                h += RowH + 2;
                componentIndex++;
                continue;
            }
            var entry = App.Catalog?.Find(cd.Type);
            h += RowH + 6;
            if (entry == null)
                h += RowH;
            else
            {
                foreach (var field in entry.Schema)
                {
                    if (!DocVisible(field, cd))
                        continue;
                    h += MeasureDocField(field, FindProp(cd, field),
                        "c:" + g.Id + ":" + componentIndex + ":" + field.Name);
                }
            }
            if (entry != null && entry.Previewable)
                h += RowH;
            componentIndex++;
        }
        h += RowH; // Add Component butonu
        if (_addingComp && App.Catalog != null)
            foreach (var _ in App.Catalog.Entries)
                h += RowH - 2;
        return h;
    }
}
