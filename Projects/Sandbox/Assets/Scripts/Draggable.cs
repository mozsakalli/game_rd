using DigitoyEngine;

// Ornek surukleme script'i: GO'ya ekle (ayni GO'da bir Renderer olsun — SpriteRenderer
// veya LayoutBox), Play'e gir, Game panelinde surukle. Esik/capture/kamera tipi/birim
// donusumu motorun isi; buradaki tek is: baslangic + toplam delta (kumulatif desen).
public sealed class Draggable : Component
{
    public bool tintWhileDragging = true;

    Vec3 _start;
    SpriteRenderer _sr;
    Color _origColor;

    protected override void Awake() => _sr = GetComponent<SpriteRenderer>();

    protected override void OnDragStart(PointerEvent e)
    {
        _start = transform.localPosition;
        if (tintWhileDragging && _sr != null)
        {
            _origColor = _sr.Color;
            _sr.Color = new Color(255, 220, 120, 255);
        }
    }

    protected override void OnDrag(PointerEvent e)
        => transform.localPosition = _start + e.TotalInParentOf(transform);

    protected override void OnPointerUp(PointerEvent e)
    {
        if (e.dragged && tintWhileDragging && _sr != null)
            _sr.Color = _origColor;
    }

    protected override void OnPointerClick(PointerEvent e)
        => EngineLog("Draggable: click (surukleme sayilmadi)");

    static void EngineLog(string msg) => System.Console.WriteLine(msg);
}
