using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Flash tarzi timeline editoru — Unity MovieClipTimelineWindow'un DigitoyEngine
// GUI portu. Dock paneli olarak calisir (Draw = GuiDock.PanelFunc).
//
// Orijinaldeki desenler birebir korunur:
//   - Yapisal mutasyonlar (track/key ekle-sil, sirala, tasima) PENDING'e yazilir
//     ve SONRAKI frame'in Layout pass'inde uygulanir (kontrol sirasi invariant'i).
//   - Grup surukleme: secili tum ogeler yakalanan ogenin delta'siyla tasinir,
//     snap yakalanana uygulanir, grup 0'in altina inmez.
//   - Rubber-band secim Actions seridi + track lane'lerini birlikte kapsar.
// Kapsam disi (motor karsiligi yok): SerializedObject/undo, sahne preview'u,
// sag-tik menusu (inspector butonlari karsilar), modifier alanlari.
[MenuItem("Window/Timeline", 5)]
public sealed class TimelinePanel : EditorWindow
{
    public sealed class Key
    {
        public float Time;
        public int Ease;
    }

    public sealed class Track
    {
        public readonly char[] Name = new char[24];
        public int NameLen;
        public bool Active = true;
        public readonly List<Key> Keys = new();
    }

    readonly List<Track> _tracks = new();
    readonly List<Key> _actions = new(); // aksiyon karesi = sadece zaman (Ease alani kullanilmaz)

    const float ToolbarH = 26f, RulerH = 18f, RowH = 26f, ActionRowH = 24f;
    const float Sb = 14f, KeyHalf = 5f, LanePad = 10f, SplitterW = 5f;
    const float SnapStep = 0.05f, MinDuration = 2f, TailPad = 0.5f;
    const float MinInspectorW = 170f, MinHeaderW = 120f;
    const int ActionsRow = -2;

    static readonly Color ColRulerBg = new(38, 38, 38, 255);
    static readonly Color ColLaneBg = new(46, 46, 46, 255);
    static readonly Color ColLaneAlt = new(52, 52, 52, 255);
    static readonly Color ColHeaderBg = new(56, 56, 56, 255);
    static readonly Color ColGrid = new(255, 255, 255, 15);
    static readonly Color ColGridSec = new(255, 255, 255, 30);
    static readonly Color ColKey = new(242, 191, 51, 255);
    static readonly Color ColKeySel = new(255, 255, 255, 255);
    static readonly Color ColAction = new(102, 204, 255, 255);
    static readonly Color ColActionLaneBg = new(41, 48, 56, 255);
    static readonly Color ColPlayhead = new(230, 64, 64, 255);
    static readonly Color ColLine = new(0, 0, 0, 100);
    static readonly Color ColSelRow = new(82, 115, 158, 46);
    static readonly Color ColText = new(215, 218, 228, 255);

    float _pps = 100f;
    float _duration = 5f;
    float _hScroll, _vScroll;
    float _playTime;
    bool _snap = true, _playing, _loop;
    double _lastTick;

    int _selTrack = -1, _selKey = -1;
    readonly HashSet<(int track, int key)> _multi = new();

    bool _dragKey;
    int _dragTrack, _dragKeyIdx;
    float _dragTimeOffset, _dragAnchorOrigTime, _multiMinOrigTime;
    readonly Dictionary<(int track, int key), float> _dragOrigTimes = new();

    bool _rubber;
    Vec2 _rubberStart, _rubberEnd;

    float _inspectorW = 220f;
    float _headerW = 170f;

    // Pending mutasyonlar (Layout basinda uygulanir).
    bool _pendAddTrack, _pendSort, _pendDeleteSel, _pendDuplicate;
    int _pendDeleteTrack = -1;
    int _pendMoveFrom = -1, _pendMoveTo = -1;
    int _pendAddKeyTrack = -1;
    float _pendAddKeyTime, _pendAddActionTime = -1f;

    static readonly Comparison<Key> ByTime = (a, b) => a.Time.CompareTo(b.Time);
    static readonly string[] EaseNames = { "Linear", "EaseIn", "EaseOut", "EaseInOut" };

    static readonly int HashRuler = "TL.Ruler".GetHashCode();
    static readonly int HashLane = "TL.Lane".GetHashCode();
    static readonly int HashRubber = "TL.Rubber".GetHashCode();
    static readonly int HashSplit = "TL.Split".GetHashCode();

    public TimelinePanel()
    {
        Title = "Timeline";
        // Demo verisi.
        AddTrackInternal("Pozisyon").Keys.AddRange(new[]
        {
            new Key { Time = 0f }, new Key { Time = 0.6f, Ease = 2 }, new Key { Time = 1.5f, Ease = 3 },
        });
        AddTrackInternal("Alpha").Keys.AddRange(new[]
        {
            new Key { Time = 0.25f }, new Key { Time = 1.0f, Ease = 1 },
        });
        AddTrackInternal("Olcek");
        _actions.Add(new Key { Time = 0.5f });
    }

