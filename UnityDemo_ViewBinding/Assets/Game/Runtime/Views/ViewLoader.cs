using System;
using System.Collections.Generic;
using Game.ViewContract;
using UnityEngine;

namespace Game.Views
{
    /// <summary>viewId → controller tipi. Prefab kendi davranışını bilmez; oyun kodu bu tablodan seçer.</summary>
    public static class ViewRegistry
    {
        static readonly Dictionary<string, Type> Map = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static void Register<T>(string viewId) where T : ViewController => Map[viewId] = typeof(T);

        public static bool TryGetController(string viewId, out Type type) => Map.TryGetValue(viewId, out type);

        public static IEnumerable<KeyValuePair<string, Type>> All => Map;
    }

    /// <summary>Prefab kaynağı soyutlaması: demo Resources kullanır, üretimde Addressables (README'ye bak).</summary>
    public interface IViewSource
    {
        void Load(string address, Action<GameObject> onLoaded);
    }

    public sealed class ResourcesViewSource : IViewSource
    {
        readonly string _folder;
        public ResourcesViewSource(string folder = "Views") { _folder = folder; }

        public void Load(string address, Action<GameObject> onLoaded)
        {
            var req = Resources.LoadAsync<GameObject>(_folder + "/" + address);
            req.completed += _ => onLoaded(req.asset as GameObject);
        }
    }

    /// <summary>Prefab'ı yükler, kapalı instantiate eder, controller'ı takar, Initialize eder, açar.</summary>
    public sealed class ViewLoader
    {
        readonly IViewSource _source;
        public ViewLoader(IViewSource source) { _source = source; }

        public void Open(string address, Transform parent, object context, Action<ViewController> onOpened = null)
        {
            _source.Load(address, prefab =>
            {
                if (prefab == null) { Debug.LogError($"[ViewLoader] prefab yok: {address}"); return; }
                var ctrl = Attach(prefab, parent, context, fallbackId: address);
                onOpened?.Invoke(ctrl);
            });
        }

        public static ViewController Attach(GameObject prefab, Transform parent, object context, string fallbackId = null)
        {
            // Kapalı instantiate: eklenen controller Awake/OnEnable'ı Initialize'dan ÖNCE görmesin.
            bool wasActive = prefab.activeSelf;
            prefab.SetActive(false);
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, parent, false); }
            finally { prefab.SetActive(wasActive); }

            var refs = go.GetComponent<ViewRefs>();
            string id = refs != null && !string.IsNullOrEmpty(refs.viewId) ? refs.viewId : fallbackId;

            if (id == null || !ViewRegistry.TryGetController(id, out var type))
            {
                Debug.LogError($"[ViewLoader] '{id}' için kayıtlı controller yok ({prefab.name}).", prefab);
                UnityEngine.Object.Destroy(go);
                return null;
            }

            go.name = prefab.name;
            var ctrl = (ViewController)go.AddComponent(type);
            ctrl.InternalInitialize(context);
            go.SetActive(true);
            return ctrl;
        }
    }
}
