using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Digitoy.Mcp
{
    // Komutlarin ortak yardimcilari: aktif sahne (prefab stage dahil), id cozumleme, deger ayristirma, GO dokumu.
    // GO id = instanceID (oturum ici kararli; undo ile silinen GO'nun id'si gecersizlesir).
    public static partial class Commands
    {
        public static Scene ActiveScene()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null ? stage.scene : SceneManager.GetActiveScene();
        }

        public static void MarkDirty()
        {
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(ActiveScene());
        }

        public static GameObject RequireGo(int id)
        {
            if (id == 0)
                throw new RpcError("GO id 0 kok demektir; bu komut gercek bir GO ister");
            var go = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (go == null)
                throw new RpcError($"GO yok: id {id} (scene.dump ile id'leri gor)");
            return go;
        }

        public static GameObject FindGo(int id) => id == 0 ? null : EditorUtility.InstanceIDToObject(id) as GameObject;

        public static Component RequireComp(GameObject go, int index)
        {
            var comps = go.GetComponents<Component>();
            if (index < 0 || index >= comps.Length)
                throw new RpcError($"'{go.name}' uzerinde component index {index} yok ({comps.Length} adet)");
            if (comps[index] == null)
                throw new RpcError($"'{go.name}' index {index}: eksik script (Missing)");
            return comps[index];
        }

        public static int CompIndex(Component c)
        {
            var comps = c.gameObject.GetComponents<Component>();
            return Array.IndexOf(comps, c);
        }

        public static string LivePath(GameObject go)
        {
            string path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent)
                path = t.gameObject.name + "/" + path;
            return path;
        }

        // "A/B/C" yolu ya da tekil ad ile GO; tek segment ise tum agacta ada bakar.
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var segs = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var roots = ActiveScene().GetRootGameObjects();
            GameObject cur = null;
            foreach (var r in roots)
                if (r.name == segs[0]) { cur = r; break; }
            if (cur == null && segs.Length == 1)
            {
                foreach (var r in roots)
                {
                    cur = FindByNameDeep(r.transform, segs[0]);
                    if (cur != null) break;
                }
                return cur;
            }
            for (int k = 1; cur != null && k < segs.Length; k++)
            {
                var next = cur.transform.Find(segs[k]);
                cur = next != null ? next.gameObject : null;
            }
            return cur;
        }

        static GameObject FindByNameDeep(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindByNameDeep(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // --- deger ayristirma ---

        public static string V3(Vector3 v) => JNode.F(v.x) + " " + JNode.F(v.y) + " " + JNode.F(v.z);
        public static string V2(Vector2 v) => JNode.F(v.x) + " " + JNode.F(v.y);
        public static string V4(Vector4 v) => JNode.F(v.x) + " " + JNode.F(v.y) + " " + JNode.F(v.z) + " " + JNode.F(v.w);
        public static string Col(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c).ToLowerInvariant();

        public static float[] ParseFloats(string s, int count, string what)
        {
            var parts = (s ?? "").Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count)
                throw new RpcError($"'{what}' {count} bilesenli olmali ('x y{(count > 2 ? " z" : "")}{(count > 3 ? " w" : "")}'): '{s}'");
            var r = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]))
                    throw new RpcError($"'{what}' bileseni sayi degil: '{parts[i]}'");
            return r;
        }

        public static Vector3 ParseV3(string s, string what)
        {
            var f = ParseFloats(s, 3, what);
            return new Vector3(f[0], f[1], f[2]);
        }

        public static bool ParseBool(string s, string what)
        {
            if (s == "true") return true;
            if (s == "false") return false;
            throw new RpcError($"'{what}' true|false olmali: '{s}'");
        }

        public static int ParseInt(string s, string what)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            throw new RpcError($"'{what}' tam sayi degil: '{s}'");
        }

        public static Color ParseColor(string s, string what)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '#' || !ColorUtility.TryParseHtmlString(s, out var c))
                throw new RpcError($"'{what}' renk '#RRGGBB' ya da '#RRGGBBAA' olmali: '{s}'");
            return c;
        }

        // --- tip cozumleme ---

        static Dictionary<string, List<Type>> _compTypesByShort;

        static void EnsureCompTypes()
        {
            if (_compTypesByShort != null) return;
            _compTypesByShort = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (var t in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (t.IsAbstract || t.IsGenericTypeDefinition || t.IsNotPublic && !t.IsNestedPublic && t.Namespace == null) continue;
                if (!_compTypesByShort.TryGetValue(t.Name, out var list))
                    _compTypesByShort[t.Name] = list = new List<Type>();
                list.Add(t);
            }
        }

        public static IEnumerable<Type> AllComponentTypes()
        {
            EnsureCompTypes();
            foreach (var kv in _compTypesByShort)
                foreach (var t in kv.Value)
                    yield return t;
        }

        // Kisa ad ("Image") ya da tam ad ("UnityEngine.UI.Image"); kisa ad belirsizse tam ad ister.
        public static Type RequireCompType(string name)
        {
            EnsureCompTypes();
            if (string.IsNullOrEmpty(name))
                throw new RpcError("component tipi bos");
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                string shortName = name.Substring(dot + 1);
                if (_compTypesByShort.TryGetValue(shortName, out var cands))
                    foreach (var t in cands)
                        if (t.FullName == name) return t;
                throw new RpcError($"bilinmeyen component tipi '{name}' (comp.types)");
            }
            if (!_compTypesByShort.TryGetValue(name, out var list))
                throw new RpcError($"bilinmeyen component tipi '{name}' (comp.types)");
            if (list.Count == 1)
                return list[0];
            // UnityEngine'inkini tercih et; yoksa belirsiz.
            Type unity = null;
            foreach (var t in list)
                if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)) { unity = unity == null ? t : null; if (unity == null) break; }
            if (unity != null) return unity;
            var names = new List<string>();
            foreach (var t in list) names.Add(t.FullName);
            throw new RpcError($"'{name}' belirsiz: {string.Join(" | ", names)} — tam ad ver");
        }

        public static string TypeName(Type t) => t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) ? t.Name : t.FullName;

        // --- GO dokumu ---

        public static JNode DumpGo(GameObject go, bool withProps)
        {
            var og = JNode.Map();
            og.Add("id", go.GetInstanceID());
            og.Add("parent", go.transform.parent != null ? go.transform.parent.gameObject.GetInstanceID() : 0);
            og.Add("name", go.name);
            if (!go.activeSelf)
                og.Add("active", false);
            if (go.layer != 0)
                og.Add("layer", go.layer);
            var tr = go.transform;
            if (tr is RectTransform rt)
            {
                og.Add("anchoredPos", V2(rt.anchoredPosition));
                og.Add("size", V2(rt.sizeDelta));
                og.Add("anchorMin", V2(rt.anchorMin));
                og.Add("anchorMax", V2(rt.anchorMax));
                og.Add("pivot", V2(rt.pivot));
            }
            else
                og.Add("pos", V3(tr.localPosition));
            og.Add("rot", V3(tr.localEulerAngles));
            og.Add("scale", V3(tr.localScale));
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefab != null && PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                og.Add("prefab", AssetDatabase.GetAssetPath(prefab));
            var comps = go.GetComponents<Component>();
            var seq = JNode.Seq();
            for (int i = 0; i < comps.Length; i++)
            {
                var oc = JNode.Map();
                var c = comps[i];
                if (c == null) { oc.Add("type", "Missing"); seq.Push(oc); continue; }
                oc.Add("type", TypeName(c.GetType()));
                if (c is Behaviour b && !b.enabled) oc.Add("enabled", false);
                else if (c is Renderer r && !r.enabled) oc.Add("enabled", false);
                if (withProps && !(c is Transform))
                {
                    var props = PropIO.DumpComponent(c);
                    if (props.Fields.Count > 0)
                        oc.Add("props", props);
                }
                seq.Push(oc);
            }
            og.Add("components", seq);
            return og;
        }

        public static void CollectGos(Transform t, List<GameObject> into)
        {
            into.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectGos(t.GetChild(i), into);
        }

        public static List<GameObject> AllGos()
        {
            var list = new List<GameObject>();
            foreach (var r in ActiveScene().GetRootGameObjects())
                CollectGos(r.transform, list);
            return list;
        }
    }
}
