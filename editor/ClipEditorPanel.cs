using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Generic MovieClip editoru ("animate everything"): secili GO'nun (ya da ustundeki klip
// kokunun) MovieClip'ini duzenler. Satirlar: hedef GO gruplari (katlanir) → property
// track'leri (AnimRegistry'den: serilesen alanlar, ic ice yollar, [Animatable], pseudo).
// Orta: frame izgarasi (keyframe, tween/hold span, label/aksiyon seritleri, playhead).
// Sag: secime gore inspector (key degeri Kind'a gore cizilir — ozel kod yok).
//
// Veri akisi: CANLI MovieClip dogrudan mutate edilir → Rebuild → CommitComponent (doc +
// tek undo girisi, coalesce) → SampleFrame(playhead). Sahne pozu PreviewSession icinde
// surulur: Stop = doc'tan reload. RECORD: EditorScene.LiveEdited kancasi — Inspector/
// handle ile yapilan HER duzenleme playhead'de ilgili property'ye key yazar (track yoksa acar).
// Yapisal mutasyonlar pending'e yazilir, sonraki Layout'ta uygulanir (kontrol sirasi).
[MenuItem("Window/Clip Editor", 5)]
public sealed class ClipEditorPanel : EditorWindow
{
    const float ToolbarH = 26f, RulerH = 20f, LabelRowH = 18f, ActionRowH = 18f, RowH = 20f;
    const float Sb = 14f, SplitterW = 5f, MinInspectorW = 230f, MinHeaderW = 220f;
    const int SelLabels = -2, SelActions = -3; // _selTrack ozel degerleri

    static readonly Color ColRulerBg = new(38, 38, 38, 255);
    static readonly Color ColLaneBg = new(44, 44, 44, 255);
    static readonly Color ColGroupBg = new(52, 56, 64, 255);
    static readonly Color ColLaneEmpty = new(36, 36, 36, 255);
    static readonly Color ColHeaderBg = new(56, 56, 56, 255);
    static readonly Color ColGrid = new(255, 255, 255, 14);
    static readonly Color ColGrid5 = new(255, 255, 255, 34);
    static readonly Color ColTween = new(120, 150, 200, 110);
    static readonly Color ColHold = new(130, 130, 130, 70);
    static readonly Color ColKey = new(30, 30, 30, 255);
    static readonly Color ColKeyGroup = new(200, 200, 200, 160);
    static readonly Color ColKeySel = new(255, 255, 255, 255);
    static readonly Color ColLabel = new(240, 190, 60, 255);
    static readonly Color ColAction = new(102, 204, 255, 255);
    static readonly Color ColStripBg = new(41, 46, 54, 255);
    static readonly Color ColPlayhead = new(230, 64, 64, 255);
    static readonly Color ColPlayheadCol = new(230, 64, 64, 28);
    static readonly Color ColRecord = new(230, 50, 50, 255);
    static readonly Color ColLine = new(0, 0, 0, 110);
    static readonly Color ColSelRow = new(82, 115, 158, 60);
    static readonly Color ColText = new(215, 218, 228, 255);
    static readonly Color ColTextDim = new(150, 155, 170, 255);

    static readonly string[] EaseNames = Enum.GetNames(typeof(Ease));
    static readonly string[] ModeNames = { "Tween", "Hold" };
    static readonly string[] ActionNames = { "Stop", "Play", "GotoAndPlay", "GotoAndStop", "Event" };

    static readonly int HashRuler = "CE.Ruler".GetHashCode();
    static readonly int HashLane = "CE.Lane".GetHashCode();
    static readonly int HashStrip = "CE.Strip".GetHashCode();
    static readonly int HashSplit = "CE.Split".GetHashCode();

    // Baglam (her OnGui yeniden cozulur; canli nesne reload'da degisebilir).
    EditorScene _es;
    SceneDoc.GoDoc _g;
    SceneDoc.CompDoc _cd;
    MovieClip _clip;
    int _compIndex;
    int _boundId;

    float _frameW = 12f;
    float _hScroll, _vScroll;
    float _playhead;
    bool _playing, _record;
    double _lastTick;
    float _inspectorW = 280f, _headerW = 280f;

    int _selTrack = -1, _selKey = -1, _selLabel = -1, _selAction = -1;
    int _selGroup = -1; // docId

    bool _drag, _dragMoved;
    int _dragTrack, _dragKey, _dragGrabOffset;

    // Satir modeli (OnGui basinda kurulur).
    struct Row { public bool Group; public int GoId; public GameObject Go; public int Track; public string Label; }
    readonly List<Row> _rows = new();
    readonly HashSet<int> _collapsed = new();
    readonly Dictionary<GameObject, int> _goIds = new();

    // Pending (Layout basinda).
    bool _pendAddKey, _pendAddBlank, _pendDelKey, _pendInsFrame, _pendDelFrame, _pendDelSel;
    int _pendDelTrack = -1, _pendMoveGroupFrom = -1, _pendMoveGroupTo = -1;
    int _pendAddLabelFrame = -1, _pendAddActionFrame = -1;
    int _pendAddObjectId, _pendAddPropGo = 0, _pendAddPropComp = -1; string _pendAddPropPath;

    // Inspector metin tamponlari / combo item cache'leri.
    readonly char[] _nameBuf = new char[64];
    int _nameLen, _nameOwner = int.MinValue;
    string[] _propItems = Array.Empty<string>();
    // Component REFERANSI degil indeks saklanir: RefreshLive sonrasi canli nesneler degisir.
    readonly List<(int comp, string path)> _propTargets = new();
    GameObject _propItemsLive;
    int _propItemsGo = -1, _propItemsSig = -1;

    public ClipEditorPanel()
    {
        Title = "Clip";
        EditorScene.LiveEdited += OnLiveEdited;
    }

    // ---- baglam ------------------------------------------------------------

    // Baglama YAPISKAN: panel bir klibe baglaninca sahnede baska nesne secmek koparmaz
    // (secilen nesne "+ Selected" ile kliğe eklenir). Secilen GO'nun kendisinde ya da
    // ustunde MovieClip varsa ona gecilir; toolbar combo'su sahnedeki tum klipleri listeler.
    bool Resolve()
    {
        _es = App.EditScene;
        _g = null; _cd = null; _clip = null;
        if (_es == null)
            return false;
        int found = 0;
        int id = Selection.DocId;
        for (int guard = 0; id != 0 && guard < 64 && found == 0; guard++)
        {
            var g = _es.FindGo(id);
            if (g == null) break;
            if (g.Components.Exists(c => c.Type == "MovieClip")) found = id;
            id = g.Parent;
        }
        if (found == 0) found = _boundId; // yapiskan
        if (!Bind(found))
            return false;
        PreviewSession.Pin(); // secim degisse de preview oturumu yasar
        return true;
    }

    bool Bind(int goId)
    {
        _g = null; _cd = null; _clip = null;
        if (goId == 0) return false;
        var g = _es.FindGo(goId);
        if (g == null) return false;
        int ci = g.Components.FindIndex(c => c.Type == "MovieClip");
        if (ci < 0) return false;
        var clip = _es.Live(goId)?.GetComponent<MovieClip>();
        if (clip == null) return false;
        _g = g; _cd = g.Components[ci]; _compIndex = ci; _clip = clip;
        if (_boundId != goId)
        {
            _boundId = goId;
            _playhead = 0f; _playing = false; _record = false;
            _selTrack = -1; _selKey = -1; _selLabel = -1; _selAction = -1; _selGroup = -1;
            _hScroll = _vScroll = 0f;
        }
        return true;
    }

    bool Previewing => _g != null && PreviewSession.IsOwner(_g.Id, _compIndex);

    void EnsurePreview()
    {
        if (Previewing) return;
        PreviewSession.Begin(_g.Id, _compIndex);
        _clip.Stop();
    }

    // Sahneye yazan TEK kapi: her ornekleme preview oturumu icinde olur (Stop = reload geri alir).
    void Sample()
    {
        EnsurePreview();
        _clip.SampleFrame(_playhead);
    }

    void Commit()
    {
        _clip.Rebuild();
        _es.CommitComponent(_g, _cd);
        Sample();
    }

    void SetPlayhead(float f)
    {
        _playhead = Math.Clamp(f, 0f, Math.Max(0, _clip.FrameCount - 1));
        Sample();
    }

