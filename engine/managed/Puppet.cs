using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// PUPPET / CUE — "sanatsal metot" modeli (docs/engine-notes.md).
//   Puppet   = kendi iradesi olmayan gorsel varlik (prefab koku). Cue = disaridan cagrilan, ISIMLI ve PARAMETRELI
//              hareket: card.Play("Deal", slot). Cue karar vermez, gosterir; secim ve zamanlama Director'da (oyun kodu).
//   Cue      = parametreler + property track'leri (keyframe) + aksiyonlar (frame girisinde bir kez tetiklenen yan etki).
//   Key      = degerin KAYNAGI key'in kendi alanidir: Const (yazilan deger) | Current (Play aninda property'nin degeri —
//              ucus bulundugu yerden baslar) | Param (argumandan). Yan tablo yok.
//   Action   = Reparent (Node → Target'in cocugu; sonraki key'ler yeni ebeveynin YEREL uzayinda) | Event (Director'a).
//              Target sabit ya da Param'dan.
// Oynatma: ayni anda tek cue; yeni Play oncekini keser (kalan Reparent'lar uygulanir, Event atlanir, son frame'e ziplar).
// Scrub (editor): frame'e kadar Reparent'lar uygulanir, Event susar; geri sarma = cagiranin reload'u.
// Iki katmanli veri: authoring listeleri (serilesir) ↔ paketlenmis diziler (Rebuild'de bir kez; oynatmada alloc yok).

public enum ParamKind : byte { Float, Bool, Vec2, Vec3, Color, Target }
public enum KeySource : byte { Const, Current, Param }
public enum KeyMode : byte { Tween, Hold }
public enum ActionKind : byte { Reparent, Event }

[Serializable]
public sealed class CueParam
{
    public string Name = "p";
    public ParamKind Kind;
    public Vec4 Default;             // skaler turler (onizleme + eksik arguman)
    public Transform DefaultTarget;  // Kind=Target
}

[Serializable]
public sealed class CueKey
{
    public int Frame;
    public KeyMode Mode;
    public Ease Ease;
    public KeySource Source;
    public Vec4 Value;   // Const: Float/Int/Bool/Enum x; Vec2/3/4 xyzw; Color rgba 0..255
    public IAsset Ref;   // Const, Ref turleri
    public int Param;    // Source=Param

    public AnimValue ToAnim() => new(Value.x, Value.y, Value.z, Value.w) { Ref = Ref };
    public void SetAnim(in AnimValue v) { Value = new Vec4(v.X, v.Y, v.Z, v.W); Ref = v.Ref as IAsset; }
}

[Serializable]
public sealed class CueTrack
{
    public Component Node;       // prefab-local ref (instance'larda otomatik remap)
    public string Property = ""; // AnimProperty yolu ("Position", "Fill.Color")
    public int Mask = AnimValue.MaskAll;
    public List<CueKey> Keys = new();
}

[Serializable]
public sealed class CueAction
{
    public int Frame;
    public ActionKind Kind;
    public Transform Node;       // Reparent: tasinan (bos = puppet koku)
    public Transform Target;     // Reparent: sabit hedef (Param < 0)
    public int Param = -1;       // Reparent: hedef bu parametreden (Kind=Target)
    public bool KeepWorld = true;
    public string Label = "";    // Event adi
}

[Serializable]
public sealed class Cue
{
    public string Name = "cue";
    public int Fps = 30;
    public int Frames = 30;
    public List<CueParam> Params = new();
    public List<CueTrack> Tracks = new();
    public List<CueAction> Actions = new();
}

// Cue olaylari: puppet ile AYNI GameObject'teki component'ler uygular.
public interface ICueHandler
{
    void OnCueComplete(Puppet puppet, int cueIndex);
    void OnCueEvent(Puppet puppet, int cueIndex, int eventId) { }
}

public static class CueHash
{
    // FNV-1a: cue/event adlari int'e iner; oynatmada string yok.
    public static int Of(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        uint h = 2166136261;
        for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619;
        return (int)h;
    }
}

[Previewable]
[ExecuteInEditMode]
[AddComponentMenu("Animation/Puppet")]
public sealed class Puppet : Component
{
    public const int MaxCatchUpFrames = 8; // tek Update'te en fazla bu kadar frame (spike = atla)

    public List<Cue> Cues = new();

