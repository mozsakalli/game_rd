using System;

namespace DigitoyEngine;

// CORE tween: sahneye ait struct havuzu — nesne yok, closure yok, alloc yok.
// Hedef = PropertyBinding (AnimProperty): HER animatable property tweenlenir, ozel
// kanal kodu yok. Yazim: AnimValue lerp → maske birlestirme → tipli Set delegate'i.
// Guvenlik varsayilan: hedef yok edildi -> tween sessiz olur; bayat handle -> no-op;
// sahne unload -> havuz sahneyle gider. Ad cozumu (string → AnimProperty) YARATMA aninda.

public readonly struct TweenHandle
{
    internal readonly TweenPool Pool;
    internal readonly int Index;
    internal readonly int Gen;

    internal TweenHandle(TweenPool pool, int index, int gen)
    {
        Pool = pool;
        Index = index;
        Gen = gen;
    }

    public bool IsActive => Pool != null && Pool.IsActive(Index, Gen);
    public float Value => Pool?.ValueOf(Index, Gen) ?? 0f;

    public void Cancel() => Pool?.Cancel(Index, Gen);

    public TweenHandle Ease(Ease e) { Pool?.SetEase(Index, Gen, e); return this; }
    public TweenHandle WithCurve(Curve c) { Pool?.SetCurve(Index, Gen, c); return this; }
    public TweenHandle Delay(float seconds) { Pool?.SetDelay(Index, Gen, seconds); return this; }
    public TweenHandle From(float v) { Pool?.SetFrom(Index, Gen, new AnimValue(v, v, v, v)); return this; }
    public TweenHandle From(in AnimValue v) { Pool?.SetFrom(Index, Gen, v); return this; }
    public TweenHandle Loops(int count, bool yoyo = false) { Pool?.SetLoops(Index, Gen, count, yoyo); return this; }
    public TweenHandle OnDone(Action done) { Pool?.SetOnDone(Index, Gen, done); return this; }

    // Zincir: bu tween bitince digeri baslar (uyuyan slot uyanir) — nesne yok.
    public TweenHandle Then(TweenHandle next)
    {
        if (Pool != null && next.Pool == Pool)
            Pool.Link(Index, Gen, next.Index, next.Gen);
        return next;
    }
}

public sealed class TweenPool
{
    struct Slot
    {
        public int Gen;
        public bool Alive;
        public bool Waiting;    // zincirde sirasini bekliyor
        public bool HasFrom;
        public bool Yoyo;
        public bool Forward;
        public byte Mask;       // AnimValue kanal maskesi (1=X..8=W)
        public AnimKind Kind;
        public Ease Ease;
        public short LoopsLeft; // -1 sonsuz
        public float Delay;
        public float T;         // 0..1 normalize
        public float Duration;
        public AnimValue From, To;
        public Component Target;     // null = hedefsiz deger tween'i
        public AnimProperty Prop;    // hedefli ise zorunlu
        public Curve Curve;          // null degilse Ease yerine
        public Action OnDone;
        public int Next;
        public int NextGen;
    }

    Slot[] _slots = new Slot[64];
    int _count;
    int[] _free = new int[64];
    int _freeCount;

    // --- Yaratma ---

    internal TweenHandle Start(Component target, AnimProperty prop, byte mask, in AnimValue to, float duration)
    {
        int i;
        if (_freeCount > 0)
        {
            i = _free[--_freeCount];
        }
        else
        {
            if (_count == _slots.Length)
                Array.Resize(ref _slots, _count * 2);
            i = _count++;
        }
        ref var s = ref _slots[i];
        int gen = s.Gen;
        s.Alive = true;
        s.Waiting = false;
        s.HasFrom = false;
        s.Yoyo = false;
        s.Forward = true;
        s.Mask = mask;
        s.Kind = prop?.Kind ?? AnimKind.Float;
        s.Ease = DigitoyEngine.Ease.Linear;
        s.LoopsLeft = 1;
        s.Delay = 0f;
        s.T = 0f;
        s.Duration = duration > 1e-6f ? duration : 1e-6f;
        s.To = to;
        s.Target = target;
        s.Prop = prop;
        s.Curve = null;
        s.OnDone = null;
        s.Next = -1;
        return new TweenHandle(this, i, gen);
    }

    // --- Frame surucusu (Scene.Update cagirir; dt sahne saatidir) ---

