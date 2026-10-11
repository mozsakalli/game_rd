using DigitoyEngine;

// Policy metnindeki tiklanabilir baglanti parcasi (Hizmet Kosullari / Gizlilik Politikasi).
// Simdilik yalnizca log yazar; ileride tarayici/popup acilacak.
public class PolicyLink : Component
{
    public string linkId = "";

    protected override void OnPointerClick(PointerEvent e)
    {
        if (e.dragged) return;
        System.Console.WriteLine($"[PolicyLink] clicked: {linkId}");
        e.Use();
    }
}
