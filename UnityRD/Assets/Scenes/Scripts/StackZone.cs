using UnityEngine;

// Yığın: deste, ıskarta, tavla noktası. offset = her piece için kayma (yön vektörü).
public class StackZone : BoardZone
{
    public Vector3 offset = Vector3.zero;
    [Tooltip(">0 ise bu sayıdan sonra ofsetler sıkışır (toplam uzunluk sabit kalır).")]
    public int compactAfter = -1;
    [Header("Jitter (piece kimliğine bağlı, relayout'ta sabit)")]
    public Vector2 positionJitter = Vector2.zero;
    [Tooltip("Derece, ± bu kadar.")]
    public float rotationJitter = 0f;

    public override Pose Layout(int index, int count, float seed)
    {
        var p = BasePose();
        var step = offset;
        if (compactAfter > 0 && count > compactAfter)
            step *= (float)compactAfter / count;
        p.position = step * index;
        if (positionJitter != Vector2.zero)
            p.position += new Vector3(Jitter(seed, 1) * positionJitter.x, Jitter(seed, 2) * positionJitter.y, 0);
        if (rotationJitter != 0f)
            p.rotation = Quaternion.Euler(0, 0, Jitter(seed, 3) * rotationJitter);
        return p;
    }

    // Yığına bırakma her zaman üste.
    public override bool PreviewSlot(BoardPieceBase piece, Vector2 local, out int index, out int groupNo, out BoardPieceBase swapWith)
    {
        groupNo = piece.slot.groupNo; swapWith = null;
        index = CountWith(piece) - 1;
        return true;
    }
}
