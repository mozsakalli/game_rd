#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Open Points paneli (docs/engine-notes.md "KOD SAHNESI"): Play'deki kod sahnesinin `virtual` literal uyelerini
// dokunulabilir listeler. Degisiklik dogrudan C# kaynaga yazilir (OpenPoints.Set) -> AssetWatcher derler -> Play taze
// dogar. Surukleme sirasinda yazim debounce'lu: en az WriteGapMs ve onceki derleme bitmisse; birakinca son deger yazilir.
[MenuItem("Window/Open Points", 7)]
public sealed class OpenPointsPanel : EditorWindow
{
    const float RowH = 22;
    const int WriteGapMs = 150;

    Vec2 _scroll;
    // Bekleyen (henuz yazilmamis) degerler: key -> deger. Hot kontrol surerken ya da derleme surerken birikir.
    readonly Dictionary<string, object> _pending = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    long _lastWriteMs;
    string _error;
    // String alanlari icin tampon (key -> buffer); TextField caller tamponu ister.
    readonly Dictionary<string, (char[] buf, int len, string seen)> _text = new();

    public OpenPointsPanel() => Title = "Open Points";

    protected override void OnGui()
    {
        Event ev = Event.Current;
        Rect area = GuiClip.VisibleRect;
        var scene = PlayMode.PlayScene;
        if (scene == null)
        {
            Gui.Label(new Rect(4, 4, area.width - 8, 20), "Enter Play on a code scene (startScene = class name).");
            return;
        }
        var points = OpenPoints.Points;
        if (points.Count == 0)
        {
            Gui.Label(new Rect(4, 4, area.width - 8, 20), "No open points: declare `protected virtual float X => 10;` in the scene class.");
            return;
        }

        float nameW = MathF.Min(160, area.width * 0.35f);
        float clsW = 110;
        var view = new Rect(0, 0, area.width - 16, points.Count * RowH + 24);
        _scroll = Gui.BeginScrollView(area, _scroll, view);
        float y = 0;
        string lastCls = null;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var row = new Rect(0, y, view.width, RowH);
            if (ev.Type == EventType.Repaint)
            {
                if ((i & 1) == 1)
                    GuiRenderer.DrawRect(row, new Color(255, 255, 255, 8), 0);
                GuiRenderer.DrawTextIn(new Rect(4, y, nameW - 4, RowH), p.Name, Gui.FontSize - 1f,
                    p.Writable ? new Color(220, 224, 232, 255) : new Color(140, 144, 152, 255));
                if (p.Declaring.Name != lastCls)
                    GuiRenderer.DrawTextIn(new Rect(view.width - clsW, y, clsW - 4, RowH), p.Declaring.Name, Gui.FontSize - 3f,
                        new Color(150, 160, 190, 255));
            }
            lastCls = p.Declaring.Name;
            var ctl = new Rect(nameW, y + 2, view.width - nameW - clsW - 4, RowH - 4);
            if (!p.Writable)
            {
                if (ev.Type == EventType.Repaint)
                    GuiRenderer.DrawTextIn(ctl, p.Expr ?? OpenPoints.Show(p.Value), Gui.FontSize - 2f, new Color(140, 144, 152, 255));
            }
            else
                DrawControl(p, ctl);
            y += RowH;
        }
        Gui.EndScrollView();

        if (_error != null && ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(4, area.height - 20, area.width - 8, 18), _error, Gui.FontSize - 2f, new Color(230, 120, 110, 255));
        if (AssetWatcher.IsBuilding && ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(area.width - 90, area.height - 20, 86, 18), "compiling...", Gui.FontSize - 2f, new Color(200, 190, 120, 255));

