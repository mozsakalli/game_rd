using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// "Kaydedilmemis degisiklik var" sorusu: Save / Discard / Cancel. Odak kaybi veya Esc = Cancel.
// Secim callback'leri popup kapandiktan SONRA kosar (callback sahne/stage degistirebilir).
public sealed class SaveChangesPopup : EditorPopup
{
    const float W = 340f, H = 92f;

    static SaveChangesPopup _instance;
    public static void Show(string what, Action onSave, Action onDiscard)
    {
        _instance ??= new SaveChangesPopup();
        _instance._what = what;
        _instance._onSave = onSave;
        _instance._onDiscard = onDiscard;
        _instance._choice = null;
        _instance.Open(W, H);
    }

    string _what;
    Action _onSave, _onDiscard, _choice;

    protected override void OnGui(Event ev)
    {
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawTextIn(new Rect(12, 10, W - 24, 20), "Save changes to " + _what + "?", Gui.FontSize - 1f,
                new Color(220, 224, 232, 255), false, 2);
            GuiRenderer.DrawTextIn(new Rect(12, 32, W - 24, 18), "Unsaved edits will be lost if you discard.", Gui.FontSize - 3f,
                new Color(150, 153, 163, 255), false, 2);
        }
        float by = H - 32;
        if (Gui.Button(new Rect(W - 258, by, 80, 22), "Save"))
            Pick(_onSave);
        if (Gui.Button(new Rect(W - 172, by, 80, 22), "Discard"))
            Pick(_onDiscard);
        if (Gui.Button(new Rect(W - 86, by, 76, 22), "Cancel"))
            Pick(null);
    }

    void Pick(Action a)
    {
        _choice = a;
        Close();
    }

    protected override void OnClosing()
    {
        var c = _choice;
        _choice = null;
        c?.Invoke();
    }
}
