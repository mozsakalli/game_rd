using System;

namespace DigitoyEngine;

// Scroll jesti + atalet: ayni GO'daki LayoutBox'in (Overflow=Scroll ekseni) ScrollOffset'ini surer.
// LayoutBox pointer handler'i TASIMAZ (handler bayragi tip-bazli: her kutu pick adayi olur, altindaki
// seylerin tiklamasini calardi) â€” jest bu kucuk component'te; Update/pointer bayragi yalniz bunu
// tasiyan GO'larda. Cocuk butonlar Down'i alir, esik asilinca OnDragStart'ta kendileri iptal eder
// (Button.cs deseni), Click yalniz suruklenmediyse â€” ayrim Pointer'in esiginden bedava gelir.
// Hareket: parmak alti icerik (worldTotal kutu uzayina cevrilir), sinir disinda OverscrollDrag ile
// direnc; birakinca sinir disindaysa yay (SpringSpeed), icindeyse atalet (Friction ile ussel sonum,
// sinira carpinca durur). Alloc yok; hareket bitince Update'te 1-2 kiyas.
[AddComponentMenu("Layout/Scroll View")]
public sealed class ScrollView : Component
{
    public bool Inertia = true;
    public float Friction = 4f;        // 1/sn: hiz *= exp(-Friction*dt)
    public float OverscrollDrag = 0.5f; // sinir disinda parmak hareketinin uygulanan orani (0 = sert sinir)
    public float SpringSpeed = 12f;    // 1/sn: sinir disindan geri donus
    public float MinVelocity = 5f;     // px/sn: altinda atalet biter

    LayoutBox _box;
    Vec2 _startOffset;
    Vec2 _vel;
    Vec2 _prevOffset;
    bool _dragMoved; // bu frame OnDrag geldi mi (Update sifirlar; pointer/Update sirasindan bagimsiz)
    bool _dragging;

    public Vec2 Velocity => _vel;
    public bool IsDragging => _dragging;

    protected internal override void OnEnable()
    {
        _box = GetComponent<LayoutBox>();
        _vel = default;
        _dragging = false;
    }

    protected internal override void OnDisable()
    {
        _dragging = false;
        _vel = default;
    }

    protected internal override void OnPointerDown(PointerEvent e)
    {
        if (_box == null) return;
        _vel = default; // parmak degdi: atalet durur
        _startOffset = _prevOffset = _box.ScrollOffset;
    }

    protected internal override void OnDragStart(PointerEvent e)
    {
        if (_box == null) return;
        _dragging = true;
        _dragMoved = true;
    }

    protected internal override void OnDrag(PointerEvent e)
    {
        if (_box == null || !_dragging) return;
        // Dunya toplami -> kutu lokal uzayi (icerik parmakla birlikte kayar: ofset = baslangic - hareket).
        var w = e.worldTotal;
        ref var wm = ref _box.transform._getWorldMatrix();
        Vec3 local = Mat4.Inverse(ref wm, out var inv) ? Mat4.TransformVector(ref inv, w) : w;
        var max = _box.ScrollMax;
        var cur = _box.ScrollOffset;
        float nx = _box.ScrollsX ? Resist(_startOffset.x - local.x, max.x) : cur.x;
        float ny = _box.ScrollsY ? Resist(_startOffset.y - local.y, max.y) : cur.y;
        var next = new Vec2(nx, ny);
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            // Hiz: frame basina ofset farki, yumusatilmis (ani sicramalar ataleti patlatmasin).
            float vx = (next.x - _prevOffset.x) / dt, vy = (next.y - _prevOffset.y) / dt;
            _vel = new Vec2(_vel.x * 0.4f + vx * 0.6f, _vel.y * 0.4f + vy * 0.6f);
        }
        _prevOffset = next;
        _dragMoved = true;
        _box.ScrollOffset = next;
    }

    protected internal override void OnPointerUp(PointerEvent e)
    {
        _dragging = false;
        if (!Inertia)
            _vel = default;
    }

    // Sinir disi: asim OverscrollDrag ile olceklenir (lastik his).
    float Resist(float v, float max)
    {
        if (v < 0f) return v * OverscrollDrag;
        if (v > max) return max + (v - max) * OverscrollDrag;
        return v;
    }

    protected internal override void Update()
    {
        if (_box == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (_dragging)
        {
            // Parmak duruyor (bu frame OnDrag gelmedi): hiz soner, birakinca firlamasin.
            if (!_dragMoved)
                _vel = new Vec2(_vel.x * 0.5f, _vel.y * 0.5f);
            _dragMoved = false;
            return;
        }
        var cur = _box.ScrollOffset;
        var max = _box.ScrollMax;
        float x = cur.x, y = cur.y;
        bool moved = false;
        if (_box.ScrollsX) moved |= Step(ref x, ref _vel.x, max.x, dt);
        if (_box.ScrollsY) moved |= Step(ref y, ref _vel.y, max.y, dt);
        if (moved)
            _box.ScrollOffset = new Vec2(x, y);
    }

    // Tek eksen: sinir disinda yay (hiz sifir), icinde atalet; sinira carpinca durur.
    bool Step(ref float v, ref float vel, float max, float dt)
    {
        float target = v < 0f ? 0f : (v > max ? max : v);
        if (target != v)
        {
            vel = 0f;
            float k = 1f - MathF.Exp(-SpringSpeed * dt);
            float nv = v + (target - v) * k;
            if (MathF.Abs(target - nv) < 0.25f) nv = target;
            v = nv;
            return true;
        }
        if (vel == 0f)
            return false;
        if (MathF.Abs(vel) < MinVelocity)
        {
            vel = 0f;
            return false;
        }
        float next = v + vel * dt;
        if (next < 0f) { next = 0f; vel = 0f; }
        else if (next > max) { next = max; vel = 0f; }
        else vel *= MathF.Exp(-Friction * dt);
        v = next;
        return true;
    }
}