        Flush(force: ev.Type == EventType.MouseUp);
    }

    object Current(OpenPoints.Point p) => _pending.TryGetValue(p.Key, out var v) ? v : p.Value;

    void Queue(OpenPoints.Point p, object v)
    {
        if (Equals(v, Current(p)))
            return;
        _pending[p.Key] = v;
    }

    void DrawControl(OpenPoints.Point p, in Rect r)
    {
        var t = p.Type;
        object cur = Current(p);
        if (t == typeof(float))
        {
            float v = Convert.ToSingle(cur);
            // Kaynaga yazilacak deger okunur olsun: buyuk degerler tam sayi, kucukler 0.1 adim.
            float step = MathF.Abs(v) >= 20 ? 1f : 0.1f;
            float nv = Gui.DragFloat(r, v, step * 0.5f);
            Queue(p, MathF.Round(nv / step) * step);
        }
        else if (t == typeof(int))
            Queue(p, Gui.DragInt(r, Convert.ToInt32(cur), 0.1f));
        else if (t == typeof(bool))
            Queue(p, Gui.Toggle(new Rect(r.x, r.y, r.height, r.height), (bool)cur));
        else if (t.IsEnum)
        {
            // Tik = sonraki uye (kucuk enumlar icin yeterli; dropdown sonra).
            if (Gui.Button(r, Enum.GetName(t, cur) ?? cur.ToString()))
            {
                var names = Enum.GetNames(t);
                int idx = Array.IndexOf(names, Enum.GetName(t, cur));
                Queue(p, Enum.Parse(t, names[(idx + 1) % names.Length]));
            }
        }
        else if (t == typeof(string))
            DrawString(p, r, (string)cur ?? "");
        else if (t == typeof(Color))
        {
            var c = (Color)cur;
            if (ColorPickerWindow.TryGet(p.Key, out var picked))
                Queue(p, c = picked);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawRect(new Rect(r.x, r.y, 40, r.height), c, 0);
            if (Gui.Button(new Rect(r.x + 44, r.y, 80, r.height), OpenPoints.Show(c)))
                ColorPickerWindow.Open(p.Key, c);
        }
        else if (t == typeof(Vec2))
        {
            var v = (Vec2)cur;
            float w = (r.width - 4) / 2;
            v.x = Half(Gui.DragFloat(new Rect(r.x, r.y, w, r.height), v.x, 0.5f));
            v.y = Half(Gui.DragFloat(new Rect(r.x + w + 4, r.y, w, r.height), v.y, 0.5f));
            Queue(p, v);
        }
        else if (t == typeof(Vec3))
        {
            var v = (Vec3)cur;
            float w = (r.width - 8) / 3;
            v.x = Half(Gui.DragFloat(new Rect(r.x, r.y, w, r.height), v.x, 0.5f));
            v.y = Half(Gui.DragFloat(new Rect(r.x + w + 4, r.y, w, r.height), v.y, 0.5f));
            v.z = Half(Gui.DragFloat(new Rect(r.x + 2 * (w + 4), r.y, w, r.height), v.z, 0.5f));
            Queue(p, v);
        }
    }

    static float Half(float x) => MathF.Round(x * 2) / 2;

    void DrawString(OpenPoints.Point p, in Rect r, string cur)
    {
        if (!_text.TryGetValue(p.Key, out var st) || st.seen != cur)
        {
            int cap = 256;
            while (cap <= cur.Length) cap *= 2;
            st = (new char[cap], cur.Length, cur);
            cur.CopyTo(0, st.buf, 0, cur.Length);
        }
        var buf = st.buf;
        int len = st.len;
        Gui.TextField(r, ref buf, ref len);
        // Metin: her tusta derleme istemiyoruz; Enter ile yaz.
        var ev = Event.Current;
        bool commit = ev.Type == EventType.KeyDown && (ev.KeyCode == GLFWConst.KEY_ENTER || ev.KeyCode == 335 /* GLFW KP_ENTER */);
        string now = new string(buf, 0, len);
        _text[p.Key] = (buf, len, commit ? now : cur);
        if (commit && now != cur)
        {
            Queue(p, now);
            Flush(force: true);
        }
    }

    // Bekleyenleri koda yaz. force=false: hiz siniri + derleme bitmis olmali.
    void Flush(bool force)
    {
        if (_pending.Count == 0)
            return;
        long now = _clock.ElapsedMilliseconds;
        if (!force && (now - _lastWriteMs < WriteGapMs || AssetWatcher.IsBuilding))
            return;
        _error = null;
        var keys = new List<string>(_pending.Keys);
        foreach (var k in keys)
        {
            var p = OpenPoints.Find(k);
            if (p == null) { _pending.Remove(k); continue; }
            if (OpenPoints.Set(p, _pending[k], out var err))
                _pending.Remove(k);
            else
            {
                _error = err;
                _pending.Remove(k);
            }
        }
        _lastWriteMs = now;
    }
}
#endif
