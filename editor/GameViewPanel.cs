using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Game View: AKAN oyunun goruntusu (GameOutput RT'si, aspect korumali letterbox).
// Duzenleme yok — ama Play'de fare olaylari oyun sahnesinin pointer boru hattina
// (Scene.Pointer) letterbox'tan oyun koordinatina cevrilerek iletilir.
[MenuItem("Window/Game", 2)]
public sealed class GameViewPanel : EditorWindow
{
    static readonly int _inputHash = "GameView.Input".GetHashCode();

    public GameViewPanel() => Title = "Game";

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        if (vis.width < 8 || vis.height < 8)
            return;
        float gw = MathF.Max(1f, GameOutput.ViewW), gh = MathF.Max(1f, GameOutput.ViewH);
        float k = MathF.Min(vis.width / gw, vis.height / gh);
        var dst = new Rect((vis.width - gw * k) * 0.5f, (vis.height - gh * k) * 0.5f, gw * k, gh * k);

        // Oyun input'u yalniz Play'de (edit sahnesinde handler kostumak yaniltici).
        var scene = PlayMode.PlayScene;
        int id = GuiUtility.GetControlID(_inputHash, FocusType.Passive);
        var ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (ev.Button == 0 && scene != null && dst.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id; // capture: up panel disinda da gelir
                    var p = ToGame(ev.MousePosition, dst, k);
                    scene.Pointer.Down(p.x, p.y);
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    if (scene != null)
                    {
                        var p = ToGame(ev.MousePosition, dst, k);
                        scene.Pointer.Move(p.x, p.y);
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    if (scene != null)
                    {
                        var p = ToGame(ev.MousePosition, dst, k);
                        scene.Pointer.Up(p.x, p.y);
                    }
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                GameOutput.RequestSize(vis.width, vis.height); // ilk isteyen surucu
                GuiRenderer.DrawTexture(dst, GameOutput.EnsureTarget());
                break;
        }
    }

    // Panel-lokal nokta -> oyun ciktisi mantiksal koordinati (letterbox tersine).
    static Vec2 ToGame(Vec2 p, in Rect dst, float k)
        => new((p.x - dst.x) / k, (p.y - dst.y) / k);
}
