using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Flash MovieClip, generic govdeyle: timeline = property track'leri.
//   track = (hedef Component, AnimProperty yolu, kanal maskesi) + keyframe'ler
//   key   = frame + AnimValue (Vec4 + IAsset ref) + ease + tween/hold
// Hangi property'nin animasyonlandigi motorun umurunda degil: Transform.Position da,
// LayoutBox.Fill.Color da, senin Particle.StartDelay'in de ayni yoldan gecer; [Animatable]
// metodlar Trigger track'i olur (frame'e key = cagri). Nested klip = cocugun "Frame"
// property'sine track (Sync = lineer, SingleFrame = hold, Independent = track yok).
//
// Iki katmanli veri: authoring listeleri (serilesir) ↔ runtime packed diziler (Rebuild'de
// BIR KEZ; oynatmada alloc/string/reflection yok). Oynatma Fps'e gore tam frame'lerde
// ilerler (aksiyon/tetik kesin bir kez), gorsel ornekleme kesirli (Interpolate).
// SampleFrame(f) SAF: editor scrub ve parent-drive bunu kullanir. Yazim dedup track basina.

public enum ClipKeyMode : byte { Tween, Hold }

public enum ClipActionKind : byte
{
    Stop,
    Play,
    GotoAndPlay, // Label ya da TargetFrame
    GotoAndStop,
    Event,       // IClipEventHandler.OnClipEvent(ClipHash(Label))
}

[Serializable]
public sealed class ClipKey
{
    public int Frame;
    public ClipKeyMode Mode;
    public Ease Ease;
    public Vec4 Value;  // Float/Int/Bool/Enum: x; Vec2/3/4: xyzw; Color: rgba 0..255
    public IAsset Ref;  // Ref turleri (Sprite, Font, Prefab...)

    public AnimValue ToAnim() => new(Value.x, Value.y, Value.z, Value.w) { Ref = Ref };

    public void SetAnim(in AnimValue v)
    {
        Value = new Vec4(v.X, v.Y, v.Z, v.W);
        Ref = v.Ref as IAsset;
    }
}

[Serializable]
public sealed class ClipTrack
{
    public Component Target; // prefab-local ref: instance'larda otomatik remap
    public string Path = ""; // AnimProperty yolu ("Position", "Fill.Color", "Emit")
    public int Mask = AnimValue.MaskAll; // kanal maskesi (1=X..8=W)
    public List<ClipKey> Keys = new();
}

[Serializable]
public sealed class ClipLabel
{
    public int Frame;
    public string Name = "label";
}

[Serializable]
public sealed class ClipAction
{
    public int Frame;
    public ClipActionKind Kind;
    public string Label; // Goto hedef label'i ya da Event adi (bos = TargetFrame)
    public int TargetFrame;
}

// Frame event hedefi: klip ile AYNI GameObject'teki component'ler uygular.
public interface IClipEventHandler
{
    void OnClipEvent(MovieClip clip, int eventId);
}

public static class ClipHash
{
    // FNV-1a: label/event adlari derleme/cagri aninda int'e iner; oynatmada string yok.
    public static int Of(string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;
        uint h = 2166136261;
        for (int i = 0; i < s.Length; i++)
            h = (h ^ s[i]) * 16777619;
        return (int)h;
    }
}

[Previewable]
public sealed class MovieClip : Component
{
    public static readonly int CompleteEvent = ClipHash.Of("@complete");
    // Buyuk dt'de bir Update'te en fazla bu kadar frame ilerlenir (spike = atla).
    public const int MaxCatchUpFrames = 8;

    // --- Authoring (serilesir) ---
    public int Fps = 30;
    public int FrameCount = 30;
    public bool Loop = true;
    public bool PlayOnStart = true;
    public bool Interpolate = true;
    public float Speed = 1f;
    public List<ClipTrack> Tracks = new();
    public List<ClipLabel> Labels = new();
    public List<ClipAction> Actions = new();

    // --- Runtime packed ---
    struct KeyRt
    {
        public int Frame;
        public ClipKeyMode Mode;
        public Ease Ease;
        public AnimValue V;
    }

    struct TrackRt
    {
        public Component Target;
        public AnimProperty Prop;
        public byte Mask;
        public int Authoring; // Tracks listesindeki indeks (editor sorgulari)
        public int KeyStart, KeyCount;
        public int Cursor; // son bulunan span (ileri oynatmada O(1))
        public bool HasLast;
        public AnimValue Last; // yazim dedup
    }

    struct LabelRt { public int Frame; public int Hash; public string Name; }

    struct ActionRt
    {
        public int Frame;
        public ClipActionKind Kind;
        public int Target; // Goto: cozulmus frame; Event: hash
    }