    internal void Tick(float dt)
    {
        int n = _count;
        for (int i = 0; i < n; i++)
        {
            ref var s = ref _slots[i];
            if (!s.Alive || s.Waiting)
                continue;
            // Hedef oldu -> sessiz olum (GO destroy tum component'leri isaretler).
            if (s.Target != null && s.Target._destroyed)
            {
                Kill(ref s, i);
                continue;
            }
            if (s.Delay > 0f)
            {
                s.Delay -= dt;
                if (s.Delay > 0f)
                    continue;
            }
            if (!s.HasFrom)
            {
                if (s.Prop?.Get != null)
                    s.From = s.Prop.Get(s.Target); // ilk tick: mevcut degerden basla
                s.HasFrom = true;
            }

            s.T += dt / s.Duration;
            bool cycleEnd = s.T >= 1f;
            float p = cycleEnd ? 1f : s.T;
            if (!s.Forward)
                p = 1f - p;
            float e = s.Curve != null ? s.Curve.Evaluate(p) : Easing.Evaluate(s.Ease, p);
            Write(ref s, e);

            if (!cycleEnd)
                continue;
            if (s.LoopsLeft == -1 || --s.LoopsLeft > 0)
            {
                s.T = 0f;
                if (s.Yoyo)
                    s.Forward = !s.Forward;
                continue;
            }
            // Bitti: callback + zincir + slot geri.
            var done = s.OnDone;
            Kill(ref s, i);
            done?.Invoke();
        }
    }

    void Write(ref Slot s, float e)
    {
        if (s.Prop?.Set == null)
            return; // hedefsiz: deger handle.Value ile okunur
        var v = AnimValue.Lerp(s.From, s.To, e, s.Kind);
        if (s.Mask != AnimValue.MaskAll && s.Prop.Get != null)
            v = AnimValue.Merge(s.Prop.Get(s.Target), v, s.Mask);
        s.Prop.Set(s.Target, v);
    }

    void Kill(ref Slot s, int index)
    {
        // Zincir politikasi: olen tween (bitis, iptal, olu hedef) sonrakini uyandirir;
        // sonraki de olu hedefliyse ilk tick'te kendisi sessiz olur.
        int next = s.Next, nextGen = s.NextGen;
        s.Gen++; // bayat handle'lar aninda gecersiz
        s.Alive = false;
        s.Target = null;
        s.Prop = null;
        s.Curve = null;
        s.OnDone = null;
        s.From.Ref = null;
        s.To.Ref = null;
        s.Next = -1;
        if (_freeCount == _free.Length)
            Array.Resize(ref _free, _freeCount * 2);
        _free[_freeCount++] = index;
        if (next >= 0 && next < _count && _slots[next].Gen == nextGen)
            _slots[next].Waiting = false;
    }

    // --- Handle islemleri (generation korumali; bayat = no-op) ---

    internal bool IsActive(int i, int gen)
        => i >= 0 && i < _count && _slots[i].Gen == gen && _slots[i].Alive;

    internal float ValueOf(int i, int gen)
    {
        if (!IsActive(i, gen))
            return 0f;
        ref var s = ref _slots[i];
        float p = s.Forward ? s.T : 1f - s.T;
        float e = s.Curve != null ? s.Curve.Evaluate(p) : Easing.Evaluate(s.Ease, p);
        return s.From.X + (s.To.X - s.From.X) * e;
    }

    internal void Cancel(int i, int gen)
    {
        if (IsActive(i, gen))
            Kill(ref _slots[i], i);
    }

    internal void SetEase(int i, int gen, Ease e) { if (IsActive(i, gen)) _slots[i].Ease = e; }
    internal void SetCurve(int i, int gen, Curve c) { if (IsActive(i, gen)) _slots[i].Curve = c; }
    internal void SetDelay(int i, int gen, float d) { if (IsActive(i, gen)) _slots[i].Delay = d; }

    internal void SetFrom(int i, int gen, in AnimValue v)
    {
        if (!IsActive(i, gen))
            return;
        ref var s = ref _slots[i];
        s.From = v;
        s.HasFrom = true;
    }

    internal void SetLoops(int i, int gen, int count, bool yoyo)
    {
        if (!IsActive(i, gen))
            return;
        ref var s = ref _slots[i];
        s.LoopsLeft = (short)count;
        s.Yoyo = yoyo;
    }

    internal void SetOnDone(int i, int gen, Action done) { if (IsActive(i, gen)) _slots[i].OnDone = done; }

    internal void Link(int i, int gen, int nextI, int nextGen)
    {
        if (!IsActive(i, gen) || !IsActive(nextI, nextGen))
            return;
        _slots[i].Next = nextI;
        _slots[i].NextGen = nextGen;
        _slots[nextI].Waiting = true;
    }
}

