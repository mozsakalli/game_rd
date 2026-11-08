using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ViewContract
{
    /// <summary>
    /// Remote prefab ile oyun kodu arasındaki TEK sözleşme script'i.
    /// Bu assembly dondurulmuştur: alan ekleme/çıkarma/yeniden adlandırma YASAK.
    /// Prefab bundle'ları bu tipe (assembly + namespace + class) bağlanır; oyun kodu ne kadar
    /// değişirse değişsin bu dosya değişmediği sürece bundle/oyun uyumsuzluğu oluşmaz.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ViewRefs : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public UnityEngine.Object obj;
        }

        [Tooltip("Oyun kodundaki controller tablosunda kullanılan kimlik (örn. offer_starter).")]
        public string viewId;

        [Tooltip("Oyun kodunun isteyeceği referanslar. Key'ler controller'daki [Bind(\"key\")] ile eşleşir.")]
        public List<Entry> refs = new List<Entry>();

        Dictionary<string, UnityEngine.Object> _map;

        public bool TryGet(string key, out UnityEngine.Object obj)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, UnityEngine.Object>(refs.Count);
                for (int i = 0; i < refs.Count; i++)
                    if (!string.IsNullOrEmpty(refs[i].key)) _map[refs[i].key] = refs[i].obj;
            }
            return _map.TryGetValue(key, out obj) && obj != null;
        }

        public T Get<T>(string key) where T : UnityEngine.Object
        {
            if (!TryGet(key, out var obj)) return null;
            if (obj is T t) return t;
            // GameObject verildiyse üzerindeki component'i, component verildiyse GameObject'i tolere et.
            if (obj is GameObject go) return typeof(Component).IsAssignableFrom(typeof(T)) ? go.GetComponent(typeof(T)) as T : null;
            if (obj is Component c)
            {
                if (typeof(T) == typeof(GameObject)) return c.gameObject as T;
                return c.GetComponent(typeof(T)) as T;
            }
            return null;
        }

        public void Set(string key, UnityEngine.Object obj)
        {
            for (int i = 0; i < refs.Count; i++)
                if (refs[i].key == key) { refs[i] = new Entry { key = key, obj = obj }; _map = null; return; }
            refs.Add(new Entry { key = key, obj = obj });
            _map = null;
        }
    }
}
