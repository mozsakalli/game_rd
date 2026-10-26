using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// CUE EDITORU â€” Puppet'in "sanatsal metot"larini yazar (ClipEditorPanel'den turetildi; sandbox dilimi 1).
// Sol ust: cue listesi (isimli metotlar) + parametreleri. Orta: secili cue'nun timeline'i
// (hedef GO gruplari â†’ property track'leri; MovieClip ile AYNI veri ve AYNI ornekleyici).
// Sag: secime gore inspector â€” cue (ad/fps/frames/parametreler), track, key. Key degeri
// SABIT ya da bir PARAMETREYE BAGLI olabilir (bagli key mor cizilir; "Deal(to)" tek klip).
// Onizleme argumani = parametrenin Default'u (inspector'da duzenlenir â†’ sahnede aninda gorulur).
//
// Veri akisi: CANLI Puppet mutate â†’ Rebuild â†’ CommitComponent (doc + tek undo) â†’ Sample(cue, playhead).
// Sahne pozu PreviewSession icinde surulur: Stop = doc'tan reload. Rec: Inspector/handle duzenlemesi key olur.
[MenuItem("Window/Cue Editor", 6)]
public sealed class CueEditorPanel : EditorWindow
{
    const float ToolbarH = 26f, RulerH = 20f, ActionRowH = 18f, RowH = 20f;
    const float Sb = 14f, SplitterW = 5f, MinInspectorW = 250f, MinHeaderW = 220f;

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
    static readonly Color ColKeyBound = new(190, 110, 230, 255); // parametreye bagli key
    static readonly Color ColKeyGroup = new(200, 200, 200, 160);
    static readonly Color ColKeySel = new(255, 255, 255, 255);
    static readonly Color ColPlayhead = new(230, 64, 64, 255);
    static readonly Color ColPlayheadCol = new(230, 64, 64, 28);
    static readonly Color ColRecord = new(230, 50, 50, 255);
    static readonly Color ColLine = new(0, 0, 0, 110);
    static readonly Color ColSelRow = new(82, 115, 158, 60);
    static readonly Color ColText = new(215, 218, 228, 255);
    static readonly Color ColTextDim = new(150, 155, 170, 255);
    static readonly Color ColCue = new(240, 190, 60, 255);
    static readonly Color ColAction = new(102, 204, 255, 255);
    static readonly Color ColStripBg = new(41, 46, 54, 255);

    static readonly string[] EaseNames = Enum.GetNames(typeof(Ease));
    static readonly string[] ModeNames = { "Tween", "Hold" };
    static readonly string[] ParamKindNames = Enum.GetNames(typeof(ParamKind));
    static readonly string[] ActionKindNames = Enum.GetNames(typeof(ActionKind));
    const int SelActions = -3; // _selTrack ozel degeri: aksiyon secili

    static readonly int HashRuler = "CUE.Ruler".GetHashCode();
    static readonly int HashLane = "CUE.Lane".GetHashCode();
    static readonly int HashSplit = "CUE.Split".GetHashCode();
    static readonly int HashStrip = "CUE.Strip".GetHashCode();

    EditorScene _es;
    SceneDoc.GoDoc _g;
    SceneDoc.CompDoc _cd;
    Puppet _puppet;
    int _compIndex;
    int _boundId;
    int _cueIdx;

    float _frameW = 12f;
    float _hScroll, _vScroll;
    float _playhead;
    bool _playing, _record;
    double _lastTick;
    float _inspectorW = 300f, _headerW = 280f;

    int _selTrack = -1, _selKey = -1, _selGroup = -1, _selParam = -1, _selAction = -1;

    bool _drag, _dragMoved;
    int _dragTrack, _dragKey, _dragGrabOffset;

    struct Row { public bool Group; public int GoId; public GameObject Go; public int Track; public string Label; }
    readonly List<Row> _rows = new();
    readonly HashSet<int> _collapsed = new();
    readonly Dictionary<GameObject, int> _goIds = new();

    bool _pendAddKey, _pendDelKey, _pendInsFrame, _pendDelFrame, _pendDelSel;
    bool _pendAddCue, _pendDelCue, _pendAddParam, _pendRestart;
    int _pendAddActionFrame = -1;
    int _pendDelParam = -1;
    int _pendDelTrack = -1;
    int _pendAddObjectId, _pendAddPropGo = 0, _pendAddPropComp = -1; string _pendAddPropPath;

    readonly char[] _nameBuf = new char[64];
    int _nameLen, _nameOwner = int.MinValue;
    string[] _propItems = Array.Empty<string>();
    readonly List<(int comp, string path)> _propTargets = new();
    GameObject _propItemsLive;
    int _propItemsGo = -1, _propItemsSig = -1;
    string[] _cueItems = Array.Empty<string>();
    string[] _bindItems = Array.Empty<string>();
    readonly List<int> _bindParams = new();

    public CueEditorPanel()
    {
        Title = "Cue";
        EditorScene.LiveEdited += OnLiveEdited;
    }

    // ---- baglam ------------------------------------------------------------

    Cue C => _puppet.Cues[_cueIdx];
    bool HasCue => _puppet != null && _cueIdx >= 0 && _cueIdx < _puppet.Cues.Count;

    bool Resolve()
    {
        _es = App.EditScene;
        _g = null; _cd = null; _puppet = null;
        if (_es == null)
            return false;
        int found = 0;
        int id = Selection.DocId;
        for (int guard = 0; id != 0 && guard < 64 && found == 0; guard++)
        {
            var g = _es.FindGo(id);
            if (g == null) break;
            if (g.Components.Exists(c => c.Type == "Puppet")) found = id;
            id = g.Parent;
        }
        if (found == 0) found = _boundId;
        if (!Bind(found))
            return false;
        PreviewSession.Pin();
        return true;
    }

