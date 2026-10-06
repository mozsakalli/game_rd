using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity Project Settings > Player karsiligi: ProjectSettings/PlayerSettings.asset'i
// sema uzerinden duzenler. Ozel UI yok — alanlar PlayerSettings sinifindan turer;
// yeni ayar = sinifa alan eklemek. Her degisiklik dosyaya yazilir (undo'suz, asset
// inspector'la ayni politika).
[MenuItem("Project/Player Settings", 10)]
public sealed class PlayerSettingsWindow : EditorWindow
{
    const float HeaderH = InspectorPanel.HeaderH;

    readonly ObjectDrawer _drawer = new() { KeyPrefix = "ps:" };
    Vec2 _scroll;

    public PlayerSettingsWindow() => Title = "Player Settings";

    protected override void OnGui()
    {
        var project = App.Project;
        if (project == null)
        {
            Gui.Label(new Rect(4, 4, 200, 20), "No project");
            return;
        }
        if (!ReferenceEquals(_drawer.Target, project.Player))
            _drawer.Bind(project.Player);

        var vis = GuiClip.VisibleRect;
        float contentHeight = 4 + HeaderH + 6 + _drawer.Measure() + 8;
        _scroll = Gui.BeginScrollView(new Rect(0, 0, vis.width, vis.height), _scroll,
            new Rect(0, 0, vis.width - 20, contentHeight));
        float w = vis.width - 24;
        _drawer.LabelW = InspectorPanel.LabelWidthFor(w);
        float y = 4;
        InspectorPanel.DrawHeaderBar(y, 0, w + 12);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, y, w - 8, HeaderH), "Player Settings",
                InspectorPanel.HeaderFont, InspectorPanel.HeaderTextColor, false, 2);
        y += HeaderH + 6;
        if (_drawer.Draw(ref y, w, Save))
            Save();
        Gui.EndScrollView();
    }

    static void Save() => App.Project.SavePlayerSettings();
}
