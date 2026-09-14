using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Dunya isini. Kaynagi CameraComponent.ScreenPointToRay — tuketici (picking/drag)
// projeksiyon tipini bilmez: ortho'da eksene paralel, perspektifte kameradan gecer.
public struct Ray
{
    public Vec3 Origin;
    public Vec3 Dir; // normalize

    public readonly Vec3 At(float t) => Origin + Dir * t;
}

// Picking sonucu. 3D detay alanlari (Normal/TriIndex/Uv) REZERVE: bugun 2D testler
// yalniz ilk ucunu doldurur; MeshRenderer geldiginde ayni API kalanlari doldurur.
public struct RayHit
{
    public Renderer Renderer;
    public float Distance;  // isin kaynagindan dunya birimi
    public Vec3 Point;      // dunya uzayi vurus noktasi
    public Vec3 Normal;
    public int TriIndex;
    public Vec2 Uv;
}

// TEK INSTANCE, yerinde mutate (Gui.Event modeli) — handler 'e'yi SAKLAMAZ,
// lazim olani kopyalar. Jest bitince referans alanlari bosaltilir (GC koku kalmaz).
public sealed class PointerEvent
{
    public GameObject target;      // asil hit alan GO (bubble'da degismez — DOM event.target)
    public Renderer targetRenderer;
    public RayHit hit;             // down anindaki vurus
    public Vec2 screenPos;         // guncel konum (oyun ciktisi mantiksal pikseli)
    public Vec2 screenTotal;       // down'dan beri toplam (piksel; scale/rotate jestleri icin)
    public bool dragged;           // esik asildi mi (up'ta drag/click ayrimi)

    internal PointerInput _owner;
    internal bool _used;
    internal bool _worldValid;
    internal Vec3 _worldTotal;

    public bool Used => _used;
    public void Use() => _used = true;

    // Down'dan beri DUNYA uzayinda toplam hareket — drag duzleminde (down'daki hit
    // noktasindan gecen, kameraya dik duzlem) olculur: tutulan nokta imlecin
    // altinda kalir. Tembel: istenmezse ray/duzlem matematigi hic kosmaz.
    public Vec3 worldTotal
    {
        get
        {
            if (!_worldValid && _owner != null)
            {
                _worldTotal = _owner.ComputeWorldTotal();
                _worldValid = true;
            }
            return _worldTotal;
        }
    }

    // worldTotal'i verilen transform'un PARENT uzayina cevirir: dogrudan
    // localPosition'a eklenebilir delta. Kumulatif desen: down'da baslangici sakla,
    // her OnDrag'de baslangic + bu deger (frame kaybi birikmez).
    public Vec3 TotalInParentOf(Transform t)
    {
        var w = worldTotal;
        var parent = t?.parent;
        if (parent == null)
            return w;
        ref var pw = ref parent._getWorldMatrix();
        return Mat4.Inverse(ref pw, out var inv) ? Mat4.TransformVector(ref inv, w) : w;
    }

    internal void ClearRefs()
    {
        target = null;
        targetRenderer = null;
        hit = default;
    }
}

// Sahne pointer boru hatti: hit-test + capture + jest makinesi (click/drag ayrimi,
// esik, bubble). Sahibi (editor GameView / standalone player) oyun ciktisi mantiksal
// koordinatlariyla Down/Move/Up besler. Maliyet modeli: tarama YALNIZ down aninda
// (aday = handler'li GO'lar + BlocksRaycast); move basina sabit birkac flop.
public sealed class PointerInput
{
    readonly Scene _scene;
    readonly PointerEvent _ev = new();

    // Jest durumu (event'te degil burada — event her dispatch'te ezilir).
    bool _pressed, _dragged;
    GameObject _captured;          // down zincirinin basi (bubble hep buradan)
    CameraComponent _camera;       // yakalayan kamera (ray uretimi icin)
    Vec2 _downScreen, _curScreen;
    Vec3 _downWorld;               // drag duzlemi uzerindeki down noktasi
    float _planeZ;                 // drag duzlemi: z = hit.z (kamera bakisi +z, duzlem dik)