    bool Bind(int goId)
    {
        _g = null; _cd = null; _puppet = null;
        if (goId == 0) return false;
        var g = _es.FindGo(goId);
        if (g == null) return false;
        int ci = g.Components.FindIndex(c => c.Type == "Puppet");
        if (ci < 0) return false;
        var p = _es.Live(goId)?.GetComponent<Puppet>();
        if (p == null) return false;
        _g = g; _cd = g.Components[ci]; _compIndex = ci; _puppet = p;
        if (_boundId != goId)
        {
            _boundId = goId;
            _cueIdx = 0;
            _playhead = 0f; _playing = false; _record = false;
            _selTrack = -1; _selKey = -1; _selGroup = -1; _selParam = -1;
            _hScroll = _vScroll = 0f;
        }
        if (_cueIdx >= _puppet.Cues.Count) _cueIdx = Math.Max(0, _puppet.Cues.Count - 1);
        return true;
    }

    bool Previewing => _g != null && PreviewSession.IsOwner(_g.Id, _compIndex);

    void EnsurePreview()
    {
        if (Previewing) return;
        PreviewSession.Begin(_g.Id, _compIndex);
        _puppet.Stop();
    }

    void Sample()
    {
        if (!HasCue) return;
        EnsurePreview();
        SyncPreviewArgs(); // Default ana Inspector'dan da degisebilir; onizleme hep Default'u yansitir
        _puppet.Sample(_cueIdx, _playhead);
    }

    // Rebuild args'i korur; onizleme argumani = Default oldugu icin her commit'te yeniden yazilir.
    void SyncPreviewArgs()
    {
        if (!HasCue) return;
        for (int i = 0; i < C.Params.Count; i++)
            _puppet.SetArg(_cueIdx, i, Puppet.DefaultArg(C.Params[i]));
    }

    void Commit()
    {
        _puppet.Rebuild();
        _es.CommitComponent(_g, _cd);
        Sample();
    }

    // Geri sarma: Reparent geri alinamaz â†’ doc'tan reload (pending), sonraki OnGui yeni playhead'i ornekler.
    void SetPlayhead(float f)
    {
        float nf = Math.Clamp(f, 0f, Math.Max(0, Frames - 1));
        bool back = (int)nf < Ph;
        _playhead = nf;
        if (back && HasCue && Previewing && _puppet.HasReparent(_cueIdx)) { _pendRestart = true; return; }
        Sample();
    }

    int Ph => (int)_playhead;
    int Frames => HasCue ? Math.Max(1, C.Frames) : 1;
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

    static string TrackLabel(CueTrack t)
        => (t.Node == null ? "?" : t.Node is Transform ? "Transform" : t.Node.GetType().Name) + "." + t.Property;

    void BuildRows()
    {
        _rows.Clear();
        _goIds.Clear();
        if (!HasCue) return;
        var tracks = C.Tracks;
        var seen = new List<GameObject>();
        for (int i = 0; i < tracks.Count; i++)
        {
            var go = tracks[i].Node?.gameObject;
            if (go == null || seen.Contains(go)) continue;
            seen.Add(go);
            int id = DocIdOf(go);
            _rows.Add(new Row { Group = true, Go = go, GoId = id, Track = -1, Label = go.name });
            if (_collapsed.Contains(id)) continue;
            for (int j = 0; j < tracks.Count; j++)
                if (tracks[j].Node?.gameObject == go)
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
            if (_puppet == null) return;
            if (_playing) TickPlay();
            else if (Previewing) Sample();
        }
        ClampSelection();
        BuildRows();

        DrawToolbar(new Rect(0, 0, area.width, ToolbarH));
        if (!HasCue)
        {
            if (ev.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(8, ToolbarH + 8, area.width - 16, 20), "Bu puppet'in cue'su yok: toolbar'dan + Cue.", Gui.FontSize - 2f, ColTextDim);
            return;
        }

        float inspectorW = Math.Clamp(_inspectorW, MinInspectorW, MathF.Max(MinInspectorW, area.width - 340));
        _inspectorW = inspectorW;
        float splitX = area.width - inspectorW - SplitterW;
        float headerW = Math.Clamp(_headerW, MinHeaderW, MathF.Max(MinHeaderW, splitX - 160));
        _headerW = headerW;
        float top = ToolbarH;
        float laneW = splitX - headerW - Sb;

        var ruler = new Rect(headerW, top, laneW, RulerH);
        var actionStrip = new Rect(headerW, top + RulerH, laneW, ActionRowH);
        float areaTop = top + RulerH + ActionRowH;
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
            GuiRenderer.DrawTextIn(new Rect(6, top, headerW - 12, RulerH), C.Name + "(" + ParamSignature() + ")", Gui.FontSize - 3f, ColCue);
            GuiRenderer.DrawTextIn(new Rect(6, top + RulerH, headerW - 12, ActionRowH), "Actions  (cift tik: ekle)", Gui.FontSize - 3f, ColTextDim);
        }

        DrawRuler(ruler);
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

    string ParamSignature()
    {
        var ps = C.Params;
        if (ps.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(ps[i].Name).Append(": ").Append(ps[i].Kind);
        }
        return sb.ToString();
    }

    void ClampSelection()
    {
        if (!HasCue) { _selTrack = -1; _selKey = -1; _selParam = -1; _selAction = -1; return; }
        if (_selTrack >= C.Tracks.Count) { _selTrack = -1; _selKey = -1; }
        if (_selTrack >= 0 && _selKey >= C.Tracks[_selTrack].Keys.Count) _selKey = -1;
        if (_selParam >= C.Params.Count) _selParam = -1;
        if (_selAction >= C.Actions.Count) _selAction = -1;
    }

