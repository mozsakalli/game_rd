#if DE_EDITOR
using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Frame Debugger dock paneli (yalniz DE_EDITOR): capture/freeze, "ilk N draw"
// slider'i, tiklanabilir draw listesi (batch-break sebepleriyle) ve frozen
// frame'in RT onizlemesi. Unity Frame Debugger'in muadili.
[MenuItem("Window/Frame Debugger", 6)]
public sealed class RenderDebugPanel : EditorWindow
{
    Vec2 _scroll;

    public RenderDebugPanel() => Title = "Frame Debugger";

    protected override void OnGui()
    {
        Event ev = Event.Current;
        Rect area = GuiClip.VisibleRect;

        // Toolbar.
        float x = 0;
        if (Gui.Button(new Rect(x, 0, 70, 20), "Capture"))
        {
            RenderDebug.CaptureRequested = true;
            // Frozen'ken recapture: once canliya don, yoksa yakalanan stream
            // ReplayTarget'i doku olarak icerir ve replay kendi hedefini okur (crash).
            RenderDebug.Unfreeze();
        }
        x += 74;
        if (RenderDebug.Frozen && Gui.Button(new Rect(x, 0, 54, 20), "Live"))
            RenderDebug.Unfreeze();
        x += 58;
        if (ev.Type == EventType.Repaint)
        {
            Span<char> tmp = stackalloc char[96];
            int n = 0;
            Append(tmp, ref n, RenderDebug.DrawCount); Append(tmp, ref n, " draw  ");
            Append(tmp, ref n, RenderDebug.TotalInstances); Append(tmp, ref n, " inst  ");
            Append(tmp, ref n, RenderDebug.PassCount); Append(tmp, ref n, " pass  ");
            Append(tmp, ref n, RenderDebug.PipelineBinds); Append(tmp, ref n, " pipe  ");
            Append(tmp, ref n, RenderDebug.StreamBytes / 1024); Append(tmp, ref n, " KB");
            GuiRenderer.DrawTextIn(new Rect(x, 0, area.width - x, 20), tmp.Slice(0, n), Gui.FontSize - 2f,
                new Color(200, 204, 214, 255));
        }

        if (!RenderDebug.Frozen)
        {
            Gui.Label(new Rect(0, 26, area.width, 20), "Press Capture to freeze a frame.");
            return;
        }

        // "Ilk N draw" slider'i.
        RenderDebug.MaxDraws = (int)MathF.Round(Gui.HorizontalSlider(
            new Rect(0, 24, MathF.Min(300, area.width - 60), 16),
            RenderDebug.MaxDraws, 0, RenderDebug.DrawCount));
        if (ev.Type == EventType.Repaint)
        {
            Span<char> tmp = stackalloc char[24];
            int n = 0;
            Append(tmp, ref n, RenderDebug.MaxDraws); Append(tmp, ref n, "/");
            Append(tmp, ref n, RenderDebug.DrawCount);
            GuiRenderer.DrawTextIn(new Rect(MathF.Min(300, area.width - 60) + 6, 22, 60, 20),
                tmp.Slice(0, n), Gui.FontSize - 2f, new Color(200, 204, 214, 255));
        }

        // Sol: draw listesi; sag: RT onizleme.
        float listW = MathF.Min(340, area.width * 0.5f);
        float top = 46;
        var listRect = new Rect(0, top, listW, area.height - top);
        var previewRect = new Rect(listW + 8, top, area.width - listW - 8, area.height - top);

        DrawList(listRect);
        DrawPreview(previewRect, ev);
    }

    void DrawList(in Rect rect)
    {
        const float rowH = 20f;
        var view = new Rect(0, 0, rect.width - 20, RenderDebug.DrawCount * rowH);
        _scroll = Gui.BeginScrollView(rect, _scroll, view);
        Event ev = Event.Current;
        Span<char> tmp = stackalloc char[80]; // dongu disinda: stack her satirda buyumesin

        for (int i = 0; i < RenderDebug.DrawCount; i++)
        {
            var row = new Rect(0, i * rowH, view.width, rowH - 1);
            // Satir tiklamasi: replay'i bu draw'a kadar oynat.
            if (ev.Type == EventType.MouseDown && row.Contains(ev.MousePosition))
            {
                RenderDebug.MaxDraws = i + 1;
                ev.Use();
            }
            if (ev.Type != EventType.Repaint)
                continue;
            ref readonly RenderDebug.DrawEntry d = ref RenderDebug.Draws[i];
            bool active = i < RenderDebug.MaxDraws;
            bool last = i == RenderDebug.MaxDraws - 1;
            GuiRenderer.DrawRect(row,
                last ? new Color(60, 100, 180, 255)
                     : active ? new Color(45, 48, 58, 255) : new Color(34, 36, 44, 255), 1);

            int n = 0;
            Append(tmp, ref n, "#"); Append(tmp, ref n, i);
            Append(tmp, ref n, "  p"); Append(tmp, ref n, d.Pass);
            Append(tmp, ref n, "  "); Append(tmp, ref n, d.NumElements);
            Append(tmp, ref n, "e x"); Append(tmp, ref n, d.NumInstances);
            Append(tmp, ref n, "  "); Append(tmp, ref n, RenderDebug.ReasonNames[(int)d.Reason]);
            GuiRenderer.DrawTextIn(row, tmp.Slice(0, n), Gui.FontSize - 3f,
                active ? new Color(220, 224, 234, 255) : new Color(130, 133, 143, 255), false, 2);
        }
        Gui.EndScrollView();
    }

    void DrawPreview(in Rect rect, Event ev)
    {
        if (ev.Type != EventType.Repaint || RenderDebug.ReplayTarget == null)
            return;
        // Aspect-fit.
        float aw = RenderDebug.ReplayTarget.Width, ah = RenderDebug.ReplayTarget.Height;
        float scale = MathF.Min(rect.width / aw, rect.height / ah);
        var img = new Rect(rect.x, rect.y, aw * scale, ah * scale);
        GuiRenderer.DrawRect(new Rect(img.x - 1, img.y - 1, img.width + 2, img.height + 2),
            new Color(80, 84, 98, 255), 0);
        GuiRenderer.DrawTexture(img, RenderDebug.ReplayTarget, 1);
    }

    // Zero-alloc satir kurucu: int veya literal ekler.
    static void Append(Span<char> dst, ref int n, int value)
    {
        value.TryFormat(dst.Slice(n), out int written);
        n += written;
    }

    static void Append(Span<char> dst, ref int n, ReadOnlySpan<char> s)
    {
        s.CopyTo(dst.Slice(n));
        n += s.Length;
    }
}
#endif