    // --- paketlenmis ---
    struct KeyRt { public int Frame; public KeyMode Mode; public Ease Ease; public KeySource Source; public int Param; public AnimValue V; }
    struct TrackRt
    {
        public Component Node; public AnimProperty Prop; public byte Mask;
        public int Authoring, KeyStart, KeyCount, Cursor;
        public bool HasLast; public AnimValue Last;
    }
    struct ActionRt { public int Frame; public ActionKind Kind; public Transform Node, Target; public int Param, EventId; public bool KeepWorld; }
    struct CueRt { public int Hash, Fps, Frames, TrackStart, TrackCount, ActionStart, ActionCount, ParamStart, ParamCount; }

    KeyRt[] _keys = Array.Empty<KeyRt>();
    TrackRt[] _tracks = Array.Empty<TrackRt>();
    ActionRt[] _actions = Array.Empty<ActionRt>();
    CueRt[] _cues = Array.Empty<CueRt>();
    AnimValue[] _args = Array.Empty<AnimValue>(); // tum cue'larin argumanlari ardisik; Target → Ref
    ICueHandler[] _handlers = Array.Empty<ICueHandler>();
    bool _built;

    int _cue = -1;
    float _frame;
    bool _playing;
    int _capturedCue = -1; // Current key'leri bu cue icin yakalandi (scrub oturumu)

    public event Action<Puppet, int> CueCompleted;
    public event Action<Puppet, int, int> CueEvent;

    public bool IsPlaying => _playing;
    public int CurrentCue => _cue;
    public int CurrentFrame => (int)_frame;
    public float CurrentFramePosition => _frame;

    // --- yasam dongusu ---

    protected internal override void Awake() => Rebuild();
    protected internal override void OnValidate() => Rebuild();
    protected internal override void OnAnimatedPropertiesApplied() { }

    protected internal override void Update()
    {
        if (_playing)
            Advance(Time.deltaTime * _cues[_cue].Fps);
    }

    // --- Director API ---

    public int IndexOf(string cue)
    {
        EnsureBuilt();
        int h = CueHash.Of(cue);
        for (int i = 0; i < _cues.Length; i++)
            if (_cues[i].Hash == h && string.Equals(Cues[i].Name, cue, StringComparison.Ordinal)) return i;
        return -1;
    }

    public bool Has(string cue) => IndexOf(cue) >= 0;

    public void SetArg(int cueIndex, int param, in AnimValue v)
    {
        EnsureBuilt();
        if ((uint)cueIndex >= (uint)_cues.Length) return;
        ref var c = ref _cues[cueIndex];
        if ((uint)param < (uint)c.ParamCount) _args[c.ParamStart + param] = v;
    }

    public AnimValue GetArg(int cueIndex, int param)
    {
        EnsureBuilt();
        if ((uint)cueIndex >= (uint)_cues.Length) return default;
        ref var c = ref _cues[cueIndex];
        return (uint)param < (uint)c.ParamCount ? _args[c.ParamStart + param] : default;
    }

    public bool Play(string cue) => Play(IndexOf(cue));
    public bool Play(string cue, in AnimValue a0) { int i = IndexOf(cue); SetArg(i, 0, a0); return Play(i); }
    public bool Play(string cue, in AnimValue a0, in AnimValue a1) { int i = IndexOf(cue); SetArg(i, 0, a0); SetArg(i, 1, a1); return Play(i); }
    public bool Play(string cue, in AnimValue a0, in AnimValue a1, in AnimValue a2)
    { int i = IndexOf(cue); SetArg(i, 0, a0); SetArg(i, 1, a1); SetArg(i, 2, a2); return Play(i); }
    public bool Play(string cue, Transform a0) => Play(cue, AnimValue.FromRef(a0));
    public bool Play(string cue, Transform a0, in AnimValue a1) => Play(cue, AnimValue.FromRef(a0), a1);

    // Mevcut argumanlarla oynat. Oynayan varsa: kalan Reparent'lar + son frame (hedefe zipla), Event yok.
    // Yeni cue: frame 0 aksiyonlari → arguman/Current yakalama (yeni ebeveynin yerel uzayi) → ornekle.
    public bool Play(int cueIndex)
    {
        EnsureBuilt();
        if ((uint)cueIndex >= (uint)_cues.Length) return false;
        if (_playing && _cue >= 0)
        {
            ApplyReparents(_cue, (int)_frame + 1, int.MaxValue);
            SampleFrame(_cue, _cues[_cue].Frames - 1);
        }
        _cue = cueIndex; _frame = 0f; _playing = true;
        EnterFrame(cueIndex, 0);
        Resolve(cueIndex, capture: true);
        _capturedCue = cueIndex;
        SampleFrame(cueIndex, 0f);
        return true;
    }

