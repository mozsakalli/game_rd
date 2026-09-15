using System;

namespace DigitoyEngine;

// Unity GameObject karsiligi — ama HAFIF: yaratma = 2 allocation (GO + Transform),
// reflection yok (tip bayraklari statik generic cache), native handle yok.
public sealed class GameObject
{
    public string name;
    // Render/picking yonlendirme katmani (0-7): kamera cullingMask biti.
    // Cocuklara MIRAS YOK (Unity paritesi) — her GO kendi layer'ini tasir.
    public int layer;
    public readonly Transform transform;

    internal Scene _scene;
    internal int _rootIndex = -1;
    Component[] _components = new Component[4];
    int _componentCount;
    bool _activeSelf = true;
    internal bool _activeInHierarchy = true;
    internal bool _destroyed, _destroyQueued;

    public GameObject(string name = "GameObject")
    {
        this.name = name;
        transform = new Transform { _gameObject = this };
        _components[0] = transform;
        _componentCount = 1;
        _scene = Scene.Active;
        _scene.AddRoot(this);
    }

    public Scene scene => _scene;
    public bool activeSelf => _activeSelf;
    public bool activeInHierarchy => !_destroyed && _activeInHierarchy;
    internal int ComponentCount => _componentCount;
    internal Component ComponentAt(int i) => _components[i];

    public T AddComponent<T>() where T : Component, new()
    {
        var c = new T();
        c._flags = Component.TypeFlags<T>.Value;
        c._gameObject = this;
        if (_componentCount == _components.Length)
            Array.Resize(ref _components, _componentCount * 2);
        _components[_componentCount++] = c;
        if (activeInHierarchy)
            _scene.ActivateComponent(c); // Awake + OnEnable hemen (Unity)
        return c;
    }

    // Deserialize yolu: aktivasyonsuz ekleme — alanlar Awake'ten ONCE yazilir,
    // aktivasyon ActivateComponentsNow ile topluca yapilir (Unity sirasi).
    internal Component AddComponentRaw(TypeCatalog.Entry entry)
    {
        var c = entry.Create();
        c._flags = entry.Flags;
        c._gameObject = this;
        if (_componentCount == _components.Length)
            Array.Resize(ref _components, _componentCount * 2);
        _components[_componentCount++] = c;
        return c;
    }

    internal void ActivateComponentsNow()
    {
        if (!activeInHierarchy)
            return;
        int n = _componentCount;
        for (int i = 0; i < n; i++)
            _scene.ActivateComponent(_components[i]);
    }

    public T GetComponent<T>() where T : Component
    {
        for (int i = 0; i < _componentCount; i++)
            if (_components[i] is T t && !t._destroyed)
                return t;
        return null;
    }

    // Tip nesnesiyle arama (Unity GetComponent(Type) karsiligi; editor/reflection yolu).
    public Component GetComponentOfType(Type type)
    {
        for (int i = 0; i < _componentCount; i++)
            if (!_components[i]._destroyed && type.IsInstanceOfType(_components[i]))
                return _components[i];
        return null;
    }

    public bool TryGetComponent<T>(out T component) where T : Component
    {
        component = GetComponent<T>();
        return component != null;
    }

