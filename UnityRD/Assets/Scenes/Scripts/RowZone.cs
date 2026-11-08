using UnityEngine;

// Yatay sıra (el, meld). Ortalanır; maxSpreadCount aşılınca aralık daralır.
public class RowZone : BoardZone
{
    public float spacing = 75f;
    [Tooltip(">0 ise bu sayıdan sonra aralık daralır (toplam genişlik sabit).")]
    public int maxSpreadCount = -1;

    protected float EffectiveSpacing(int count) =>
        maxSpreadCount > 0 && count > maxSpreadCount ? spacing * maxSpreadCount / count : spacing;

    public override Pose Layout(int index, int count, float seed)
    {
        var p = BasePose();
        p.position = new Vector3((index - (count - 1) * 0.5f) * EffectiveSpacing(count), 0, 0);
        return p;
    }

    // Pointer x → sıra içi index (komşular Relayout ile açılır). Grup oyunundur, korunur.
    public override bool PreviewSlot(BoardPieceBase piece, Vector2 local, out int index, out int groupNo, out BoardPieceBase swapWith)
    {
        groupNo = piece.slot.groupNo; swapWith = null;
        var count = CountWith(piece);
        var s = EffectiveSpacing(count);
        index = Mathf.Clamp(Mathf.RoundToInt(local.x / s + (count - 1) * 0.5f), 0, count - 1);
        return true;
    }
}
