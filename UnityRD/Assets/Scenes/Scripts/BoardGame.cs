using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Generic olmayan taban: zone'lar ve morph döngüsü buradadır; somut piece tipi bilinmez.
public abstract class BoardGameBase : MonoBehaviour
{
    public BoardZone[] zones;
    [Tooltip("Drag ve flyOnTop uçuşları için üst katman. Boşsa bu transform.")]
    public Transform dragLayer;

    internal readonly List<BoardPieceBase> _pieces = new List<BoardPieceBase>();

    public Transform DragLayer => dragLayer != null ? dragLayer : transform;

    protected virtual void Awake()
    {
        if (zones != null)
            for (var i = 0; i < zones.Length; i++)
                if (zones[i] != null) zones[i]._game = this;
    }

    // Runtime'da üretilen zone'lar için.
    public void RegisterZone(BoardZone zone)
    {
        zone._game = this;
        var n = zones != null ? zones.Length : 0;
        var arr = new BoardZone[n + 1];
        if (n > 0) System.Array.Copy(zones, arr, n);
        arr[n] = zone;
        zones = arr;
    }

    // Piece'i yeni slot'a taşı (gerçek hamle: realSlot da yazılır). Komşular her iki zone'da relayout olur.
    // HoldsPieces=false zone'a gönderim: piece oraya uçar, varınca release edilir (zone.count artar).
    public void SetSlot(BoardPieceBase piece, BoardZone zone, int index = -1, int groupNo = 0, Transition? transition = null)
    {
        var tr = transition ?? zone.defaultTransition;
        var oldZone = piece.slot.zone;

        if (!zone.HoldsPieces)
        {
            if (oldZone != null)
            {
                oldZone.pieces.Remove(piece);
                oldZone.Reindex();
            }
            piece.slot = piece.realSlot = new Slot(zone, -1, groupNo);
            piece.releaseWhenArrived = true;
            zone.OnPieceSent(piece);
            Reparent(piece, DragLayer);
            piece.transform.SetAsLastSibling();
            if (oldZone != null) oldZone.Relayout();
            ApplyTarget(piece, zone.Layout(0, 0, piece.seed), tr);
            return;
        }

        PlaceInList(piece, zone, index, groupNo, relayout: false);
        piece.realSlot = piece.slot;

        // Parent değişimi: dünyada sabit kal, pose'u yeni uzayda yeniden tanımla. Drag'deki piece DragLayer'da kalır.
        var parent = piece.isDragging || (zone.flyOnTop && tr.duration != 0f) ? DragLayer : zone.PieceParent;
        Reparent(piece, parent);
        piece.transform.SetAsLastSibling();

        if (oldZone != null && oldZone != zone) oldZone.Relayout();
        // Çağıranın transition'ı (spawn/flip/lift) yalnız taşınan piece'e ait; komşular zone default'u ile kayar.
        zone.Relayout();
        ApplyTarget(piece, zone.PoseFor(piece), tr);
    }

    // Zone listeleri arasında taşıma: yalnız slot yazılır (realSlot dokunulmaz). SetSlot ve drag preview'ın ortak yolu.
    // relayout=true ise iki zone'un komşuları kayar; piece'in kendisine hedef verilmez (drag'de dışarıdan sürülür).
    // Hiçbir şey değişmediyse false döner.
    internal bool PlaceInList(BoardPieceBase piece, BoardZone zone, int index, int groupNo, bool relayout)
    {
        var oldZone = piece.slot.zone;
        if (index < 0) index = zone.FirstFreeIndex();
        if (oldZone == zone && piece.slot.index == index && piece.slot.groupNo == groupNo) return false;

        if (oldZone != null)
        {
            oldZone.pieces.Remove(piece);
            oldZone.Reindex();
        }
        if (zone.IsIndexed)
        {
            zone.pieces.Add(piece);
        }
        else
        {
            index = Mathf.Clamp(index, 0, zone.pieces.Count);
            zone.pieces.Insert(index, piece);
        }
        piece.slot = new Slot(zone, index, groupNo);
        zone.Reindex();
        if (relayout)
        {
            if (oldZone != null && oldZone != zone) oldZone.Relayout();
            zone.Relayout();
        }
        return true;
    }

