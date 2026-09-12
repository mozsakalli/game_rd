using System;

namespace DigitoyEngine;

// CORE tween: sahneye ait struct havuzu — nesne yok, closure yok, alloc yok.
// Guvenlik varsayilan: hedef yok edildi -> tween sessiz olur (exception imkansiz);
// bayat handle (generation uyusmaz) -> no-op; sahne unload -> havuz sahneyle gider.
// Hiz: duz dizi taramasi, kanal switch'i (sanal cagri yok), LUT easing.
// Alan yolu: serilesen sayisal alanlar otomatik tweenable (FieldSchema erisimcileri —
// editorde expression-compile, release'te source-gen ayni slotlari doldurur).

public enum TweenChannel : byte
{
    PosX, PosY, Rot, ScaleX, ScaleY, ScaleUniform,
    Alpha, ColorRgb,
    FieldFloat,
    Value, // hedefsiz: kullanici handle.Value okur
}

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
    public TweenHandle From(float v) { Pool?.SetFrom(Index, Gen, v, v, v, v); return this; }
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
        public TweenChannel Channel;
        public Ease Ease;
        public short LoopsLeft; // -1 sonsuz
        public float Delay;
        public float T;         // 0..1 normalize
        public float Duration;
        public float FromX, FromY, FromZ, FromW;
        public float ToX, ToY, ToZ, ToW;
        public Component Target;                 // olum kontrolu (Transform dahil)
        public SerializedType.FieldSchema Field; // FieldFloat kanali
        public Curve Curve;                      // null degilse Ease yerine
        public Action OnDone;
        public int Next;
        public int NextGen;
    }

    Slot[] _slots = new Slot[64];
    int _count;
    int[] _free = new int[64];
    int _freeCount;

    // --- Yaratma ---

    internal TweenHandle Start(Component target, TweenChannel ch, SerializedType.FieldSchema field,
        float toX, float toY, float toZ, float toW, float duration)
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
        s.Channel = ch;
        s.Ease = DigitoyEngine.Ease.Linear;
        s.LoopsLeft = 1;
        s.Delay = 0f;
        s.T = 0f;
        s.Duration = duration > 1e-6f ? duration : 1e-6f;
        s.ToX = toX; s.ToY = toY; s.ToZ = toZ; s.ToW = toW;
        s.Target = target;
        s.Field = field;
        s.Curve = null;
        s.OnDone = null;
        s.Next = -1;
        return new TweenHandle(this, i, gen);
    }

    // --- Frame surucusu (Scene.Update cagirir; dt sahne saatidir) ---

    public void Tick(float dt)
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
                CaptureFrom(ref s); // ilk tick: mevcut degerden basla
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
            // Bitti: callback + zincir + slot geri (zinciri Kill uyandirir).
            var done = s.OnDone;
            Kill(ref s, i);
            done?.Invoke();
        }
    }

    void CaptureFrom(ref Slot s)
    {
        switch (s.Channel)
        {
            case TweenChannel.PosX: s.FromX = Tr(ref s).localPosition.x; break;
            case TweenChannel.PosY: s.FromX = Tr(ref s).localPosition.y; break;
            case TweenChannel.Rot: s.FromX = Tr(ref s).localEulerAngles.z; break;
            case TweenChannel.ScaleX: s.FromX = Tr(ref s).localScale.x; break;
            case TweenChannel.ScaleY: s.FromX = Tr(ref s).localScale.y; break;
            case TweenChannel.ScaleUniform: s.FromX = Tr(ref s).localScale.x; break;
            case TweenChannel.Alpha: s.FromX = ((SpriteRenderer)s.Target).Color.a / 255f; break;
            case TweenChannel.ColorRgb:
                {
                    var c = ((SpriteRenderer)s.Target).Color;
                    s.FromX = c.r; s.FromY = c.g; s.FromZ = c.b;
                    break;
                }
            case TweenChannel.FieldFloat: s.FromX = s.Field.GetFloat(s.Target); break;
            case TweenChannel.Value: break; // From zaten arguman
        }
    }

    static Transform Tr(ref Slot s) => s.Target._gameObject.transform;

    void Write(ref Slot s, float e)
    {
        switch (s.Channel)
        {
            case TweenChannel.PosX:
                {
                    var tr = Tr(ref s);
                    var p = tr.localPosition;
                    tr.localPosition = new Vec3(L(s.FromX, s.ToX, e), p.y, p.z);
                    break;
                }
            case TweenChannel.PosY:
                {
                    var tr = Tr(ref s);
                    var p = tr.localPosition;
                    tr.localPosition = new Vec3(p.x, L(s.FromX, s.ToX, e), p.z);
                    break;
                }
            case TweenChannel.Rot:
                {
                    var tr = Tr(ref s);
                    var r = tr.localEulerAngles;
                    tr.localEulerAngles = new Vec3(r.x, r.y, L(s.FromX, s.ToX, e));
                    break;
                }
            case TweenChannel.ScaleX:
                {
                    var tr = Tr(ref s);
                    var sc = tr.localScale;
                    tr.localScale = new Vec3(L(s.FromX, s.ToX, e), sc.y, sc.z);
                    break;
                }
            case TweenChannel.ScaleY:
                {
                    var tr = Tr(ref s);
                    var sc = tr.localScale;
                    tr.localScale = new Vec3(sc.x, L(s.FromX, s.ToX, e), sc.z);
                    break;
                }
            case TweenChannel.ScaleUniform:
                {
                    var tr = Tr(ref s);
                    float v = L(s.FromX, s.ToX, e);
                    var sc = tr.localScale;
                    tr.localScale = new Vec3(v, v, sc.z);
                    break;
                }
            case TweenChannel.Alpha:
                {
                    var sr = (SpriteRenderer)s.Target;
                    var c = sr.Color;
                    c.a = (byte)Math.Clamp((int)(L(s.FromX, s.ToX, e) * 255f), 0, 255);
                    sr.Color = c;
                    break;
                }
            case TweenChannel.ColorRgb:
                {
                    var sr = (SpriteRenderer)s.Target;
                    var c = sr.Color;
                    c.r = (byte)Math.Clamp((int)L(s.FromX, s.ToX, e), 0, 255);
                    c.g = (byte)Math.Clamp((int)L(s.FromY, s.ToY, e), 0, 255);
                    c.b = (byte)Math.Clamp((int)L(s.FromZ, s.ToZ, e), 0, 255);
                    sr.Color = c;
                    break;
                }
            case TweenChannel.FieldFloat:
                s.Field.SetFloat(s.Target, L(s.FromX, s.ToX, e));
                break;
            case TweenChannel.Value:
                break; // deger handle.Value ile okunur
        }
    }

    static float L(float a, float b, float t) => a + (b - a) * t;

    void Kill(ref Slot s, int index)
    {
        // Zincir politikasi: olen tween (bitis, iptal, olu hedef) sonrakini uyandirir;
        // sonraki de olu hedefliyse ilk tick'te kendisi sessiz olur.
        int next = s.Next, nextGen = s.NextGen;
        s.Gen++; // bayat handle'lar aninda gecersiz
        s.Alive = false;
        s.Target = null;
        s.Field = null;
        s.Curve = null;
        s.OnDone = null;
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
        return L(s.FromX, s.ToX, e);
    }

    internal void Cancel(int i, int gen)
    {
        if (IsActive(i, gen))
            Kill(ref _slots[i], i);
    }

    internal void SetEase(int i, int gen, Ease e) { if (IsActive(i, gen)) _slots[i].Ease = e; }
    internal void SetCurve(int i, int gen, Curve c) { if (IsActive(i, gen)) _slots[i].Curve = c; }
    internal void SetDelay(int i, int gen, float d) { if (IsActive(i, gen)) _slots[i].Delay = d; }

    internal void SetFrom(int i, int gen, float x, float y, float z, float w)
    {
        if (!IsActive(i, gen))
            return;
        ref var s = ref _slots[i];
        s.FromX = x; s.FromY = y; s.FromZ = z; s.FromW = w;
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
public static class TweenExtensions
{
    static TweenPool PoolOf(Component c)
        => c?._gameObject?._scene?.Tweens;

    static TweenHandle Start(Component c, TweenChannel ch, float toX, float dur,
        float toY = 0, float toZ = 0)
    {
        var pool = PoolOf(c);
        return pool != null
            ? pool.Start(c, ch, null, toX, toY, toZ, 0, dur)
            : default; // sahnesiz/olu hedef: olu handle (no-op)
    }

    public static TweenHandle TweenMoveX(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.PosX, to, duration);

    public static TweenHandle TweenMoveY(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.PosY, to, duration);

    public static TweenHandle TweenRotation(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.Rot, to, duration);

    public static TweenHandle TweenScaleX(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleX, to, duration);

    public static TweenHandle TweenScaleY(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleY, to, duration);

    public static TweenHandle TweenScale(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleUniform, to, duration);

    public static TweenHandle TweenAlpha(this SpriteRenderer sr, float to01, float duration)
        => Start(sr, TweenChannel.Alpha, to01, duration);

    public static TweenHandle TweenColor(this SpriteRenderer sr, Color to, float duration)
        => Start(sr, TweenChannel.ColorRgb, to.r, duration, to.g, to.b);

    // Serilesen sayisal alan = otomatik tweenable. Ad cozumu YARATMA aninda bir kez.
    public static TweenHandle TweenField(this Component c, string fieldName, float to, float duration)
    {
        var pool = PoolOf(c);
        var catalog = c?._gameObject?._scene?.Catalog;
        var entry = catalog?.Find(c.GetType());
        var f = entry != null ? SerializedType.Find(entry.Schema, fieldName) : null;
        if (pool == null || f == null
            || (f.Kind != SerializedType.Kind.Float && f.Kind != SerializedType.Kind.Int))
            return default;
        SerializedType.EnsureFloatAccessors(f);
        return pool.Start(c, TweenChannel.FieldFloat, f, to, 0, 0, 0, duration);
    }

    // Hedefsiz deger tween'i: kullanici handle.Value okur (callback yok = alloc yok).
    public static TweenHandle TweenValue(this Scene scene, float from, float to, float duration)
    {
        var h = scene.Tweens.Start(null, TweenChannel.Value, null, to, 0, 0, 0, duration);
        return h.From(from);
    }
}
