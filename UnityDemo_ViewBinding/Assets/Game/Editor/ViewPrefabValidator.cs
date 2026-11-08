using System.Collections.Generic;
using System.Text;
using Game.ViewContract;
using Game.Views;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Remote prefab sözleşmesi denetimi (bundle build'inden önce koştur):
    ///  1. Prefab'ta Game.ViewContract ve Unity dışı hiçbir MonoBehaviour olmamalı (oyun script'i sızmasın).
    ///  2. viewId için kayıtlı controller olmalı.
    ///  3. Controller'ın [Bind] key'lerinin hepsi prefab'tan çözülebilmeli (ViewRefs ya da @isim).
    /// Menü: Tools/ViewBinding Demo/Validate View Prefabs. CI'da -executeMethod ile de çağrılabilir.
    /// </summary>
    public static class ViewPrefabValidator
    {
        static readonly HashSet<string> AllowedAssemblies = new HashSet<string> { "Game.ViewContract" };

        [MenuItem("Tools/ViewBinding Demo/Validate View Prefabs")]
        public static void ValidateMenu()
        {
            if (!ValidateAll(out var report)) Debug.LogError(report);
            else Debug.Log(report);
        }

        public static bool ValidateAll(out string report)
        {
            DemoBootstrap.RegisterViews(); // edit modunda RuntimeInitializeOnLoad koşmaz
            var sb = new StringBuilder();
            bool ok = true;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var refs = prefab != null ? prefab.GetComponent<ViewRefs>() : null;
                if (refs == null) continue;
                ok &= Validate(prefab, refs, sb);
            }
            report = ok ? "[ViewPrefabValidator] all view prefabs OK\n" + sb : "[ViewPrefabValidator] FAILED\n" + sb;
            return ok;
        }

        static bool Validate(GameObject prefab, ViewRefs refs, StringBuilder sb)
        {
            bool ok = true;
            sb.Append("• ").Append(prefab.name).Append(" (").Append(refs.viewId).AppendLine(")");

            // 1. Yabancı script var mı?
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) { sb.AppendLine("   ✗ missing script"); ok = false; continue; }
                var asm = mb.GetType().Assembly.GetName().Name;
                if (asm.StartsWith("Unity") || AllowedAssemblies.Contains(asm)) continue;
                sb.Append("   ✗ game script leaked into prefab: ").Append(mb.GetType().FullName)
                  .Append(" on ").AppendLine(mb.name);
                ok = false;
            }

            // 2. Controller kayıtlı mı?
            if (!ViewRegistry.TryGetController(refs.viewId, out var ctrlType))
            {
                sb.Append("   ✗ no controller registered for viewId '").Append(refs.viewId).AppendLine("'");
                return false;
            }

            // 3. Key'ler çözülüyor mu? (instance yaratmadan, sadece [Bind] alan listesinden)
            foreach (var (key, type, optional) in ViewBinder.RequiredKeys(ctrlType))
            {
                bool found = refs.TryGet(key, out var obj) && Coercible(obj, type);
                if (!found)
                {
                    var t = FindNamed(prefab.transform, "@" + key);
                    found = t != null && Coercible(t, type);
                }
                if (!found && !optional)
                {
                    sb.Append("   ✗ missing key '").Append(key).Append("' (").Append(type.Name).AppendLine(")");
                    ok = false;
                }
            }
            if (ok) sb.AppendLine("   ✓ ok");
            return ok;
        }

        static bool Coercible(Object obj, System.Type want)
        {
            if (want.IsInstanceOfType(obj)) return true;
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (go == null) return false;
            if (want == typeof(GameObject)) return true;
            return typeof(Component).IsAssignableFrom(want) && go.GetComponent(want) != null;
        }

        static Transform FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
