using UnityEngine;

// Rakip koltuğu / avatar: piece tutmaz. Gönderilen piece buraya uçar ve varınca release edilir;
// buradan çıkan piece Spawn ile bu noktadan doğar. count yalnız sayaçtır (Sync için "rakip elinde N").
public class SeatZone : BoardZone
{
    [Tooltip("Varış/çıkış anındaki piece ölçeği (avatara küçülerek girsin).")]
    public float pieceScale = 0.5f;
    public int count;

    public override bool HoldsPieces => false;

    protected internal override void OnPieceSent(BoardPieceBase piece) => count++;
    protected internal override void OnPieceTaken() => count = Mathf.Max(0, count - 1);

    public override Pose Layout(int index, int cnt, float seed)
    {
        var p = BasePose();
        p.scale = Vector3.one * pieceScale;
        return p;
    }
}