    public void Stop() => _playing = false;

    // Editor/scrub: frame'e kadar Reparent'lar (Event susar), Current oturumda bir kez (ya da frame 0'da), ornekle.
    public void Sample(int cueIndex, float frame)
    {
        EnsureBuilt();
        if ((uint)cueIndex >= (uint)_cues.Length) return;
        _cue = cueIndex;
        ApplyReparents(cueIndex, 0, (int)frame);
        Resolve(cueIndex, capture: _capturedCue != cueIndex || (int)frame == 0);
        _capturedCue = cueIndex;
        SampleFrame(cueIndex, frame);
    }

    public int FramesOf(int cueIndex)
    {
        EnsureBuilt();
        return (uint)cueIndex < (uint)_cues.Length ? _cues[cueIndex].Frames : 1;
    }

    public bool HasReparent(int cueIndex)
    {
        EnsureBuilt();
        if ((uint)cueIndex >= (uint)_cues.Length) return false;
        ref var c = ref _cues[cueIndex];
        for (int i = c.ActionStart; i < c.ActionStart + c.ActionCount; i++)
            if (_actions[i].Kind == ActionKind.Reparent) return true;
        return false;
    }

    // Track'in frame'deki degeri (yazmadan). Editor: "bu frame'de deger ne?"
    public bool TryEvaluate(int cueIndex, int trackIndex, float frame, out AnimValue value)
    {
        EnsureBuilt();
        value = default;
        if ((uint)cueIndex >= (uint)_cues.Length) return false;
        ref var c = ref _cues[cueIndex];
        for (int i = c.TrackStart; i < c.TrackStart + c.TrackCount; i++)
        {
            ref var T = ref _tracks[i];
            if (T.Authoring != trackIndex) continue;
            if (T.KeyCount == 0) return false;
            value = Evaluate(ref T, frame);
            return true;
        }
        return false;
    }

    // --- oynatma ---

    void Advance(float df)
    {
        ref var c = ref _cues[_cue];
        if (df <= 0f) { SampleFrame(_cue, _frame); return; }
        int guard = MaxCatchUpFrames;
        while (df > 0f)
        {
            int cur = (int)_frame;
            float toNext = (cur + 1) - _frame;
            if (df < toNext) { _frame += df; break; }
            if (--guard < 0) break;
            df -= toNext;
            int next = cur + 1;
            if (next >= c.Frames)
            {
                _frame = c.Frames - 1; _playing = false;
                SampleFrame(_cue, _frame);
                Complete(_cue);
                return;
            }
            _frame = next;
            EnterFrame(_cue, next);
            if (!_playing) break;
        }
        SampleFrame(_cue, _frame);
    }

    void Complete(int cueIndex)
    {
        for (int i = 0; i < _handlers.Length; i++) _handlers[i].OnCueComplete(this, cueIndex);
        CueCompleted?.Invoke(this, cueIndex);
    }

    // Frame'e GIRIS: aksiyonlar (frame sirali) bir kez.
    void EnterFrame(int cueIndex, int frame)
    {
        ref var c = ref _cues[cueIndex];
        for (int i = c.ActionStart; i < c.ActionStart + c.ActionCount; i++)
        {
            ref var a = ref _actions[i];
            if (a.Frame < frame) continue;
            if (a.Frame > frame) break;
            if (a.Kind == ActionKind.Reparent) DoReparent(cueIndex, ref a);
            else
            {
                for (int h = 0; h < _handlers.Length; h++) _handlers[h].OnCueEvent(this, cueIndex, a.EventId);
                CueEvent?.Invoke(this, cueIndex, a.EventId);
            }
        }
    }

    void ApplyReparents(int cueIndex, int from, int to)
    {
        ref var c = ref _cues[cueIndex];
        for (int i = c.ActionStart; i < c.ActionStart + c.ActionCount; i++)
        {
            ref var a = ref _actions[i];
            if (a.Frame < from) continue;
            if (a.Frame > to) break;
            if (a.Kind == ActionKind.Reparent) DoReparent(cueIndex, ref a);
        }
    }

