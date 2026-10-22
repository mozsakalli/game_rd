using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Game View: AKAN oyunun goruntusu (GameOutput RT'si, aspect korumali letterbox).
// Ust bar (Unity paritesi): boyut combo (Free Aspect | cihaz preset'leri), yon,
// Fit/1:1, bilgi etiketi — tercihler EditorSettings'e kalici, RPC view.* ile ayni bogaz.
// Duzenleme yok — ama Play'de fare olaylari oyun sahnesinin pointer boru hattina
// (Scene.Pointer) letterbox'tan oyun koordinatina cevrilerek iletilir.
[MenuItem("Window/Game", 2)]
public sealed class GameViewPanel : EditorWindow
{
    static readonly int _inputHash = "GameView.Input".GetHashCode();
    const float BarH = 24f;

    // Combo ogeleri: 0 = Free Aspect, 1.. = GameOutput.Devices (sabit tablo, bir kez kurulur).
    static string[] _sizeItems;
    static string[] SizeItems
    {
        get
        {
            if (_sizeItems == null)
            {
                _sizeItems = new string[GameOutput.Devices.Length + 1];
                _sizeItems[0] = "Free Aspect";
                for (int i = 0; i < GameOutput.Devices.Length; i++)
                    _sizeItems[i + 1] = GameOutput.Devices[i].Name;
            }
            return _sizeItems;
        }
    }

    public GameViewPanel() => Title = "Game";

    protected override void OnGui()
    {
        var full = GuiClip.VisibleRect;
        if (full.width < 8 || full.height < 8 + BarH)
            return;
        DrawBar(full);
        var vis = new Rect(full.x, full.y + BarH, full.width, full.height - BarH);

        float gw = MathF.Max(1f, GameOutput.ViewW), gh = MathF.Max(1f, GameOutput.ViewH);
        float k = GameOutput.OneToOne ? 1f : MathF.Min(vis.width / gw, vis.height / gh);
        var dst = new Rect(vis.x + (vis.width - gw * k) * 0.5f, vis.y + (vis.height - gh * k) * 0.5f, gw * k, gh * k);

        // Oyun input'u yalniz Play'de (edit sahnesinde handler kostumak yaniltici).
        var scene = PlayMode.PlayScene;
        int id = GuiUtility.GetControlID(_inputHash, FocusType.Passive);
        var ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (ev.Button == 0 && scene != null && dst.Contains(ev.MousePosition) && vis.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id; // capture: up panel disinda da gelir
                    var p = ToGame(ev.MousePosition, dst, k);
                    try { scene.Pointer.Down(p.x, p.y); }
                    catch (Exception e) { PlayMode.ReportGameException(e, "OnPointerDown"); }
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    if (scene != null)
                    {
                        var p = ToGame(ev.MousePosition, dst, k);
                        try { scene.Pointer.Move(p.x, p.y); }
                        catch (Exception e) { PlayMode.ReportGameException(e, "OnDrag"); }
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
                        try { scene.Pointer.Up(p.x, p.y); }
                        catch (Exception e) { PlayMode.ReportGameException(e, "OnPointerUp/Click"); }
                    }
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                GameOutput.RequestSize(vis.width, vis.height); // ilk isteyen surucu
                GuiClip.Push(vis); // 1:1 panelden tasarsa kirpilir (Push orijini vis'e tasir)
                GuiRenderer.DrawTexture(new Rect(dst.x - vis.x, dst.y - vis.y, dst.width, dst.height), GameOutput.EnsureTarget());
                GuiClip.Pop();
                App.DrawPerfHud(new Rect(vis.x + 6, vis.y + 4, 356, 18)); // Window/Perf HUD acikken; toolbar'la cakismaz
                break;
        }
    }

    void DrawBar(in Rect full)
    {
        var ev = Event.Current;
        var bar = new Rect(full.x, full.y, full.width, BarH);
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(bar, new Color(44, 47, 56, 255));

        // Boyut: Free Aspect | preset; RPC view.set ile ozel boyut verildiyse "(none)".
        int sel = 0;
        if (GameOutput.Forced)
        {
            sel = -1;
            for (int i = 0; i < GameOutput.Devices.Length; i++)
                if (string.Equals(GameOutput.Devices[i].Name, GameOutput.ForcedName, StringComparison.OrdinalIgnoreCase))
                { sel = i + 1; break; }
        }
        int pick = Gui.ComboBox(new Rect(bar.x + 4, bar.y + 2, 150, BarH - 4), sel, SizeItems);
        if (pick != sel && pick >= 0)
        {
            if (pick == 0) GameOutput.SetDevice(0, 0, 1f);
            else GameOutput.ApplyDevice(GameOutput.Devices[pick - 1]);
            App.SaveSettings();
        }

        // Yon tercihi (cihazda anlamli; Free Aspect'te soluk ama kontrol sayisi sabit).
        bool device = GameOutput.Forced;
        string oriLabel = GameOutput.Orientation switch
        {
            ScreenOrientation.Portrait => "Portrait",
            ScreenOrientation.Landscape => "Landscape",
            _ => "Auto",
        };
        var oriRect = new Rect(bar.x + 158, bar.y + 2, 84, BarH - 4);
        if (Gui.Button(oriRect, oriLabel) && device)
        {
            GameOutput.Orientation = GameOutput.Orientation switch
            {
                ScreenOrientation.Auto => ScreenOrientation.Portrait,
                ScreenOrientation.Portrait => ScreenOrientation.Landscape,
                _ => ScreenOrientation.Auto,
            };
            var dv = GameOutput.FindDevice(GameOutput.ForcedName);
            if (dv != null)
                GameOutput.ApplyDevice(dv.Value);
            App.SaveSettings();
        }
        if (!device && ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(oriRect, new Color(44, 47, 56, 150), 3); // soluk

        if (Gui.Button(new Rect(bar.x + 246, bar.y + 2, 44, BarH - 4), GameOutput.OneToOne ? "1:1" : "Fit"))
        {
            GameOutput.OneToOne = !GameOutput.OneToOne;
            App.SaveSettings();
        }

        if (ev.Type == EventType.Repaint)
        {
            string info = $"{(int)GameOutput.ViewW}x{(int)GameOutput.ViewH} @{GameOutput.PixelRatio:0.##}x";
            GuiRenderer.DrawTextIn(new Rect(bar.x + 298, bar.y, bar.width - 304, BarH), info,
                Gui.FontSize - 2, new Color(170, 175, 190, 255));
        }
    }

    // Panel-lokal nokta -> oyun ciktisi mantiksal koordinati (letterbox tersine).
    static Vec2 ToGame(Vec2 p, in Rect dst, float k)
        => new((p.x - dst.x) / k, (p.y - dst.y) / k);
}