    // İki piece'in slot'larını atomik yer değiştirir (okey değişimi vb.). Arada boş/çift dolu hücre oluşmaz,
    // her zone bir kez relayout olur; a ve b yalnız kendi transition'larını alır.
    public void Swap(BoardPieceBase a, BoardPieceBase b, Transition? transitionA = null, Transition? transitionB = null)
    {
        if (a == b) return;
        var za = a.slot.zone; var zb = b.slot.zone;
        if (za == null || zb == null || !za.HoldsPieces || !zb.HoldsPieces) return;

        var sa = a.slot; var sb = b.slot;
        var ia = za.pieces.IndexOf(a); var ib = zb.pieces.IndexOf(b);
        za.pieces[ia] = b; zb.pieces[ib] = a;
        a.slot = a.realSlot = sb;
        b.slot = b.realSlot = sa;
        // Aynı zone içinde liste sırası = index olsun diye sıralı tut (IsIndexed=false zone'larda Reindex ezer).
        za.Reindex(); if (zb != za) zb.Reindex();

        var tra = transitionA ?? zb.defaultTransition;
        var trb = transitionB ?? za.defaultTransition;
        Reparent(a, a.isDragging || (zb.flyOnTop && tra.duration != 0f) ? DragLayer : zb.PieceParent);
        Reparent(b, b.isDragging || (za.flyOnTop && trb.duration != 0f) ? DragLayer : za.PieceParent);
        a.transform.SetAsLastSibling(); b.transform.SetAsLastSibling();

        za.Relayout(); if (zb != za) zb.Relayout();
        ApplyTarget(a, zb.PoseFor(a), tra);
        ApplyTarget(b, za.PoseFor(b), trb);
    }

    // Zone'dan çıkar, slot None olur; görsel olarak yerinde kalır (çağıran release eder veya yeniden slot verir).
    public void ClearSlot(BoardPieceBase piece)
    {
        var zone = piece.slot.zone;
        if (zone == null) return;
        zone.pieces.Remove(piece);
        zone.Reindex();
        piece.slot = piece.realSlot = Slot.None;
        Reparent(piece, DragLayer);
        zone.Relayout();
    }

    internal void ApplyTarget(BoardPieceBase piece, Pose target, in Transition tr)
    {
        // Hedef flyOnTop katmanındaysa Layout zone-local pose'unu dragLayer uzayına çevir.
        var zone = piece.slot.zone;
        if (zone != null && piece.transform.parent != zone.PieceParent)
        {
            var zp = zone.PieceParent;
            var wp = zp.TransformPoint(target.position);
            var wr = zp.rotation * target.rotation;
            var ws = Vector3.Scale(zp.lossyScale, target.scale);
            target = Pose.WorldToLocal(piece.transform.parent, wp, wr, ws, target.faceUp);
        }
        piece.SetTarget(target, tr, Time.time);
    }

    protected void Reparent(BoardPieceBase piece, Transform parent)
    {
        var t = piece.transform;
        if (t.parent == parent) return;
        // Uçuş ortasındaysa hedefi dünyada sabitle, yeni uzayda yeniden tanımla.
        var moving = piece._moving;
        var old = t.parent;
        var tg = piece._target;
        Vector3 wp = default; Quaternion wr = default; Vector3 ws = default;
        if (moving)
        {
            wp = old.TransformPoint(tg.position); wr = old.rotation * tg.rotation; ws = Vector3.Scale(old.lossyScale, tg.scale);
        }
        t.SetParent(parent, true);
        piece.RebaseToParent();
        if (moving) piece._target = Pose.WorldToLocal(parent, wp, wr, ws, tg.faceUp);
    }

    protected virtual void Update()
    {
        var now = Time.time;
        var i = 0;
        while (i < _pieces.Count)
        {
            var piece = _pieces[i];
            if (piece.Tick(now))
            {
                var zone = piece.slot.zone;
                // flyOnTop ile uçtuysa varışta zone'a in (sink zone'a inilmez, release edilir).
                if (zone != null && zone.HoldsPieces && !piece.isDragging && piece.transform.parent != zone.PieceParent)
                {
                    Reparent(piece, zone.PieceParent);
                    zone.SortChildren();
                }
                var release = piece.OnArrived() || piece.releaseWhenArrived;
                if (release)
                {
                    piece.releaseWhenArrived = false;
                    ReleasePieceBase(piece);
                    continue;
                }
            }
            i++;
        }
    }