    Track AddTrackInternal(string name)
    {
        var t = new Track();
        name.CopyTo(0, t.Name, 0, Math.Min(name.Length, t.Name.Length));
        t.NameLen = Math.Min(name.Length, t.Name.Length);
        _tracks.Add(t);
        return t;
    }

    List<Key> ItemsOf(int track) => track == ActionsRow ? _actions : _tracks[track].Keys;

    float SnapTime(float t) => _snap ? MathF.Round(t / SnapStep) * SnapStep : t;

    float TimeToX(float t) => t * _pps - _hScroll + LanePad;
    float XToTime(float x) => (x - LanePad + _hScroll) / _pps;

    float ComputeDuration()
    {
        float max = 0f;
        foreach (Track tr in _tracks)
            foreach (Key k in tr.Keys)
                if (k.Time > max) max = k.Time;
        foreach (Key a in _actions)
            if (a.Time > max) max = a.Time;
        return MathF.Max(MinDuration, max + TailPad);
    }

    // ---- panel girisi ------------------------------------------------------

    protected override void OnGui()
    {
        Event ev = Event.Current;
        if (ev.Type == EventType.Layout)
            ApplyPending();
        if (_playing && ev.Type == EventType.Layout)
            TickPlay();

        Rect area = GuiClip.VisibleRect;
        _duration = ComputeDuration();
        ClampSelection();

        DrawToolbar(new Rect(0, 0, area.width, ToolbarH));

        float inspectorW = Math.Clamp(_inspectorW, MinInspectorW, MathF.Max(MinInspectorW, area.width - 280));
        _inspectorW = inspectorW;
        float splitX = area.width - inspectorW - SplitterW;
        float headerW = Math.Clamp(_headerW, MinHeaderW, MathF.Max(MinHeaderW, splitX - 160));
        _headerW = headerW;
        float laneX = headerW;
        float top = ToolbarH;

        var ruler = new Rect(laneX, top, splitX - laneX - Sb, RulerH);
        float actionTop = top + RulerH;
        var actionHeader = new Rect(0, actionTop, headerW, ActionRowH);
        var actionLane = new Rect(laneX, actionTop, splitX - laneX - Sb, ActionRowH);
        float areaTop = actionTop + ActionRowH;
        float areaBottom = MathF.Max(areaTop + RowH, area.height - Sb);
        var header = new Rect(0, areaTop, headerW, areaBottom - areaTop);
        var lanes = new Rect(laneX, areaTop, splitX - laneX - Sb, areaBottom - areaTop);
        var vbar = new Rect(splitX - Sb, areaTop, Sb, areaBottom - areaTop);
        var hbar = new Rect(laneX, areaBottom, splitX - laneX - Sb, Sb);
        var splitter = new Rect(splitX, top, SplitterW, area.height - top);
        var inspector = new Rect(splitX + SplitterW, top, inspectorW, area.height - top);

        float contentW = _duration * _pps + LanePad * 2;
        float contentH = _tracks.Count * RowH;
        _hScroll = Math.Clamp(_hScroll, 0, MathF.Max(0, contentW - lanes.width));
        _vScroll = Math.Clamp(_vScroll, 0, MathF.Max(0, contentH - lanes.height));

        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(0, top, headerW, RulerH), ColHeaderBg);

        DrawRuler(ruler);
        DrawActionsLane(actionLane);
        DrawActionsHeader(actionHeader);
        DrawLanes(lanes);
        DrawHeaders(header);

        _hScroll = Gui.HorizontalScrollbar(hbar, _hScroll, lanes.width, 0, MathF.Max(contentW, lanes.width));
        _vScroll = Gui.VerticalScrollbar(vbar, _vScroll, lanes.height, 0, MathF.Max(contentH, lanes.height));

