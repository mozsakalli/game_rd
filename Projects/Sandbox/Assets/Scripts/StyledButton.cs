using System;
using DigitoyEngine;

// BoxStyle durum surucusu ornegi: ayni GO'daki LayoutBox'in State'ini pointer'dan surer.
// Gorunum tamamen stil asset'inde (Pressed/Disabled varyanti + transition); kod yalniz durum.
public sealed class StyledButton : Component
{
    public bool interactable = true;
    LayoutBox _box;
    bool _pressed;

    protected override void OnEnable()
    {
        _box = GetComponent<LayoutBox>();
        Refresh();
    }

    void Refresh()
    {
        if (_box == null) return;
        _box.SetState(!interactable ? BoxStyle.Disabled : _pressed ? BoxStyle.Pressed : 0);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (!interactable) { e.Use(); return; }
        _pressed = true; Refresh();
    }

    protected override void OnPointerUp(PointerEvent e) { _pressed = false; Refresh(); }
    protected override void OnDragStart(PointerEvent e) { _pressed = false; Refresh(); }
    protected override void OnPointerClick(PointerEvent e) { if (interactable && !e.dragged) Console.WriteLine("[styled] click " + gameObject.name); }
}