    int Ph => (int)_playhead;
    int Frames => Math.Max(1, _clip.FrameCount);
    TypeCatalog Catalog => App.Catalog;

    float FrameToX(float f) => f * _frameW - _hScroll;
    int XToFrame(float x) => (int)MathF.Floor((x + _hScroll) / _frameW);

    int DocIdOf(GameObject go)
    {
        if (go == null) return 0;
        if (_goIds.TryGetValue(go, out int id)) return id;
        foreach (var o in _es.Doc.Objects)
            if (_es.Live(o.Id) == go) { _goIds[go] = o.Id; return o.Id; }
        return 0;
    }

    bool InClipSubtree(int docId)
    {
        for (int guard = 0; docId != 0 && guard < 64; guard++)
        {
            if (docId == _g.Id) return true;
            var g = _es.FindGo(docId);
            if (g == null) return false;
            docId = g.Parent;
        }
        return false;
    }

    static string TrackLabel(ClipTrack t)
        => (t.Target == null ? "?" : t.Target is Transform ? "Transform" : t.Target.GetType().Name) + "." + t.Path;

    // Satirlar: hedef GO'ya gore grupla (ilk gorulme sirasi), katlanmamissa track satirlari.
    void BuildRows()
    {
        _rows.Clear();
        _goIds.Clear();
        var tracks = _clip.Tracks;
        var seen = new List<GameObject>();
        for (int i = 0; i < tracks.Count; i++)
        {
            var go = tracks[i].Target?.gameObject;
            if (go == null || seen.Contains(go)) continue;
            seen.Add(go);
            int id = DocIdOf(go);
            _rows.Add(new Row { Group = true, Go = go, GoId = id, Track = -1, Label = go.name });
            if (_collapsed.Contains(id)) continue;
            for (int j = 0; j < tracks.Count; j++)
                if (tracks[j].Target?.gameObject == go)
                    _rows.Add(new Row { Group = false, Go = go, GoId = id, Track = j, Label = TrackLabel(tracks[j]) });
        }
    }

    // ---- panel girisi ------------------------------------------------------

    protected override void OnGui()
    {
        Event ev = Event.Current;
        Rect area = GuiClip.VisibleRect;
        if (!Resolve())
        {
            DrawEmptyState(area);
            return;
        }
        if (ev.Type == EventType.Layout)
        {
            ApplyPending();
            if (_clip == null) return; // pending RefreshLive yapti
            if (_playing) TickPlay();
            else if (Previewing) _clip.SampleFrame(_playhead); // reload sonrasi pozu geri koy
        }
        ClampSelection();
        BuildRows();

        DrawToolbar(new Rect(0, 0, area.width, ToolbarH));

        float inspectorW = Math.Clamp(_inspectorW, MinInspectorW, MathF.Max(MinInspectorW, area.width - 340));
        _inspectorW = inspectorW;
        float splitX = area.width - inspectorW - SplitterW;
        float headerW = Math.Clamp(_headerW, MinHeaderW, MathF.Max(MinHeaderW, splitX - 160));
        _headerW = headerW;
        float top = ToolbarH;
        float laneW = splitX - headerW - Sb;

        var ruler = new Rect(headerW, top, laneW, RulerH);
        var labelStrip = new Rect(headerW, top + RulerH, laneW, LabelRowH);
        var actionStrip = new Rect(headerW, top + RulerH + LabelRowH, laneW, ActionRowH);
        float areaTop = top + RulerH + LabelRowH + ActionRowH;
        float areaBottom = MathF.Max(areaTop + RowH, area.height - Sb);
        var header = new Rect(0, areaTop, headerW, areaBottom - areaTop);
        var lanes = new Rect(headerW, areaTop, laneW, areaBottom - areaTop);
        var vbar = new Rect(splitX - Sb, areaTop, Sb, areaBottom - areaTop);
        var hbar = new Rect(headerW, areaBottom, laneW, Sb);
        var splitter = new Rect(splitX, top, SplitterW, area.height - top);
        var inspector = new Rect(splitX + SplitterW, top, inspectorW, area.height - top);

        float contentW = Frames * _frameW + _frameW * 4;
        float contentH = _rows.Count * RowH;
        _hScroll = Math.Clamp(_hScroll, 0, MathF.Max(0, contentW - lanes.width));
        _vScroll = Math.Clamp(_vScroll, 0, MathF.Max(0, contentH - lanes.height));

        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(new Rect(0, top, headerW, areaTop - top), ColHeaderBg);
            GuiRenderer.DrawTextIn(new Rect(6, top + RulerH, headerW - 12, LabelRowH), "Labels", Gui.FontSize - 3f, ColTextDim);
            GuiRenderer.DrawTextIn(new Rect(6, top + RulerH + LabelRowH, headerW - 12, ActionRowH), "Actions", Gui.FontSize - 3f, ColTextDim);
        }

        DrawRuler(ruler);
        DrawLabelStrip(labelStrip);
        DrawActionStrip(actionStrip);
        DrawLanes(lanes);
        DrawHeaders(header);
        HandleDrop(new Rect(0, areaTop, splitX, areaBottom - areaTop));

        _hScroll = Gui.HorizontalScrollbar(hbar, _hScroll, lanes.width, 0, MathF.Max(contentW, lanes.width));
        _vScroll = Gui.VerticalScrollbar(vbar, _vScroll, lanes.height, 0, MathF.Max(contentH, lanes.height));

