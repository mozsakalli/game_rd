using UnityEngine;

// Hücreli ızgara (okey ıstakası). index = hücre; boş hücre olabilir. Sol-üst köşe orijin.
public class GridZone : BoardZone
{
    public int columns = 10;
    public int rows = 2;
    public Vector2 cellSize = new Vector2(60, 90);

    public override bool IsIndexed => true;

    void OnValidate() => capacity = columns * rows;

    public int Column(int index) => index % Mathf.Max(1, columns);
    public int Row(int index) => index / Mathf.Max(1, columns);
    public int Index(int row, int col) => row * columns + col;

    public override Pose Layout(int index, int count, float seed)
    {
        var p = BasePose();
        p.position = new Vector3(Column(index) * cellSize.x + cellSize.x * 0.5f, -Row(index) * cellSize.y - cellSize.y * 0.5f, 0);
        return p;
    }

    void Awake() { if (capacity <= 0) capacity = columns * rows; }
}