    // Click/drag ayrim esigi (mantiksal piksel; dokunmatik icin genis).
    public float DragThreshold = 6f;

    internal PointerInput(Scene scene) => _scene = scene;

    public void Down(float x, float y)
    {
        if (_pressed)
            return;
        _ev._used = false;
        if (!Pick(x, y, out var hit, out var cam))
            return;
        _pressed = true;
        _dragged = false;
        _captured = hit.Renderer._gameObject;
        _camera = cam;
        _downScreen = _curScreen = new Vec2(x, y);
        _planeZ = hit.Point.z;
        _downWorld = hit.Point;

        _ev._owner = this;
        _ev.target = _captured;
        _ev.targetRenderer = hit.Renderer;
        _ev.hit = hit;
        _ev.dragged = false;
        SyncEvent();
        Bubble(EvDown, LifecycleFlags.PointerDown);
    }

    public void Move(float x, float y)
    {
        if (!_pressed)
            return; // hover yok (bilincli): bosta move sifir maliyet
        _curScreen = new Vec2(x, y);
        SyncEvent();
        if (!_dragged)
        {
            float dx = _curScreen.x - _downScreen.x, dy = _curScreen.y - _downScreen.y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold)
                return;
            _dragged = true;
            _ev.dragged = true;
            Bubble(EvDragStart, LifecycleFlags.DragStart);
        }
        Bubble(EvDrag, LifecycleFlags.Drag);
    }

    public void Up(float x, float y)
    {
        if (!_pressed)
            return;
        _curScreen = new Vec2(x, y);
        SyncEvent();
        Bubble(EvUp, LifecycleFlags.PointerUp);
        if (!_dragged)
            Bubble(EvClick, LifecycleFlags.PointerClick);

        // Jest bitti: referanslar bosalir (yeniden kullanilan event GO koklemez).
        _pressed = false;
        _dragged = false;
        _captured = null;
        _camera = null;
        _ev.ClearRefs();
    }

    void SyncEvent()
    {
        _ev.screenPos = _curScreen;
        _ev.screenTotal = new Vec2(_curScreen.x - _downScreen.x, _curScreen.y - _downScreen.y);
        _ev._worldValid = false; // world hesabi tembel: istenirse guncel konumdan
    }

    // Guncel imlec isininin drag duzlemiyle kesisimi - down noktasi.
    internal Vec3 ComputeWorldTotal()
    {
        if (_camera == null)
            return default;
        if (!_camera.ScreenPointToRay(_curScreen.x, _curScreen.y,
                _scene.ScreenWidth, _scene.ScreenHeight, out var ray))
            return default;
        if (ray.Dir.z > -1e-8f && ray.Dir.z < 1e-8f)
            return default;
        float t = (_planeZ - ray.Origin.z) / ray.Dir.z;
        return ray.At(t) - _downWorld;
    }

    // --- Hit test (yalniz down aninda) ---

    // Kameralar ters depth (ustteki once): bir kamerada hit varsa alttakilere
    // bakilmaz (UI kamerasi dunyayi dogal olarak bloklar). Kamera icinde en yakin
    // mesafe kazanir; esit mesafede (2D ayni duzlem) buyuk SortingOrder / gec
    // kayit (ustte cizilen) kazanir.
    bool Pick(float sx, float sy, out RayHit best, out CameraComponent bestCam)
    {
        best = default;
        bestCam = null;
        float vw = _scene.ScreenWidth, vh = _scene.ScreenHeight;
        int camCount = _scene.CameraCount;
        if (_order.Length < camCount)
            _order = new CameraComponent[camCount];
        // Az sayida kamera: siralamak yerine her turda islenmemis en buyuk depth secilir.
        for (int done = 0; done < (camCount == 0 ? 1 : camCount); done++)
        {
            CameraComponent cam = null;
            if (camCount > 0)
            {
                for (int i = 0; i < camCount; i++)
                {
                    var c = _scene.CameraAt(i);
                    if (!c.isActiveAndEnabled || _seen(c, done))
                        continue;
                    if (cam == null || c.depth > cam.depth)
                        cam = c;
                }
                if (cam == null)
                    return false;
                _order[done] = cam;
            }
            int mask = cam?.cullingMask ?? -1;
            Ray ray;
            if (cam != null)
            {
                if (!cam.ScreenPointToRay(sx, sy, vw, vh, out ray))
                    continue;
            }
            else
            {
                // Sahnede kamera yok: piksel-ortho fallback (render fallback'iyla ayni).
                ray = new Ray { Origin = new Vec3(sx, sy, -16384f), Dir = new Vec3(0, 0, 1) };
            }

            bool found = false;
            int bestOrder = 0, bestSlot = 0;
            int n = _scene.RendererCount;
            for (int i = 0; i < n; i++)
            {
                var r = _scene.RendererAt(i);
                if (r == null || r._destroyed || !r._enabled)
                    continue;
                if ((mask & (1 << r._gameObject.layer)) == 0)
                    continue;
                if (!r.BlocksRaycast && !HasHandlerInChain(r._gameObject))
                    continue;
                if (!r.HitTest(in ray, out float dist, out var point))
                    continue;
                const float eps = 1e-4f;
                bool win = !found
                    || dist < best.Distance - eps
                    || (dist <= best.Distance + eps
                        && (r.SortingOrder > bestOrder
                            || (r.SortingOrder == bestOrder && i > bestSlot)));
                if (!win)
                    continue;
                found = true;
                bestOrder = r.SortingOrder;
                bestSlot = i;
                best = new RayHit { Renderer = r, Distance = dist, Point = point };
            }
            if (found)
            {
                bestCam = cam;
                return true;
            }
        }
        return false;
    }

    // Ters-depth gezinti icin islenmis kamera izleme (alloc'suz kucuk dizi).
    CameraComponent[] _order = new CameraComponent[8];

    bool _seen(CameraComponent c, int done)
    {
        for (int i = 0; i < done; i++)
            if (_order[i] == c)
                return true;
        return false;
    }

    // Aday filtresi: GO veya bir atasi pointer handler tasiyor mu (tik aninda,
    // zincir kisa). Handler parent'ta olsa da cocuk renderer hit kaynagi olur.
    static bool HasHandlerInChain(GameObject go)
    {
        for (var g = go; g != null; g = g.transform.parent?._gameObject)
        {
            int n = g.ComponentCount;
            for (int i = 0; i < n; i++)
            {
                var c = g.ComponentAt(i);
                if (!c._destroyed && (c._flags & LifecycleFlags.AnyPointer) != 0)
                    return true;
            }
        }
        return false;
    }

    // --- Dispatch: hit GO'dan parent zincirine (e.Use() keser) ---

    const int EvDown = 0, EvUp = 1, EvClick = 2, EvDragStart = 3, EvDrag = 4;

    void Bubble(int evCode, LifecycleFlags flag)
    {
        _ev._used = false;
        for (var g = _captured; g != null && !_ev._used; g = g.transform.parent?._gameObject)
        {
            int n = g.ComponentCount;
            for (int i = 0; i < n; i++)
            {
                var c = g.ComponentAt(i);
                if (c._destroyed || !c._enabled || (c._flags & flag) == 0)
                    continue;
                switch (evCode)
                {
                    case EvDown: c.OnPointerDown(_ev); break;
                    case EvUp: c.OnPointerUp(_ev); break;
                    case EvClick: c.OnPointerClick(_ev); break;
                    case EvDragStart: c.OnDragStart(_ev); break;
                    case EvDrag: c.OnDrag(_ev); break;
                }
                if (_ev._used)
                    break;
            }
        }
    }
}