    KeyRt[] _keys = Array.Empty<KeyRt>();
    TrackRt[] _tracks = Array.Empty<TrackRt>();     // deger track'leri
    TrackRt[] _triggers = Array.Empty<TrackRt>();   // Trigger track'leri (frame girisinde cagri)
    LabelRt[] _labels = Array.Empty<LabelRt>();
    ActionRt[] _actions = Array.Empty<ActionRt>();
    IClipEventHandler[] _handlers = Array.Empty<IClipEventHandler>();
    int _frameCount = 1;
    bool _built;
    int _gotoDepth;  // aksiyon→goto→aksiyon zinciri siniri
    int _gotoSerial; // Advance, aksiyonun goto yaptigini bununla anlar

    float _frame;    // kesirli oynatma konumu [0, _frameCount)
    bool _playing;

    public bool IsPlaying => _playing;
    public int CurrentFrame => (int)_frame;
    public float CurrentFramePosition => _frame;
    public int TotalFrames { get { EnsureBuilt(); return _frameCount; } }

    // Parent klip / tween tarafindan surulebilir: yazmak oynatmayi durdurur ve ornekler.
    [Animatable]
    public float Frame
    {
        get => _frame;
        set
        {
            EnsureBuilt();
            _playing = false;
            _frame = Wrap(value);
            SampleFrame(_frame);
        }
    }

    [Animatable]
    public bool Playing
    {
        get => _playing;
        set { if (value) Play(); else Stop(); }
    }

    public string CurrentLabel
    {
        get
        {
            EnsureBuilt();
            int f = (int)_frame;
            string best = null;
            for (int i = 0; i < _labels.Length && _labels[i].Frame <= f; i++)
                best = _labels[i].Name;
            return best;
        }
    }

    // --- Lifecycle ---

    protected internal override void Awake() => Rebuild();

    protected internal override void Start()
    {
        // Edit modunda (simule edilmeyen sahne) sahneye DOKUNMA: nesneler doc pozunda
        // kalir; ornekleme yalniz preview oturumunun isidir (Unity Animation gibi).
        if (_gameObject?._scene != null && !_gameObject._scene.Simulating)
            return;
        if (PlayOnStart)
            GotoAndPlay(0);
        else
            SampleFrame(_frame);
    }

    protected internal override void OnValidate()
    {
        Rebuild(); // edit modunda ornekleme yok (preview paneli playhead'ini kendi yazar)
    }

    protected internal override void Update()
    {
        if (!_playing)
            return;
        Advance(Time.deltaTime * Fps * Speed);
    }

    // --- Flash API ---

    public void Play()
    {
        EnsureBuilt();
        _playing = true;
    }

    public void Stop() => _playing = false;

    public void GotoAndPlay(int frame) => Goto(frame, true);
    public void GotoAndStop(int frame) => Goto(frame, false);
    public void GotoAndPlay(string label) => Goto(FrameOfLabel(label), true);
    public void GotoAndStop(string label) => Goto(FrameOfLabel(label), false);
    public void NextFrame() => Goto((int)_frame + 1, false);
    public void PrevFrame() => Goto((int)_frame - 1, false);

    public int FrameOfLabel(string label)
    {
        EnsureBuilt();
        int h = ClipHash.Of(label);
        for (int i = 0; i < _labels.Length; i++)
            if (_labels[i].Hash == h && string.Equals(_labels[i].Name, label, StringComparison.Ordinal))
                return _labels[i].Frame;
        return -1;
    }

    // Flash gotoAndX: hedef frame'in script'i CALISIR (seek degil, gercek giris).
    void Goto(int frame, bool play)
    {
        EnsureBuilt();
        if (frame < 0)
            return; // bilinmeyen label: no-op
        if (frame >= _frameCount)
            frame = _frameCount - 1;
        _frame = frame;
        _playing = play;
        _gotoSerial++;
        if (_gotoDepth < 4)
        {
            _gotoDepth++;
            EnterFrame(frame);
            _gotoDepth--;
        }
        SampleFrame(_frame);
    }

    float Wrap(float f)
    {
        if (_frameCount <= 1) return 0f;
        if (Loop)
        {
            f %= _frameCount;
            if (f < 0f) f += _frameCount;
            return f;
        }
        return Math.Clamp(f, 0f, _frameCount - 1);
    }

