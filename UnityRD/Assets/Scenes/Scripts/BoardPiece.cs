using UnityEngine;
using UnityEngine.EventSystems;

// Generic olmayan taban: zone listeleri ve BoardGameBase'in morph döngüsü bunu kullanır.
public abstract class BoardPieceBase : MonoBehaviour
{
    internal BoardGameBase _gameBase;

    public int no;
    // Kimliğe bağlı rastgelelik (jitter vb.). Alloc'ta atanır, piece yaşadıkça sabit → relayout'ta oynamaz.
    public float seed;
    public bool isDragging;
    public bool releaseWhenArrived;

    // slot = anlık (drag/preview'da geçici), realSlot = Sync'in yazdığı gerçek yer.
    public Slot slot;
    public Slot realSlot;
    public void SaveSlot() => realSlot = slot;
    public void RestoreSlot() => slot = realSlot;

    // Tek morph kanalı: tüm alanlar aynı t ile _from → _target.
    public Pose pose;          // en son uygulanan
    internal Pose _from;
    internal Pose _target;
    internal Transition _tr;
    internal float _startTime;
    internal bool _moving;
    internal bool _startEventCalled;
    internal bool _released;

    public Pose targetPose => _target;
    public bool isMoving => _moving;

    // Hedef pose ver; mevcut (anlık) pose'dan başlar, uçuş ortasında gelirse retarget olur.
    internal void SetTarget(in Pose target, in Transition tr, float now)
    {
        _from = pose;
        _target = target;
        _tr = tr;
        if (tr.spawnZone != null)
        {
            var z = tr.spawnZone;
            var zp = z.PieceParent;
            var lp = z.Layout(z.Count, z.Count + 1, seed);
            _from = Pose.WorldToLocal(transform.parent,
                zp.TransformPoint(lp.position), zp.rotation * lp.rotation, Vector3.Scale(zp.lossyScale, lp.scale), lp.faceUp);
            Apply(_from);
        }
        else if (tr.spawnFrom != null)
        {
            var s = tr.spawnFrom;
            _from = Pose.WorldToLocal(transform.parent, s.position, s.rotation, s.lossyScale, tr.spawnFaceUp);
            Apply(_from);
        }
        if (_tr.duration < 0f)
            _tr.duration = AutoDuration(_from.position, target.position);
        if (_tr.duration <= 0f && _tr.delay <= 0f)
        {
            _moving = false;
            Apply(target);
            return;
        }
        _startTime = now;
        _moving = true;
        _startEventCalled = false;
    }

    // Drag lift: devam eden uçuş varsa yalnız hedefin rotation/scale'ini değiştirir (flip zamanlaması korunur),
    // yoksa kısa bir morph başlatır.
    internal void LiftTo(Quaternion rotation, Vector3 scale, float duration, float now)
    {
        if (_moving)
        {
            _target.rotation = rotation;
            _target.scale = scale;
            return;
        }
        var p = pose;
        p.rotation = rotation;
        p.scale = scale;
        SetTarget(p, Transition.Default.WithDuration(duration), now);
    }

    // TileManager'daki mesafe bazlı hız: 250..850 ms, 1.2 çarpanla hızlandırılmış.
    static float AutoDuration(Vector3 a, Vector3 b)
    {
        var d = Vector3.Distance(a, b);
        return Mathf.Clamp(d * 0.9f, 250f, 850f) / 1000f / 1.2f;
    }

    static float Ease(float t) { var u = 1f - t; return 1f - u * u * u; }

    // Bir frame ilerlet; vardıysa true döner.
    internal bool Tick(float now)
    {
        if (!_moving) return false;
        var time = now - _startTime - _tr.delay;
        if (time < 0f) return false;
        if (!_startEventCalled)
        {
            _startEventCalled = true;
            OnStartedToMove();
        }
        if (time >= _tr.duration)
        {
            _moving = false;
            Apply(_target);
            return true;
        }
        var t = Ease(time / _tr.duration);
        Apply(Pose.Lerp(_from, _target, t, _tr.flipAt, _tr.lift));
        return false;
    }

    // Drag'de pozisyon dışarıdan yazılır; rotation/scale/face morph'tan akmaya devam eder.
    void Apply(in Pose p)
    {
        var faceChanged = p.faceUp != pose.faceUp;
        var keepPos = pose.position;
        pose = p;
        var t = transform;
        if (isDragging) pose.position = keepPos;
        else t.localPosition = p.position;
        t.localRotation = p.rotation;
        t.localScale = p.scale;
        if (faceChanged) OnFaceChanged(p.faceUp);
        ApplyPose(p);
    }

    // Drag katmanı mouse'tan gelen local pozisyonu buradan yazar.
    internal void SetDragPosition(Vector3 localPos)
    {
        pose.position = localPos;
        transform.localPosition = localPos;
    }

    // Reparent sonrası anlık pose'u yeni parent uzayında yeniden tanımlar (transform zaten dünyada sabit kaldı).
    internal void RebaseToParent()
    {
        var t = transform;
        pose.position = t.localPosition;
        pose.rotation = t.localRotation;
        pose.scale = t.localScale;
        if (_moving)
        {
            // Uçuş ortasında reparent: kalan yolu yeni uzayda yeniden başlat.
            _from = pose;
            _startTime = Time.time;
            _tr.delay = 0f;
        }
    }

    internal void ResetState(int no)
    {
        this.no = no;
        seed = Random.value;
        isDragging = releaseWhenArrived = _moving = _startEventCalled = _released = false;
        slot = realSlot = Slot.None;
        pose = Pose.Identity;
        var t = transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        ResetVisual();
        OnFaceChanged(pose.faceUp);
    }

    // --- Oyuna özel görsel hook'ları ---
    protected virtual void ApplyPose(in Pose p) { }
    protected virtual void OnFaceChanged(bool faceUp) { }
    public virtual void ResetVisual() { }
    public virtual void UpdateVisual() { }
    public virtual void OnStartedToMove() { }
    public virtual bool OnArrived() => false;
    public virtual void OnReleased() { }
}

public class BoardPiece<T> : BoardPieceBase, IPointerDownHandler, IPointerUpHandler, IDragHandler, IEndDragHandler where T : BoardPiece<T>
{
    internal BoardGame<T> _game;
    // Drag'i başlatan pointer; başka pointer'ların drag/up olayları yok sayılır.
    internal int _pointerId = int.MinValue;

    public void OnPointerDown(PointerEventData eventData) => _game?._onPiecePointerDown((T)this, eventData);
    public void OnPointerUp(PointerEventData eventData) => _game?._onPiecePointerUp((T)this, eventData);
    public void OnDrag(PointerEventData eventData) => _game?._onPieceDrag((T)this, eventData);
    // Pointer up gelmeden drag kesilirse (odak kaybı vb.) EndDrag de bırakma sayılır.
    public void OnEndDrag(PointerEventData eventData) => _game?._onPiecePointerUp((T)this, eventData);
}