    void DoReparent(int cueIndex, ref ActionRt a)
    {
        var node = a.Node ?? transform;
        var target = a.Param >= 0 ? _args[_cues[cueIndex].ParamStart + a.Param].Ref as Transform : a.Target;
        if (node == null || node._destroyed || target == null || target._destroyed) return;
        node.SetParent(target, a.KeepWorld); // ayni ebeveyn = no-op; dongu SetParent'ta reddedilir
    }

    // Const disi key degerlerini coz: Param → arguman; Current → (capture ise) property'nin su anki degeri.
    void Resolve(int cueIndex, bool capture)
    {
        ref var c = ref _cues[cueIndex];
        for (int t = c.TrackStart; t < c.TrackStart + c.TrackCount; t++)
        {
            ref var T = ref _tracks[t];
            for (int k = T.KeyStart; k < T.KeyStart + T.KeyCount; k++)
            {
                ref var key = ref _keys[k];
                if (key.Source == KeySource.Param)
                {
                    if ((uint)key.Param < (uint)c.ParamCount) key.V = _args[c.ParamStart + key.Param];
                }
                else if (key.Source == KeySource.Current && capture && T.Prop?.Get != null && T.Node != null && !T.Node._destroyed)
                    key.V = T.Prop.Get(T.Node);
            }
            if (capture) T.HasLast = false; // dedup onbellegi gecersiz
        }
    }

    void SampleFrame(int cueIndex, float frame)
    {
        ref var c = ref _cues[cueIndex];
        if (frame < 0f) frame = 0f;
        if (frame > c.Frames - 1) frame = c.Frames - 1;
        for (int i = c.TrackStart; i < c.TrackStart + c.TrackCount; i++)
        {
            ref var T = ref _tracks[i];
            if (T.Prop == null || T.KeyCount == 0 || T.Node == null || T.Node._destroyed) continue;
            var v = Evaluate(ref T, frame);
            if (T.Mask != AnimValue.MaskAll && T.Prop.Get != null)
                v = AnimValue.Merge(T.Prop.Get(T.Node), v, T.Mask);
            if (T.HasLast && T.Last.Same(v)) continue;
            T.Prop.Apply(T.Node, v);
            T.Last = v; T.HasLast = true;
        }
    }

