using System;
using System.Collections;

namespace DigitoyEngine;

// LISTE BAGLAMA (pull): modeldeki bir IList'i cocuk gorunumlere yansitir. Her frame listeyi okur,
// Count + indeks basina eleman REFERANSINI onceki turla kiyaslar: eksik gorunum dogar (Prefab ya da Template
// klonu), fazlasi Destroy edilir, elemani degisen indeksin BindingContext.Model'i degisir — gorunum icindeki
// Binding'ler "en yakin context" kurali ile kendiliginden baglanir; ek mekanizma yok.
// Sinir: eleman tipi SINIF (referans kiyasi). Struct listesi her turda model-degisimi sayilacagi icin desteklenmez.
// Gorunumler yalniz Simulating sahnede uretilir (edit sahnesinde doc'a sizan runtime cocuk yok).
[ExecuteInEditMode]
[AddComponentMenu("Data/List Binding")]
public sealed class ListBinding : Component
{
    public string Source = "";    // liste yolu: "Players", "Lobby.Tables", "$Lobby.Tables"
    public Prefab Prefab;         // eleman gorunumu (asset)
    public GameObject Template;   // Prefab yoksa klonlanacak sablon (hiyerarside PASIF tutulur)
    public Transform Container;   // bos = kendi transform'u

    GameObject[] _views = Array.Empty<GameObject>();
    BindingContext[] _ctx = Array.Empty<BindingContext>();
    object[] _items = Array.Empty<object>();
    int _viewCount;

    string _path, _global;
    BindingContext _srcCtx;
    object _model;
    BindSource _src;
    bool _dirty = true;
    int _globalsSeen = -1;
    BindingSystem _sys;
    internal int _slot = -1;

    public int ViewCount => _viewCount;
    public GameObject ViewAt(int i) => (uint)i < (uint)_viewCount ? _views[i] : null;

    public void Invalidate() => _dirty = true;

    protected internal override void OnEnable()
    {
        _sys = _gameObject._scene.GetSystem<BindingSystem>();
        _sys.Register(this);
        _dirty = true;
    }

    protected internal override void OnDisable()
    {
        _sys?.Unregister(this);
        _sys = null;
    }

    protected internal override void OnValidate() => _dirty = true;

    protected internal override void OnDestroy() => Trim(0);

    void Rebuild(BindingSystem sys)
    {
        _dirty = false;
        _globalsSeen = sys.GlobalsVersion;
        _path = _global = null;
        _srcCtx = null;
        _src = null;
        _model = null;
        if (string.IsNullOrEmpty(Source))
            return;
        if (Source[0] == '$')
        {
            int dot = Source.IndexOf('.');
            if (dot < 2 || dot == Source.Length - 1)
                return;
            _global = Source.Substring(1, dot - 1);
            _path = Source.Substring(dot + 1);
        }
        else
        {
            _path = Source;
            _srcCtx = FindContext();
        }
    }

    // Kendi GO'sundaki context eleman context'i DEGIL, kaynak context'tir (gorunumler cocukta dogar).
    BindingContext FindContext()
    {
        for (var t = transform; t != null; t = t.parent)
        {
            var c = t.gameObject.GetComponent<BindingContext>();
            if (c != null && !c.IsDestroyed)
                return c;
        }
        return null;
    }

    internal void Sync(BindingSystem sys, Scene scene)
    {
        if (_dirty || _globalsSeen != sys.GlobalsVersion)
            Rebuild(sys);
        if (_path == null)
            return;
        object model;
        if (_global != null)
            model = sys.GetGlobal(_global);
        else
        {
            if (_srcCtx == null) return;
            if (_srcCtx.IsDestroyed) { _dirty = true; return; }
            model = _srcCtx.Current(sys);
        }
        IList list = null;
        if (model != null)
        {
            if (!ReferenceEquals(model, _model) || _src == null)
            {
                if (_model == null || model.GetType() != _model.GetType() || _src == null)
                    _src = sys.Resolve(model.GetType(), _path);
                _model = model;
            }
            if (_src != null && (_src.IsList || _src.Dynamic))
                list = _src.Get(model).Ref as IList;
        }
        int n = list != null && scene.Simulating ? list.Count : 0;
        if (n > _views.Length)
            Grow(n);
        for (int i = 0; i < n; i++)
        {
            var item = list[i];
            var view = _views[i];
            bool fresh = view == null || view.IsDestroyed;
            if (fresh)
            {
                view = Spawn(scene);
                if (view == null) { n = i; break; } // Prefab/Template yok: uretilemez
                _views[i] = view;
                _ctx[i] = view.GetComponent<BindingContext>() ?? view.AddComponent<BindingContext>();
            }
            if (fresh || !ReferenceEquals(_items[i], item))
            {
                _items[i] = item;
                _ctx[i].SetModel(item);
            }
        }
        Trim(n);
        _viewCount = n;
    }

    GameObject Spawn(Scene scene)
    {
        var parent = Container != null ? Container : transform;
        // Prefab/Instantiate GO'yu Scene.Active'e dogurur; sahip sahne baska olabilir (editor: edit + play).
        var prev = Scene.Active;
        Scene.SetActive(scene);
        try
        {
            if (Prefab != null)
                return Prefab.Instantiate(parent);
            if (Template != null && !Template.IsDestroyed)
            {
                var go = GameObject.Instantiate(Template, parent);
                if (go != null && !go.activeSelf)
                    go.SetActive(true); // sablon pasif tutulur; kopya canli
                return go;
            }
            return null;
        }
        finally
        {
            Scene.SetActive(prev);
        }
    }

    void Grow(int n)
    {
        int cap = Math.Max(n, Math.Max(8, _views.Length * 2));
        Array.Resize(ref _views, cap);
        Array.Resize(ref _ctx, cap);
        Array.Resize(ref _items, cap);
    }

    // n'den sonraki gorunumleri yok et (sahip biziz).
    void Trim(int n)
    {
        for (int i = n; i < _viewCount; i++)
        {
            var v = _views[i];
            if (v != null && !v.IsDestroyed)
                GameObject.Destroy(v);
            _views[i] = null;
            _ctx[i] = null;
            _items[i] = null;
        }
        if (_viewCount > n)
            _viewCount = n;
    }
}
