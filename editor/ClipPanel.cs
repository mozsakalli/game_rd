using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Kompakt MovieClip editoru: secili GO'nun MovieClip'ini duzenler.
// KANAL AYRIMI: scrub/play = PreviewSession icinde CANLIYA SampleAt (iz birakir,
// Stop/secim-degisiminde olur); key/track duzenlemeleri = CommitComponent
// bogazından DOC'a (undo'lu, kalici). Preview'da duzenle: sonraki sample yeni
// degerle orneklenir — sicak iterasyon.
[MenuItem("Window/Clip", 4)]
public sealed class ClipPanel : EditorWindow
{
    const float TrackX = 96f;   // kanal etiket kolonu genisligi
    const float RowH = 22f;
    const float KeyHalf = 5f;

    float _playhead;
    bool _playing;
    double _lastT;
    float _zoom = 120f; // px/saniye
    int _selTrack = -1, _selKey = -1;
    bool _draggingKey;

    public ClipPanel() => Title = "Clip";

    protected override void OnGui()
    {
        var es = App.EditScene;
        var g = es.FindGo(Selection.DocId);
        var vis = GuiClip.VisibleRect;

        SceneDoc.CompDoc cd = null;
        MovieClip clip = null;
        if (g != null)
        {
            foreach (var c in g.Components)
                if (c.Type == "MovieClip")
                {
                    cd = c;
                    break;
                }
            clip = es.Live(g.Id)?.GetComponent<MovieClip>();
        }
        if (cd == null || clip == null)
        {
            Gui.Label(new Rect(4, 4, vis.width - 8, 20), "Selected GO has no MovieClip (Inspector > Add Component)");
            return;
        }

        int ci = g.Components.IndexOf(cd);
        bool owner = PreviewSession.IsOwner(g.Id, ci);
        var ev = Event.Current;

        // --- Toolbar ---
        float x = 2;
        if (Gui.Button(new Rect(x, 2, 56, 20), owner && _playing ? "Pause" : "Play"))
        {
            if (!owner)
            {
                PreviewSession.Begin(g.Id, ci);
                clip.Stop(); // scrub/oynatici zamani devralir (kendi Update'i karismasin)
                owner = true;
            }
            _playing = !_playing;
            _lastT = GLFW.GetTime();
        }
        x += 60;
        if (Gui.Button(new Rect(x, 2, 50, 20), "Stop"))
        {
            _playing = false;
            PreviewSession.End(); // izler doc'tan reload ile olur
        }
        x += 54;
        if (Gui.Button(new Rect(x, 2, 66, 20), "+ Track"))
        {
            clip.Tracks.Add(new MovieTrack());
            es.CommitComponent(g, cd);
        }
        x += 70;
        if (Gui.Button(new Rect(x, 2, 56, 20), "+ Key") && _selTrack >= 0 && _selTrack < clip.Tracks.Count)
        {
            var tr = clip.Tracks[_selTrack];
            var k = new MovieKey { Time = _playhead, Value = 0f };
            int at = 0;
            while (at < tr.Keys.Count && tr.Keys[at].Time <= _playhead)
                at++;
            tr.Keys.Insert(at, k);
            _selKey = at;
            es.CommitComponent(g, cd);
        }
        x += 60;

        // Secili key: deger + ease (Commit'li — kalici duzenleme).
        var selKeyObj = SelectedKey(clip);
        if (selKeyObj != null)
        {
            float nv = Gui.DragFloat(new Rect(x, 2, 70, 20), selKeyObj.Value, 0.02f);
            if (nv != selKeyObj.Value)
            {
                selKeyObj.Value = nv;
                es.CommitComponent(g, cd);
                if (owner)
                    clip.SampleAt(_playhead); // aninda gor
            }
            x += 74;
            if (Gui.Button(new Rect(x, 2, 76, 20), EaseName(selKeyObj.Ease)))
            {
                selKeyObj.Ease = (Ease)(((int)selKeyObj.Ease + 1) % Easing.EaseCount);
                es.CommitComponent(g, cd);
            }
            x += 80;
            if (Gui.Button(new Rect(x, 2, 50, 20), "Del"))
            {
                clip.Tracks[_selTrack].Keys.RemoveAt(_selKey);
                _selKey = -1;
                es.CommitComponent(g, cd);
            }
        }

        // --- Oynatma: editor saatiyle surulur (sahne saati degil — preview'in saati bizim) ---
        double now = GLFW.GetTime();
        if (_playing && owner)
        {
            _playhead += (float)(now - _lastT);
            float d = clip.Duration;
            if (d > 0f && _playhead > d)
                _playhead %= d;
            clip.SampleAt(_playhead);
        }
        _lastT = now;

        // --- Cetvel + scrub ---
        var ruler = new Rect(0, 26, vis.width, 16);
        if ((ev.Type == EventType.MouseDown || ev.Type == EventType.MouseDrag)
            && ruler.Contains(ev.MousePosition))
        {
            if (!owner)
            {
                PreviewSession.Begin(g.Id, ci);
                clip.Stop();
                owner = true;
            }
            _playhead = MathF.Max(0f, (ev.MousePosition.x - TrackX) / _zoom);
            clip.SampleAt(_playhead);
            ev.Use();
        }
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(ruler, new Color(30, 32, 40, 255), 1);
            for (int s = 0; s * _zoom < vis.width - TrackX; s++)
                GuiRenderer.DrawRect(new Rect(TrackX + s * _zoom, 26, 1, 16), new Color(90, 93, 103, 255), 2);
        }

