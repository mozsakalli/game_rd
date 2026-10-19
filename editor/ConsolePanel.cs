using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Editor log'u: derleme/import hatalari VERIDIR, crash degil. Console paneli
// gosterir; her giris stdout'a da akar. Ana thread disindan da cagrilabilir (lock).
public static class EditorLog
{
    public enum Severity { Info, Warning, Error }

    public readonly struct Entry
    {
        public readonly Severity Sev;
        public readonly string Message;
        public readonly DateTime Time; // yerel saat; stdout/log.tail "HH:mm:ss.fff" (AI "benden sonra mi?" ayrimi)
        public Entry(Severity s, string m, DateTime t) { Sev = s; Message = m; Time = t; }
        public string TimeText => Time.ToString("HH:mm:ss.fff");
    }

    const int Max = 500;
    static readonly List<Entry> _entries = new();
    static readonly object _lock = new();
    public static int Version; // panel scroll-to-bottom tetigi

    public static int ErrorCount { get; private set; }

    public static void Info(string msg) => Add(Severity.Info, msg);
    public static void Warning(string msg) => Add(Severity.Warning, msg);
    public static void Error(string msg) => Add(Severity.Error, msg);
    /// <summary>Gorsel ayrac (Play start/stop, hot reload): log akisinda oturum sinirlari.</summary>
    public static void Marker(string label) => Add(Severity.Info, $"â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ {label} â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");

    static void Add(Severity sev, string msg)
    {
        var entry = new Entry(sev, msg, DateTime.Now);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > Max)
                _entries.RemoveRange(0, _entries.Count - Max);
            if (sev == Severity.Error)
                ErrorCount++;
            Version++;
        }
        Console.WriteLine($"{entry.TimeText} [{sev.ToString().ToLowerInvariant()}] {msg}");
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            ErrorCount = 0;
            Version++;
        }
    }

    // Panel cizimi icin anlik kopya (kisa liste; frame basina bir kez cagrilir).
    public static void Snapshot(List<Entry> into)
    {
        into.Clear();
        lock (_lock)
            into.AddRange(_entries);
    }
}


[MenuItem("Window/Console", 3)]
public sealed class ConsolePanel : EditorWindow
{
    const float ToolbarH = 26f;
    const float RowH = 20f; // ProjectPanel ile ayni
    const float SplitterH = 5f, MinDetailH = 40f, MinListH = 60f;
    static readonly int HashSplit = "Console.Split".GetHashCode();

    Vec2 _scroll, _detailScroll;
    int _seenVersion = -1;
    float _detailH = 90f;
    readonly List<EditorLog.Entry> _view = new();
    // Secim: index degil girisin kendisi (Max kirpmasinda liste kayar). Message referansi + zaman yeterli ayirt edici.
    bool _hasSel;
    EditorLog.Entry _sel;

    public ConsolePanel() => Title = "Console";

    static Color SevColor(EditorLog.Severity s) => s switch
    {
        EditorLog.Severity.Error => new Color(235, 120, 120, 255),
        EditorLog.Severity.Warning => new Color(230, 200, 120, 255),
        _ => new Color(200, 204, 214, 255),
    };

    static string FullText(in EditorLog.Entry e)
        => $"{e.TimeText} [{e.Sev.ToString().ToLowerInvariant()}] {e.Message}";

    static void CopyToClipboard(in EditorLog.Entry e)
        => GLFW.SetClipboardString(NativeWindow.MainWindow, FullText(e));

    static bool Same(in EditorLog.Entry a, in EditorLog.Entry b)
        => ReferenceEquals(a.Message, b.Message) && a.Time == b.Time;

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        Event ev = Event.Current;
        if (Gui.Button(new Rect(2, 2, 56, 20), "Clear"))
        {
            EditorLog.Clear();
            _hasSel = false;
        }
        float msgFont = Gui.FontSize - 2f;  // Hierarchy/Project ana metin boyu
        float timeFont = Gui.FontSize - 3f; // ikincil bilgi
        // Zaman sutunu: ornek metni olc, kucuk pay birak (sabit genislik font olcegiyle kayiyordu).
        float timeW = (Gui.Font != null ? Gui.Font.TextSize("00:00:00.000", timeFont).x : 60f) + 10f;

        EditorLog.Snapshot(_view);
        int selIdx = -1;
        if (_hasSel)
        {
            for (int i = 0; i < _view.Count; i++)
                if (Same(_view[i], _sel)) { selIdx = i; break; }
            if (selIdx < 0) _hasSel = false; // kirpildi / temizlendi
        }

        // Yerlesim: secim varken altta detay bolmesi + splitter.
        float detailTotal = _hasSel ? _detailH + SplitterH : 0f;
        float maxDetail = Math.Max(MinDetailH, vis.height - ToolbarH - MinListH - SplitterH);
        if (_hasSel) { _detailH = Math.Clamp(_detailH, MinDetailH, maxDetail); detailTotal = _detailH + SplitterH; }
        var listRect = new Rect(0, ToolbarH, vis.width, Math.Max(1f, vis.height - ToolbarH - detailTotal));

