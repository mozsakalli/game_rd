using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Digitoy.Mcp
{
    public static partial class Commands
    {
        [RpcCommand("rpc.describe", "Tum komutlar + parametre semasi (makine-okunur)")]
        static JNode Describe() => RpcRegistry.Describe();

        // --- gozlem ---

        static readonly Dictionary<int, JNode> _diffBaseline = new Dictionary<int, JNode>();
        static bool _diffWithProps = true;

        [RpcCommand("scene.tree", "KOMPAKT hiyerarsi (ONCE BUNU CAGIR): satir basina '{id} {girinti}{ad} [inactive] {prefab} (component tipleri)'. root: id ya da 'A/B' yolu (bos = tum sahne); depth: en fazla derinlik (0 = sinirsiz); comps=false → tipler gizli")]
        static JNode SceneTree(string root = "", int depth = 0, bool comps = true)
        {
            var lines = JNode.Seq();
            int count = 0;
            void Walk(Transform t, int d)
            {
                if (depth > 0 && d >= depth) return;
                var go = t.gameObject;
                var sb = new System.Text.StringBuilder();
                sb.Append(go.GetInstanceID()).Append(' ').Append(' ', d * 2).Append(go.name);
                if (!go.activeSelf) sb.Append(" [inactive]");
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(go) && PrefabUtility.GetCorrespondingObjectFromSource(go) is GameObject src)
                    sb.Append(" {").Append(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(src))).Append('}');
                if (comps)
                {
                    var names = new List<string>();
                    foreach (var c in go.GetComponents<Component>())
                    {
                        if (c == null) { names.Add("Missing"); continue; }
                        if (c is Transform || c is CanvasRenderer) continue;
                        names.Add(TypeName(c.GetType()));
                    }
                    if (names.Count > 0) sb.Append(" (").Append(string.Join(", ", names)).Append(')');
                }
                lines.Push(JNode.Str_(sb.ToString()));
                count++;
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1);
            }
            if (root.Length > 0)
            {
                var g = (int.TryParse(root, out int id) ? FindGo(id) : FindByPath(root)) ?? throw new RpcError($"GO yok: '{root}'");
                Walk(g.transform, 0);
            }
            else
                foreach (var r in ActiveScene().GetRootGameObjects()) Walk(r.transform, 0);
            var res = JNode.Map();
            res.Add("scene", ActiveScene().name);
            res.Add("lines", lines);
            res.Add("count", count);
            return res;
        }

        [RpcCommand("scene.dump", "Acik sahnenin (ya da prefab stage'in) tam dokumu: id/parent/ad/transform/component'ler; props=true ile tum alan degerleri (BUYUK — once scene.tree, tek component icin prop.get). diff baseline'ini sifirlar")]
        static JNode SceneDump(bool props = false)
        {
            var scene = ActiveScene();
            var root = JNode.Map();
            root.Add("name", scene.name);
            root.Add("path", scene.path ?? "");
            root.Add("dirty", scene.isDirty);
            root.Add("playing", Application.isPlaying);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) root.Add("prefabStage", stage.assetPath);
            var objects = JNode.Seq();
            _diffBaseline.Clear();
            _diffWithProps = props;
            foreach (var go in AllGos())
            {
                var og = DumpGo(go, props);
                _diffBaseline[go.GetInstanceID()] = og.Clone();
                objects.Push(og);
            }
            root.Add("objects", objects);
            return root;
        }

        [RpcCommand("scene.diff", "Son scene.dump/diff'ten beri eklenen/silinen/degisen GO'lar")]
        static JNode SceneDiff()
        {
            var added = JNode.Seq();
            var changed = JNode.Seq();
            var removed = JNode.Seq();
            var seen = new HashSet<int>();
            foreach (var go in AllGos())
            {
                int id = go.GetInstanceID();
                seen.Add(id);
                var now = DumpGo(go, _diffWithProps);
                if (!_diffBaseline.TryGetValue(id, out var was)) added.Push(now.Clone());
                else if (!JNode.Equal(now, was)) changed.Push(now.Clone());
                _diffBaseline[id] = now;
            }
            var gone = new List<int>();
            foreach (var id in _diffBaseline.Keys)
                if (!seen.Contains(id)) gone.Add(id);
            gone.Sort();
            foreach (var id in gone)
            {
                removed.Push(JNode.Num(id));
                _diffBaseline.Remove(id);
            }
            var root = JNode.Map();
            root.Add("added", added);
            root.Add("changed", changed);
            root.Add("removed", removed);
            return root;
        }

        [RpcCommand("scene.dumpLive", "Canli dokum (play'de runtime degerler; Unity'de edit ve play ayni dunyadir — scene.dump'in baseline'i bozmayan hali). go: yalniz bu GO'nun alt agaci (id ya da 'A/B' yolu)")]
        static JNode DumpLive(string go = "")
        {
            var root = JNode.Map();
            root.Add("playing", Application.isPlaying);
            root.Add("frame", Time.frameCount);
            var objects = JNode.Seq();
            List<GameObject> list;
            if (go.Length > 0)
            {
                var target = (int.TryParse(go, out int id) ? FindGo(id) : FindByPath(go))
                    ?? throw new RpcError($"GO yok: '{go}'");
                list = new List<GameObject>();
                CollectGos(target.transform, list);
            }
            else list = AllGos();
            foreach (var g in list)
                objects.Push(DumpGo(g, true));
            root.Add("objects", objects);
            return root;
        }

        // --- sahne yapisi ---

        [RpcCommand("scene.addGo", "Yeni GameObject ekler, id doner. parent=0 kok; insertBefore: kardes id (o kardesin onune); ui=true → RectTransform ile")]
        static JNode SceneAddGo(string name = "GameObject", int parent = 0, int insertBefore = 0, bool ui = false)
        {
            var go = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP add " + name);
            var scene = ActiveScene();
            if (parent != 0)
            {
                var p = RequireGo(parent);
                Undo.SetTransformParent(go.transform, p.transform, false, "MCP parent");
            }
            else if (go.scene != scene)
                SceneManager.MoveGameObjectToScene(go, scene);
            if (go.transform is RectTransform rt)
            {
                rt.anchoredPosition3D = Vector3.zero;
                rt.localScale = Vector3.one;
            }
            if (insertBefore != 0)
                PlaceBefore(go, insertBefore);
            MarkDirty();
            var r = JNode.Map();
            r.Add("id", go.GetInstanceID());
            r.Add("name", go.name);
            return r;
        }

        static void PlaceBefore(GameObject go, int insertBefore)
        {
            var sib = RequireGo(insertBefore);
            if (sib.transform.parent != go.transform.parent)
                throw new RpcError($"insertBefore={insertBefore} hedef parent'in cocugu degil");
            int idx = sib.transform.GetSiblingIndex();
            int my = go.transform.GetSiblingIndex();
            Undo.SetSiblingIndex(go.transform, my < idx ? idx - 1 : idx, "MCP reorder");
        }

        [RpcCommand("scene.removeGo", "GameObject'i (alt agaciyla) siler")]
        static JNode SceneRemoveGo(int id)
        {
            var go = RequireGo(id);
            Undo.DestroyObjectImmediate(go);
            MarkDirty();
            return JNode.Map().Add("removed", id);
        }

        [RpcCommand("scene.rename", "GameObject adini degistirir")]
        static JNode SceneRename(int id, string name)
        {
            var go = RequireGo(id);
            Undo.RecordObject(go, "MCP rename");
            go.name = name;
            MarkDirty();
            return JNode.Map().Add("id", id).Add("name", name);
        }

        [RpcCommand("scene.reparent", "GO'yu tasir: parent (0=kok), insertBefore (kardes id; 0 = sona). Ayni parent + insertBefore = kardes sirasi degisimi (cizim/UI sirasi)")]
        static JNode SceneReparent(int id, int parent = 0, int insertBefore = 0, bool worldPositionStays = false)
        {
            var go = RequireGo(id);
            var newParent = parent != 0 ? RequireGo(parent).transform : null;
            if (newParent != null && (newParent == go.transform || newParent.IsChildOf(go.transform)))
                throw new RpcError("GO kendi alt agacina tasinamaz");
            Undo.SetTransformParent(go.transform, newParent, worldPositionStays, "MCP reparent");
            if (newParent == null && go.scene != ActiveScene())
                SceneManager.MoveGameObjectToScene(go, ActiveScene());
            if (insertBefore != 0) PlaceBefore(go, insertBefore);
            else Undo.SetSiblingIndex(go.transform, newParent != null ? newParent.childCount - 1 : ActiveScene().rootCount - 1, "MCP reorder");
            MarkDirty();
            return JNode.Map().Add("id", id).Add("parent", parent).Add("siblingIndex", go.transform.GetSiblingIndex());
        }

        [RpcCommand("go.setActive", "GO aktif/pasif")]
        static JNode GoSetActive(int id, bool active)
        {
            var go = RequireGo(id);
            Undo.RecordObject(go, "MCP active");
            go.SetActive(active);
            MarkDirty();
            return JNode.Map().Add("id", id).Add("active", active);
        }

        [RpcCommand("go.setLayer", "GO katmani (ad ya da index); children=true alt agaca da uygular")]
        static JNode GoSetLayer(int id, string layer, bool children = false)
        {
            var go = RequireGo(id);
            int l = int.TryParse(layer, out int li) ? li : LayerMask.NameToLayer(layer);
            if (l < 0 || l > 31) throw new RpcError($"katman yok: '{layer}'");
            var list = new List<GameObject>();
            if (children) CollectGos(go.transform, list); else list.Add(go);
            foreach (var g in list) { Undo.RecordObject(g, "MCP layer"); g.layer = l; }
            MarkDirty();
            return JNode.Map().Add("id", id).Add("layer", l).Add("count", list.Count);
        }

        [RpcCommand("select.set", "Editor secimi (Inspector/Hierarchy): id listesi ya da tek id; 0 = temizle")]
        static JNode SelectSet(JNode ids)
        {
            var list = new List<Object>();
            if (ids != null)
            {
                if (ids.IsSeq) foreach (var n in ids.Items) list.Add(RequireGo(ParseInt(n.Scalar, "ids")));
                else if (ids.IsScalar && ids.Str != "0") list.Add(RequireGo(ParseInt(ids.Str, "ids")));
            }
            Selection.objects = list.ToArray();
            return JNode.Map().Add("count", list.Count);
        }

        [RpcCommand("select.asset", "Project panelinde asset secer (Inspector onizleme)")]
        static JNode SelectAsset(string path)
        {
            var o = AssetDatabase.LoadMainAssetAtPath(path) ?? throw new RpcError($"asset yok: {path}");
            Selection.activeObject = o;
            EditorGUIUtility.PingObject(o);
            return JNode.Map().Add("path", path);
        }

        // --- oturum ---

        [RpcCommand("scene.save", "Acik sahneyi (ya da prefab stage'i) kaydeder. YALNIZ kullanici isterse")]
        static JNode SceneSave()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                stage.ClearDirtiness();
                return JNode.Map().Add("saved", stage.assetPath);
            }
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
                throw new RpcError("sahnenin yolu yok: scene.saveAs(path)");
            if (!EditorSceneManager.SaveScene(scene)) throw new RpcError("kaydedilemedi");
            return JNode.Map().Add("saved", scene.path);
        }

        [RpcCommand("scene.saveAs", "Acik sahneyi yeni yola kaydeder (Assets/..unity)")]
        static JNode SceneSaveAs(string path)
        {
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) path += ".unity";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path)) throw new RpcError("kaydedilemedi");
            return JNode.Map().Add("saved", path);
        }

        [RpcCommand("scene.new", "Yeni bos sahne acar (kaydedilmemis degisiklikler kaybolur; additive=false). setup: empty|default")]
        static JNode SceneNew(string setup = "default")
        {
            if (Application.isPlaying) throw new RpcError("play modunda sahne acilamaz (play.stop)");
            var s = setup == "empty" ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            var scene = EditorSceneManager.NewScene(s, NewSceneMode.Single);
            return JNode.Map().Add("name", scene.name);
        }

        [RpcCommand("scene.open", "Sahne acar (Assets/..unity). Kaydedilmemis degisiklikler kaybolur")]
        static JNode SceneOpen(string path)
        {
            if (Application.isPlaying) throw new RpcError("play modunda sahne acilamaz (play.stop)");
            if (!File.Exists(path)) throw new RpcError($"sahne yok: {path}");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return JNode.Map().Add("name", scene.name).Add("path", scene.path);
        }

        [RpcCommand("scene.list", "Projedeki sahneler (yol + build index)")]
        static JNode SceneList()
        {
            var seq = JNode.Seq();
            var build = new Dictionary<string, int>();
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++) build[EditorBuildSettings.scenes[i].path] = i;
            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = JNode.Map().Add("path", path).Add("guid", guid);
                if (build.TryGetValue(path, out int bi)) m.Add("buildIndex", bi);
                seq.Push(m);
            }
            return JNode.Map().Add("scenes", seq).Add("active", SceneManager.GetActiveScene().path);
        }

        [RpcCommand("prefab.open", "Prefab'i duzenleme icin acar (prefab stage); sahne komutlari artik prefab icinde calisir")]
        static JNode PrefabOpen(string path)
        {
            if (!File.Exists(path)) throw new RpcError($"prefab yok: {path}");
            var stage = PrefabStageUtility.OpenPrefab(path) ?? throw new RpcError("prefab stage acilamadi");
            return JNode.Map().Add("path", stage.assetPath).Add("root", stage.prefabContentsRoot.GetInstanceID());
        }

        [RpcCommand("prefab.close", "Prefab stage'den cikar; save=true once kaydeder")]
        static JNode PrefabClose(bool save = false)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage() ?? throw new RpcError("acik prefab stage yok");
            if (save) PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
            stage.ClearDirtiness();
            StageUtility.GoToMainStage();
            return JNode.Map().Add("closed", stage.assetPath).Add("saved", save);
        }

        [RpcCommand("prefab.instantiate", "Prefab'i sahneye ornekler (baglantili instance); parent=0 kok")]
        static JNode PrefabInstantiate(string path, int parent = 0, string name = "")
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new RpcError($"prefab yok: {path}");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ActiveScene());
            Undo.RegisterCreatedObjectUndo(go, "MCP instantiate");
            if (parent != 0) Undo.SetTransformParent(go.transform, RequireGo(parent).transform, false, "MCP parent");
            if (name.Length > 0) go.name = name;
            MarkDirty();
            return JNode.Map().Add("id", go.GetInstanceID()).Add("name", go.name);
        }

        // --- component'ler ---

        [RpcCommand("comp.types", "Component tipleri (filter: ad icinde gecen metin; builtin=false → yalniz proje/paket script'leri)")]
        static JNode CompTypes(string filter = "", bool builtin = true)
        {
            var seq = JNode.Seq();
            var names = new List<string>();
            foreach (var t in AllComponentTypes())
            {
                bool isUnity = t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal);
                if (!builtin && isUnity) continue;
                string n = TypeName(t);
                if (filter.Length > 0 && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                names.Add(n);
            }
            names.Sort(StringComparer.Ordinal);
            foreach (var n in names) seq.Push(JNode.Str_(n));
            return JNode.Map().Add("types", seq).Add("count", names.Count);
        }

        [RpcCommand("comp.schema", "Component tipinin alan semasi: ad/tip/default/enum degerleri/format (ic ice nesneler, listeler, referans bicimleri)")]
        static JNode CompSchema(string type) => PropIO.Schema(RequireCompType(type));

        [RpcCommand("comp.add", "GO'ya component ekler, index doner")]
        static JNode CompAdd(int go, string type)
        {
            var g = RequireGo(go);
            var t = RequireCompType(type);
            var c = Undo.AddComponent(g, t) ?? throw new RpcError($"{type} eklenemedi (DisallowMultiple / RequireComponent?)");
            MarkDirty();
            return JNode.Map().Add("index", CompIndex(c)).Add("type", TypeName(t));
        }

        [RpcCommand("comp.remove", "Component'i index ile siler")]
        static JNode CompRemove(int go, int index)
        {
            var c = RequireComp(RequireGo(go), index);
            if (c is Transform) throw new RpcError("Transform silinemez");
            Undo.DestroyObjectImmediate(c);
            MarkDirty();
            return JNode.Map().Add("removed", index);
        }

        [RpcCommand("comp.setEnabled", "Component enabled (Behaviour/Renderer/Collider)")]
        static JNode CompSetEnabled(int go, int index, bool enabled)
        {
            var c = RequireComp(RequireGo(go), index);
            Undo.RecordObject(c, "MCP enabled");
            switch (c)
            {
                case Behaviour b: b.enabled = enabled; break;
                case Renderer r: r.enabled = enabled; break;
                case Collider col: col.enabled = enabled; break;
                default: throw new RpcError($"{TypeName(c.GetType())} enabled tasimaz");
            }
            MarkDirty();
            return JNode.Map().Add("index", index).Add("enabled", enabled);
        }

        [RpcCommand("prop.set", "Component alanina deger yazar (undo'lu). comp: index ya da tip adi (ilk eslesen). prop ic ice yol: 'Tracks[0].Keys[1].Value' — liste index'i eleman sayisina esitse SONA EKLER; value null → liste elemanini SILER / alani default'a ceker. Deger bicimleri: rpc.guide values")]
        static JNode PropSet(int go, string comp, string prop, JNode value = null)
        {
            var g = RequireGo(go);
            var c = ResolveComp(g, comp);
            PropIO.Set(c, prop, value);
            MarkDirty();
            var r = JNode.Map().Add("go", go).Add("comp", CompIndex(c)).Add("prop", prop);
            return r;
        }

        [RpcCommand("prop.get", "Component alanini okur (ic ice yol); prop bos = tum alanlar")]
        static JNode PropGet(int go, string comp, string prop = "")
        {
            var c = ResolveComp(RequireGo(go), comp);
            if (prop.Length == 0) return PropIO.DumpComponent(c);
            var so = new SerializedObject(c);
            var p = so.FindProperty(prop.Replace("[", ".Array.data[")) ?? throw new RpcError($"alan yok: '{prop}' (comp.schema)");
            return JNode.Map().Add("prop", prop).Add("value", PropIO.Dump(p, 1));
        }

        public static Component ResolveComp(GameObject g, string comp)
        {
            if (int.TryParse(comp, out int idx)) return RequireComp(g, idx);
            var t = RequireCompType(comp);
            var c = g.GetComponent(t);
            if (c == null) throw new RpcError($"'{g.name}' uzerinde {comp} yok");
            return c;
        }

        [RpcCommand("transform.set", "Transform: pos/rot(euler)/scale 'x y z' (bos = dokunma); RectTransform icin anchoredPos 'x y', size 'w h', anchorMin/anchorMax/pivot 'x y'")]
        static JNode TransformSet(int go, string pos = "", string rot = "", string scale = "", string anchoredPos = "", string size = "", string anchorMin = "", string anchorMax = "", string pivot = "")
        {
            var g = RequireGo(go);
            var tr = g.transform;
            Undo.RecordObject(tr, "MCP transform");
            if (pos.Length > 0) tr.localPosition = ParseV3(pos, "pos");
            if (rot.Length > 0) tr.localEulerAngles = ParseV3(rot, "rot");
            if (scale.Length > 0) tr.localScale = ParseV3(scale, "scale");
            if (tr is RectTransform rt)
            {
                if (anchorMin.Length > 0) { var f = ParseFloats(anchorMin, 2, "anchorMin"); rt.anchorMin = new Vector2(f[0], f[1]); }
                if (anchorMax.Length > 0) { var f = ParseFloats(anchorMax, 2, "anchorMax"); rt.anchorMax = new Vector2(f[0], f[1]); }
                if (pivot.Length > 0) { var f = ParseFloats(pivot, 2, "pivot"); rt.pivot = new Vector2(f[0], f[1]); }
                if (anchoredPos.Length > 0) { var f = ParseFloats(anchoredPos, 2, "anchoredPos"); rt.anchoredPosition = new Vector2(f[0], f[1]); }
                if (size.Length > 0) { var f = ParseFloats(size, 2, "size"); rt.sizeDelta = new Vector2(f[0], f[1]); }
            }
            else if (anchoredPos.Length + size.Length + anchorMin.Length + anchorMax.Length + pivot.Length > 0)
                throw new RpcError("RectTransform alanlari verildi ama GO'nun Transform'u RectTransform degil (scene.addGo ui=true)");
            MarkDirty();
            return DumpGo(g, false);
        }
    }
}
