using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Game View: AKAN oyunun goruntusu (GameOutput RT'si, aspect korumali letterbox).
// Duzenleme yok — duzenleme Scene View'da, doc uzerinde yapilir.
[MenuItem("Window/Game", 2)]
public sealed class GameViewPanel : EditorWindow
{
    public GameViewPanel() => Title = "Game";

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        if (Event.Current.Type != EventType.Repaint || vis.width < 8 || vis.height < 8)
            return;
        GameOutput.RequestSize(vis.width, vis.height); // ilk isteyen surucu
        float gw = MathF.Max(1f, GameOutput.ViewW), gh = MathF.Max(1f, GameOutput.ViewH);
        float k = MathF.Min(vis.width / gw, vis.height / gh);
        var dst = new Rect((vis.width - gw * k) * 0.5f, (vis.height - gh * k) * 0.5f, gw * k, gh * k);
        GuiRenderer.DrawTexture(dst, GameOutput.EnsureTarget());
    }
}