        HandleKeyboard(ev, vis);

        var view = new Rect(0, 0, vis.width - 20, Math.Max(1, _view.Count) * RowH);
        if (_seenVersion != EditorLog.Version)
        {
            _seenVersion = EditorLog.Version;
            if (!_hasSel) // okurken kacmasin: secim varken otomatik dibe kayma yok
                _scroll.y = Math.Max(0, view.height - listRect.height);
        }
        _scroll = Gui.BeginScrollView(listRect, _scroll, view);
        for (int i = 0; i < _view.Count; i++)
        {
            var row = new Rect(0, i * RowH, view.width, RowH - 1);
            if (ev.Type == EventType.MouseDown && ev.Button == 0 && ev.MouseInClip && row.Contains(ev.MousePosition))
            {
                _sel = _view[i];
                _hasSel = true;
                selIdx = i;
                if (ev.ClickCount == 2)
                    CopyToClipboard(_sel);
                ev.Use();
            }
            if (ev.Type == EventType.Repaint)
            {
                var e = _view[i];
                if (i == selIdx)
                    GuiRenderer.DrawRect(row, new Color(60, 100, 180, 255), 1); // secili: hiyerarsiyle ayni mavi
                GuiRenderer.DrawTextIn(new Rect(6, row.y, timeW, RowH), e.TimeText, timeFont, new Color(130, 134, 144, 255), false, 2);
                // Unity tarzi: yalniz ilk satir; devami varsa "…" ipucu.
                ReadOnlySpan<char> msg = e.Message;
                int nl = msg.IndexOf('\n');
                bool multi = nl >= 0;
                if (multi) msg = msg.Slice(0, nl).TrimEnd('\r');
                var msgRect = new Rect(6 + timeW, row.y, view.width - 10 - timeW, RowH);
                GuiRenderer.DrawTextIn(msgRect, msg, msgFont, SevColor(e.Sev), false, 2);
                if (multi && Gui.Font != null)
                {
                    float w = Gui.Font.TextSize(msg, msgFont).x;
                    GuiRenderer.DrawTextIn(new Rect(msgRect.x + w + 4f, row.y, 20, RowH), "…", msgFont, new Color(130, 134, 144, 255), false, 2);
                }
            }
        }
        Gui.EndScrollView();

        if (_hasSel)
        {
            var splitter = new Rect(0, listRect.yMax, vis.width, SplitterH);
            var detail = new Rect(0, splitter.yMax, vis.width, vis.height - splitter.yMax);
            DrawSplitter(splitter, vis);
            DrawDetail(detail, msgFont);
        }
    }

    void DrawSplitter(in Rect splitter, in Rect vis)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashSplit, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (splitter.Contains(ev.MousePosition)) { GuiUtility.HotControl = id; ev.Use(); }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id) { _detailH = vis.height - ev.MousePosition.y - SplitterH * 0.5f; ev.Use(); }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                bool hov = splitter.Contains(ev.MousePosition) || GuiUtility.HotControl == id;
                if (hov) GuiCursorManager.Request(GuiCursor.ResizeV);
                GuiRenderer.DrawRect(splitter, hov ? new Color(110, 150, 235, 255) : new Color(64, 68, 84, 255), 1);
                break;
        }
    }

    void DrawDetail(in Rect area, float font)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(area, new Color(34, 36, 44, 255), 0);
        if (Gui.Button(new Rect(area.xMax - 62, area.y + 3, 56, 20), "Copy"))
            CopyToClipboard(_sel);

        string text = FullText(_sel);
        Vec2 size = Gui.Font != null ? Gui.Font.TextSize(text, font) : new Vec2(100, RowH);
        var body = new Rect(area.x, area.y + 26, area.width, area.height - 26);
        var content = new Rect(0, 0, Math.Max(body.width - 20, size.x + 12), Math.Max(body.height - 20, size.y + 8));
        _detailScroll = Gui.BeginScrollView(body, _detailScroll, content);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawText(new Vec2(6, 4), text, font, SevColor(_sel.Sev), 2);
        Gui.EndScrollView();
    }

    void HandleKeyboard(Event ev, in Rect vis)
    {
        // Kisayol yalniz imlec panel uzerindeyken (IMGUI'de panel odagi yok).
        if (!_hasSel || ev.Type != EventType.KeyDown || GuiUtility.KeyboardControl != 0 || !vis.Contains(ev.MousePosition))
            return;
        if (ev.KeyCode == GLFWConst.KEY_C && (ev.Modifiers & EventModifiers.Control) != 0)
        {
            CopyToClipboard(_sel);
            ev.Use();
        }
    }
}