        // --- Track satirlari ---
        float y = 46;
        for (int ti = 0; ti < clip.Tracks.Count; ti++)
        {
            var tr = clip.Tracks[ti];
            bool act = Gui.Toggle(new Rect(2, y, 16, 16), tr.Active);
            if (act != tr.Active)
            {
                tr.Active = act;
                es.CommitComponent(g, cd);
            }
            if (Gui.Button(new Rect(20, y, TrackX - 24, 18), ChannelName(tr.Channel)))
            {
                tr.Channel = (AnimChannel)(((int)tr.Channel + 1) % 6);
                es.CommitComponent(g, cd);
            }

            var lane = new Rect(TrackX, y, vis.width - TrackX, RowH - 2);
            if (ev.Type == EventType.Repaint)
                GuiRenderer.DrawRect(lane, ti == _selTrack ? new Color(40, 44, 56, 255) : new Color(34, 36, 44, 255), 1);

            // Key secim/surukleme (elmaslar).
            if (ev.Type == EventType.MouseDown && lane.Contains(ev.MousePosition))
            {
                _selTrack = ti;
                _selKey = -1;
                for (int k = 0; k < tr.Keys.Count; k++)
                {
                    float kx = TrackX + tr.Keys[k].Time * _zoom;
                    if (MathF.Abs(ev.MousePosition.x - kx) <= KeyHalf + 3)
                    {
                        _selKey = k;
                        _draggingKey = true;
                        break;
                    }
                }
                ev.Use();
            }
            if (_draggingKey && ev.Type == EventType.MouseDrag && ti == _selTrack && _selKey >= 0)
            {
                var k = tr.Keys[_selKey];
                k.Time = MathF.Max(0f, (ev.MousePosition.x - TrackX) / _zoom);
                _selKey = Resort(tr, k); // zaman sirasi korunur, secim taşinan key'de kalir
                if (owner)
                    clip.SampleAt(_playhead);
                ev.Use();
            }
            if (ev.Type == EventType.Repaint)
                for (int k = 0; k < tr.Keys.Count; k++)
                {
                    bool sel = ti == _selTrack && k == _selKey;
                    GuiRenderer.DrawDiamond(
                        new Vec2(TrackX + tr.Keys[k].Time * _zoom, y + RowH * 0.5f - 1),
                        KeyHalf, sel ? new Color(255, 210, 80, 255) : new Color(220, 170, 40, 255), 3);
                }
            y += RowH;
        }

        // Surukleme bitti: TEK commit (op zaten coalesce'li ama spam da onlenir).
        if (_draggingKey && ev.Type == EventType.MouseUp)
        {
            _draggingKey = false;
            es.CommitComponent(g, cd);
        }

        // Playhead cizgisi (tum panel boyu).
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(TrackX + _playhead * _zoom, 26, 1, y - 22), new Color(230, 80, 80, 255), 4);
    }

    MovieKey SelectedKey(MovieClip clip)
        => _selTrack >= 0 && _selTrack < clip.Tracks.Count
           && _selKey >= 0 && _selKey < clip.Tracks[_selTrack].Keys.Count
            ? clip.Tracks[_selTrack].Keys[_selKey]
            : null;

    static int Resort(MovieTrack tr, MovieKey moved)
    {
        tr.Keys.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        return tr.Keys.IndexOf(moved);
    }

    static string ChannelName(AnimChannel c) => c switch
    {
        AnimChannel.PositionX => "PosX",
        AnimChannel.PositionY => "PosY",
        AnimChannel.Rotation => "Rot",
        AnimChannel.ScaleX => "ScaleX",
        AnimChannel.ScaleY => "ScaleY",
        _ => "Alpha",
    };

    static readonly string[] _easeNames = Enum.GetNames(typeof(Ease)); // bir kez

    static string EaseName(Ease e) => _easeNames[(int)e];
}