    // Zaman ilerletme: tam frame sinirlarini tek tek gecer (aksiyon/tetik kesin bir kez),
    // kalan kesir gorsel icin saklanir. Goto/Stop tetiklenirse kalan sure atilir (Flash).
    void Advance(float df)
    {
        if (df <= 0f)
        {
            SampleFrame(_frame);
            return;
        }
        int guard = MaxCatchUpFrames;
        while (df > 0f)
        {
            int cur = (int)_frame;
            float toNext = (cur + 1) - _frame;
            if (df < toNext)
            {
                _frame += df;
                break;
            }
            if (--guard < 0)
                break; // spike: kalan zamani at, mevcut frame'de kal
            df -= toNext;
            int next = cur + 1;
            if (next >= _frameCount)
            {
                if (!Loop)
                {
                    _frame = _frameCount - 1;
                    _playing = false;
                    SampleFrame(_frame);
                    Dispatch(CompleteEvent);
                    return;
                }
                next = 0;
            }
            _frame = next;
            int serial = _gotoSerial;
            EnterFrame(next);
            if (!_playing || serial != _gotoSerial)
                break;
        }
        SampleFrame(_frame);
    }

    // Frame'e GIRIS: tetikler + aksiyonlar (sirali kucuk diziler → dogrusal tarama).
    void EnterFrame(int frame)
    {
        for (int t = 0; t < _triggers.Length; t++)
        {
            ref var tr = ref _triggers[t];
            if (tr.Target == null || tr.Target._destroyed) continue;
            for (int k = tr.KeyStart; k < tr.KeyStart + tr.KeyCount; k++)
            {
                if (_keys[k].Frame < frame) continue;
                if (_keys[k].Frame > frame) break;
                tr.Prop.Set(tr.Target, default);
            }
        }
        for (int i = 0; i < _actions.Length; i++)
        {
            ref var a = ref _actions[i];
            if (a.Frame < frame) continue;
            if (a.Frame > frame) break;
            switch (a.Kind)
            {
                case ClipActionKind.Stop: _playing = false; break;
                case ClipActionKind.Play: _playing = true; break;
                case ClipActionKind.GotoAndPlay: Goto(a.Target, true); return;
                case ClipActionKind.GotoAndStop: Goto(a.Target, false); return;
                case ClipActionKind.Event: Dispatch(a.Target); break;
            }
        }
    }

    void Dispatch(int eventId)
    {
        for (int i = 0; i < _handlers.Length; i++)
            _handlers[i].OnClipEvent(this, eventId);
    }

    // --- Ornekleme (saf: zaman/oynatma durumu degismez, aksiyon tetiklenmez) ---

    public void SampleFrame(float frame)
    {
        EnsureBuilt();
        if (frame < 0f) frame = 0f;
        if (frame > _frameCount - 1) frame = _frameCount - 1;
        if (!Interpolate)
            frame = (int)frame;
        for (int i = 0; i < _tracks.Length; i++)
            SampleTrack(ref _tracks[i], frame);
    }

    // Track'in frame'deki degeri (yazmadan). Editor: "bu frame'de deger ne?"
    public bool TryEvaluate(int trackIndex, float frame, out AnimValue value)
    {
        EnsureBuilt();
        value = default;
        for (int i = 0; i < _tracks.Length; i++)
        {
            if (_tracks[i].Authoring != trackIndex) continue;
            if (_tracks[i].KeyCount == 0) return false;
            value = Evaluate(ref _tracks[i], frame);
            return true;
        }
        return false;
    }

    void SampleTrack(ref TrackRt T, float frame)
    {
        if (T.Prop == null || T.KeyCount == 0 || T.Target == null || T.Target._destroyed)
            return;
        var v = Evaluate(ref T, frame);
        if (T.Mask != AnimValue.MaskAll && T.Prop.Get != null)
            v = AnimValue.Merge(T.Prop.Get(T.Target), v, T.Mask);
        if (T.HasLast && T.Last.Same(v))
            return; // dedup: degismeyen deger yazilmaz (dirty yayilimi yok)
        T.Prop.Set(T.Target, v);
        T.Last = v;
        T.HasLast = true;
    }

