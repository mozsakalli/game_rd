using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity Project Settings > Player karsiligi: ProjectSettings/PlayerSettings.asset'i
// sema uzerinden duzenler. Alanlar PlayerSettings sinifindan turer; yeni ayar = sinifa
// alan eklemek. Tek ozel bolum: "Scenes In Build" (Unity Build Settings karsiligi) —
// projedeki tum .scene dosyalari listelenir, dahil/start secimi PlayerSettings.scenes +
// startScene'e yazilir ([HideInInspector], jenerik cizici atlar). Her degisiklik dosyaya
// yazilir (undo'suz, asset inspector'la ayni politika).
[MenuItem("Project/Player Settings", 10)]
public sealed class PlayerSettingsWindow : EditorWindow
{
    const float HeaderH = InspectorPanel.HeaderH;
    const float RowH = InspectorPanel.RowH;

    static readonly Color MissingColor = new(226, 96, 96, 255);
    static readonly Color StartColor = new(120, 200, 140, 255);

    readonly ObjectDrawer _drawer = new() { KeyPrefix = "ps:" };
    readonly ObjectDrawer _signDrawer = new() { KeyPrefix = "ps.sign:" };
    readonly List<string> _sceneRows = new();
    Vec2 _scroll;

    public PlayerSettingsWindow()
    {
        Title = "Player Settings";
        Utility = true; // Unity gibi: dock'lanmaz, ayri pencere
        UtilityWidth = 560;
        UtilityHeight = 720;
    }

    protected override void OnGui()
    {
        var project = App.Project;
        if (project == null)
        {
            Gui.Label(new Rect(4, 4, 200, 20), "No project");
            return;
        }
        var ps = project.Player;
        if (!ReferenceEquals(_drawer.Target, ps))
            _drawer.Bind(ps);
        if (!ReferenceEquals(_signDrawer.Target, project.AndroidSigning))
            _signDrawer.Bind(project.AndroidSigning);
        CollectSceneRows(ps);

        var vis = GuiClip.VisibleRect;
        float scenesH = RowH * (Math.Max(1, _sceneRows.Count) + 1) + 4;
        float contentHeight = 4 + HeaderH + 6 + _drawer.Measure() + 8 + HeaderH + 6 + scenesH + 8
            + HeaderH + 6 + RowH + _signDrawer.Measure() + 8;
        _scroll = Gui.BeginScrollView(new Rect(0, 0, vis.width, vis.height), _scroll,
            new Rect(0, 0, vis.width - 20, contentHeight));
        float w = vis.width - 24;
        _drawer.LabelW = InspectorPanel.LabelWidthFor(w);
        _signDrawer.LabelW = _drawer.LabelW;
        float y = 4;
        DrawHeader(ref y, w, "Player Settings");
        if (_drawer.Draw(ref y, w, Save))
            Save();
        y += 8;
        DrawHeader(ref y, w, "Scenes In Build");
        if (DrawScenes(ps, ref y, w))
            Save();
        y += 8;
        // Parolalar PlayerSettings.asset'e (git) DEGIL, UserSettings/AndroidSigning.asset'e yazilir.
        DrawHeader(ref y, w, "Android Signing (local)");
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(12, y, w - 12, RowH),
                "UserSettings/AndroidSigning.asset — git'e girmez. CI: DE_ANDROID_KEYSTORE_PASS / DE_ANDROID_KEY_PASS",
                InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
        y += RowH;
        if (_signDrawer.Draw(ref y, w, SaveSigning))
            SaveSigning();
        Gui.EndScrollView();
    }

    static void DrawHeader(ref float y, float w, string title)
    {
        InspectorPanel.DrawHeaderBar(y, 0, w + 12);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, y, w - 8, HeaderH), title,
                InspectorPanel.HeaderFont, InspectorPanel.HeaderTextColor, false, 2);
        y += HeaderH + 6;
    }

    // Satirlar: projedeki tum .scene (sirali) + listede olup projede olmayanlar (missing).
    void CollectSceneRows(PlayerSettings ps)
    {
        _sceneRows.Clear();
        var assets = App.Assets;
        if (assets != null)
            foreach (var rel in assets.AllAssets.Keys)
                if (rel.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                    _sceneRows.Add(rel);
        _sceneRows.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var s in ps.BuildScenes())
            if (!ContainsIgnoreCase(_sceneRows, s))
                _sceneRows.Add(s);
    }

    // Satir: [dahil] [start] yol — start secimi dahil'i zorlar; start'in dahil'i kaldirilamaz.
    // Missing satir: kirmizi + "Remove" (hem listeden hem start'tan duser).
    bool DrawScenes(PlayerSettings ps, ref float y, float w)
    {
        bool changed = false;
        if (_sceneRows.Count == 0)
        {
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(12, y, w - 12, RowH), "Projede .scene yok",
                    InspectorPanel.LabelFont, InspectorPanel.LabelDimColor);
            y += RowH;
            return false;
        }
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawTextIn(new Rect(8, y, 40, 18), "Build", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
            GuiRenderer.DrawTextIn(new Rect(48, y, 40, 18), "Start", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
            GuiRenderer.DrawTextIn(new Rect(86, y, w - 86, 18), "Scene", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
        }
        y += RowH;
        var assets = App.Assets;
        foreach (var rel in _sceneRows)
        {
            bool exists = assets != null && assets.AllAssets.ContainsKey(rel);
            bool isStart = string.Equals(ps.startScene, rel, StringComparison.OrdinalIgnoreCase);
            bool included = isStart || ContainsIgnoreCase(ps.scenes, rel);

            bool nextIncluded = Gui.Toggle(new Rect(16, y + 2, 18, 18), included);
            if (nextIncluded != included && !isStart)
            {
                SetIncluded(ps, rel, nextIncluded);
                changed = true;
            }

            bool nextStart = Gui.Toggle(new Rect(56, y + 2, 18, 18), isStart);
            if (nextStart && !isStart)
            {
                ps.startScene = rel;
                SetIncluded(ps, rel, true);
                changed = true;
            }

            float textX = 90;
            float textW = w - textX - (exists ? 0 : 70);
            if (Event.Current.Type == EventType.Repaint)
            {
                var color = !exists ? MissingColor : isStart ? StartColor : InspectorPanel.LabelColor;
                string text = exists ? rel : rel + "  (missing)";
                GuiRenderer.DrawTextIn(new Rect(textX, y + 2, textW, 18), text, InspectorPanel.LabelFont, color);
            }
            if (!exists && Gui.Button(new Rect(w - 64, y + 2, 64, 18), "Remove"))
            {
                SetIncluded(ps, rel, false);
                if (isStart)
                    ps.startScene = FirstOrEmpty(ps.scenes);
                changed = true;
            }
            y += RowH;
        }
        y += 4;
        return changed;
    }

    static void SetIncluded(PlayerSettings ps, string rel, bool on)
    {
        int idx = ps.scenes.FindIndex(s => string.Equals(s, rel, StringComparison.OrdinalIgnoreCase));
        if (on && idx < 0)
            ps.scenes.Add(rel);
        else if (!on && idx >= 0)
            ps.scenes.RemoveAt(idx);
    }

    static bool ContainsIgnoreCase(List<string> list, string s)
    {
        foreach (var x in list)
            if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string FirstOrEmpty(List<string> list) => list.Count > 0 ? list[0] : "";

    static void Save() => App.Project.SavePlayerSettings();
    static void SaveSigning() => App.Project.SaveAndroidSigning();
}