    void DrawEmptyState(in Rect area)
    {
        var ev = Event.Current;
        RefreshPuppetItems();
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, 4, area.width - 16, 20),
                _puppetItems.Length > 1 ? "Puppet sec:" : "Sahnede Puppet yok.", Gui.FontSize - 2f, ColTextDim);
        if (_puppetItems.Length > 1)
        {
            int pick = Gui.ComboBox(new Rect(90, 4, 200, 20), 0, _puppetItems);
            if (pick > 0 && pick < _puppetIds.Count) { _boundId = _puppetIds[pick]; Selection.DocId = _boundId; }
        }
        var sel = _es?.FindGo(Selection.DocId);
        if (sel != null && Gui.Button(new Rect(8, 30, 260, 22), "Secili nesneye Puppet ekle: " + sel.Name))
        {
            _es.AddComponent(sel, "Puppet");
            _boundId = sel.Id;
        }
        else if (sel == null && ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, 30, area.width - 16, 20),
                "Hierarchy'de prefab kokunu sec â†’ Puppet ekle â†’ + Cue â†’ nesneleri panele surukle.", Gui.FontSize - 3f, ColTextDim);
    }

    string[] _puppetItems = Array.Empty<string>();
    readonly List<int> _puppetIds = new();

    void RefreshPuppetItems()
    {
        _puppetIds.Clear();
        var list = new List<string> { _g != null ? _g.Name : "Puppet..." };
        _puppetIds.Add(0);
        if (_es == null) return;
        foreach (var o in _es.Doc.Objects)
            if (o.Components.Exists(c => c.Type == "Puppet") && o.Id != _boundId) { _puppetIds.Add(o.Id); list.Add(o.Name); }
        if (_puppetItems.Length != list.Count) _puppetItems = new string[list.Count];
        for (int i = 0; i < list.Count; i++) _puppetItems[i] = list[i];
    }

    void RefreshCueItems()
    {
        int n = _puppet.Cues.Count;
        if (_cueItems.Length != n) _cueItems = new string[n];
        for (int i = 0; i < n; i++) _cueItems[i] = _puppet.Cues[i].Name;
    }

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
        // Cue gecisi: onceki cue'nun izleri (dokunmadigi property'ler) kalmasin â†’ doc'tan reload, sonra yeni cue orneklenir.
        if (_pendRestart)
        {
            _pendRestart = false;
            if (Previewing) { PreviewSession.Restart(); return; } // canli nesneler degisti; sonraki OnGui yeniden baglanir
        }
        if (_pendAddCue)
        {
            _pendAddCue = false;
            _puppet.Cues.Add(new Cue { Name = UniqueCueName("Cue") });
            _cueIdx = _puppet.Cues.Count - 1;
            _playhead = 0; _selTrack = -1; _selKey = -1; _selGroup = -1; _selParam = -1;
            changed = true;
        }
        if (_pendDelCue && HasCue)
        {
            _pendDelCue = false;
            _puppet.Cues.RemoveAt(_cueIdx);
            _cueIdx = Math.Max(0, _cueIdx - 1);
            _selTrack = -1; _selKey = -1; _selGroup = -1; _selParam = -1;
            changed = true;
        }
        _pendDelCue = false;
        if (!HasCue) { if (changed) Commit(); return; }
        if (_pendAddParam)
        {
            _pendAddParam = false;
            C.Params.Add(new CueParam { Name = "p" + C.Params.Count, Kind = ParamKind.Float });
            _selParam = C.Params.Count - 1;
            changed = true;
        }
        if (_pendDelParam >= 0 && _pendDelParam < C.Params.Count)
        {
            int pi = _pendDelParam;
            C.Params.RemoveAt(pi);
            // Parametreye isaret eden key/aksiyonlar: silinen → Const/sabit, sonrakiler kayar.
            foreach (var t in C.Tracks)
                foreach (var k in t.Keys)
                    if (k.Source == KeySource.Param) { if (k.Param == pi) k.Source = KeySource.Const; else if (k.Param > pi) k.Param--; }
            foreach (var a in C.Actions)
                if (a.Param == pi) a.Param = -1; else if (a.Param > pi) a.Param--;
            _selParam = -1;
            changed = true;
        }
        _pendDelParam = -1;
        if (_pendAddActionFrame >= 0)
        {
            C.Actions.Add(new CueAction { Frame = _pendAddActionFrame, Kind = ActionKind.Event, Label = "event" });
            _selAction = C.Actions.Count - 1; _selTrack = SelActions; _selKey = -1; _selGroup = -1; _selParam = -1;
            _pendAddActionFrame = -1;
            changed = true;
        }
        if (_pendAddObjectId != 0)
        {
            int id = _pendAddObjectId; _pendAddObjectId = 0;
            var tr = _es.Live(id)?.transform;
            if (tr != null) { AddTrack(tr, "Position", keyNow: true); changed = true; }
        }
        if (_pendAddPropGo != 0)
        {
            var go = _es.Live(_pendAddPropGo);
            var c = go != null && _pendAddPropComp >= 0 && _pendAddPropComp < go.ComponentCount ? go.ComponentAt(_pendAddPropComp) : null;
            string p = _pendAddPropPath;
            _pendAddPropGo = 0; _pendAddPropComp = -1; _pendAddPropPath = null;
            if (c != null && !c._destroyed) { AddTrack(c, p, keyNow: true); changed = true; }
        }
        if (_pendDelTrack >= 0 && _pendDelTrack < C.Tracks.Count)
        {
            C.Tracks.RemoveAt(_pendDelTrack);
            _selTrack = -1; _selKey = -1;
            changed = true;
        }
        _pendDelTrack = -1;
        if (_pendAddKey)
        {
            _pendAddKey = false;
            if (_selTrack >= 0) { InsertKey(_selTrack, Ph); changed = true; }
            else if (_selGroup != 0 && _selGroup != -1)
            {
                for (int i = 0; i < C.Tracks.Count; i++)
                    if (DocIdOf(C.Tracks[i].Node?.gameObject) == _selGroup) { InsertKey(i, Ph); changed = true; }
                _selTrack = -1; _selKey = -1;
            }
        }
        if (_pendDelKey)
        {
            _pendDelKey = false;
            if (_selTrack >= 0)
            {
                var keys = C.Tracks[_selTrack].Keys;
                int idx = _selKey >= 0 ? _selKey : keys.FindIndex(k => k.Frame == Ph);
                if (idx >= 0) { keys.RemoveAt(idx); _selKey = -1; changed = true; }
            }
        }
        if (_pendInsFrame) { _pendInsFrame = false; ShiftFrames(Ph, +1); changed = true; }
        if (_pendDelFrame) { _pendDelFrame = false; if (Frames > 1) { ShiftFrames(Ph, -1); changed = true; } }
        if (_pendDelSel)
        {
            _pendDelSel = false;
            if (_selTrack == SelActions && _selAction >= 0) { C.Actions.RemoveAt(_selAction); _selAction = -1; _selTrack = -1; changed = true; }
            else if (_selTrack >= 0 && _selKey >= 0) { C.Tracks[_selTrack].Keys.RemoveAt(_selKey); _selKey = -1; changed = true; }
        }
        if (changed)
            Commit();
    }

    string UniqueCueName(string baseName)
    {
        string n = baseName;
        for (int i = 1; _puppet.Cues.Exists(c => c.Name == n); i++) n = baseName + i;
        return n;
    }

    CueTrack AddTrack(Component target, string path, bool keyNow)
    {
        int idx = C.Tracks.FindIndex(t => t.Node == target && t.Property == path);
        if (idx < 0)
        {
            var track = new CueTrack { Node = target, Property = path };
            int insert = C.Tracks.Count;
            for (int i = C.Tracks.Count - 1; i >= 0; i--)
                if (C.Tracks[i].Node?.gameObject == target.gameObject) { insert = i + 1; break; }
            C.Tracks.Insert(insert, track);
            idx = insert;
            if (keyNow && C.Tracks[idx].Keys.Count == 0 && Ph != 0)
                InsertKey(idx, 0);
        }
        if (keyNow) InsertKey(idx, Ph);
        _selTrack = idx;
        _selGroup = -1;
        return C.Tracks[idx];
    }

    void InsertKey(int track, int frame)
    {
        var T = C.Tracks[track];
        int existing = T.Keys.FindIndex(k => k.Frame == frame);
        var prop = _puppet.ResolveProperty(T);
        CueKey nk;
        if (existing >= 0) { nk = T.Keys[existing]; _selTrack = track; _selKey = existing; }
        else
        {
            nk = new CueKey { Frame = frame };
            CueKey prev = null;
            foreach (var k in T.Keys)
                if (k.Frame < frame && (prev == null || k.Frame > prev.Frame)) prev = k;
            if (prev != null) { nk.Mode = prev.Mode; nk.Ease = prev.Ease; nk.Value = prev.Value; nk.Ref = prev.Ref; }
            int pos = 0;
            while (pos < T.Keys.Count && T.Keys[pos].Frame < frame) pos++;
            T.Keys.Insert(pos, nk);
            _selTrack = track; _selKey = pos;
        }
        if (prop == null || prop.Kind == AnimKind.Trigger) return;
        if (nk.Source != KeySource.Const) return; // degeri parametreden/Current'tan gelen key'e yazilmaz
        if (prop.Get != null && T.Node != null && !T.Node._destroyed)
            nk.SetAnim(prop.Get(T.Node));
    }

    void ShiftFrames(int from, int delta)
    {
        foreach (var T in C.Tracks)
        {
            if (delta < 0)
                T.Keys.RemoveAll(k => k.Frame == from + 1 && T.Keys.Exists(o => o.Frame == from));
            foreach (var k in T.Keys)
                if (delta > 0 ? k.Frame >= from : k.Frame > from) k.Frame += delta;
        }
        foreach (var a in C.Actions) if (delta > 0 ? a.Frame >= from : a.Frame > from) a.Frame += delta;
        C.Frames = Math.Max(1, C.Frames + delta);
    }

    // ---- record ------------------------------------------------------------

    readonly List<(Component target, string path, AnimValue before)> _preEdit = new();

    void OnLiveEdited(int goId, int compIndex, string path, bool after)
    {
        if (!_record || _puppet == null || _es == null || _g == null || !HasCue)
            return;
        var gd = _es.FindGo(goId);
        var live = _es.Live(goId);
        if (gd == null || live == null) return;
        Component target;
        if (compIndex < 0) target = live.transform;
        else if (compIndex < gd.Components.Count) target = _es.FindLiveComponent(gd, gd.Components[compIndex]);
        else return;
        if (target == null || target == _puppet) return;

        if (!after)
        {
            foreach (var p in MatchingProps(target, path))
                if (p.Get != null && !C.Tracks.Exists(t => t.Node == target && t.Property == p.Path)
                    && !_preEdit.Exists(e => e.target == target && e.path == p.Path))
                    _preEdit.Add((target, p.Path, p.Get(target)));
            return;
        }

        bool changed = false;
        foreach (var p in MatchingProps(target, path))
        {
            bool exact = p.Path == path || path == "Enabled";
            int idx = C.Tracks.FindIndex(t => t.Node == target && t.Property == p.Path);
            int pre = _preEdit.FindIndex(e => e.target == target && e.path == p.Path);
            if (idx < 0 && !exact) { if (pre >= 0) _preEdit.RemoveAt(pre); continue; }
            if (idx < 0)
            {
                var track = AddTrack(target, p.Path, keyNow: true);
                if (pre >= 0 && Ph != 0)
                {
                    var k0 = track.Keys.Find(k => k.Frame == 0);
                    k0?.SetAnim(_preEdit[pre].before);
                }
            }
            else InsertKey(idx, Ph);
            if (pre >= 0) _preEdit.RemoveAt(pre);
            changed = true;
        }
        if (changed) Commit();
    }

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
        // Cue secici + ekle/sil (kontrol sayisi sabit: cue yokken combo tek ogeli).
        RefreshCueItems();
        if (_cueItems.Length > 0)
        {
            int pick = Gui.ComboBox(new Rect(x, 3, 130, 20), _cueIdx, _cueItems);
            if (pick != _cueIdx && pick >= 0 && pick < _cueItems.Length)
            {
                _cueIdx = pick; _playhead = 0; _selTrack = -1; _selKey = -1; _selGroup = -1; _selParam = -1;
                _playing = false;
                _pendRestart = true;
            }
        }
        else if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(x, 3, 130, 20), "(cue yok)", Gui.FontSize - 2f, ColTextDim);
        x += 134;
        if (Gui.Button(new Rect(x, 3, 52, 20), "+ Cue")) _pendAddCue = true;
        x += 56;
        if (Gui.Button(new Rect(x, 3, 24, 20), "x") && HasCue) _pendDelCue = true;
        x += 32;

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
            if (Previewing) PreviewSession.End();
        }
        x += 44;
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
        if (HasCue)
        {
            Gui.Label(new Rect(x, 3, 26, 20), "Fps");
            x += 26;
            int fps = Gui.DragInt(new Rect(x, 4, 40, 18), C.Fps, 0.1f, 1, 240);
            if (fps != C.Fps) { C.Fps = fps; Commit(); }
            x += 46;
            Gui.Label(new Rect(x, 3, 46, 20), "Frames");
            x += 48;
            int fc = Gui.DragInt(new Rect(x, 4, 46, 18), C.Frames, 0.2f, 1, 100000);
            if (fc != C.Frames) { C.Frames = fc; Commit(); }
            x += 52;
        }
        else x += 172;
        _frameW = Gui.HorizontalSlider(new Rect(x, 6, 70, 14), _frameW, 5f, 32f);
        x += 78;
        RefreshPuppetItems();
        int pp = Gui.ComboBox(new Rect(x, 3, 120, 20), 0, _puppetItems);
        if (pp > 0 && pp < _puppetIds.Count) { Bind(_puppetIds[pp]); Selection.DocId = _boundId; }
        x += 124;
        int selId = Selection.DocId;
        bool canAdd = selId != 0 && _es.Live(selId) != null && HasCue;
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
        if (!HasCue) return;
        _playing = !_playing;
        if (_playing) { EnsurePreview(); _lastTick = GLFW.GetTime(); if (Ph >= Frames - 1) _playhead = 0; }
    }

    void TickPlay()
    {
        double now = GLFW.GetTime();
        float dt = (float)(now - _lastTick);
        _lastTick = now;
        _playhead += dt * C.Fps;
        float end = Frames - 1;
        if (_playhead > end) { _playhead = end; _playing = false; }
        Sample();
    }

    void RefreshPropItems(GameObject go, int goId)
    {
        int sig = go.ComponentCount * 1000 + C.Tracks.Count + _cueIdx * 100000;
        if (_propItemsGo == goId && _propItemsSig == sig && ReferenceEquals(_propItemsLive, go)) return;
        _propItemsGo = goId; _propItemsSig = sig; _propItemsLive = go;
        _propTargets.Clear();
        var list = new List<string> { "+ Property..." };
        _propTargets.Add((-1, null));
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var c = go.ComponentAt(i);
            if (c == null || c._destroyed || c == _puppet) continue;
            string tn = c is Transform ? "Transform" : c.GetType().Name;
            var props = AnimRegistry.PropsOf(Catalog, c.GetType());
            for (int p = 0; p < props.Length; p++)
            {
                if (props[p].Kind == AnimKind.Trigger) continue; // dilim 1: tetik yok
                if (C.Tracks.Exists(t => t.Node == c && t.Property == props[p].Path)) continue;
                _propTargets.Add((i, props[p].Path));
                list.Add(tn + "." + props[p].Path);
            }
            if (!(c is Transform) && !C.Tracks.Exists(t => t.Node == c && t.Property == "Enabled"))
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
                            (f + 1).TryFormat(tmp, out int n);
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

    // ---- aksiyon seridi ----------------------------------------------------

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
                    int hit = C.Actions.FindIndex(a => a.Frame == f);
                    if (hit >= 0) { _selTrack = SelActions; _selAction = hit; _selKey = -1; _selGroup = -1; _selParam = -1; }
                    else if (ev.ClickCount == 2 && f >= 0 && f < Frames) _pendAddActionFrame = f;
                    else { _playing = false; SetPlayhead(f); }
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                GuiRenderer.DrawRect(local, ColStripBg);
                DrawGrid(local);
                for (int i = 0; i < C.Actions.Count; i++)
                {
                    bool sel = _selTrack == SelActions && _selAction == i;
                    bool bound = C.Actions[i].Param >= 0;
                    GuiRenderer.DrawDiamond(new Vec2(FrameToX(C.Actions[i].Frame + 0.5f), cy), 5f, sel ? ColKeySel : bound ? ColKeyBound : ColAction, 3);
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
        var tracks = C.Tracks;

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
                    _selParam = -1; _selAction = -1;
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
                            _puppet.Rebuild();
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
            foreach (var t in C.Tracks)
                if (t.Node?.gameObject == r.Go && t.Keys.Exists(k => k.Frame == f)) { any = true; break; }
            if (any) GuiRenderer.DrawDiamond(new Vec2(x + _frameW * 0.5f, cy), rad, ColKeyGroup, 3);
        }
        GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, width, 1), ColLine, 1);
    }

    void DrawTrackRow(CueTrack T, int index, float rowY, float width)
    {
        GuiRenderer.DrawRect(new Rect(0, rowY, FrameToX(Frames), RowH), ColLaneBg);
        if (index == _selTrack)
            GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH), ColSelRow, 1);
        var keys = T.Keys;
        var prop = _puppet.ResolveProperty(T);
        for (int k = 0; k < keys.Count; k++)
        {
            var key = keys[k];
            int end = k + 1 < keys.Count ? keys[k + 1].Frame : Frames;
            float x0 = FrameToX(key.Frame), x1 = FrameToX(end);
            if (x1 < 0 || x0 > width) continue;
            bool tween = key.Mode == KeyMode.Tween && k + 1 < keys.Count;
            if (end > key.Frame + 1 || tween)
                GuiRenderer.DrawRect(new Rect(x0 + 1, rowY + 3, x1 - x0 - 2, RowH - 7), tween ? ColTween : ColHold, 1);
            if (tween)
                GuiRenderer.DrawRect(new Rect(x0 + _frameW * 0.5f, rowY + RowH * 0.5f, x1 - x0 - _frameW * 0.5f - 3, 1), ColText, 2);
            float cx = x0 + _frameW * 0.5f, cy = rowY + RowH * 0.5f;
            bool sel = index == _selTrack && k == _selKey;
            bool bound = key.Source != KeySource.Const;
            float r = MathF.Min(5f, _frameW * 0.4f);
            if (sel) GuiRenderer.DrawDiamond(new Vec2(cx, cy), r + 2.5f, ColKeySel, 2);
            GuiRenderer.DrawDiamond(new Vec2(cx, cy), r, bound ? ColKeyBound : ColKey, 3);
        }
        GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, width, 1), ColLine, 1);
        if (prop == null)
            GuiRenderer.DrawRect(new Rect(0, rowY, width, RowH - 1), new Color(200, 40, 40, 50), 2);
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
                    GuiRenderer.DrawTextIn(new Rect(22, rowY, header.width - 22 - 140, RowH), r.Label, Gui.FontSize - 2f, ColText);
                    GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, header.width, 1), ColLine, 1);
                }
                if (Gui.Button(new Rect(2, lineY, 16, 16), collapsed ? ">" : "v"))
                {
                    if (!_collapsed.Remove(r.GoId)) _collapsed.Add(r.GoId);
                }
                if (ev.Type == EventType.MouseDown && GuiUtility.HotControl == 0 && visible && new Rect(20, rowY, header.width - 20 - 140, RowH).Contains(ev.MousePosition))
                {
                    _selGroup = r.GoId; _selTrack = -1; _selKey = -1; _selParam = -1;
                    Selection.DocId = r.GoId;
                    ev.Use();
                }
                RefreshPropItems(r.Go, r.GoId);
                int pick = Gui.ComboBox(new Rect(header.width - 136, lineY, 132, 16), 0, _propItems);
                if (pick > 0 && pick < _propTargets.Count) { _pendAddPropGo = r.GoId; _pendAddPropComp = _propTargets[pick].comp; _pendAddPropPath = _propTargets[pick].path; }
            }
            else
            {
                if (ev.Type == EventType.Repaint && visible)
                {
                    if (r.Track == _selTrack)
                        GuiRenderer.DrawRect(new Rect(0, rowY, header.width, RowH), new Color(76, 102, 140, 90), 1);
                    var prop = _puppet.ResolveProperty(C.Tracks[r.Track]);
                    GuiRenderer.DrawTextIn(new Rect(26, rowY, header.width - 26 - 22, RowH), r.Label, Gui.FontSize - 3f,
                        prop == null ? ColPlayhead : ColText);
                    GuiRenderer.DrawRect(new Rect(0, rowY + RowH - 1, header.width, 1), ColLine, 1);
                }
                if (ev.Type == EventType.MouseDown && GuiUtility.HotControl == 0 && visible && new Rect(0, rowY, header.width - 22, RowH).Contains(ev.MousePosition))
                {
                    _selTrack = r.Track; _selKey = -1; _selGroup = -1; _selParam = -1;
                    ev.Use();
                }
                if (Gui.Button(new Rect(header.width - 18, lineY, 16, 16), "x")) _pendDelTrack = r.Track;
            }
        }
        GuiClip.Pop();
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

        if (_selParam >= 0 && _selParam < C.Params.Count)
            InspectParam(C.Params[_selParam], _selParam);
        else if (_selTrack == SelActions && _selAction >= 0 && _selAction < C.Actions.Count)
            InspectAction(C.Actions[_selAction], _selAction);
        else if (_selTrack >= 0 && _selKey >= 0)
            InspectKey(C.Tracks[_selTrack], _selTrack, _selKey);
        else if (_selTrack >= 0)
            InspectTrack(C.Tracks[_selTrack], _selTrack);
        else
            InspectCue();

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

    // Parametre degeri (Kind'a gore satir): Default alanini duzenler.
    Vec4 ParamValueField(string label, ParamKind kind, Vec4 v, out bool changed)
    {
        changed = false;
        switch (kind)
        {
            case ParamKind.Float: { float nv = FloatField(label, v.x, 0.05f); if (nv != v.x) { v.x = nv; changed = true; } break; }
            case ParamKind.Bool: { bool nv = ToggleField(label, v.x != 0f); if (nv != (v.x != 0f)) { v.x = nv ? 1f : 0f; changed = true; } break; }
            case ParamKind.Vec2: { var nv = VecField(label, v, 2, 0.5f); if (!Same(nv, v)) { v = nv; changed = true; } break; }
            case ParamKind.Vec3: { var nv = VecField(label, v, 3, 0.5f); if (!Same(nv, v)) { v = nv; changed = true; } break; }
            case ParamKind.Color: { var nv = VecField(label, v, 4, 1f, 0f, 255f); if (!Same(nv, v)) { v = nv; changed = true; } break; }
        }
        return v;
    }

    void InspectCue()
    {
        Gui.LayoutLabel("Cue  (sanatsal metot)");
        GuiLayout.Space(4);
        bool ch = false;
        string name = TextField("Name", C.Name, 1000 + _cueIdx);
        if (!ReferenceEquals(name, C.Name) && name.Length > 0 && !_puppet.Cues.Exists(c => c != C && c.Name == name)) { C.Name = name; ch = true; }
        int fps = IntField("Fps", C.Fps, 1, 240); if (fps != C.Fps) { C.Fps = fps; ch = true; }
        int fc = IntField("Frames", C.Frames, 1, 100000); if (fc != C.Frames) { C.Frames = fc; ch = true; }
        GuiLayout.Space(8);
        Gui.LayoutLabel("Parameters  (onizleme degeri = Default)");
        for (int i = 0; i < C.Params.Count; i++)
        {
            var p = C.Params[i];
            var r = Row_();
            if (Gui.Button(new Rect(r.x, r.y + 1, r.width - 24, r.height - 2), p.Name + ": " + p.Kind)) _selParam = i;
            if (Gui.Button(new Rect(r.x + r.width - 20, r.y + 1, 18, r.height - 2), "x")) _pendDelParam = i;
        }
        if (Gui.LayoutButton("+ Parameter")) _pendAddParam = true;
        GuiLayout.Space(8);
        Gui.LayoutLabel("Hierarchy'den nesneyi panele surukle (veya + Selected)");
        Gui.LayoutLabel("Key sec â†’ 'Bind to' ile parametreye bagla (mor key)");
        Gui.LayoutLabel("Rec: Inspector/handle duzenlemesi = key");
        Gui.LayoutLabel("F6 key, Shift+F6 sil, F5/Shift+F5 frame, Space oynat");
        if (ch) Commit();
    }

    // Transform referansi satiri: ad + "Use Selected" (Hierarchy secimi) + temizle. Donus: degisti mi.
    bool TransformField(string label, ref Transform t)
    {
        var r = Row_();
        Gui.Label(new Rect(r.x, r.y, 70, r.height), label);
        Gui.Label(new Rect(r.x + 72, r.y, r.width - 72 - 110, r.height), t == null ? "(none)" : t._destroyed ? "(destroyed)" : t.gameObject.name);
        bool ch = false;
        int selId = Selection.DocId;
        var live = selId != 0 ? _es.Live(selId) : null;
        if (Gui.Button(new Rect(r.x + r.width - 106, r.y + 1, 84, r.height - 2), "Use Selected") && live != null && live.transform != t)
        { t = live.transform; ch = true; }
        if (Gui.Button(new Rect(r.x + r.width - 20, r.y + 1, 18, r.height - 2), "x") && t != null) { t = null; ch = true; }
        return ch;
    }

    void InspectAction(CueAction a, int index)
    {
        Gui.LayoutLabel("Action  (frame'e giriste bir kez)");
        GuiLayout.Space(4);
        bool ch = false;
        int f = IntField("Frame", a.Frame, 0, Frames - 1); if (f != a.Frame) { a.Frame = f; ch = true; }
        int kind = ComboField("Kind", (int)a.Kind, ActionKindNames); if (kind != (int)a.Kind) { a.Kind = (ActionKind)kind; ch = true; }
        GuiLayout.Space(4);
        if (a.Kind == ActionKind.Reparent)
        {
            Gui.LayoutLabel("Node â†’ Target'in cocugu olur; sonraki key'ler YENI ebeveynin yerel uzayinda");
            var node = a.Node; if (TransformField("Node", ref node)) { a.Node = node; ch = true; }
            if (a.Node == null) Gui.LayoutLabel("  (bos = puppet koku)");
            // Target: parametreden mi, sabit mi (Action.Param alani).
            RefreshBindItems(ParamKind.Target);
            int cur = Math.Max(0, _bindParams.IndexOf(a.Param));
            int pick = ComboField("Target", cur, _bindItems);
            if (pick != cur && pick >= 0 && pick < _bindParams.Count) { a.Param = _bindParams[pick]; ch = true; }
            if (a.Param >= 0 && a.Param < C.Params.Count)
            {
                var p = C.Params[a.Param];
                Gui.LayoutLabel("Hedef $" + p.Name + " parametresinden (onizleme: Default)");
                var t = p.DefaultTarget; if (TransformField("Preview", ref t)) { p.DefaultTarget = t; ch = true; }
            }
            else
            {
                var t = a.Target; if (TransformField("Fixed", ref t)) { a.Target = t; ch = true; }
            }
            bool kw = ToggleField("Keep World", a.KeepWorld); if (kw != a.KeepWorld) { a.KeepWorld = kw; ch = true; }
        }
        else
        {
            string lbl = TextField("Event", a.Label, 5000 + _cueIdx * 100 + index);
            if (!ReferenceEquals(lbl, a.Label)) { a.Label = lbl; ch = true; }
            Gui.LayoutLabel("ICueHandler.OnCueEvent / Puppet.CueEvent (ClipHash)");
        }
        GuiLayout.Space(6);
        if (Gui.LayoutButton("Delete Action")) _pendDelSel = true;
        if (ch) Commit();
    }

    // Parametre turleri icin bagla listesi (aksiyon Target'i).
    void RefreshBindItems(ParamKind kind)
    {
        _bindParams.Clear();
        var list = new List<string> { "(fixed)" };
        _bindParams.Add(-1);
        for (int i = 0; i < C.Params.Count; i++)
            if (C.Params[i].Kind == kind) { list.Add("$" + C.Params[i].Name); _bindParams.Add(i); }
        if (_bindItems.Length != list.Count) _bindItems = new string[list.Count];
        for (int i = 0; i < list.Count; i++) _bindItems[i] = list[i];
    }

    void InspectParam(CueParam p, int index)
    {
        Gui.LayoutLabel("Parameter");
        GuiLayout.Space(4);
        bool ch = false;
        string name = TextField("Name", p.Name, 4000 + _cueIdx * 100 + index);
        if (!ReferenceEquals(name, p.Name) && name.Length > 0) { p.Name = name; ch = true; }
        int kind = ComboField("Kind", (int)p.Kind, ParamKindNames); if (kind != (int)p.Kind) { p.Kind = (ParamKind)kind; ch = true; }
        if (p.Kind == ParamKind.Target)
        {
            var t = p.DefaultTarget; if (TransformField("Default", ref t)) { p.DefaultTarget = t; ch = true; }
            Gui.LayoutLabel("Hierarchy'de hedefi sec â†’ Use Selected (onizleme hedefi)");
        }
        else
        {
            var d = ParamValueField("Default", p.Kind, p.Default, out bool dch); if (dch) { p.Default = d; ch = true; }
        }
        GuiLayout.Space(4);
        int uses = 0;
        foreach (var t in C.Tracks) foreach (var kk in t.Keys) if (kk.Source == KeySource.Param && kk.Param == index) uses++;
        foreach (var a in C.Actions) if (a.Param == index) uses++;
        Gui.LayoutLabel("Bagli key/aksiyon: " + uses);
        GuiLayout.Space(6);
        if (Gui.LayoutButton("Back to Cue")) _selParam = -1;
        if (Gui.LayoutButton("Delete Parameter")) _pendDelParam = index;
        if (ch) Commit();
    }

    void InspectTrack(CueTrack T, int index)
    {
        Gui.LayoutLabel("Track");
        GuiLayout.Space(4);
        var prop = _puppet.ResolveProperty(T);
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

    // Property turune uyan parametre turleri (bagla combo'su).
    static bool Compatible(AnimKind prop, ParamKind param) => prop switch
    {
        AnimKind.Float or AnimKind.Int => param == ParamKind.Float,
        AnimKind.Bool => param == ParamKind.Bool,
        AnimKind.Vec2 => param == ParamKind.Vec2,
        AnimKind.Vec3 => param == ParamKind.Vec3,
        AnimKind.Vec4 => param == ParamKind.Color,
        AnimKind.Color => param == ParamKind.Color,
        _ => false,
    };

    // Key "Bind to" listesi: -2 = Const, -1 = Current, >=0 = parametre (tur-uyumlu).
    void RefreshBindItems(AnimKind kind)
    {
        _bindParams.Clear();
        var list = new List<string> { "(constant)", "(current at start)" };
        _bindParams.Add(-2);
        _bindParams.Add(-1);
        for (int i = 0; i < C.Params.Count; i++)
            if (Compatible(kind, C.Params[i].Kind)) { list.Add("$" + C.Params[i].Name); _bindParams.Add(i); }
        if (_bindItems.Length != list.Count) _bindItems = new string[list.Count];
        for (int i = 0; i < list.Count; i++) _bindItems[i] = list[i];
    }

    void InspectKey(CueTrack T, int trackIndex, int keyIndex)
    {
        var k = T.Keys[keyIndex];
        var prop = _puppet.ResolveProperty(T);
        Gui.LayoutLabel("Keyframe  " + TrackLabel(T));
        GuiLayout.Space(4);
        bool ch = false;
        int f = IntField("Frame", k.Frame, 0, Frames - 1);
        if (f != k.Frame && !T.Keys.Exists(o => o != k && o.Frame == f))
        {
            k.Frame = f; T.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame)); _selKey = T.Keys.IndexOf(k); _playhead = f; ch = true;
        }
        int mode = ComboField("Mode", (int)k.Mode, ModeNames); if (mode != (int)k.Mode) { k.Mode = (KeyMode)mode; ch = true; }
        int ease = ComboField("Ease", (int)k.Ease, EaseNames); if (ease != (int)k.Ease) { k.Ease = (Ease)ease; ch = true; }
        GuiLayout.Space(4);
        if (prop != null)
        {
            // Degerin kaynagi: sabit / Play aninda mevcut / parametre — key'in kendi alani.
            RefreshBindItems(prop.Kind);
            int curSel = k.Source == KeySource.Const ? -2 : k.Source == KeySource.Current ? -1 : k.Param;
            int cur = Math.Max(0, _bindParams.IndexOf(curSel));
            int pick = ComboField("Bind to", cur, _bindItems);
            if (pick != cur && pick >= 0 && pick < _bindParams.Count)
            {
                int np = _bindParams[pick];
                if (np == -2) k.Source = KeySource.Const;
                else if (np == -1) k.Source = KeySource.Current;
                else { k.Source = KeySource.Param; k.Param = np; }
                ch = true;
            }
            if (k.Source == KeySource.Current)
                Gui.LayoutLabel("Deger Play aninda property'den yakalanir (reparent sonrasi)");
            else if (k.Source == KeySource.Param && k.Param >= 0 && k.Param < C.Params.Count)
            {
                var p = C.Params[k.Param];
                Gui.LayoutLabel("Deger $" + p.Name + " parametresinden gelir (onizleme: Default)");
                var d = ParamValueField("Preview", p.Kind, p.Default, out bool dch);
                if (dch) { p.Default = d; ch = true; }
            }
            else
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
                            int curE = (int)MathF.Round(v.x);
                            int nv = ComboField("Value", curE, names); if (nv != curE && nv >= 0) { v.x = nv; ch = true; }
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
                            break;
                        }
                }
                k.Value = v;
            }
        }
        GuiLayout.Space(6);
        if (prop != null && k.Source == KeySource.Const && Gui.LayoutButton("Capture From Stage"))
        {
            if (prop.Get != null && T.Node != null) { k.SetAnim(prop.Get(T.Node)); ch = true; }
        }
        if (Gui.LayoutButton("Delete Keyframe")) _pendDelSel = true;
        if (ch) Commit();
    }

    static bool Same(in Vec4 a, in Vec4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    // ---- klavye ------------------------------------------------------------

    void HandleKeyboard(Event ev)
    {
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
                if (_selKey >= 0 || (_selTrack == SelActions && _selAction >= 0)) { _pendDelSel = true; ev.Use(); }
                break;
            case GLFWConst.KEY_LEFT: _playing = false; SetPlayhead(Ph - 1); ev.Use(); break;
            case GLFWConst.KEY_RIGHT: _playing = false; SetPlayhead(Ph + 1); ev.Use(); break;
            case GLFWConst.KEY_HOME: _playing = false; SetPlayhead(0); ev.Use(); break;
            case GLFWConst.KEY_END: _playing = false; SetPlayhead(Frames - 1); ev.Use(); break;
            case GLFWConst.KEY_SPACE: TogglePlay(); ev.Use(); break;
        }
    }
}



