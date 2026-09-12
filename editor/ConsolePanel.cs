using System;
using System.Collections.Generic;
using DigitoyEngine;

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
        public Entry(Severity s, string m) { Sev = s; Message = m; }
    }

    const int Max = 500;
    static readonly List<Entry> _entries = new();
    static readonly object _lock = new();
    public static int Version; // panel scroll-to-bottom tetigi

    public static int ErrorCount { get; private set; }

    public static void Info(string msg) => Add(Severity.Info, msg);
    public static void Warning(string msg) => Add(Severity.Warning, msg);
    public static void Error(string msg) => Add(Severity.Error, msg);

    static void Add(Severity sev, string msg)
    {
        lock (_lock)
        {
            _entries.Add(new Entry(sev, msg));
            if (_entries.Count > Max)
                _entries.RemoveRange(0, _entries.Count - Max);
            if (sev == Severity.Error)
                ErrorCount++;
            Version++;
        }
        Console.WriteLine($"[{sev.ToString().ToLowerInvariant()}] {msg}");
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
    Vec2 _scroll;
    int _seenVersion = -1;
    readonly List<EditorLog.Entry> _view = new();

    public ConsolePanel() => Title = "Console";

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        if (Gui.Button(new Rect(2, 2, 56, 20), "Clear"))
            EditorLog.Clear();
        const float toolbarH = 26f;
        const float rowH = 18f;

        EditorLog.Snapshot(_view);
        var view = new Rect(0, 0, vis.width - 20, Math.Max(1, _view.Count) * rowH);
        if (_seenVersion != EditorLog.Version)
        {
            _seenVersion = EditorLog.Version;
            _scroll.y = Math.Max(0, view.height - (vis.height - toolbarH)); // yeni giris: dibe kay
        }
        _scroll = Gui.BeginScrollView(new Rect(0, toolbarH, vis.width, vis.height - toolbarH), _scroll, view);
        if (Event.Current.Type == EventType.Repaint)
        {
            for (int i = 0; i < _view.Count; i++)
            {
                var e = _view[i];
                var col = e.Sev switch
                {
                    EditorLog.Severity.Error => new Color(235, 120, 120, 255),
                    EditorLog.Severity.Warning => new Color(230, 200, 120, 255),
                    _ => new Color(200, 204, 214, 255),
                };
                GuiRenderer.DrawTextIn(new Rect(6, i * rowH, view.width - 10, rowH), e.Message, Gui.FontSize - 3f, col, false, 2);
            }
        }
        Gui.EndScrollView();
    }
}