    public T GetComponentInChildren<T>() where T : Component
    {
        var own = GetComponent<T>();
        if (own != null)
            return own;
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var found = t._gameObject?.GetComponentInChildren<T>();
            if (found != null)
                return found;
        }
        return null;
    }

    public void SetActive(bool value)
    {
        if (_activeSelf == value || _destroyed)
            return;
        _activeSelf = value;
        var p = transform.parent;
        bool parentActive = p == null || p._gameObject == null || p._gameObject._activeInHierarchy;
        SetActiveInHierarchy(parentActive && value);
    }

    // Etkin hiyerarsi durumu degisti: componentleri ve alt agaci bilgilendir.
    internal void SetActiveInHierarchy(bool value)
    {
        if (_activeInHierarchy == value || _destroyed)
            return;
        _activeInHierarchy = value;
        int n = _componentCount;
        if (value)
            for (int i = 0; i < n; i++) _scene.ActivateComponent(_components[i]);
        else
            for (int i = 0; i < n; i++) _scene.DisableComponent(_components[i]);
        for (var t = transform.FirstChild; t != null; t = t.NextSibling)
        {
            var go = t._gameObject;
            if (go != null && go._activeSelf)
                go.SetActiveInHierarchy(value);
        }
    }

    // Transform.SetParent sonrasi: aktiflik + kok listesi guncellenir.
    internal void OnTransformParentChanged()
    {
        if (_destroyed)
            return;
        var p = transform.parent;
        if (p == null) _scene.AddRoot(this);
        else _scene.RemoveRoot(this);
        bool parentActive = p == null || p._gameObject == null || p._gameObject._activeInHierarchy;
        SetActiveInHierarchy(parentActive && _activeSelf);
    }

    // Transform mesaj dispatch'i: yalniz override eden (bayrakli) component'lere —
    // tipik GO'da bayrak kontrolu disinda sifir maliyet.
    internal void NotifyTransformParentChanged()
    {
        if (_destroyed)
            return;
        int n = _componentCount;
        for (int i = 0; i < n; i++)
        {
            var c = _components[i];
            if (!c._destroyed && (c._flags & LifecycleFlags.ParentChanged) != 0)
                c.OnTransformParentChanged();
        }
    }

    internal void NotifyTransformChildrenChanged()
    {
        if (_destroyed)
            return;
        int n = _componentCount;
        for (int i = 0; i < n; i++)
        {
            var c = _components[i];
            if (!c._destroyed && (c._flags & LifecycleFlags.ChildrenChanged) != 0)
                c.OnTransformChildrenChanged();
        }
    }

    internal void RemoveComponent(Component c)
    {
        for (int i = 0; i < _componentCount; i++)
        {
            if (_components[i] != c)
                continue;
            // Sira korunur (GetComponent onceligi) — kaydirmali cikarma.
            for (int j = i; j < _componentCount - 1; j++)
                _components[j] = _components[j + 1];
            _components[--_componentCount] = null;
            return;
        }
    }

    internal void MarkDestroyed() => _destroyed = true;

    public static void Destroy(GameObject go) => go?._scene.QueueDestroy(go);
    public static void Destroy(Component c) => c?._gameObject?._scene.QueueDestroy(c);

    // Unity Object.Instantiate: alt agac klonu. Alanlar kopyalandiktan SONRA
    // aktivasyon (Awake kopya degerleri gorur). Klon ICINE isaret eden referans
    // alanlar klonun kopyalarina remap edilir; dis referanslar aynen kalir (Unity).
    public static GameObject Instantiate(GameObject original, Transform parent = null)
    {
        if (original == null || original._destroyed)
            return null;
        var catalog = original._scene.Catalog;
        var goMap = new System.Collections.Generic.Dictionary<GameObject, GameObject>();
        var compMap = new System.Collections.Generic.Dictionary<Component, Component>();
        var clone = CloneRecursive(original, catalog, goMap, compMap);
        if (parent != null)
            clone.transform.SetParent(parent, false);
        RemapRefs(catalog, compMap, goMap);
        ActivateTree(clone);
        return clone;
    }

    static GameObject CloneRecursive(GameObject src, TypeCatalog catalog,
        System.Collections.Generic.Dictionary<GameObject, GameObject> goMap,
        System.Collections.Generic.Dictionary<Component, Component> compMap)
    {
        var go = new GameObject(src.name);
        goMap[src] = go;
        go.layer = src.layer;
        go.transform.localPosition = src.transform.localPosition;
        go.transform.localEulerAngles = src.transform.localEulerAngles;
        go.transform.localScale = src.transform.localScale;
        compMap[src.transform] = go.transform;
        if (!src._activeSelf)
            go.SetActive(false); // henuz component yok, lifecycle tetiklenmez

        int n = src._componentCount;
        for (int i = 0; i < n; i++)
        {
            var c = src._components[i];
            if (c is Transform || c._destroyed)
                continue;
            var entry = catalog?.Find(c.GetType());
            if (entry == null)
                continue;
            var copy = go.AddComponentRaw(entry);
            entry.CopyTo(c, copy);
            copy._enabled = c._enabled;
            compMap[c] = copy;
        }

        for (var t = src.transform.FirstChild; t != null; t = t.NextSibling)
            if (t._gameObject != null)
                CloneRecursive(t._gameObject, catalog, goMap, compMap).transform.SetParent(go.transform, false);
        return go;
    }

    static void RemapRefs(TypeCatalog catalog,
        System.Collections.Generic.Dictionary<Component, Component> compMap,
        System.Collections.Generic.Dictionary<GameObject, GameObject> goMap)
    {
        foreach (var kv in compMap)
        {
            if (kv.Key is Transform)
                continue;
            var entry = catalog?.Find(kv.Key.GetType());
            if (entry == null)
                continue;
            foreach (var f in entry.Schema)
            {
                if (f.Kind == SerializedType.Kind.CompRef)
                {
                    if (f.Get(kv.Value) is Component rc && compMap.TryGetValue(rc, out var mapped))
                        f.Set(kv.Value, mapped);
                }
                else if (f.Kind == SerializedType.Kind.GoRef)
                {
                    if (f.Get(kv.Value) is GameObject rg && goMap.TryGetValue(rg, out var mappedGo))
                        f.Set(kv.Value, mappedGo);
                }
            }
        }
    }

    static void ActivateTree(GameObject go)
    {
        go.ActivateComponentsNow();
        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t._gameObject != null)
                ActivateTree(t._gameObject);
    }
}