    protected abstract void ReleasePieceBase(BoardPieceBase piece);
}

public class BoardGame<T> : BoardGameBase where T : BoardPiece<T>
{
    public T piecePrefab;
    readonly List<T> _pool = new List<T>();
    protected readonly List<T> _draggingPieces = new List<T>();

    public T AllocPiece(int no)
    {
        T piece;
        if (_pool.Count > 0)
        {
            piece = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            piece = Instantiate(piecePrefab);
            piece._game = this;
            piece._gameBase = this;
        }
        piece.transform.SetParent(DragLayer, false);
        piece.ResetState(no);
        piece.gameObject.SetActive(true);
        piece.UpdateVisual();
        _pieces.Add(piece);
        return piece;
    }

    public void ReleasePiece(T piece)
    {
        if (piece == null || piece._released) return;
        piece._released = true;
        var zone = piece.slot.zone;
        if (zone != null && zone.HoldsPieces && zone.pieces.Remove(piece))
        {
            zone.Reindex();
            zone.Relayout();
        }
        piece.slot = piece.realSlot = Slot.None;
        _pieces.Remove(piece);
        _draggingPieces.Remove(piece);
        piece.gameObject.SetActive(false);
        piece.transform.SetParent(DragLayer, false);
        piece.OnReleased();
        _pool.Add(piece);
    }

    protected override void ReleasePieceBase(BoardPieceBase piece) => ReleasePiece((T)piece);

    // Yeni piece doğur ve from zone'undan to zone'una uçur (rakip koltuğundan ıskartaya vb.).
    public T Spawn(int no, BoardZone from, BoardZone to, int index = -1, int groupNo = 0, Transition? transition = null)
    {
        var piece = AllocPiece(no);
        var tr = transition ?? to.defaultTransition;
        if (from != null)
        {
            tr = tr.From(from);
            from.OnPieceTaken();
        }
        SetSlot(piece, to, index, groupNo, tr);
        return piece;
    }

    public void ReleaseAll()
    {
        while (_pieces.Count > 0) ReleasePiece((T)_pieces[0]);
    }

    internal void _onPiecePointerDown(T piece, PointerEventData ev) => OnPiecePointerDown(piece, ev);
    internal void _onPiecePointerUp(T piece, PointerEventData ev)
    {
        if (piece != _dragPiece || ev.pointerId != piece._pointerId) return;
        EndDrag(ev);
    }
    internal void _onPieceDrag(T piece, PointerEventData ev)
    {
        if (piece != _dragPiece || ev.pointerId != piece._pointerId) return;
        UpdateDrag(ev.position, ev.pressEventCamera);
    }

    // ---- Drag (1a: tek pointer, tek piece; bırakınca realSlot'a döner) ----
    [Tooltip("Drag sırasında piece ölçeği (DragLayer uzayında).")]
    public float dragScale = 1.1f;
    [Tooltip("Drag'e girerken lift morph süresi (sn).")]
    public float dragLiftDuration = 0.12f;

    T _dragPiece;
    Vector2 _grabOffset;
    BoardZone _previewZone;
    BoardPieceBase _previewSwap;
    public T DraggingPiece => _dragPiece;
    // Şu an bırakılırsa gidilecek zone / yer değiştirilecek piece (OnPieceDropped içinde oyun bakar).
    public BoardZone PreviewZone => _previewZone;
    public BoardPieceBase PreviewSwapWith => _previewSwap;

