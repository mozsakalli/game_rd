using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Evet/Hayir onayi (silme gibi geri alinamaz isler). Esc / odak kaybi / Cancel = hayir.
// Callback popup kapandiktan SONRA kosar.
public sealed class ConfirmPopup : EditorPopup
{
    const float W = 360f, H = 92f;

    static ConfirmPopup _instance;
    public static void Show(string title, string detail, string okLabel, Action onOk)
    {
        _instance ??= new ConfirmPopup();
        var p = _instance;
        p._title = title;
        p._detail = detail ?? "";
        p._okLabel = okLabel ?? "OK";
        p._onOk = onOk;
        p._accepted = false;
        p.Open(W, H);
    }

    string _title, _detail, _okLabel;
    Action _onOk;
    bool _accepted;

    protected override void OnGui(Event ev)
    {
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawTextIn(new Rect(12, 10, W - 24, 20), _title, Gui.FontSize - 1f,
                new Color(220, 224, 232, 255), false, 2);
            GuiRenderer.DrawTextIn(new Rect(12, 32, W - 24, 18), _detail, Gui.FontSize - 3f,
                new Color(150, 153, 163, 255), false, 2);
        }
        if (ev.Type == EventType.KeyDown && ev.KeyCode == GLFWConst.KEY_ENTER)
        {
            ev.Use();
            _accepted = true;
            Close();
            return;
        }
        float by = H - 32;
        if (Gui.Button(new Rect(W - 172, by, 80, 22), _okLabel))
        {
            _accepted = true;
            Close();
        }
        if (Gui.Button(new Rect(W - 86, by, 76, 22), "Cancel"))
            Close();
    }

    protected override void OnClosing()
    {
        bool ok = _accepted;
        _accepted = false;
        if (ok)
            _onOk?.Invoke();
    }
}