// Kullanici yuzeyi: DOTween ergonomisi, motor tarafinda sifir bedel.
// Genel yol: c.Tween("Fill.Color", color, 0.4f) — yol adi yaratma aninda cozulur.
public static class TweenExtensions
{
    static TweenPool PoolOf(Component c)
        => c?._gameObject?._scene?.Tweens;

    static TweenHandle Start(Component c, AnimProperty prop, byte mask, in AnimValue to, float dur)
    {
        var pool = PoolOf(c);
        return pool != null && prop != null && prop.Set != null
            ? pool.Start(c, prop, mask, to, dur)
            : default; // sahnesiz/olu hedef/bilinmeyen property: olu handle (no-op)
    }

    static AnimProperty Resolve(Component c, string path)
        => c == null ? null : AnimRegistry.Find(c._gameObject?._scene?.Catalog, c.GetType(), path);

    // --- Genel: her animatable property ---

    public static TweenHandle Tween(this Component c, string path, in AnimValue to, float duration, byte mask = AnimValue.MaskAll)
        => Start(c, Resolve(c, path), mask, to, duration);

    public static TweenHandle Tween(this Component c, string path, float to, float duration)
        => Start(c, Resolve(c, path), AnimValue.MaskAll, AnimValue.FromFloat(to), duration);

    public static TweenHandle Tween(this Component c, string path, Vec2 to, float duration)
        => Start(c, Resolve(c, path), AnimValue.MaskAll, AnimValue.FromVec2(to), duration);

    public static TweenHandle Tween(this Component c, string path, Vec3 to, float duration)
        => Start(c, Resolve(c, path), AnimValue.MaskAll, AnimValue.FromVec3(to), duration);

    public static TweenHandle Tween(this Component c, string path, Vec4 to, float duration)
        => Start(c, Resolve(c, path), AnimValue.MaskAll, AnimValue.FromVec4(to), duration);

    public static TweenHandle Tween(this Component c, string path, Color to, float duration)
        => Start(c, Resolve(c, path), AnimValue.MaskAll, AnimValue.FromColor(to), duration);

    // Eski ad: serilesen sayisal alan.
    public static TweenHandle TweenField(this Component c, string fieldName, float to, float duration)
        => Tween(c, fieldName, to, duration);

    // --- Transform / SpriteRenderer kisayollari (ayni motor, yalniz maske) ---

    public static TweenHandle TweenMove(this Transform tr, Vec3 to, float duration)
        => Start(tr, AnimRegistry.TransformPosition, AnimValue.MaskAll, AnimValue.FromVec3(to), duration);

    public static TweenHandle TweenMoveX(this Transform tr, float to, float duration)
        => Start(tr, AnimRegistry.TransformPosition, AnimValue.MaskX, new AnimValue(to), duration);

    public static TweenHandle TweenMoveY(this Transform tr, float to, float duration)
        => Start(tr, AnimRegistry.TransformPosition, AnimValue.MaskY, new AnimValue(0, to), duration);

    public static TweenHandle TweenRotation(this Transform tr, float toZ, float duration)
        => Start(tr, AnimRegistry.TransformRotation, AnimValue.MaskZ, new AnimValue(0, 0, toZ), duration);

    public static TweenHandle TweenScaleX(this Transform tr, float to, float duration)
        => Start(tr, AnimRegistry.TransformScale, AnimValue.MaskX, new AnimValue(to), duration);

    public static TweenHandle TweenScaleY(this Transform tr, float to, float duration)
        => Start(tr, AnimRegistry.TransformScale, AnimValue.MaskY, new AnimValue(0, to), duration);

    public static TweenHandle TweenScale(this Transform tr, float to, float duration)
        => Start(tr, AnimRegistry.TransformScale, AnimValue.MaskXY, new AnimValue(to, to), duration);

    public static TweenHandle TweenAlpha(this SpriteRenderer sr, float to01, float duration)
        => Start(sr, Resolve(sr, nameof(SpriteRenderer.Color)), AnimValue.MaskW, new AnimValue(0, 0, 0, to01 * 255f), duration);

    public static TweenHandle TweenColor(this SpriteRenderer sr, Color to, float duration)
        => Start(sr, Resolve(sr, nameof(SpriteRenderer.Color)), AnimValue.MaskXYZ, AnimValue.FromColor(to), duration);

    // Hedefsiz deger tween'i: kullanici handle.Value okur (callback yok = alloc yok).
    public static TweenHandle TweenValue(this Scene scene, float from, float to, float duration)
    {
        var h = scene.Tweens.Start(null, null, AnimValue.MaskAll, new AnimValue(to), duration);
        return h.From(from);
    }
}