    // Piece'i pointer'ın altında tutarak drag'e al. Slot'u değişmez (komşular Relayout'ta ona yer ayırır).
    // Morph kanalı çalışmaya devam eder: SetSlot ile verilmiş flip/scale drag sırasında da akar.
    public void BeginDrag(T piece, PointerEventData ev)
    {
        if (_dragPiece != null) EndDrag(null);
        _dragPiece = piece;
        piece._pointerId = ev.pointerId;
        piece.isDragging = true;
        Reparent(piece, DragLayer);
        piece.transform.SetAsLastSibling();

        var local = ScreenToDragLocal(ev.position, ev.pressEventCamera);
        _grabOffset = (Vector2)piece.pose.position - local;
        piece.LiftTo(Quaternion.identity, Vector3.one * dragScale, dragLiftDuration, Time.time);
    }

    void UpdateDrag(Vector2 screen, Camera cam)
    {
        var piece = _dragPiece;
        var local = ScreenToDragLocal(screen, cam) + _grabOffset;
        piece.SetDragPosition(new Vector3(local.x, local.y, 0));

        // Üstteki (listede sonraki) zone öncelikli; oyun kuralı CanDrop ile eler.
        BoardZone hit = null; Vector2 zl = default;
        if (zones != null)
            for (var i = zones.Length - 1; i >= 0; i--)
            {
                var z = zones[i];
                if (z != null && z.HoldsPieces && z.HitTest(screen, cam, out zl) && CanDrop(piece, z)) { hit = z; break; }
            }

        if (hit == null || !hit.PreviewSlot(piece, zl, out var index, out var groupNo, out var swap))
        {
            // Hiçbir zone kabul etmiyor: gerçek yerindeki boşluk geri açılır.
            _previewZone = null; _previewSwap = null;
            PlaceInList(piece, piece.realSlot.zone, piece.realSlot.index, piece.realSlot.groupNo, relayout: true);
            return;
        }
        _previewZone = hit;
        _previewSwap = swap;
        if (swap != null)
            PlaceInList(piece, piece.realSlot.zone, piece.realSlot.index, piece.realSlot.groupNo, relayout: true);
        else
            PlaceInList(piece, hit, index, groupNo, relayout: true);
    }

    public void EndDrag(PointerEventData ev)
    {
        var piece = _dragPiece;
        if (piece == null) return;
        _dragPiece = null;
        piece._pointerId = int.MinValue;
        piece.isDragging = false;
        var zone = _previewZone; var swap = _previewSwap;
        if (OnPieceDropped(piece, ev)) { _previewZone = null; _previewSwap = null; return; }
        _previewZone = null; _previewSwap = null;
        if (zone == null) { ReturnToSlot(piece); return; }
        if (swap != null) Swap(piece, swap);
        else SetSlot(piece, zone, piece.slot.index, piece.slot.groupNo);
        OnPieceCommitted(piece, zone);
    }

    // Piece'i gerçek slot'una geri uçur (drag iptali / kabul edilmeyen bırakma).
    public void ReturnToSlot(T piece)
    {
        var zone = piece.realSlot.zone;
        if (zone == null) return;
        PlaceInList(piece, zone, piece.realSlot.index, piece.realSlot.groupNo, relayout: true);
        piece.realSlot = piece.slot;
        ApplyTarget(piece, zone.PoseFor(piece), zone.defaultTransition);
    }

    protected Vector2 ScreenToDragLocal(Vector2 screen, Camera cam)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(DragLayer as RectTransform, screen, cam, out var local);
        return local;
    }

    // Varsayılan: izin varsa basar basmaz drag. Oyun, deck'ten çekme gibi "önce SetSlot sonra drag" akışını burada kurar.
    protected virtual void OnPiecePointerDown(T piece, PointerEventData ev)
    {
        if (CanDragPiece(piece)) BeginDrag(piece, ev);
    }
    // true dönerse oyun bırakmayı kendi işledi (SetSlot/Swap vb.); false → PreviewZone varsa oraya commit, yoksa slot'una döner.
    protected virtual bool OnPieceDropped(T piece, PointerEventData ev) => false;
    // Varsayılan commit sonrası bildirim (oyun modele yazar / sunucuya gönderir).
    protected virtual void OnPieceCommitted(T piece, BoardZone zone) { }
    public virtual bool CanDragPiece(T piece) => true;
    // Drag sırasında bu zone üstünde önizleme/bırakma kabul edilir mi (oyun kuralı).
    public virtual bool CanDrop(T piece, BoardZone zone) => true;
}
