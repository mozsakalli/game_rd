using UnityEngine;

// Sabit yerleşimli sıra (101 meld gibi): pose yalnız index'in fonksiyonudur, count'a bağlı değildir.
// Bu yüzden ekleme/çıkarma yerleşmiş taşları kıpırdatmaz; index sahiplidir (IsIndexed), boşluk olabilir.
// groupNo satır seçer (gruplu melding için). Sola ekleme ve grup akışı oyun tarafı türevlerinin işidir.
public class AnchoredRowZone : BoardZone
{
    public float spacing = 75f;
    public float rowSpacing = 110f;

    public override bool IsIndexed => true;

    public override Pose Layout(int index, int count, float seed)
    {
        var p = BasePose();
        p.position = new Vector3(index * spacing, 0, 0);
        return p;
    }

    // groupNo → satır; base Layout index'i satır içi konumdur.
    public override Pose PoseFor(BoardPieceBase piece)
    {
        var p = Layout(piece.slot.index, pieces.Count, piece.seed);
        p.position.y = -piece.slot.groupNo * rowSpacing;
        return p;
    }

    public BoardPieceBase PieceAtCell(int index, int groupNo)
    {
        for (var i = 0; i < pieces.Count; i++)
            if (pieces[i].slot.index == index && pieces[i].slot.groupNo == groupNo) return pieces[i];
        return null;
    }

    // Pointer'ın hücresi: boşsa oraya; doluysa swap adayı (yerleşmiş taşlar kıpırdamaz).
    public override bool PreviewSlot(BoardPieceBase piece, Vector2 local, out int index, out int groupNo, out BoardPieceBase swapWith)
    {
        index = Mathf.Max(0, Mathf.RoundToInt(local.x / spacing));
        groupNo = Mathf.Max(0, Mathf.RoundToInt(-local.y / rowSpacing));
        var occupant = PieceAtCell(index, groupNo);
        swapWith = occupant != null && occupant != piece ? occupant : null;
        return true;
    }

    public override int DrawOrder(BoardPieceBase piece) => piece.slot.groupNo * 10000 + piece.slot.index;

    public override void Relayout(in Transition tr)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.isDragging) continue;
            _game.ApplyTarget(piece, PoseFor(piece), tr);
        }
        SortChildren();
    }
}