    AnimValue Evaluate(ref TrackRt T, float frame)
    {
        int first = T.KeyStart, last = T.KeyStart + T.KeyCount - 1;
        if (frame <= _keys[first].Frame)
            return _keys[first].V; // ilk key'den once: ilk degere kenetlen
        // Span: keys[i].Frame <= frame < keys[i+1].Frame. Cursor'dan basla, gerekirse ara.
        int i = T.Cursor;
        if (i < first || i > last || _keys[i].Frame > frame || (i < last && _keys[i + 1].Frame <= frame))
        {
            if (i >= first && i < last && _keys[i + 1].Frame <= frame
                && (i + 1 == last || _keys[i + 2].Frame > frame))
                i++; // ileri oynatma: bir sonraki span
            else
            {
                int lo = first, hi = last;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (_keys[mid].Frame <= frame) lo = mid; else hi = mid - 1;
                }
                i = lo;
            }
            T.Cursor = i;
        }
        ref var k = ref _keys[i];
        if (i == last || k.Mode == ClipKeyMode.Hold || k.Frame == _keys[i + 1].Frame)
            return k.V;
        ref var n = ref _keys[i + 1];
        float t = (frame - k.Frame) / (float)(n.Frame - k.Frame);
        return AnimValue.Lerp(k.V, n.V, Easing.Evaluate(k.Ease, t), T.Prop.Kind);
    }

    // --- Derleme: authoring → packed (yukleme zamani; alloc burada serbest) ---

    TypeCatalog Catalog => _gameObject?._scene?.Catalog;

    void EnsureBuilt()
    {
        if (!_built)
            Rebuild();
    }

    public AnimProperty ResolveProperty(ClipTrack t)
        => t?.Target != null && !t.Target._destroyed ? AnimRegistry.Find(Catalog, t.Target.GetType(), t.Path) : null;

    public void Rebuild()
    {
        _built = true;
        int keyTotal = 0, valueTracks = 0, triggerTracks = 0;
        var props = new AnimProperty[Tracks.Count];
        for (int i = 0; i < Tracks.Count; i++)
        {
            props[i] = ResolveProperty(Tracks[i]);
            if (props[i] == null) continue;
            keyTotal += Tracks[i].Keys.Count;
            if (props[i].Kind == AnimKind.Trigger) triggerTracks++; else valueTracks++;
        }
        _tracks = valueTracks == _tracks.Length ? _tracks : new TrackRt[valueTracks];
        _triggers = triggerTracks == _triggers.Length ? _triggers : new TrackRt[triggerTracks];
        _keys = keyTotal == _keys.Length ? _keys : new KeyRt[keyTotal];

        int maxFrame = FrameCount - 1;
        int ki = 0, vi = 0, ti = 0;
        for (int i = 0; i < Tracks.Count; i++)
        {
            var p = props[i];
            if (p == null) continue;
            var track = Tracks[i];
            var T = new TrackRt
            {
                Target = track.Target, Prop = p, Mask = (byte)(track.Mask & AnimValue.MaskAll), Authoring = i,
                KeyStart = ki, KeyCount = track.Keys.Count, Cursor = ki,
            };
            // Frame sirali kopya (insertion sort: editor zaten sirali tutar).
            int start = ki;
            for (int j = 0; j < track.Keys.Count; j++)
            {
                var k = track.Keys[j];
                int pos = ki;
                while (pos > start && _keys[pos - 1].Frame > k.Frame)
                { _keys[pos] = _keys[pos - 1]; pos--; }
                _keys[pos] = new KeyRt { Frame = k.Frame, Mode = k.Mode, Ease = k.Ease, V = k.ToAnim() };
                ki++;
                if (k.Frame > maxFrame) maxFrame = k.Frame;
            }
            if (p.Kind == AnimKind.Trigger) _triggers[ti++] = T; else _tracks[vi++] = T;
        }
        _frameCount = Math.Max(1, maxFrame + 1);

        _labels = Labels.Count == _labels.Length ? _labels : new LabelRt[Labels.Count];
        for (int i = 0; i < Labels.Count; i++)
            _labels[i] = new LabelRt { Frame = Labels[i].Frame, Hash = ClipHash.Of(Labels[i].Name), Name = Labels[i].Name };
        Array.Sort(_labels, (x, y) => x.Frame.CompareTo(y.Frame));

        _actions = Actions.Count == _actions.Length ? _actions : new ActionRt[Actions.Count];
        for (int i = 0; i < Actions.Count; i++)
        {
            var a = Actions[i];
            int target = a.Kind == ClipActionKind.Event
                ? ClipHash.Of(a.Label)
                : (!string.IsNullOrEmpty(a.Label) ? FrameOfLabelAuthoring(a.Label) : a.TargetFrame);
            _actions[i] = new ActionRt { Frame = a.Frame, Kind = a.Kind, Target = target };
        }
        Array.Sort(_actions, (x, y) => x.Frame.CompareTo(y.Frame));

        // Event hedefleri: ayni GO'daki IClipEventHandler component'leri.
        int hc = 0;
        for (int i = 0; i < _gameObject.ComponentCount; i++)
            if (_gameObject.ComponentAt(i) is IClipEventHandler) hc++;
        _handlers = hc == _handlers.Length ? _handlers : new IClipEventHandler[hc];
        hc = 0;
        for (int i = 0; i < _gameObject.ComponentCount; i++)
            if (_gameObject.ComponentAt(i) is IClipEventHandler h) _handlers[hc++] = h;

        if (_frame > _frameCount - 1)
            _frame = _frameCount - 1;
    }

    int FrameOfLabelAuthoring(string label)
    {
        for (int i = 0; i < Labels.Count; i++)
            if (string.Equals(Labels[i].Name, label, StringComparison.Ordinal))
                return Labels[i].Frame;
        return -1;
    }
}