        DrawSplitter(splitter, area.width);
        DrawInspector(inspector);
        HandleKeyboard(ev);
    }

    void ClampSelection()
    {
        if (_selTrack >= _clip.Tracks.Count) { _selTrack = -1; _selKey = -1; }
        if (_selTrack >= 0 && _selKey >= _clip.Tracks[_selTrack].Keys.Count) _selKey = -1;
        if (_selLabel >= _clip.Labels.Count) _selLabel = -1;
        if (_selAction >= _clip.Actions.Count) _selAction = -1;
    }

    // Klip yok: sahnedeki kliplerden sec ya da secili GO'ya MovieClip ekle.
    void DrawEmptyState(in Rect area)
    {
        var ev = Event.Current;
        RefreshClipItems();
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, 4, area.width - 16, 20),
                _clipItems.Length > 1 ? "Klip sec:" : "Sahnede MovieClip yok.", Gui.FontSize - 2f, ColTextDim);
        if (_clipItems.Length > 1)
        {
            int pick = Gui.ComboBox(new Rect(80, 4, 200, 20), 0, _clipItems);
            if (pick > 0 && pick < _clipIds.Count) { _boundId = _clipIds[pick]; Selection.DocId = _boundId; }
        }
        var sel = _es?.FindGo(Selection.DocId);
        if (sel != null && Gui.Button(new Rect(8, 30, 260, 22), "Secili nesneye MovieClip ekle: " + sel.Name))
        {
            _es.AddComponent(sel, "MovieClip");
            _boundId = sel.Id;
        }
        else if (sel == null && ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, 30, area.width - 16, 20),
                "Hierarchy'de bir nesne sec → buradan MovieClip ekle. Sonra nesneleri Hierarchy'den bu panele surukle.", Gui.FontSize - 3f, ColTextDim);
    }

    string[] _clipItems = Array.Empty<string>();
    readonly List<int> _clipIds = new();

    void RefreshClipItems()
    {
        _clipIds.Clear();
        var list = new List<string> { _g != null ? _g.Name : "Klip..." };
        _clipIds.Add(0);
        if (_es == null) return;
        foreach (var o in _es.Doc.Objects)
            if (o.Components.Exists(c => c.Type == "MovieClip") && o.Id != _boundId) { _clipIds.Add(o.Id); list.Add(o.Name); }
        if (_clipItems.Length != list.Count) _clipItems = new string[list.Count];
        for (int i = 0; i < list.Count; i++) _clipItems[i] = list[i];
    }

    // Hierarchy'den surukle-birak: GO kliğe eklenir (Position track'i ile; sonra "+ Property").
    void HandleDrop(in Rect zone)
    {
        if (DragDrop.Kind != DragDrop.Payload.SceneObject || !zone.Contains(Event.Current.MousePosition))
            return;
        int goId = DragDrop.GoId;
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(zone, new Color(90, 160, 90, 40), 5);
        DragDrop.RegisterTarget(() => _pendAddObjectId = goId);
    }

    // ---- pending -----------------------------------------------------------

    void ApplyPending()
    {
        bool changed = false;
        if (_pendAddObjectId != 0)
        {
            int id = _pendAddObjectId; _pendAddObjectId = 0;
            var tr = _es.Live(id)?.transform;
            if (tr != null) { AddTrack(tr, "Position", keyNow: true); changed = true; }
        }
        if (_pendAddPropGo != 0)
        {
            // Secim aninda GUNCEL canli GO uzerinden coz (reload'a dayanikli).
            var go = _es.Live(_pendAddPropGo);
            var c = go != null && _pendAddPropComp >= 0 && _pendAddPropComp < go.ComponentCount ? go.ComponentAt(_pendAddPropComp) : null;
            string p = _pendAddPropPath;
            _pendAddPropGo = 0; _pendAddPropComp = -1; _pendAddPropPath = null;
            if (c != null && !c._destroyed) { AddTrack(c, p, keyNow: true); changed = true; }
        }
        if (_pendDelTrack >= 0 && _pendDelTrack < _clip.Tracks.Count)
        {
            _clip.Tracks.RemoveAt(_pendDelTrack);
            _selTrack = -1; _selKey = -1;
            changed = true;
        }
        _pendDelTrack = -1;
        if (_pendMoveGroupFrom != -1 && _pendMoveGroupTo != -1)
        {
            int from = _pendMoveGroupFrom, to = _pendMoveGroupTo;
            _pendMoveGroupFrom = _pendMoveGroupTo = -1;
            MoveGroup(from, to);
            return; // MoveGameObject → RefreshLive
        }
        if (_pendAddKey || _pendAddBlank)
        {
            bool blank = _pendAddBlank;
            _pendAddKey = _pendAddBlank = false;
            if (_selTrack >= 0) { InsertKey(_selTrack, Ph, blank); changed = true; }
            else if (_selGroup != 0 && _selGroup != -1)
            {
                for (int i = 0; i < _clip.Tracks.Count; i++)
                    if (DocIdOf(_clip.Tracks[i].Target?.gameObject) == _selGroup) { InsertKey(i, Ph, blank); changed = true; }
                _selTrack = -1; _selKey = -1;
            }
        }
        if (_pendDelKey)
        {
            _pendDelKey = false;
            if (_selTrack >= 0)
            {
                var keys = _clip.Tracks[_selTrack].Keys;
                int idx = _selKey >= 0 ? _selKey : keys.FindIndex(k => k.Frame == Ph);
                if (idx >= 0) { keys.RemoveAt(idx); _selKey = -1; changed = true; }
            }
        }
        if (_pendInsFrame) { _pendInsFrame = false; ShiftFrames(Ph, +1); changed = true; }
        if (_pendDelFrame) { _pendDelFrame = false; if (Frames > 1) { ShiftFrames(Ph, -1); changed = true; } }
        if (_pendAddLabelFrame >= 0)
        {
            _clip.Labels.Add(new ClipLabel { Frame = _pendAddLabelFrame, Name = "label" });
            _selLabel = _clip.Labels.Count - 1; _selAction = -1; _selKey = -1; _selTrack = SelLabels;
            _pendAddLabelFrame = -1;
            changed = true;
        }
        if (_pendAddActionFrame >= 0)
        {
            _clip.Actions.Add(new ClipAction { Frame = _pendAddActionFrame, Kind = ClipActionKind.Stop });
            _selAction = _clip.Actions.Count - 1; _selLabel = -1; _selKey = -1; _selTrack = SelActions;
            _pendAddActionFrame = -1;
            changed = true;
        }
        if (_pendDelSel)
        {
            _pendDelSel = false;
            if (_selTrack == SelLabels && _selLabel >= 0) { _clip.Labels.RemoveAt(_selLabel); _selLabel = -1; changed = true; }
            else if (_selTrack == SelActions && _selAction >= 0) { _clip.Actions.RemoveAt(_selAction); _selAction = -1; changed = true; }
            else if (_selTrack >= 0 && _selKey >= 0) { _clip.Tracks[_selTrack].Keys.RemoveAt(_selKey); _selKey = -1; changed = true; }
        }
        if (changed)
            Commit();
    }

    // Track ac (yoksa) ve istenirse playhead'de guncel degerle key koy.
    ClipTrack AddTrack(Component target, string path, bool keyNow)
    {
        int idx = _clip.Tracks.FindIndex(t => t.Target == target && t.Path == path);
        if (idx < 0)
        {
            var track = new ClipTrack { Target = target, Path = path };
            // Ayni GO'nun track'lerinin ardina ekle (grup butunlugu).
            int insert = _clip.Tracks.Count;
            for (int i = _clip.Tracks.Count - 1; i >= 0; i--)
                if (_clip.Tracks[i].Target?.gameObject == target.gameObject) { insert = i + 1; break; }
            _clip.Tracks.Insert(insert, track);
            idx = insert;
            if (keyNow && _clip.Tracks[idx].Keys.Count == 0 && Ph != 0)
                InsertKey(idx, 0, false); // ilk key frame 0'da (baslangic pozu)
        }
        if (keyNow) InsertKey(idx, Ph, false);
        _selTrack = idx;
        _selGroup = -1;
        return _clip.Tracks[idx];
    }

    // F6/F7: playhead'de key — F6 property'nin SU ANKI degerini yakalar (Flash "convert to keyframe").
    void InsertKey(int track, int frame, bool blank)
    {
        var T = _clip.Tracks[track];
        int existing = T.Keys.FindIndex(k => k.Frame == frame);
        var prop = _clip.ResolveProperty(T);
        ClipKey nk;
        if (existing >= 0) { nk = T.Keys[existing]; _selTrack = track; _selKey = existing; }
        else
        {
            nk = new ClipKey { Frame = frame };
            ClipKey prev = null;
            foreach (var k in T.Keys)
                if (k.Frame < frame && (prev == null || k.Frame > prev.Frame)) prev = k;
            if (prev != null) { nk.Mode = prev.Mode; nk.Ease = prev.Ease; nk.Value = prev.Value; nk.Ref = prev.Ref; }
            int pos = 0;
            while (pos < T.Keys.Count && T.Keys[pos].Frame < frame) pos++;
            T.Keys.Insert(pos, nk);
            _selTrack = track; _selKey = pos;
        }
        if (prop == null || prop.Kind == AnimKind.Trigger) return;
        if (blank)
        {
            if (prop.Kind == AnimKind.Bool) nk.SetAnim(AnimValue.FromBool(false));
            return;
        }
        if (prop.Get != null && T.Target != null && !T.Target._destroyed)
            nk.SetAnim(prop.Get(T.Target));
    }

    // F5 / Shift+F5: playhead'den itibaren tum track/label/aksiyonlari kaydir.
    void ShiftFrames(int from, int delta)
    {
        foreach (var T in _clip.Tracks)
        {
            if (delta < 0)
                T.Keys.RemoveAll(k => k.Frame == from + 1 && T.Keys.Exists(o => o.Frame == from));
            foreach (var k in T.Keys)
                if (delta > 0 ? k.Frame >= from : k.Frame > from) k.Frame += delta;
        }
        foreach (var l in _clip.Labels) if (delta > 0 ? l.Frame >= from : l.Frame > from) l.Frame += delta;
        foreach (var a in _clip.Actions) if (delta > 0 ? a.Frame >= from : a.Frame > from) a.Frame += delta;
        _clip.FrameCount = Math.Max(1, _clip.FrameCount + delta);
    }

    // Grup sirasi: track listesinde grubu tasi + hedef GO'yu doc'ta ayni siraya (z-order).
    void MoveGroup(int fromGoId, int beforeGoId)
    {
        var moving = _clip.Tracks.FindAll(t => DocIdOf(t.Target?.gameObject) == fromGoId);
        if (moving.Count == 0) return;
        _clip.Tracks.RemoveAll(t => DocIdOf(t.Target?.gameObject) == fromGoId);
        int insert = beforeGoId == 0 ? _clip.Tracks.Count
            : _clip.Tracks.FindIndex(t => DocIdOf(t.Target?.gameObject) == beforeGoId);
        if (insert < 0) insert = _clip.Tracks.Count;
        _clip.Tracks.InsertRange(insert, moving);
        Commit();
        var g = _es.FindGo(fromGoId);
        var before = beforeGoId != 0 ? _es.FindGo(beforeGoId) : null;
        if (g != null && (before == null || before.Parent == g.Parent))
            _es.MoveGameObject(g, g.Parent, beforeGoId);
    }

    // ---- record ------------------------------------------------------------

    // Kullanici bir property'yi duzenledi (Inspector/handle/RPC): record acikken playhead'e key.
    // Iki faz: ONCE (after=false) track'i olmayan property'lerin mevcut degeri yakalanir
    // (yeni track'in frame-0 key'i = duzenleme oncesi poz), SONRA key yazilir.
    readonly List<(Component target, string path, AnimValue before)> _preEdit = new();

    void OnLiveEdited(int goId, int compIndex, string path, bool after)
    {
        if (!_record || _clip == null || _es == null || _g == null)
            return;
        var gd = _es.FindGo(goId);
        var live = _es.Live(goId);
        if (gd == null || live == null) return;
        Component target;
        if (compIndex < 0) target = live.transform;
        else if (compIndex < gd.Components.Count) target = _es.FindLiveComponent(gd, gd.Components[compIndex]);
        else return;
        if (target == null || target == _clip) return;

        if (!after)
        {
            foreach (var p in MatchingProps(target, path))
                if (p.Get != null && !_clip.Tracks.Exists(t => t.Target == target && t.Path == p.Path)
                    && !_preEdit.Exists(e => e.target == target && e.path == p.Path))
                    _preEdit.Add((target, p.Path, p.Get(target)));
            return;
        }

        bool changed = false;
        foreach (var p in MatchingProps(target, path))
        {
            bool exact = p.Path == path || path == "Enabled";
            int idx = _clip.Tracks.FindIndex(t => t.Target == target && t.Path == p.Path);
            int pre = _preEdit.FindIndex(e => e.target == target && e.path == p.Path);
            // Ic ice ust alan duzenlemesinde hangi alt alanin degistigi bilinmez:
            // yalniz track'i zaten olan alt yollara key yazilir (gurultu yok).
            if (idx < 0 && !exact) { if (pre >= 0) _preEdit.RemoveAt(pre); continue; }
            if (idx < 0)
            {
                var track = AddTrack(target, p.Path, keyNow: true);
                if (pre >= 0 && Ph != 0)
                {
                    var k0 = track.Keys.Find(k => k.Frame == 0);
                    k0?.SetAnim(_preEdit[pre].before); // baslangic = duzenleme oncesi deger
                }
            }
            else InsertKey(idx, Ph, false);
            if (pre >= 0) _preEdit.RemoveAt(pre);
            changed = true;
        }
        if (changed) Commit();
    }

    // Yola uyan animatable'lar: tam eslesme ya da ic ice alt yollar ("Fill" → "Fill.Tint"); Enabled pseudo dahil.
    IEnumerable<AnimProperty> MatchingProps(Component target, string path)
    {
        if (path == "Enabled" && !(target is Transform))
        {
            yield return AnimRegistry.Enabled;
            yield break;
        }
        var props = AnimRegistry.PropsOf(Catalog, target.GetType());
        for (int i = 0; i < props.Length; i++)
        {
            var p = props[i];
            if (p.Kind == AnimKind.Trigger || p.Get == null) continue;
            if (p.Path == path || p.Path.StartsWith(path + ".", StringComparison.Ordinal))
                yield return p;
        }
    }

    // ---- toolbar -----------------------------------------------------------

    void DrawToolbar(in Rect bar)
    {
        Event ev = Event.Current;
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(bar, ColHeaderBg);
        float x = 4;
        if (Gui.Button(new Rect(x, 3, 24, 20), "|<")) { _playing = false; SetPlayhead(0); }
        x += 26;
        if (Gui.Button(new Rect(x, 3, 24, 20), "<")) { _playing = false; SetPlayhead(Ph - 1); }
        x += 26;
        if (Gui.Button(new Rect(x, 3, 44, 20), _playing ? "Pause" : "Play")) TogglePlay();
        x += 46;
        if (Gui.Button(new Rect(x, 3, 24, 20), ">")) { _playing = false; SetPlayhead(Ph + 1); }
        x += 26;
        if (Gui.Button(new Rect(x, 3, 24, 20), ">|")) { _playing = false; SetPlayhead(Frames - 1); }
        x += 26;
        if (Gui.Button(new Rect(x, 3, 40, 20), "Stop"))
        {
            _playing = false; _record = false;
            if (Previewing) PreviewSession.End(); // doc'tan reload: sahne pozu geri
        }
        x += 44;
        // Record: Inspector/handle duzenlemeleri playhead'de key olur.
        if (ev.Type == EventType.Repaint && _record)
            GuiRenderer.DrawRect(new Rect(x - 1, 2, 44, 22), ColRecord, 0);
        if (Gui.Button(new Rect(x, 3, 42, 20), "Rec")) { _record = !_record; _preEdit.Clear(); if (_record) EnsurePreview(); }
        x += 50;
        if (ev.Type == EventType.Repaint)
        {
            Span<char> tmp = stackalloc char[32];
            int n = 0;
            Ph.TryFormat(tmp, out int n1); n += n1;
            tmp[n++] = ' '; tmp[n++] = '/'; tmp[n++] = ' ';
            Frames.TryFormat(tmp.Slice(n), out int n2); n += n2;
            GuiRenderer.DrawTextIn(new Rect(x, 3, 70, 20), tmp.Slice(0, n), Gui.FontSize - 2f, Previewing ? ColPlayhead : ColText);
        }
        x += 72;
        Gui.Label(new Rect(x, 3, 26, 20), "Fps");
        x += 26;
        int fps = Gui.DragInt(new Rect(x, 4, 40, 18), _clip.Fps, 0.1f, 1, 240);
        if (fps != _clip.Fps) { _clip.Fps = fps; Commit(); }
        x += 46;
        Gui.Label(new Rect(x, 3, 46, 20), "Frames");
        x += 48;
        int fc = Gui.DragInt(new Rect(x, 4, 46, 18), _clip.FrameCount, 0.2f, 1, 100000);
        if (fc != _clip.FrameCount) { _clip.FrameCount = fc; Commit(); }
        x += 52;
        bool loop = Gui.Toggle(new Rect(x, 4, 18, 18), _clip.Loop);
        if (loop != _clip.Loop) { _clip.Loop = loop; Commit(); }
        Gui.Label(new Rect(x + 20, 3, 36, 20), "Loop");
        x += 60;
        _frameW = Gui.HorizontalSlider(new Rect(x, 5, 70, 16), _frameW, 5f, 32f);
        x += 78;
        // Klip secici: sahnedeki diger MovieClip'lere gec.
        RefreshClipItems();
        int pick = Gui.ComboBox(new Rect(x, 3, 120, 20), 0, _clipItems);
        if (pick > 0 && pick < _clipIds.Count) { Bind(_clipIds[pick]); Selection.DocId = _boundId; }
        x += 124;
        // Secili sahne nesnesini kliğe ekle (Hierarchy'den surukle-birak da ayni isi yapar).
        int selId = Selection.DocId;
        bool canAdd = selId != 0 && _es.Live(selId) != null;
        if (Gui.Button(new Rect(x, 3, 80, 20), "+ Selected") && canAdd) _pendAddObjectId = selId;
        x += 84;
        bool canKey = _selTrack >= 0 || _selGroup > 0;
        if (Gui.Button(new Rect(x, 3, 60, 20), "Key F6") && canKey) _pendAddKey = true;
        x += 62;
        if (Gui.Button(new Rect(x, 3, 56, 20), "Del Key") && _selTrack >= 0) _pendDelKey = true;
        x += 60;
        if (Gui.Button(new Rect(x, 3, 60, 20), "+Frame F5")) _pendInsFrame = true;
        x += 62;
        if (Gui.Button(new Rect(x, 3, 56, 20), "-Frame")) _pendDelFrame = true;
    }

    void TogglePlay()
    {
        _playing = !_playing;
        if (_playing) { EnsurePreview(); _lastTick = GLFW.GetTime(); if (Ph >= Frames - 1) _playhead = 0; }
    }

    void TickPlay()
    {
        double now = GLFW.GetTime();
        float dt = (float)(now - _lastTick);
        _lastTick = now;
        _playhead += dt * _clip.Fps * _clip.Speed;
        float end = Frames - 1;
        if (_playhead > end)
        {
            if (_clip.Loop) _playhead %= Frames;
            else { _playhead = end; _playing = false; }
        }
        Sample();
    }

    // GO'nun eklenebilir property'leri: her component × AnimRegistry (track'i olanlar haric).
    void RefreshPropItems(GameObject go, int goId)
    {
        int sig = go.ComponentCount * 1000 + _clip.Tracks.Count;
        if (_propItemsGo == goId && _propItemsSig == sig && ReferenceEquals(_propItemsLive, go)) return;
        _propItemsGo = goId; _propItemsSig = sig; _propItemsLive = go;
        _propTargets.Clear();
        var list = new List<string> { "+ Property..." };
        _propTargets.Add((-1, null));
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var c = go.ComponentAt(i);
            if (c == null || c._destroyed || c == _clip) continue;
            string tn = c is Transform ? "Transform" : c.GetType().Name;
            var props = AnimRegistry.PropsOf(Catalog, c.GetType());
            for (int p = 0; p < props.Length; p++)
            {
                if (_clip.Tracks.Exists(t => t.Target == c && t.Path == props[p].Path)) continue;
                _propTargets.Add((i, props[p].Path));
                list.Add(tn + "." + props[p].Path + (props[p].Kind == AnimKind.Trigger ? " ()" : ""));
            }
            if (!(c is Transform) && !_clip.Tracks.Exists(t => t.Target == c && t.Path == "Enabled"))
            {
                _propTargets.Add((i, "Enabled"));
                list.Add(tn + ".Enabled");
            }
        }
        _propItems = list.ToArray();
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
                    _playing = false;
                    SetPlayhead(XToFrame(ev.MousePosition.x));
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id) { SetPlayhead(XToFrame(ev.MousePosition.x)); ev.Use(); }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(local, ColRulerBg);
                    Span<char> tmp = stackalloc char[8];
                    int step = _frameW >= 10 ? 5 : (_frameW >= 6 ? 10 : 20);
                    for (int f = 0; f <= Frames; f++)
                    {
                        float x = FrameToX(f);
                        if (x < -40 || x > ruler.width + 40) continue;
                        bool major = f % step == 0;
                        GuiRenderer.DrawRect(new Rect(x, major ? 6 : 13, 1, RulerH - (major ? 6 : 13)), major ? ColGrid5 : ColGrid, 1);
                        if (major && f < Frames)
                        {
                            (f + 1).TryFormat(tmp, out int n); // Flash gibi 1-tabanli gosterim
                            GuiRenderer.DrawText(new Vec2(x + 3, 1), tmp.Slice(0, n), Gui.FontSize - 4f, ColText, 2);
                        }
                    }
                    GuiRenderer.DrawRect(new Rect(FrameToX(Ph), 0, _frameW, RulerH), ColPlayheadCol, 2);
                    GuiRenderer.DrawRect(new Rect(FrameToX(_playhead + 0.5f), 0, 1, RulerH), ColPlayhead, 3);
                    break;
                }
        }
        GuiClip.Pop();
    }

    // ---- label / action seritleri -----------------------------------------

    void DrawLabelStrip(in Rect strip)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashStrip, FocusType.Passive);
        GuiClip.Push(strip);
        var local = new Rect(0, 0, strip.width, strip.height);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    int f = XToFrame(ev.MousePosition.x);
                    int hit = _clip.Labels.FindIndex(l => l.Frame == f);
                    if (hit >= 0) { _selTrack = SelLabels; _selLabel = hit; _selAction = -1; _selKey = -1; _selGroup = -1; }
                    else if (ev.ClickCount == 2 && f >= 0 && f < Frames) _pendAddLabelFrame = f;
                    else { _playing = false; SetPlayhead(f); }
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                GuiRenderer.DrawRect(local, ColStripBg);
                DrawGrid(local);
                for (int i = 0; i < _clip.Labels.Count; i++)
                {
                    var l = _clip.Labels[i];
                    float x = FrameToX(l.Frame);
                    bool sel = _selTrack == SelLabels && _selLabel == i;
                    GuiRenderer.DrawRect(new Rect(x, 2, 2, strip.height - 4), sel ? ColKeySel : ColLabel, 3);
                    GuiRenderer.DrawText(new Vec2(x + 5, 2), l.Name, Gui.FontSize - 4f, sel ? ColKeySel : ColLabel, 3);
                }
                GuiRenderer.DrawRect(new Rect(FrameToX(_playhead + 0.5f), 0, 1, strip.height), ColPlayhead, 4);
                GuiRenderer.DrawRect(new Rect(0, strip.height - 1, strip.width, 1), ColLine, 1);
                break;
        }
        GuiClip.Pop();
    }

    void DrawActionStrip(in Rect strip)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashStrip, FocusType.Passive);
        GuiClip.Push(strip);
        var local = new Rect(0, 0, strip.width, strip.height);
        float cy = strip.height * 0.5f;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    int f = XToFrame(ev.MousePosition.x);
                    int hit = _clip.Actions.FindIndex(a => a.Frame == f);
                    if (hit >= 0) { _selTrack = SelActions; _selAction = hit; _selLabel = -1; _selKey = -1; _selGroup = -1; }
                    else if (ev.ClickCount == 2 && f >= 0 && f < Frames) _pendAddActionFrame = f;
                    else { _playing = false; SetPlayhead(f); }
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                GuiRenderer.DrawRect(local, ColStripBg);
                DrawGrid(local);
                for (int i = 0; i < _clip.Actions.Count; i++)
                {
                    bool sel = _selTrack == SelActions && _selAction == i;
                    GuiRenderer.DrawDiamond(new Vec2(FrameToX(_clip.Actions[i].Frame + 0.5f), cy), 5f, sel ? ColKeySel : ColAction, 3);
                }
                GuiRenderer.DrawRect(new Rect(FrameToX(_playhead + 0.5f), 0, 1, strip.height), ColPlayhead, 4);
                GuiRenderer.DrawRect(new Rect(0, strip.height - 1, strip.width, 1), ColLine, 1);
                break;
        }
        GuiClip.Pop();
    }

    // ---- izgara ------------------------------------------------------------

    void DrawLanes(in Rect lanes)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashLane, FocusType.Passive);
        GuiClip.Push(lanes);
        var local = new Rect(0, 0, lanes.width, lanes.height);
        var tracks = _clip.Tracks;

        switch (ev.GetTypeForControl(id))
        {
            case EventType.ScrollWheel:
                if (local.Contains(ev.MousePosition))
                {
                    if ((ev.Modifiers & EventModifiers.Control) != 0)
                    {
                        float before = (ev.MousePosition.x + _hScroll) / _frameW;
                        _frameW = Math.Clamp(_frameW + ev.Delta.y * 1.5f, 5f, 32f);
                        _hScroll = MathF.Max(0, before * _frameW - ev.MousePosition.x);
                    }
                    else if ((ev.Modifiers & EventModifiers.Shift) != 0) _hScroll -= ev.Delta.y * 16;
                    else _vScroll -= ev.Delta.y * 12;
                    ev.Use();
                }
                break;
            case EventType.MouseDown:
                if (local.Contains(ev.MousePosition))
                {
                    int row = (int)MathF.Floor((ev.MousePosition.y + _vScroll) / RowH);
                    int f = XToFrame(ev.MousePosition.x);
                    _selLabel = -1; _selAction = -1;
                    if (row >= 0 && row < _rows.Count)
                    {
                        var r = _rows[row];
                        if (r.Group)
                        {
                            _selGroup = r.GoId; _selTrack = -1; _selKey = -1;
                            if (ev.ClickCount == 2 && f >= 0 && f < Frames) { _playing = false; _playhead = f; _pendAddKey = true; }
                        }
                        else
                        {
                            _selGroup = -1; _selTrack = r.Track;
                            int hit = tracks[r.Track].Keys.FindIndex(k => k.Frame == f);
                            _selKey = hit;
                            if (hit >= 0)
                            {
                                GuiUtility.HotControl = id;
                                _drag = true; _dragMoved = false;
                                _dragTrack = r.Track; _dragKey = hit; _dragGrabOffset = f - tracks[r.Track].Keys[hit].Frame;
                            }
                            else if (ev.ClickCount == 2 && f >= 0 && f < Frames)
                            {
                                _playing = false; _playhead = f; _pendAddKey = true;
                            }
                        }
                    }
                    else { _selTrack = -1; _selGroup = -1; }
                    _playing = false;
                    SetPlayhead(f);
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id && _drag && _dragTrack < tracks.Count)
                {
                    var keys = tracks[_dragTrack].Keys;
                    if (_dragKey >= 0 && _dragKey < keys.Count)
                    {
                        int target = Math.Clamp(XToFrame(ev.MousePosition.x) - _dragGrabOffset, 0, Frames - 1);
                        var k = keys[_dragKey];
                        if (target != k.Frame && !keys.Exists(o => o != k && o.Frame == target))
                        {
                            k.Frame = target;
                            keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
                            _dragKey = keys.IndexOf(k);
                            _selKey = _dragKey;
                            _dragMoved = true;
                            _playhead = target;
                            _clip.Rebuild();
                            Sample();
                        }
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    if (_drag && _dragMoved) Commit();
                    _drag = false;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(local, ColLaneEmpty);
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        float rowY = i * RowH - _vScroll;
                        if (rowY + RowH < 0 || rowY > lanes.height) continue;
                        if (_rows[i].Group) DrawGroupRow(_rows[i], rowY, lanes.width);
                        else DrawTrackRow(tracks[_rows[i].Track], _rows[i].Track, rowY, lanes.width);
                    }
                    DrawGrid(local);
                    GuiRenderer.DrawRect(new Rect(FrameToX(Frames), 0, 1, lanes.height), new Color(255, 255, 255, 60), 2);
                    GuiRenderer.DrawRect(new Rect(FrameToX(Ph), 0, _frameW, lanes.height), ColPlayheadCol, 3);
                    GuiRenderer.DrawRect(new Rect(FrameToX(_playhead + 0.5f), 0, 1, lanes.height), ColPlayhead, 4);
                    break;
                }
        }
        GuiClip.Pop();
    }

    // Grup satiri: altindaki track'lerin key'lerinin birlesimi (ozet elmaslar).
    void DrawGroupRow(in Row r, float rowY, float width)
    {
        GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH), ColGroupBg);
        if (r.GoId == _selGroup)
            GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH), ColSelRow, 1);
        float cy = rowY + RowH * 0.5f, rad = MathF.Min(4f, _frameW * 0.35f);
        for (int f = 0; f < Frames; f++)
        {
            float x = FrameToX(f);
            if (x + _frameW < 0 || x > width) continue;
            bool any = false;
            foreach (var t in _clip.Tracks)
                if (t.Target?.gameObject == r.Go && t.Keys.Exists(k => k.Frame == f)) { any = true; break; }
            if (any) GuiRenderer.DrawDiamond(new Vec2(x + _frameW * 0.5f, cy), rad, ColKeyGroup, 3);
        }
        GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, width, 1), ColLine, 1);
    }

    void DrawTrackRow(ClipTrack T, int index, float rowY, float width)
    {
        GuiRenderer.DrawRect(new Rect(0, rowY, FrameToX(Frames), RowH), ColLaneBg);
        if (index == _selTrack)
            GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH), ColSelRow, 1);
        var keys = T.Keys;
        var prop = _clip.ResolveProperty(T);
        bool trigger = prop != null && prop.Kind == AnimKind.Trigger;
        for (int k = 0; k < keys.Count; k++)
        {
            var key = keys[k];
            int end = k + 1 < keys.Count ? keys[k + 1].Frame : Frames;
            float x0 = FrameToX(key.Frame), x1 = FrameToX(end);
            if (x1 < 0 || x0 > width) continue;
            bool tween = !trigger && key.Mode == ClipKeyMode.Tween && k + 1 < keys.Count;
            if (!trigger && (end > key.Frame + 1 || tween))
                GuiRenderer.DrawRect(new Rect(x0 + 1, rowY + 3, x1 - x0 - 2, RowH - 6), tween ? ColTween : ColHold, 1);
            if (tween)
                GuiRenderer.DrawRect(new Rect(x0 + _frameW * 0.5f, rowY + RowH * 0.5f, x1 - x0 - _frameW * 0.5f - 3, 1), ColText, 2);
            float cx = x0 + _frameW * 0.5f, cy = rowY + RowH * 0.5f;
            bool sel = index == _selTrack && k == _selKey;
            float r = MathF.Min(5f, _frameW * 0.4f);
            if (sel) GuiRenderer.DrawDiamond(new Vec2(cx, cy), r + 2.5f, ColKeySel, 2);
            GuiRenderer.DrawDiamond(new Vec2(cx, cy), r, trigger ? ColAction : ColKey, 3);
        }
        GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, width, 1), ColLine, 1);
        if (prop == null)
            GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH - 1), new Color(200, 40, 40, 50), 2); // cozulemeyen yol
    }

    void DrawGrid(in Rect local)
    {
        int step = _frameW >= 10 ? 5 : (_frameW >= 6 ? 10 : 20);
        for (int f = 0; f <= Frames; f++)
        {
            float x = FrameToX(f);
            if (x < 0 || x > local.width) continue;
            GuiRenderer.DrawRect(new Rect(x, 0, 1, local.height), f % step == 0 ? ColGrid5 : ColGrid, 1);
        }
    }

    // ---- satir basliklari --------------------------------------------------

    void DrawHeaders(in Rect header)
    {
        Event ev = Event.Current;
        GuiClip.Push(header);
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(0, 0, header.width, header.height), ColHeaderBg);
        int groupOrdinal = 0, groupCount = _rows.FindAll(r => r.Group).Count;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            float rowY = i * RowH - _vScroll;
            bool visible = rowY + RowH >= 0 && rowY <= header.height;
            float lineY = rowY + (RowH - 16) * 0.5f;
            if (r.Group)
            {
                bool collapsed = _collapsed.Contains(r.GoId);
                if (ev.Type == EventType.Repaint && visible)
                {
                    GuiRenderer.DrawRect(new Rect(0, rowY, header.width, RowH), ColGroupBg);
                    if (r.GoId == _selGroup)
                        GuiRenderer.DrawRect(new Rect(0, rowY, header.width, RowH), new Color(76, 102, 140, 90), 1);
                    GuiRenderer.DrawTextIn(new Rect(22, rowY, header.width - 22 - 172, RowH), r.Label, Gui.FontSize - 2f, ColText);
                    GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, header.width, 1), ColLine, 1);
                }
                // Kontrol sirasi korunumu: gorunmeyen satirlar da widget cagirir.
                if (Gui.Button(new Rect(2, lineY, 16, 16), collapsed ? ">" : "v"))
                {
                    if (!_collapsed.Remove(r.GoId)) _collapsed.Add(r.GoId);
                }
                if (ev.Type == EventType.MouseDown && GuiUtility.HotControl == 0 && visible && new Rect(20, rowY, header.width - 20 - 172, RowH).Contains(ev.MousePosition))
                {
                    _selGroup = r.GoId; _selTrack = -1; _selKey = -1; _selLabel = -1; _selAction = -1;
                    Selection.DocId = r.GoId; // sahnede de sec (handle'lar + Inspector)
                    ev.Use();
                }
                // + Property combo (GO'nun tum component'lerinin animatable'lari)
                RefreshPropItems(r.Go, r.GoId);
                int pick = Gui.ComboBox(new Rect(header.width - 170, lineY, 132, 16), 0, _propItems);
                if (pick > 0 && pick < _propTargets.Count) { _pendAddPropGo = r.GoId; _pendAddPropComp = _propTargets[pick].comp; _pendAddPropPath = _propTargets[pick].path; }
                int next = NextGroupId(i), prevBefore = PrevGroupId(i);
                if (Gui.Button(new Rect(header.width - 36, lineY, 16, 16), "^") && groupOrdinal > 0) { _pendMoveGroupFrom = r.GoId; _pendMoveGroupTo = prevBefore; }
                if (Gui.Button(new Rect(header.width - 18, lineY, 16, 16), "v") && groupOrdinal < groupCount - 1) { _pendMoveGroupFrom = r.GoId; _pendMoveGroupTo = next; }
                groupOrdinal++;
            }
            else
            {
                if (ev.Type == EventType.Repaint && visible)
                {
                    if (r.Track == _selTrack)
                        GuiRenderer.DrawRect(new Rect(0, rowY, header.width, RowH), new Color(76, 102, 140, 90), 1);
                    var prop = _clip.ResolveProperty(_clip.Tracks[r.Track]);
                    GuiRenderer.DrawTextIn(new Rect(26, rowY, header.width - 26 - 22, RowH), r.Label, Gui.FontSize - 3f,
                        prop == null ? ColPlayhead : ColText);
                    GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, header.width, 1), ColLine, 1);
                }
                if (ev.Type == EventType.MouseDown && GuiUtility.HotControl == 0 && visible && new Rect(0, rowY, header.width - 22, RowH).Contains(ev.MousePosition))
                {
                    _selTrack = r.Track; _selKey = -1; _selGroup = -1; _selLabel = -1; _selAction = -1;
                    ev.Use();
                }
                if (Gui.Button(new Rect(header.width - 18, lineY, 16, 16), "x")) _pendDelTrack = r.Track;
            }
        }
        GuiClip.Pop();
    }

    int NextGroupId(int rowIndex)
    {
        for (int i = rowIndex + 1; i < _rows.Count; i++)
            if (_rows[i].Group)
            {
                // "sonraki grubun onune" = sonrakinin sonrasina: bir sonraki-sonraki grup (yoksa 0 = sona)
                for (int j = i + 1; j < _rows.Count; j++)
                    if (_rows[j].Group) return _rows[j].GoId;
                return 0;
            }
        return 0;
    }

    int PrevGroupId(int rowIndex)
    {
        for (int i = rowIndex - 1; i >= 0; i--)
            if (_rows[i].Group) return _rows[i].GoId;
        return 0;
    }

    // ---- splitter + inspector ---------------------------------------------

    void DrawSplitter(in Rect splitter, float areaW)
    {
        Event ev = Event.Current;
        int id = GuiUtility.GetControlID(HashSplit, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (splitter.Contains(ev.MousePosition)) { GuiUtility.HotControl = id; ev.Use(); }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id) { _inspectorW = areaW - ev.MousePosition.x - SplitterW * 0.5f; ev.Use(); }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                bool hov = splitter.Contains(ev.MousePosition) || GuiUtility.HotControl == id;
                if (hov) GuiCursorManager.Request(GuiCursor.ResizeH);
                GuiRenderer.DrawRect(splitter, hov ? new Color(110, 150, 235, 255) : new Color(64, 68, 84, 255), 1);
                break;
        }
    }

    void DrawInspector(in Rect area)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(area, ColHeaderBg);
        GuiLayoutUtility.BeginArea(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16));

        if (_selTrack == SelLabels && _selLabel >= 0 && _selLabel < _clip.Labels.Count)
            InspectLabel(_clip.Labels[_selLabel]);
        else if (_selTrack == SelActions && _selAction >= 0 && _selAction < _clip.Actions.Count)
            InspectAction(_clip.Actions[_selAction]);
        else if (_selTrack >= 0 && _selKey >= 0)
            InspectKey(_clip.Tracks[_selTrack], _clip.Tracks[_selTrack].Keys[_selKey]);
        else if (_selTrack >= 0)
            InspectTrack(_clip.Tracks[_selTrack], _selTrack);
        else
            InspectClip();

        GuiLayoutUtility.EndArea();
    }

    Rect Row_(float h = 20f) => GuiLayoutUtility.GetRect(60, 4000, h, h, true, false, default);

    float FloatField(string label, float v, float speed = 0.01f, float min = float.MinValue, float max = float.MaxValue)
    {
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        return Gui.DragFloat(new Rect(r.x + 72, r.y + 1, r.width - 72, r.height - 2), v, speed, min, max);
    }

    int IntField(string label, int v, int min = int.MinValue, int max = int.MaxValue)
    {
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        return Gui.DragInt(new Rect(r.x + 72, r.y + 1, r.width - 72, r.height - 2), v, 0.1f, min, max);
    }

    int ComboField(string label, int v, string[] items)
    {
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        return Gui.ComboBox(new Rect(r.x + 72, r.y + 1, r.width - 72, r.height - 2), v, items);
    }

    bool ToggleField(string label, bool v)
    {
        var r = Row_();
        bool nv = Gui.Toggle(new Rect(r.x, r.y + 2, 16, 16), v);
        Gui.Label(new Rect(r.x + 20, r.y, r.width - 20, r.height), label);
        return nv;
    }

    // N bilesenli float satiri (Vec2/3/4, Color).
    Vec4 VecField(string label, Vec4 v, int n, float speed, float min = float.MinValue, float max = float.MaxValue)
    {
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        float w = (r.width - 72) / n;
        if (n > 0) v.x = Gui.DragFloat(new Rect(r.x + 72, r.y + 1, w - 2, r.height - 2), v.x, speed, min, max);
        if (n > 1) v.y = Gui.DragFloat(new Rect(r.x + 72 + w, r.y + 1, w - 2, r.height - 2), v.y, speed, min, max);
        if (n > 2) v.z = Gui.DragFloat(new Rect(r.x + 72 + w * 2, r.y + 1, w - 2, r.height - 2), v.z, speed, min, max);
        if (n > 3) v.w = Gui.DragFloat(new Rect(r.x + 72 + w * 3, r.y + 1, w - 2, r.height - 2), v.w, speed, min, max);
        return v;
    }

    string TextField(string label, string value, int owner)
    {
        if (_nameOwner != owner)
        {
            _nameOwner = owner;
            value ??= "";
            _nameLen = Math.Min(value.Length, _nameBuf.Length);
            value.CopyTo(0, _nameBuf, 0, _nameLen);
        }
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        Gui.TextField(new Rect(r.x + 72, r.y + 1, r.width - 72, r.height - 2), _nameBuf, ref _nameLen);
        var span = new ReadOnlySpan<char>(_nameBuf, 0, _nameLen);
        return span.SequenceEqual(value ?? "") ? value : new string(span);
    }

    void InspectClip()
    {
        Gui.LayoutLabel("MovieClip");
        GuiLayout.Space(4);
        bool b;
        b = ToggleField("Play On Start", _clip.PlayOnStart); if (b != _clip.PlayOnStart) { _clip.PlayOnStart = b; Commit(); }
        b = ToggleField("Loop", _clip.Loop); if (b != _clip.Loop) { _clip.Loop = b; Commit(); }
        b = ToggleField("Interpolate", _clip.Interpolate); if (b != _clip.Interpolate) { _clip.Interpolate = b; Commit(); }
        float sp = FloatField("Speed", _clip.Speed, 0.01f, -10f, 10f); if (sp != _clip.Speed) { _clip.Speed = sp; Commit(); }
        GuiLayout.Space(8);
        Gui.LayoutLabel("Hierarchy'den nesneyi panele surukle (veya + Selected)");
        Gui.LayoutLabel("Nesne satirinda + Property: her component'in");
        Gui.LayoutLabel("  animatable alani (ic ice, [Animatable], tetik)");
        Gui.LayoutLabel("Rec: Inspector/handle duzenlemesi = key");
        Gui.LayoutLabel("F6 key, Shift+F6 sil, F5/Shift+F5 frame");
        Gui.LayoutLabel("Space oynat, </> frame, Ctrl+tekerlek zoom");
    }

    void InspectTrack(ClipTrack T, int index)
    {
        Gui.LayoutLabel("Track");
        GuiLayout.Space(4);
        var prop = _clip.ResolveProperty(T);
        Gui.LayoutLabel(TrackLabel(T) + (prop == null ? "  (cozulemedi)" : "  [" + prop.Kind + "]"));
        if (prop != null && prop.Kind is AnimKind.Vec2 or AnimKind.Vec3 or AnimKind.Vec4 or AnimKind.Color)
        {
            int n = prop.Kind switch { AnimKind.Vec2 => 2, AnimKind.Vec3 => 3, _ => 4 };
            string[] names = prop.Kind == AnimKind.Color ? new[] { "R", "G", "B", "A" } : new[] { "X", "Y", "Z", "W" };
            var r = Row_();
            Gui.Label(new Rect(r.x, r.y, 70, r.height), "Channels");
            int mask = T.Mask;
            for (int i = 0; i < n; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                bool nv = Gui.Toggle(new Rect(r.x + 72 + i * 44, r.y + 2, 16, 16), on);
                Gui.Label(new Rect(r.x + 92 + i * 44, r.y, 20, r.height), names[i]);
                if (nv != on) mask = nv ? mask | (1 << i) : mask & ~(1 << i);
            }
            if (mask != T.Mask && mask != 0) { T.Mask = mask; Commit(); }
        }
        GuiLayout.Space(6);
        if (Gui.LayoutButton("Key at Playhead (F6)")) _pendAddKey = true;
        if (Gui.LayoutButton("Delete Track")) _pendDelTrack = index;
    }

    void InspectKey(ClipTrack T, ClipKey k)
    {
        var prop = _clip.ResolveProperty(T);
        Gui.LayoutLabel("Keyframe  " + TrackLabel(T));
        GuiLayout.Space(4);
        bool ch = false;
        int f = IntField("Frame", k.Frame, 0, Frames - 1);
        if (f != k.Frame && !T.Keys.Exists(o => o != k && o.Frame == f))
        {
            k.Frame = f; T.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame)); _selKey = T.Keys.IndexOf(k); _playhead = f; ch = true;
        }
        if (prop == null || prop.Kind != AnimKind.Trigger)
        {
            int mode = ComboField("Mode", (int)k.Mode, ModeNames); if (mode != (int)k.Mode) { k.Mode = (ClipKeyMode)mode; ch = true; }
            int ease = ComboField("Ease", (int)k.Ease, EaseNames); if (ease != (int)k.Ease) { k.Ease = (Ease)ease; ch = true; }
        }
        GuiLayout.Space(4);
        // Deger: Kind'a gore (ozel property kodu yok).
        if (prop != null)
        {
            var v = k.Value;
            switch (prop.Kind)
            {
                case AnimKind.Float: { float nv = FloatField("Value", v.x, 0.05f); if (nv != v.x) { v.x = nv; ch = true; } break; }
                case AnimKind.Int: { int nv = IntField("Value", (int)MathF.Round(v.x)); if (nv != (int)MathF.Round(v.x)) { v.x = nv; ch = true; } break; }
                case AnimKind.Bool: { bool nv = ToggleField("Value", v.x != 0f); if (nv != (v.x != 0f)) { v.x = nv ? 1f : 0f; ch = true; } break; }
                case AnimKind.Enum:
                    {
                        var names = Enum.GetNames(prop.ValueType);
                        int cur = (int)MathF.Round(v.x);
                        int nv = ComboField("Value", cur, names); if (nv != cur && nv >= 0) { v.x = nv; ch = true; }
                        break;
                    }
                case AnimKind.Vec2: { var nv = VecField("Value", v, 2, 0.5f); if (!Same(nv, v)) { v = nv; ch = true; } break; }
                case AnimKind.Vec3: { var nv = VecField("Value", v, 3, 0.5f); if (!Same(nv, v)) { v = nv; ch = true; } break; }
                case AnimKind.Vec4: { var nv = VecField("Value", v, 4, 0.5f); if (!Same(nv, v)) { v = nv; ch = true; } break; }
                case AnimKind.Color: { var nv = VecField("RGBA", v, 4, 1f, 0f, 255f); if (!Same(nv, v)) { v = nv; ch = true; } break; }
                case AnimKind.Ref:
                    {
                        var r = Row_();
                        Gui.Label(new Rect(r.x, r.y, 70, r.height), "Ref");
                        Gui.Label(new Rect(r.x + 72, r.y, r.width - 72, r.height), k.Ref?.Name ?? "(none)");
                        Gui.LayoutLabel("Ref degeri: hedefe Inspector'dan ata, 'Capture' ile key'e al");
                        break;
                    }
                case AnimKind.Trigger: Gui.LayoutLabel("Tetik: bu frame'e girildiginde cagrilir"); break;
            }
            k.Value = v;
        }
        GuiLayout.Space(6);
        if (prop != null && prop.Kind != AnimKind.Trigger && Gui.LayoutButton("Capture From Stage"))
        {
            if (prop.Get != null && T.Target != null) { k.SetAnim(prop.Get(T.Target)); ch = true; }
        }
        if (Gui.LayoutButton("Delete Keyframe")) _pendDelSel = true;
        if (ch) Commit();
    }

    void InspectLabel(ClipLabel l)
    {
        Gui.LayoutLabel("Label");
        GuiLayout.Space(4);
        bool ch = false;
        string name = TextField("Name", l.Name, 2000 + _selLabel);
        if (!ReferenceEquals(name, l.Name)) { l.Name = name; ch = true; }
        int f = IntField("Frame", l.Frame, 0, Frames - 1); if (f != l.Frame) { l.Frame = f; ch = true; }
        GuiLayout.Space(6);
        if (Gui.LayoutButton("Delete Label")) _pendDelSel = true;
        if (ch) Commit();
    }

    void InspectAction(ClipAction a)
    {
        Gui.LayoutLabel("Action");
        GuiLayout.Space(4);
        bool ch = false;
        int f = IntField("Frame", a.Frame, 0, Frames - 1); if (f != a.Frame) { a.Frame = f; ch = true; }
        int kind = ComboField("Kind", (int)a.Kind, ActionNames); if (kind != (int)a.Kind) { a.Kind = (ClipActionKind)kind; ch = true; }
        if (a.Kind is ClipActionKind.GotoAndPlay or ClipActionKind.GotoAndStop or ClipActionKind.Event)
        {
            string lbl = TextField(a.Kind == ClipActionKind.Event ? "Event" : "Label", a.Label, 3000 + _selAction);
            if (!ReferenceEquals(lbl, a.Label)) { a.Label = lbl; ch = true; }
        }
        if (a.Kind is ClipActionKind.GotoAndPlay or ClipActionKind.GotoAndStop)
        {
            int tf = IntField("Frame (alt)", a.TargetFrame, 0, Frames - 1); if (tf != a.TargetFrame) { a.TargetFrame = tf; ch = true; }
            Gui.LayoutLabel("Label bossa Frame kullanilir");
        }
        GuiLayout.Space(6);
        if (Gui.LayoutButton("Delete Action")) _pendDelSel = true;
        if (ch) Commit();
    }

    static bool Same(in Vec4 a, in Vec4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    // ---- klavye ------------------------------------------------------------

    void HandleKeyboard(Event ev)
    {
        // Kisayollar yalniz imlec panel uzerindeyken (IMGUI'de panel odagi yok).
        if (ev.Type != EventType.KeyDown || GuiUtility.KeyboardControl != 0 || !GuiClip.VisibleRect.Contains(ev.MousePosition))
            return;
        bool shift = (ev.Modifiers & EventModifiers.Shift) != 0;
        switch (ev.KeyCode)
        {
            case GLFWConst.KEY_F6:
                if (shift) { if (_selTrack >= 0) { _pendDelKey = true; ev.Use(); } }
                else if (_selTrack >= 0 || _selGroup > 0) { _pendAddKey = true; ev.Use(); }
                break;
            case GLFWConst.KEY_F5: if (shift) _pendDelFrame = true; else _pendInsFrame = true; ev.Use(); break;
            case GLFWConst.KEY_DELETE:
            case GLFWConst.KEY_BACKSPACE:
                if (_selKey >= 0 || _selLabel >= 0 || _selAction >= 0) { _pendDelSel = true; ev.Use(); }
                break;
            case GLFWConst.KEY_LEFT: _playing = false; SetPlayhead(Ph - 1); ev.Use(); break;
            case GLFWConst.KEY_RIGHT: _playing = false; SetPlayhead(Ph + 1); ev.Use(); break;
            case GLFWConst.KEY_HOME: _playing = false; SetPlayhead(0); ev.Use(); break;
            case GLFWConst.KEY_END: _playing = false; SetPlayhead(Frames - 1); ev.Use(); break;
            case GLFWConst.KEY_SPACE: TogglePlay(); ev.Use(); break;
        }
    }
}
