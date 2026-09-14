using System;
using System.Reflection;

namespace DigitoyEngine;

// Yasam dongusu bayraklari: tip basina BIR KEZ reflection ile saptanir.
// Yalniz override eden metodlar dispatch listelerine girer — bos sanal cagri yok.
[Flags]
public enum LifecycleFlags : ushort
{
    None = 0,
    Awake = 1,
    OnEnable = 2,
    Start = 4,
    Update = 8,
    LateUpdate = 16,
    OnDisable = 32,
    OnDestroy = 64,
    ParentChanged = 128,
    ChildrenChanged = 256,
    PointerDown = 512,
    PointerUp = 1024,
    PointerClick = 2048,
    DragStart = 4096,
    Drag = 8192,

    AnyPointer = PointerDown | PointerUp | PointerClick | DragStart | Drag,
}

// Unity Component/Behaviour karsiligi. Yasam dongusu Unity ile birebir:
// Awake (GO ilk aktif oldugunda, enabled'dan bagimsiz) → OnEnable → Start
// (ilk Update'ten once, bir kez) → Update → LateUpdate → OnDisable → OnDestroy.
// Destroy frame SONUNDA uygulanir (Scene.FlushDestroyed).
public abstract class Component
{
    internal GameObject _gameObject;
    internal LifecycleFlags _flags;
    internal bool _enabled = true;
    internal bool _awakeCalled, _started, _startQueued, _enableCalled, _destroyed;
    internal int _updateSlot = -1, _lateUpdateSlot = -1;

    public GameObject gameObject => _gameObject;
    public Transform transform => _gameObject.transform;
    public string name => _gameObject.name;

    public bool enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || _destroyed)
                return;
            _enabled = value;
            if (_gameObject != null && _gameObject.activeInHierarchy)
            {
                if (value) _gameObject._scene.EnableComponent(this);
                else _gameObject._scene.DisableComponent(this);
            }
        }
    }

    public bool isActiveAndEnabled
        => _enabled && !_destroyed && _gameObject != null && _gameObject.activeInHierarchy;

    public T GetComponent<T>() where T : Component => _gameObject.GetComponent<T>();

    // protected internal: kullanici sinifi protected gibi override eder,
    // Scene ayni assembly'den dogrudan cagirir (mesajlasma/reflection yok).
    protected internal virtual void Awake() { }
    // Editor bir alani dogrudan yazdiktan sonra cagirir (Unity OnValidate muadili;
    // lifecycle bayragi yok — yalniz editor patch yolundan dogrudan sanal cagri).
    protected internal virtual void OnValidate() { }
    protected internal virtual void OnEnable() { }
    protected internal virtual void Start() { }
    protected internal virtual void Update() { }
    protected internal virtual void LateUpdate() { }
    protected internal virtual void OnDisable() { }
    protected internal virtual void OnDestroy() { }
    // Transform mesajlari (Unity paritesi): SetParent sonrasi tasinan GO'nun
    // component'lerine / cocuk listesi degisen (eski+yeni) ebeveynlere.
    protected internal virtual void OnTransformParentChanged() { }
    protected internal virtual void OnTransformChildrenChanged() { }

    // Pointer/jest mesajlari (Scene.Pointer boru hatti): hit alan GO'dan parent
    // zincirine BUBBLE (e.Use() keser; e.target asil hit'i tasir). Kural: 'e'
    // SAKLANMAZ — tek instance yeniden kullanilir, dispatch sonrasi ref'ler bosalir.
    protected internal virtual void OnPointerDown(PointerEvent e) { }
    protected internal virtual void OnPointerUp(PointerEvent e) { }     // hep gelir; e.dragged ayirt eder
    protected internal virtual void OnPointerClick(PointerEvent e) { }  // yalniz esik asilmadiysa
    protected internal virtual void OnDragStart(PointerEvent e) { }     // esik ilk asildiginda bir kez
    protected internal virtual void OnDrag(PointerEvent e) { }          // move basina (coalesced)

    public static void Destroy(GameObject go) => GameObject.Destroy(go);
    public static void Destroy(Component c) => GameObject.Destroy(c);

    // AddComponent<T> bayraklari statik generic cache'ten okur — dictionary lookup bile yok.
    // (Generic static, T'nin AssemblyLoadContext'iyle birlikte cope gider — reload guvenli.
    // Type-anahtarli dictionary cache BILEREK YOK: o is TypeCatalog instance'inda.)
    internal static class TypeFlags<T> where T : Component
    {
        public static readonly LifecycleFlags Value = ComputeFlags(typeof(T));
    }

    internal static LifecycleFlags ComputeFlags(Type t)
    {
        LifecycleFlags f = LifecycleFlags.None;
        f |= Overrides(t, "Awake", LifecycleFlags.Awake);
        f |= Overrides(t, "OnEnable", LifecycleFlags.OnEnable);
        f |= Overrides(t, "Start", LifecycleFlags.Start);
        f |= Overrides(t, "Update", LifecycleFlags.Update);
        f |= Overrides(t, "LateUpdate", LifecycleFlags.LateUpdate);
        f |= Overrides(t, "OnDisable", LifecycleFlags.OnDisable);
        f |= Overrides(t, "OnDestroy", LifecycleFlags.OnDestroy);
        f |= Overrides(t, "OnTransformParentChanged", LifecycleFlags.ParentChanged);
        f |= Overrides(t, "OnTransformChildrenChanged", LifecycleFlags.ChildrenChanged);
        f |= OverridesPtr(t, "OnPointerDown", LifecycleFlags.PointerDown);
        f |= OverridesPtr(t, "OnPointerUp", LifecycleFlags.PointerUp);
        f |= OverridesPtr(t, "OnPointerClick", LifecycleFlags.PointerClick);
        f |= OverridesPtr(t, "OnDragStart", LifecycleFlags.DragStart);
        f |= OverridesPtr(t, "OnDrag", LifecycleFlags.Drag);
        return f;
    }

    static LifecycleFlags Overrides(Type t, string method, LifecycleFlags flag)
    {
        var m = t.GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        return (m != null && m.DeclaringType != typeof(Component)) ? flag : LifecycleFlags.None;
    }

    static readonly Type[] _ptrArgs = { typeof(PointerEvent) };

    static LifecycleFlags OverridesPtr(Type t, string method, LifecycleFlags flag)
    {
        var m = t.GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, _ptrArgs, null);
        return (m != null && m.DeclaringType != typeof(Component)) ? flag : LifecycleFlags.None;
    }
}