    // Span: keys[i].Frame <= frame < keys[i+1].Frame; cursor'dan basla (ileri oynatma O(1)), gerekirse ikili arama.
    AnimValue Evaluate(ref TrackRt T, float frame)
    {
        int first = T.KeyStart, last = T.KeyStart + T.KeyCount - 1;
        if (frame <= _keys[first].Frame) return _keys[first].V;
        int i = T.Cursor;
        if (i < first || i > last || _keys[i].Frame > frame || (i < last && _keys[i + 1].Frame <= frame))
        {
            if (i >= first && i < last && _keys[i + 1].Frame <= frame && (i + 1 == last || _keys[i + 2].Frame > frame))
                i++;
            else
            {
                int lo = first, hi = last;
                while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (_keys[mid].Frame <= frame) lo = mid; else hi = mid - 1; }
                i = lo;
            }
            T.Cursor = i;
        }
        ref var k = ref _keys[i];
        if (i == last || k.Mode == KeyMode.Hold || k.Frame == _keys[i + 1].Frame) return k.V;
        ref var n = ref _keys[i + 1];
        float t = (frame - k.Frame) / (float)(n.Frame - k.Frame);
        return AnimValue.Lerp(k.V, n.V, Easing.Evaluate(k.Ease, t), T.Prop.Kind);
    }

    // --- derleme: authoring → paketlenmis (alloc burada serbest) ---

    TypeCatalog Catalog => _gameObject?._scene?.Catalog;

    void EnsureBuilt() { if (!_built) Rebuild(); }

    public AnimProperty ResolveProperty(CueTrack t)
        => t?.Node != null && !t.Node._destroyed ? AnimRegistry.Find(Catalog, t.Node.GetType(), t.Property) : null;

    public static AnimValue DefaultArg(CueParam p)
        => p.Kind == ParamKind.Target
            ? AnimValue.FromRef(p.DefaultTarget != null && !p.DefaultTarget._destroyed ? p.DefaultTarget : null)
            : new AnimValue(p.Default.x, p.Default.y, p.Default.z, p.Default.w);

    public void Rebuild()
    {
        _built = true;
        _capturedCue = -1;
        int nKeys = 0, nTracks = 0, nActions = 0, nParams = 0;
        foreach (var cue in Cues)
        {
            foreach (var t in cue.Tracks)
            {
                var p = ResolveProperty(t);
                if (p == null || p.Kind == AnimKind.Trigger) continue;
                nTracks++; nKeys += t.Keys.Count;
            }
            nActions += cue.Actions.Count;
            nParams += cue.Params.Count;
        }
        _cues = Cues.Count == _cues.Length ? _cues : new CueRt[Cues.Count];
        _tracks = nTracks == _tracks.Length ? _tracks : new TrackRt[nTracks];
        _keys = nKeys == _keys.Length ? _keys : new KeyRt[nKeys];
        _actions = nActions == _actions.Length ? _actions : new ActionRt[nActions];
        var args = nParams == _args.Length ? _args : new AnimValue[nParams]; // ayni boy: argumanlar korunur

        int ki = 0, ti = 0, ai = 0, pi = 0;
        for (int ci = 0; ci < Cues.Count; ci++)
        {
            var cue = Cues[ci];
            var C = new CueRt { Hash = CueHash.Of(cue.Name), Fps = Math.Max(1, cue.Fps), TrackStart = ti, ActionStart = ai, ParamStart = pi, ParamCount = cue.Params.Count };
            int maxFrame = cue.Frames - 1;
            for (int i = 0; i < cue.Tracks.Count; i++)
            {
                var t = cue.Tracks[i];
                var p = ResolveProperty(t);
                if (p == null || p.Kind == AnimKind.Trigger) continue;
                _tracks[ti++] = new TrackRt { Node = t.Node, Prop = p, Mask = (byte)(t.Mask & AnimValue.MaskAll), Authoring = i, KeyStart = ki, KeyCount = t.Keys.Count, Cursor = ki };
                int start = ki;
                foreach (var k in t.Keys) // frame sirali kopya (insertion; editor zaten sirali tutar)
                {
                    var rt = new KeyRt { Frame = k.Frame, Mode = k.Mode, Ease = k.Ease, Source = k.Source, Param = k.Param, V = k.ToAnim() };
                    int pos = ki;
                    while (pos > start && _keys[pos - 1].Frame > rt.Frame) { _keys[pos] = _keys[pos - 1]; pos--; }
                    _keys[pos] = rt; ki++;
                    if (k.Frame > maxFrame) maxFrame = k.Frame;
                }
            }
            int aStart = ai;
            foreach (var a in cue.Actions)
            {
                bool paramOk = (uint)a.Param < (uint)cue.Params.Count && cue.Params[a.Param].Kind == ParamKind.Target;
                var rt = new ActionRt { Frame = a.Frame, Kind = a.Kind, Node = a.Node, Target = a.Target, KeepWorld = a.KeepWorld, Param = paramOk ? a.Param : -1, EventId = a.Kind == ActionKind.Event ? CueHash.Of(a.Label) : 0 };
                int pos = ai;
                while (pos > aStart && _actions[pos - 1].Frame > rt.Frame) { _actions[pos] = _actions[pos - 1]; pos--; }
                _actions[pos] = rt; ai++;
                if (a.Frame > maxFrame) maxFrame = a.Frame;
            }
            for (int i = 0; i < cue.Params.Count; i++, pi++)
                if (!ReferenceEquals(args, _args)) args[pi] = DefaultArg(cue.Params[i]);
            C.TrackCount = ti - C.TrackStart;
            C.ActionCount = ai - C.ActionStart;
            C.Frames = Math.Max(1, maxFrame + 1);
            _cues[ci] = C;
        }
        _args = args;

        int hc = 0;
        for (int i = 0; i < _gameObject.ComponentCount; i++) if (_gameObject.ComponentAt(i) is ICueHandler) hc++;
        _handlers = hc == _handlers.Length ? _handlers : new ICueHandler[hc];
        hc = 0;
        for (int i = 0; i < _gameObject.ComponentCount; i++) if (_gameObject.ComponentAt(i) is ICueHandler h) _handlers[hc++] = h;

        if (_cue >= _cues.Length) { _cue = -1; _playing = false; }
        else if (_cue >= 0 && _frame > _cues[_cue].Frames - 1) _frame = _cues[_cue].Frames - 1;
    }
}
