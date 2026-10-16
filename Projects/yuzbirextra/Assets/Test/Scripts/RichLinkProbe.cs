using DigitoyEngine;

// Rich text link tiklama probu (Test/RichTextCheck.scene): ayni GO'daki LayoutBox.LinkAt(e) -> Console.
// Oyun deseni: LayoutBox'in kendi pointer handler'i yok; davranis component'i sorar.
public class RichLinkProbe : Component
{
    protected override void OnPointerClick(PointerEvent e)
    {
        if (e.dragged) return;
        string id = GetComponent<LayoutBox>()?.LinkAt(e);
        Debug.Log(id != null ? "[richlink] click: " + id : "[richlink] click outside links");
        e.Use();
    }
}