        HandleRubberBand(actionLane, lanes);
        DrawSplitter(splitter, area.width);
        DrawInspector(inspector);
        HandleKeyboard(ev);
    }

    // ---- pending -----------------------------------------------------------

    void ApplyPending()
    {
        if (_pendAddTrack)
        {
            _pendAddTrack = false;
            AddTrackInternal("Track");
            _selTrack = _tracks.Count - 1;
            _selKey = -1;
        }
        if (_pendDeleteTrack >= 0 && _pendDeleteTrack < _tracks.Count)
        {
            _tracks.RemoveAt(_pendDeleteTrack);
            if (_selTrack == _pendDeleteTrack) { _selTrack = -1; _selKey = -1; }
            _multi.Clear();
        }
        _pendDeleteTrack = -1;
        if (_pendMoveFrom >= 0 && _pendMoveTo >= 0 && _pendMoveFrom != _pendMoveTo
            && _pendMoveFrom < _tracks.Count && _pendMoveTo < _tracks.Count)
        {
            Track t = _tracks[_pendMoveFrom];
            _tracks.RemoveAt(_pendMoveFrom);
            _tracks.Insert(_pendMoveTo, t);
            if (_selTrack == _pendMoveFrom) _selTrack = _pendMoveTo;
            _multi.Clear();
        }
        _pendMoveFrom = _pendMoveTo = -1;
        if (_pendAddKeyTrack >= 0 && _pendAddKeyTrack < _tracks.Count)
        {
            List<Key> keys = _tracks[_pendAddKeyTrack].Keys;
            // Ease'i onceki keyframe'den miras al (orijinal davranis).
            int ease = 0;
            foreach (Key k in keys)
            {
                if (k.Time > _pendAddKeyTime) break;
                ease = k.Ease;
            }
            keys.Add(new Key { Time = _pendAddKeyTime, Ease = ease });
            keys.Sort(ByTime);
            _selTrack = _pendAddKeyTrack;
            _selKey = keys.FindIndex(k => k.Time == _pendAddKeyTime);
            _multi.Clear();
        }
        _pendAddKeyTrack = -1;
        if (_pendAddActionTime >= 0f)
        {
            _actions.Add(new Key { Time = _pendAddActionTime });
            _actions.Sort(ByTime);
            _selTrack = ActionsRow;
            _selKey = _actions.FindIndex(k => k.Time == _pendAddActionTime);
            _pendAddActionTime = -1f;
        }
        if (_pendDuplicate)
        {
            _pendDuplicate = false;
            DuplicateSelected();
        }
        if (_pendDeleteSel)
        {
            _pendDeleteSel = false;
            DeleteSelected();
        }
        if (_pendSort)
        {
            _pendSort = false;
            SortAndRebuildSelection();
        }
    }

    // ---- toolbar -----------------------------------------------------------

    void DrawToolbar(in Rect bar)
    {
        Event ev = Event.Current;
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(bar, ColHeaderBg);
        float x = 4;
        if (Gui.Button(new Rect(x, 3, 76, 20), "Add Track"))
            _pendAddTrack = true;
        x += 82;
        Gui.Label(new Rect(x, 3, 40, 20), "Zoom");
        x += 40;
        _pps = Gui.HorizontalSlider(new Rect(x, 5, 90, 16), _pps, 20f, 400f);
        x += 98;
        bool snap = Gui.Toggle(new Rect(x, 4, 18, 18), _snap);
        if (snap != _snap) _snap = snap;
        Gui.Label(new Rect(x + 20, 3, 40, 20), "Snap");
        x += 64;
        if (Gui.Button(new Rect(x, 3, 46, 20), _playing ? "Stop" : "Play"))
        {
            _playing = !_playing;
            if (_playing)
            {
                _lastTick = GLFW.GetTime();
                if (_playTime >= _duration - TailPad) _playTime = 0f;
            }
        }
        x += 50;
        bool loop = Gui.Toggle(new Rect(x, 4, 18, 18), _loop);
        if (loop != _loop) _loop = loop;
        Gui.Label(new Rect(x + 20, 3, 36, 20), "Loop");
        x += 62;
        if (ev.Type == EventType.Repaint)
        {
            Span<char> tmp = stackalloc char[32];
            _playTime.TryFormat(tmp, out int n, "0.00", System.Globalization.CultureInfo.InvariantCulture);
            tmp[n++] = 's'; tmp[n++] = ' '; tmp[n++] = '/'; tmp[n++] = ' ';
            _duration.TryFormat(tmp.Slice(n), out int n2, "0.00", System.Globalization.CultureInfo.InvariantCulture);
            n += n2;
            tmp[n++] = 's';
            GuiRenderer.DrawTextIn(new Rect(x, 3, 140, 20), tmp.Slice(0, n), Gui.FontSize - 2f, ColText);
        }
    }

    void TickPlay()
    {
        double now = GLFW.GetTime();
        _playTime += (float)(now - _lastTick);
        _lastTick = now;
        float dur = MathF.Max(0.0001f, _duration - TailPad);
        if (_playTime >= dur)
        {
            if (_loop) _playTime = 0f;
            else { _playTime = dur; _playing = false; }
        }
    }

    // ---- ruler -------------------------------------------------------------

    void DrawRuler(in Rect ruler)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashRuler, FocusType.Passive);
        GuiClip.Push(ruler);
        var local = new Rect(0, 0, ruler.width, ruler.height);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    _playTime = MathF.Max(0, XToTime(ev.MousePosition.x));
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    _playTime = MathF.Max(0, XToTime(ev.MousePosition.x));
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(local, ColRulerBg);
                    int secs = (int)MathF.Ceiling(_duration) + 1;
                    Span<char> tmp = stackalloc char[8];
                    for (int s = 0; s <= secs; s++)
                    {
                        float x = TimeToX(s);
                        if (x < -40 || x > ruler.width + 40) continue;
                        GuiRenderer.DrawRect(new Rect(x, 4, 1, RulerH - 4), ColGridSec, 1);
                        s.TryFormat(tmp, out int n);
                        tmp[n++] = 's';
                        GuiRenderer.DrawText(new Vec2(x + 3, 1), tmp.Slice(0, n), Gui.FontSize - 4f, ColText, 2);
                        if (_pps >= 70)
                            GuiRenderer.DrawRect(new Rect(TimeToX(s + 0.5f), 9, 1, RulerH - 9), ColGrid, 1);
                    }
                    GuiRenderer.DrawRect(new Rect(TimeToX(_playTime), 0, 1, RulerH), ColPlayhead, 3);
                    break;
                }
        }
        GuiClip.Pop();
    }

    // ---- actions lane ------------------------------------------------------

    void DrawActionsHeader(in Rect header)
    {
        if (Event.Current.Type != EventType.Repaint)
            return;
        GuiRenderer.DrawRect(header, _selTrack == ActionsRow ? new Color(66, 88, 120, 255) : ColHeaderBg);
        GuiRenderer.DrawRect(new Rect(header.x, header.yMax - 1, header.width, 1), ColLine, 1);
        GuiRenderer.DrawTextIn(new Rect(header.x + 6, header.y, header.width - 12, header.height), "Actions", Gui.FontSize - 2f, ColText);
    }

    void DrawActionsLane(in Rect lane)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashLane, FocusType.Passive);
        GuiClip.Push(lane);
        var local = new Rect(0, 0, lane.width, lane.height);
        float cy = lane.height * 0.5f;

        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    int hit = HitTestItems(_actions, ev.MousePosition, cy);
                    if (hit >= 0)
                        BeginItemInteraction(ActionsRow, hit, id, ev);
                    else if (ev.ClickCount == 2)
                    {
                        _pendAddActionTime = SnapTime(MathF.Max(0, XToTime(ev.MousePosition.x)));
                        ev.Use();
                    }
                    // bos alan: rubber band alsin diye event birakilir
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id && _dragKey)
                {
                    GroupDrag(ev.MousePosition.x);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    _dragKey = false;
                    _pendSort = true;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(local, ColActionLaneBg);
                    DrawGrid(local);
                    for (int i = 0; i < _actions.Count; i++)
                    {
                        bool sel = (_selTrack == ActionsRow && _selKey == i) || _multi.Contains((ActionsRow, i));
                        GuiRenderer.DrawDiamond(new Vec2(TimeToX(_actions[i].Time), cy), KeyHalf, sel ? ColKeySel : ColAction, 3);
                    }
                    GuiRenderer.DrawRect(new Rect(TimeToX(_playTime), 0, 1, lane.height), ColPlayhead, 4);
                    GuiRenderer.DrawRect(new Rect(0, lane.height - 1, lane.width, 1), ColLine, 1);
                    break;
                }
        }
        GuiClip.Pop();
    }

    // ---- lanes -------------------------------------------------------------

    void DrawLanes(in Rect lanes)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashLane, FocusType.Passive);
        GuiClip.Push(lanes);
        var local = new Rect(0, 0, lanes.width, lanes.height);

        switch (ev.GetTypeForControl(id))
        {
            case EventType.ScrollWheel:
                if (local.Contains(ev.MousePosition))
                {
                    if ((ev.Modifiers & EventModifiers.Shift) != 0) _hScroll -= ev.Delta.y * 16;
                    else _vScroll -= ev.Delta.y * 12;
                    ev.Use();
                }
                break;
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    int row = (int)MathF.Floor((ev.MousePosition.y + _vScroll) / RowH);
                    int hit = (row >= 0 && row < _tracks.Count)
                        ? HitTestItems(_tracks[row].Keys, ev.MousePosition, row * RowH - _vScroll + RowH * 0.5f)
                        : -1;
                    if (hit >= 0)
                        BeginItemInteraction(row, hit, id, ev);
                    else if (ev.ClickCount == 2 && row >= 0 && row < _tracks.Count)
                    {
                        _pendAddKeyTrack = row;
                        _pendAddKeyTime = SnapTime(MathF.Max(0, XToTime(ev.MousePosition.x)));
                        ev.Use();
                    }
                    // bos alan: rubber band alsin diye event birakilir
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id && _dragKey)
                {
                    GroupDrag(ev.MousePosition.x);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    _dragKey = false;
                    _pendSort = true;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(local, ColLaneBg);
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        float rowY = i * RowH - _vScroll;
                        if (rowY + RowH < 0 || rowY > lanes.height) continue;
                        if ((i & 1) == 1)
                            GuiRenderer.DrawRect(new Rect(0, rowY, lanes.width, RowH), ColLaneAlt);
                        if (i == _selTrack)
                            GuiRenderer.DrawRect(new Rect(0, rowY, lanes.width, RowH), ColSelRow, 1);
                        GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, lanes.width, 1), ColLine, 1);
                        if (!_tracks[i].Active)
                            GuiRenderer.DrawRect(new Rect(0, rowY, lanes.width, RowH - 1), new Color(0, 0, 0, 70), 2);
                    }
                    DrawGrid(local);
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        float rowY = i * RowH - _vScroll;
                        if (rowY + RowH < 0 || rowY > lanes.height) continue;
                        float cy = rowY + RowH * 0.5f;
                        List<Key> keys = _tracks[i].Keys;
                        for (int k = 0; k < keys.Count; k++)
                        {
                            bool sel = (i == _selTrack && k == _selKey) || _multi.Contains((i, k));
                            GuiRenderer.DrawDiamond(new Vec2(TimeToX(keys[k].Time), cy), KeyHalf, sel ? ColKeySel : ColKey, 3);
                        }
                    }
                    GuiRenderer.DrawRect(new Rect(TimeToX(_playTime), 0, 1, lanes.height), ColPlayhead, 4);
                    break;
                }
        }
        GuiClip.Pop();
    }

    void DrawGrid(in Rect local)
    {
        int secs = (int)MathF.Ceiling(_duration) + 1;
        for (int s = 0; s <= secs; s++)
        {
            float x = TimeToX(s);
            if (x < 0 || x > local.width) continue;
            GuiRenderer.DrawRect(new Rect(x, 0, 1, local.height), ColGridSec, 1);
        }
    }

    // Key/aksiyon tiklamasi: shift/ctrl toggle, degilse grup drag baslat (orijinal mantik).
    void BeginItemInteraction(int row, int hit, int laneControlId, Event ev)
    {
        var keyRef = (row, hit);
        bool additive = (ev.Modifiers & (EventModifiers.Shift | EventModifiers.Control)) != 0;
        if (additive)
        {
            if (!_multi.Remove(keyRef)) _multi.Add(keyRef);
            _selTrack = row;
            _selKey = _multi.Contains(keyRef) ? hit : -1;
            ev.Use();
            return;
        }
        if (!_multi.Contains(keyRef)) { _multi.Clear(); _multi.Add(keyRef); }
        _selTrack = row;
        _selKey = hit;
        GuiUtility.HotControl = laneControlId;
        _dragKey = true;
        _dragTrack = row;
        _dragKeyIdx = hit;
        _dragTimeOffset = ItemsOf(row)[hit].Time - XToTime(ev.MousePosition.x);
        BeginGroupDrag();
        ev.Use();
    }

    int HitTestItems(List<Key> items, Vec2 mouse, float cy)
    {
        for (int i = 0; i < items.Count; i++)
        {
            float cx = TimeToX(items[i].Time);
            var box = new Rect(cx - KeyHalf - 2, cy - KeyHalf - 2, KeyHalf * 2 + 4, KeyHalf * 2 + 4);
            if (box.Contains(mouse)) return i;
        }
        return -1;
    }

    // ---- headers -----------------------------------------------------------

    void DrawHeaders(in Rect header)
    {
        Event ev = Event.Current;
        GuiClip.Push(header);
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(0, 0, header.width, header.height), ColHeaderBg);

        for (int i = 0; i < _tracks.Count; i++)
        {
            float rowY = i * RowH - _vScroll;
            // Kontrol sirasi korunumu: gorunmeyen satirlar da widget cagirir
            // (scroll degisince pass'ler arasi kontrol sayisi kaymasin).
            Track tr = _tracks[i];
            if (ev.Type == EventType.Repaint && rowY + RowH >= 0 && rowY <= header.height)
            {
                if (i == _selTrack)
                    GuiRenderer.DrawRect(new Rect(0, rowY, header.width, RowH), new Color(76, 102, 140, 90), 1);
                GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, header.width, 1), ColLine, 1);
            }
            float lineY = rowY + (RowH - 18) * 0.5f;
            tr.Active = Gui.Toggle(new Rect(2, lineY, 16, 16), tr.Active);
            Gui.TextField(new Rect(20, lineY, header.width - 20 - 58, 18), tr.Name, ref tr.NameLen);
            if (Gui.Button(new Rect(header.width - 56, lineY, 16, 18), "^") && i > 0)
            {
                _pendMoveFrom = i; _pendMoveTo = i - 1;
            }
            if (Gui.Button(new Rect(header.width - 38, lineY, 16, 18), "v") && i < _tracks.Count - 1)
            {
                _pendMoveFrom = i; _pendMoveTo = i + 1;
            }
            if (Gui.Button(new Rect(header.width - 20, lineY, 16, 18), "x"))
                _pendDeleteTrack = i;
        }
        GuiClip.Pop();
    }

    // ---- rubber band -------------------------------------------------------

    void HandleRubberBand(in Rect actionLane, in Rect lanes)
    {
        var content = new Rect(lanes.x, actionLane.y, lanes.width, lanes.yMax - actionLane.y);
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashRubber, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (content.Contains(ev.MousePosition))
                {
                    if ((ev.Modifiers & (EventModifiers.Shift | EventModifiers.Control)) == 0)
                    {
                        _multi.Clear();
                        _selKey = -1;
                    }
                    _rubber = true;
                    _rubberStart = _rubberEnd = ev.MousePosition;
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    _rubberEnd = ev.MousePosition;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    ApplyRubberBand(actionLane, lanes);
                    _rubber = false;
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                if (_rubber)
                {
                    Rect box = Rect.MinMaxRect(
                        MathF.Min(_rubberStart.x, _rubberEnd.x), MathF.Min(_rubberStart.y, _rubberEnd.y),
                        MathF.Max(_rubberStart.x, _rubberEnd.x), MathF.Max(_rubberStart.y, _rubberEnd.y));
                    box = Rect.Intersect(box, content);
                    GuiRenderer.DrawRect(box, new Color(102, 153, 230, 38), 5);
                    GuiRenderer.DrawRect(new Rect(box.x, box.y, box.width, 1), ColKeySel, 5);
                    GuiRenderer.DrawRect(new Rect(box.x, box.yMax - 1, box.width, 1), ColKeySel, 5);
                    GuiRenderer.DrawRect(new Rect(box.x, box.y, 1, box.height), ColKeySel, 5);
                    GuiRenderer.DrawRect(new Rect(box.xMax - 1, box.y, 1, box.height), ColKeySel, 5);
                }
                break;
        }
    }

    void ApplyRubberBand(in Rect actionLane, in Rect lanes)
    {
        float x0 = MathF.Min(_rubberStart.x, _rubberEnd.x), x1 = MathF.Max(_rubberStart.x, _rubberEnd.x);
        float y0 = MathF.Min(_rubberStart.y, _rubberEnd.y), y1 = MathF.Max(_rubberStart.y, _rubberEnd.y);
        float tMin = XToTime(x0 - lanes.x), tMax = XToTime(x1 - lanes.x);

        if (y1 >= actionLane.y && y0 <= actionLane.yMax)
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Time >= tMin && _actions[i].Time <= tMax)
                    _multi.Add((ActionsRow, i));

        for (int i = 0; i < _tracks.Count; i++)
        {
            float rowTop = lanes.y + i * RowH - _vScroll;
            if (rowTop + RowH < y0 || rowTop > y1) continue;
            List<Key> keys = _tracks[i].Keys;
            for (int k = 0; k < keys.Count; k++)
                if (keys[k].Time >= tMin && keys[k].Time <= tMax)
                    _multi.Add((i, k));
        }
        if (_multi.Count == 1)
            foreach (var kr in _multi) { _selTrack = kr.track; _selKey = kr.key; }
    }

    // ---- grup surukleme ----------------------------------------------------

    void BeginGroupDrag()
    {
        _dragOrigTimes.Clear();
        _multiMinOrigTime = float.MaxValue;
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key < 0 || kr.key >= items.Count) continue;
            float t = items[kr.key].Time;
            _dragOrigTimes[kr] = t;
            if (t < _multiMinOrigTime) _multiMinOrigTime = t;
        }
        _dragAnchorOrigTime = ItemsOf(_dragTrack)[_dragKeyIdx].Time;
        if (_multiMinOrigTime == float.MaxValue) _multiMinOrigTime = 0f;
    }

    void GroupDrag(float mouseX)
    {
        float rawAnchor = MathF.Max(0f, XToTime(mouseX) + _dragTimeOffset);
        float delta = SnapTime(rawAnchor) - _dragAnchorOrigTime;
        if (delta < -_multiMinOrigTime) delta = -_multiMinOrigTime; // grup 0'in altina inmez
        foreach (var kv in _dragOrigTimes)
        {
            List<Key> items = ItemsOf(kv.Key.track);
            if (kv.Key.key < 0 || kv.Key.key >= items.Count) continue;
            items[kv.Key.key].Time = MathF.Max(0f, kv.Value + delta);
        }
    }

    // ---- inspector ---------------------------------------------------------

    void DrawSplitter(in Rect splitter, float areaW)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashSplit, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (splitter.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    _inspectorW = areaW - ev.MousePosition.x - SplitterW * 0.5f;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                bool hov = splitter.Contains(ev.MousePosition) || GuiUtility.HotControl == id;
                if (hov)
                    GuiCursorManager.Request(GuiCursor.ResizeH);
                GuiRenderer.DrawRect(splitter, hov ? new Color(110, 150, 235, 255) : new Color(64, 68, 84, 255), 1);
                break;
        }
    }

    void DrawInspector(in Rect area)
    {
        Event ev = Event.Current;
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(area, ColHeaderBg);

        GuiLayoutUtility.BeginArea(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16));

        if (_multi.Count > 1)
        {
            Gui.LayoutLabel("Coklu secim");
            GuiLayout.Space(4);
            GuiLayout.BeginHorizontal();
            if (Gui.LayoutButton("< 0.05s")) ShiftSelected(-SnapStep);
            if (Gui.LayoutButton("0.05s >")) ShiftSelected(SnapStep);
            GuiLayout.EndHorizontal();
            GuiLayout.BeginHorizontal();
            if (Gui.LayoutButton("< 0.25s")) ShiftSelected(-0.25f);
            if (Gui.LayoutButton("0.25s >")) ShiftSelected(0.25f);
            GuiLayout.EndHorizontal();
            GuiLayout.Space(6);
            if (Gui.LayoutButton("Duplicate"))
                _pendDuplicate = true;
            if (Gui.LayoutButton("Delete Selected"))
                _pendDeleteSel = true;
        }
        else if (_selTrack == ActionsRow && _selKey >= 0 && _selKey < _actions.Count)
        {
            Gui.LayoutLabel("Action Frame");
            Gui.LayoutLabel("Time");
            float t = Gui.DragFloat(GuiLayoutUtility.GetRect(60, 60, 20, 20, true, false, default),
                _actions[_selKey].Time, 0.01f, 0f, 999f);
            if (t != _actions[_selKey].Time) { _actions[_selKey].Time = t; _pendSort = true; }
            GuiLayout.Space(6);
            if (Gui.LayoutButton("Delete Frame"))
            {
                _multi.Clear();
                _multi.Add((ActionsRow, _selKey));
                _pendDeleteSel = true;
            }
        }
        else if (_selTrack >= 0 && _selTrack < _tracks.Count && _selKey >= 0 && _selKey < _tracks[_selTrack].Keys.Count)
        {
            Key kf = _tracks[_selTrack].Keys[_selKey];
            Gui.LayoutLabel("Keyframe");
            Gui.LayoutLabel("Time");
            float t = Gui.DragFloat(GuiLayoutUtility.GetRect(60, 60, 20, 20, true, false, default),
                kf.Time, 0.01f, 0f, 999f);
            if (t != kf.Time) { kf.Time = t; _pendSort = true; }
            Gui.LayoutLabel("Ease");
            if (Gui.LayoutButton(EaseNames[kf.Ease & 3]))
                kf.Ease = (kf.Ease + 1) & 3;
            GuiLayout.Space(6);
            if (Gui.LayoutButton("Duplicate"))
            {
                _multi.Clear();
                _multi.Add((_selTrack, _selKey));
                _pendDuplicate = true;
            }
            if (Gui.LayoutButton("Delete Keyframe"))
            {
                _multi.Clear();
                _multi.Add((_selTrack, _selKey));
                _pendDeleteSel = true;
            }
        }
        else
        {
            Gui.LayoutLabel("Keyframe secin.");
            Gui.LayoutLabel("Cift tik: keyframe ekle");
            Gui.LayoutLabel("Bos alani surukle: kutu secim");
            Gui.LayoutLabel("Ctrl+D: kopyala, Del: sil");
        }

        GuiLayoutUtility.EndArea();
    }

    // ---- klavye + secim ops ------------------------------------------------

    void HandleKeyboard(Event ev)
    {
        if (ev.Type != EventType.KeyDown || GuiUtility.KeyboardControl != 0)
            return; // textfield focus'luyken timeline kisayollari calismaz
        if (ev.KeyCode == GLFWConst.KEY_DELETE || ev.KeyCode == GLFWConst.KEY_BACKSPACE)
        {
            if (_multi.Count > 0 || (_selKey >= 0 && _selTrack >= -2))
            {
                if (_multi.Count == 0)
                    _multi.Add((_selTrack, _selKey));
                _pendDeleteSel = true;
                ev.Use();
            }
        }
        else if (ev.KeyCode == GLFWConst.KEY_D && (ev.Modifiers & EventModifiers.Control) != 0)
        {
            if (_multi.Count == 0 && _selKey >= 0)
                _multi.Add((_selTrack, _selKey));
            if (_multi.Count > 0)
            {
                _pendDuplicate = true;
                ev.Use();
            }
        }
    }

    void ShiftSelected(float delta)
    {
        if (_multi.Count == 0) return;
        float minT = float.MaxValue;
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key >= 0 && kr.key < items.Count && items[kr.key].Time < minT)
                minT = items[kr.key].Time;
        }
        if (minT == float.MaxValue) return;
        if (delta < -minT) delta = -minT;
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key >= 0 && kr.key < items.Count)
                items[kr.key].Time = MathF.Max(0f, items[kr.key].Time + delta);
        }
        _pendSort = true;
    }

    // Kopyalar etkilenen track'lerin sonundan sonra, goreli aralik korunarak (orijinal semantik).
    void DuplicateSelected()
    {
        if (_multi.Count == 0) return;
        float minT = float.MaxValue, trackEnd = 0f;
        var affected = new HashSet<int>();
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key < 0 || kr.key >= items.Count) continue;
            if (items[kr.key].Time < minT) minT = items[kr.key].Time;
            affected.Add(kr.track);
        }
        if (minT == float.MaxValue) return;
        foreach (int tr in affected)
            foreach (Key k in ItemsOf(tr))
                if (k.Time > trackEnd) trackEnd = k.Time;
        float delta = SnapTime(trackEnd + MathF.Max(SnapStep, 0.1f)) - minT;

        var newSel = new List<(int track, Key key)>();
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key < 0 || kr.key >= items.Count) continue;
            Key src = items[kr.key];
            var copy = new Key { Time = MathF.Max(0f, src.Time + delta), Ease = src.Ease };
            items.Add(copy);
            newSel.Add((kr.track, copy));
        }
        foreach (int tr in affected)
            ItemsOf(tr).Sort(ByTime);

        _multi.Clear();
        foreach (var (track, key) in newSel)
        {
            int idx = ItemsOf(track).IndexOf(key);
            if (idx >= 0) _multi.Add((track, idx));
        }
        foreach (var kr in _multi) { _selTrack = kr.track; _selKey = kr.key; break; }
    }

    void DeleteSelected()
    {
        // Track basina yuksek indeksten sil (indeksler kaymasin).
        var byTrack = new Dictionary<int, List<int>>();
        foreach (var kr in _multi)
        {
            if (!byTrack.TryGetValue(kr.track, out List<int> list))
                byTrack[kr.track] = list = new List<int>();
            list.Add(kr.key);
        }
        foreach (var pair in byTrack)
        {
            if (pair.Key != ActionsRow && (pair.Key < 0 || pair.Key >= _tracks.Count)) continue;
            List<Key> items = ItemsOf(pair.Key);
            pair.Value.Sort();
            for (int j = pair.Value.Count - 1; j >= 0; j--)
                if (pair.Value[j] >= 0 && pair.Value[j] < items.Count)
                    items.RemoveAt(pair.Value[j]);
        }
        _multi.Clear();
        _selKey = -1;
    }

    // Grup drag sonrasi sirala ve secimi zaman eslemesiyle yeniden kur.
    void SortAndRebuildSelection()
    {
        if (_multi.Count == 0)
        {
            // Tekli secim: zamanla yeniden bul.
            if (_selKey >= 0 && (_selTrack == ActionsRow || (_selTrack >= 0 && _selTrack < _tracks.Count)))
            {
                List<Key> items = ItemsOf(_selTrack);
                if (_selKey < items.Count)
                {
                    Key k = items[_selKey];
                    items.Sort(ByTime);
                    _selKey = items.IndexOf(k);
                }
            }
            return;
        }
        var wanted = new List<(int track, Key key)>();
        foreach (var kr in _multi)
        {
            List<Key> items = ItemsOf(kr.track);
            if (kr.key >= 0 && kr.key < items.Count)
                wanted.Add((kr.track, items[kr.key]));
        }
        Key prim = (_selKey >= 0 && (_selTrack == ActionsRow || (_selTrack >= 0 && _selTrack < _tracks.Count))
            && _selKey < ItemsOf(_selTrack).Count) ? ItemsOf(_selTrack)[_selKey] : null;
        int primTrack = _selTrack;

        var tracksToSort = new HashSet<int>();
        foreach (var (track, _) in wanted) tracksToSort.Add(track);
        foreach (int tr in tracksToSort) ItemsOf(tr).Sort(ByTime);

        _multi.Clear();
        foreach (var (track, key) in wanted)
        {
            int idx = ItemsOf(track).IndexOf(key);
            if (idx >= 0) _multi.Add((track, idx));
        }
        if (prim != null)
        {
            _selTrack = primTrack;
            _selKey = ItemsOf(primTrack).IndexOf(prim);
        }
    }

    void ClampSelection()
    {
        if (_selTrack == ActionsRow)
        {
            if (_selKey >= _actions.Count) _selKey = -1;
            return;
        }
        if (_selTrack >= _tracks.Count) { _selTrack = -1; _selKey = -1; return; }
        if (_selTrack >= 0 && _selKey >= _tracks[_selTrack].Keys.Count)
            _selKey = -1;
    }
}
