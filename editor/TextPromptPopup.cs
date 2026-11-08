using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Tek satir metin sorusu (rename vb.): [baslik] [alan] OK / Cancel. Enter = OK, Esc / odak kaybi = iptal.
// Acilista alan odaklanir ve [0, selectEnd) araligi secili gelir (dosya adinda uzanti disarida kalir).
// Sonuc callback'i popup kapandiktan SONRA kosar.
public sealed class TextPromptPopup : EditorPopup
{
    const float W = 380f, H = 84f;

    static TextPromptPopup _instance;
    public static void Show(string title, string initial, Action<string> onOk, int selectEnd = -1)
    {
        _instance ??= new TextPromptPopup();
        var p = _instance;
        p._title = title;
        p._onOk = onOk;
        p._result = null;
        initial ??= "";
        if (p._buf.Length < initial.Length + 16)
            p._buf = new char[initial.Length + 64];
        p._len = initial.Length;
        initial.CopyTo(0, p._buf, 0, p._len);
        p._selectEnd = selectEnd < 0 || selectEnd > p._len ? p._len : selectEnd;
        p._focusPending = true;
        p.Open(W, H);
    }

    string _title;
    Action<string> _onOk;
    string _result;
    char[] _buf = new char[128];
    int _len;
    int _selectEnd;
    bool _focusPending;

    protected override void OnGui(Event ev)
    {
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(12, 8, W - 24, 18), _title, Gui.FontSize - 2f,
                new Color(220, 224, 232, 255), false, 2);
        // Enter: alan odakliyken bile OK (TextField Enter'i kullanmaz).
        if (ev.Type == EventType.KeyDown && ev.KeyCode == GLFWConst.KEY_ENTER)
        {
            ev.Use();
            Commit();
            return;
        }
        Gui.TextField(new Rect(12, 28, W - 24, 20), ref _buf, ref _len);
        if (_focusPending)
        {
            _focusPending = false;
            Gui.FocusTextField(GuiUtility.LastControlID, 0, _selectEnd);
        }
        float by = H - 30;
        if (Gui.Button(new Rect(W - 172, by, 80, 22), "OK"))
            Commit();
        if (Gui.Button(new Rect(W - 86, by, 76, 22), "Cancel"))
            Close();
    }

    void Commit()
    {
        _result = new string(_buf, 0, _len);
        Close();
    }

    protected override void OnClosing()
    {
        GuiUtility.KeyboardControl = 0;
        var r = _result;
        var cb = _onOk;
        _result = null;
        if (r != null)
            cb?.Invoke(r);
    }
}
