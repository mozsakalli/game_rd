using System;
using System.Collections.Generic;
using System.Reflection;
using Game.ViewContract;
using UnityEngine;

namespace Game.Views
{
    /// <summary>Controller alanını prefab'taki bir referansa bağlar. Key, ViewRefs'teki key ya da "@key" isimli GO.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class BindAttribute : Attribute
    {
        public readonly string Key;
        public bool Optional;
        public BindAttribute(string key) { Key = key; }
    }

    /// <summary>
    /// [Bind] alanlarını prefab'tan doldurur. Önce kökteki ViewRefs'e bakar (drag-drop ile doldurulmuş),
    /// bulamazsa hiyerarşide "@key" isimli objeyi arar (isim sözleşmesi). Prefab'ta oyun script'i gerekmez.
    /// </summary>
    public static class ViewBinder
    {
        static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();
        static readonly List<Transform> Scratch = new List<Transform>(64);

        public static void Bind(object target, Transform root)
        {
            var refs = root.GetComponent<ViewRefs>();
            Dictionary<string, Transform> byName = null;

            foreach (var f in GetBindFields(target.GetType()))
            {
                var attr = f.GetCustomAttribute<BindAttribute>();
                UnityEngine.Object value = null;

                if (refs != null && refs.TryGet(attr.Key, out var obj))
                    value = Coerce(obj, f.FieldType);

                if (value == null)
                {
                    if (byName == null) byName = IndexByName(root);
                    if (byName.TryGetValue(attr.Key, out var t)) value = Coerce(t, f.FieldType);
                }

                if (value == null)
                {
                    if (!attr.Optional)
                        Debug.LogError($"[ViewBinder] '{root.name}' prefab'ında '{attr.Key}' ({f.FieldType.Name}) bulunamadı → {target.GetType().Name}.{f.Name}", root);
                    continue;
                }
                f.SetValue(target, value);
            }
        }

        /// <summary>Editör validasyonu için: controller tipinin istediği key'ler.</summary>
        public static IEnumerable<(string key, Type type, bool optional)> RequiredKeys(Type controllerType)
        {
            foreach (var f in GetBindFields(controllerType))
            {
                var a = f.GetCustomAttribute<BindAttribute>();
                yield return (a.Key, f.FieldType, a.Optional);
            }
        }

        static FieldInfo[] GetBindFields(Type t)
        {
            if (FieldCache.TryGetValue(t, out var cached)) return cached;
            var list = new List<FieldInfo>();
            for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
                foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (f.IsDefined(typeof(BindAttribute), false)) list.Add(f);
            return FieldCache[t] = list.ToArray();
        }

        static Dictionary<string, Transform> IndexByName(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            Scratch.Clear();
            root.GetComponentsInChildren(true, Scratch);
            foreach (var t in Scratch)
                if (t.name.Length > 1 && t.name[0] == '@') map[t.name.Substring(1)] = t;
            return map;
        }

        static UnityEngine.Object Coerce(UnityEngine.Object obj, Type want)
        {
            if (obj == null) return null;
            if (want.IsInstanceOfType(obj)) return obj;
            if (want == typeof(GameObject)) return obj is Component c ? c.gameObject : null;
            if (!typeof(Component).IsAssignableFrom(want)) return null;
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            return go != null ? go.GetComponent(want) : null;
        }
    }
}
