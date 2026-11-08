using System.Collections.Generic;
using UnityEngine;

// Zone tabanı: piece'lerin parent'ı ve layout stratejisi. Pose'u yalnız Layout üretir.
// Tipe özel parametreler alt sınıflarda (StackZone, RowZone, GridZone, SeatZone...).
public abstract class BoardZone : MonoBehaviour
{
    internal BoardGameBase _game;

    [Tooltip("Boşsa zone transform'unun kendisi parent olur.")]
    public Transform pieceParent;
    public Transition defaultTransition = Transition.Default;
    public bool faceUp = true;
    [Tooltip("Uçuş sırasında piece drag katmanında (her şeyin üstünde) gitsin.")]
    public bool flyOnTop;
    [Tooltip("-1 = sınırsız.")]
    public int capacity = -1;
    [Tooltip("Drag bırakma alanı (PieceParent-local Rect). Boyutu sıfır = bu zone'a bırakılamaz.")]
    public Rect dropRect;

    internal readonly List<BoardPieceBase> pieces = new List<BoardPieceBase>();

    public Transform PieceParent => pieceParent != null ? pieceParent : transform;
    public int Count => pieces.Count;

    // index: slot.index, count: zone'daki toplam piece, seed: piece kimliğine bağlı 0..1 (jitter için).
    public abstract Pose Layout(int index, int count, float seed);

    // seed'den deterministik -1..1 (salt farklı eksenler için).
    protected static float Jitter(float seed, int salt)
    {
        var v = Mathf.Sin(seed * 12.9898f + salt * 78.233f) * 43758.5453f;
        return (v - Mathf.Floor(v)) * 2f - 1f;
    }

    // true: index hücredir, boşluk olabilir (Grid). false: liste sırası = index.
    public virtual bool IsIndexed => false;

    // false: piece'ler burada kalmaz; varınca release edilir, yalnız sayaç tutulur (SeatZone).
    public virtual bool HoldsPieces => true;

    // HoldsPieces=false zone'lar için sayaç hook'ları (gönderildi / alındı).
    protected internal virtual void OnPieceSent(BoardPieceBase piece) { }
    protected internal virtual void OnPieceTaken() { }

    protected Pose BasePose()
    {
        var p = Pose.Identity;
        p.faceUp = faceUp;
        return p;
    }

    // ---- Drag preview sözleşmesi ----
    // Ekran noktası dropArea içinde mi; local = PieceParent uzayında nokta (Layout ile aynı uzay).
    public bool HitTest(Vector2 screen, Camera cam, out Vector2 local)
    {
        local = default;
        if (dropRect.width <= 0f || dropRect.height <= 0f) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(PieceParent as RectTransform, screen, cam, out local)) return false;
        return dropRect.Contains(local);
    }

    // Pointer'daki nokta için geçici slot öner. swapWith != null → hücre dolu, bırakınca yer değiştirilecek
    // (piece'in slot'u değişmez). false → bu nokta kabul edilmez. Varsayılan: kabul etmez.
    public virtual bool PreviewSlot(BoardPieceBase piece, Vector2 local, out int index, out int groupNo, out BoardPieceBase swapWith)
    {
        index = -1; groupNo = 0; swapWith = null;
        return false;
    }

    // Preview sırasında piece bu zone'da sayılıyorsa count'a dahil; değilse +1.
    protected int CountWith(BoardPieceBase piece) => piece.slot.zone == this ? pieces.Count : pieces.Count + 1;

    // Varsayılan: her piece için Layout. Grup/akış bilen zone'lar (melding vb.) bunu ezer.
    public virtual void Relayout(in Transition tr)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.isDragging) continue;
            _game.ApplyTarget(piece, Layout(piece.slot.index, pieces.Count, piece.seed), tr);
        }
        SortChildren();
    }

    // Çizim sırası anahtarı (küçük altta). Varsayılan slot.index: sırada sağdaki, yığında üstteki üstte.
    public virtual int DrawOrder(BoardPieceBase piece) => piece.slot.index;

    static readonly List<BoardPieceBase> _sortBuf = new List<BoardPieceBase>();

    // Zone'a inmiş piece'leri DrawOrder'a göre sibling sırasına koyar; uçuştakiler (DragLayer) dokunulmaz.
    public void SortChildren()
    {
        var parent = PieceParent;
        _sortBuf.Clear();
        for (var i = 0; i < pieces.Count; i++)
            if (pieces[i].transform.parent == parent) _sortBuf.Add(pieces[i]);
        if (_sortBuf.Count < 2) { _sortBuf.Clear(); return; }
        // Kararlı insertion sort (küçük listeler, çoğu zaman zaten sıralı).
        for (var i = 1; i < _sortBuf.Count; i++)
        {
            var p = _sortBuf[i]; var k = DrawOrder(p); var j = i - 1;
            while (j >= 0 && DrawOrder(_sortBuf[j]) > k) { _sortBuf[j + 1] = _sortBuf[j]; j--; }
            _sortBuf[j + 1] = p;
        }
        // Piece olmayan çocuklar (avatar vb.) en altta kalsın: piece'ler parent'ın sonuna sıralı dizilir.
        for (var i = 0; i < _sortBuf.Count; i++)
            _sortBuf[i].transform.SetAsLastSibling();
        _sortBuf.Clear();
    }

    public void Relayout() => Relayout(defaultTransition);

    // Tek piece için hedef pose (SetSlot taşınan piece'e çağıranın transition'ını vermek için kullanır).
    public virtual Pose PoseFor(BoardPieceBase piece) => Layout(piece.slot.index, pieces.Count, piece.seed);

    internal void Reindex()
    {
        if (IsIndexed) return;
        for (var i = 0; i < pieces.Count; i++)
            pieces[i].slot.index = i;
    }

    public BoardPieceBase PieceAt(int index)
    {
        for (var i = 0; i < pieces.Count; i++)
            if (pieces[i].slot.index == index) return pieces[i];
        return null;
    }

    public BoardPieceBase Top => pieces.Count > 0 ? pieces[pieces.Count - 1] : null;

    public int FirstFreeIndex()
    {
        if (!IsIndexed) return pieces.Count;
        var cap = capacity > 0 ? capacity : int.MaxValue;
        for (var i = 0; i < cap; i++)
            if (PieceAt(i) == null) return i;
        return -1;
    }
}
