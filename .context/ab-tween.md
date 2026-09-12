# COMPILED CONTEXT

## TASK
TweenPool: a tween killed because its target was destroyed or cancelled never wakes its Then() chained tween; the chained slot stays Waiting forever and leaks. Decide the policy and fix the kill and cancel paths so chained slots are reclaimed.

## REPOSITORY
root: C:/Work/digitoygames/game_rd
csharp_files_indexed: 92
index_cache: hit
semantic_symbols: 1430
call_edges: 3273
syntax_diagnostics: 0
active_file: engine/managed/Tween.cs

## PROJECT STATE
goal: Evolve the DigitoyEngine editor without breaking authored scene and prefab behavior.
constraints:
- Preserve existing scene and prefab serialization compatibility.
- Keep SceneDoc as authored state and live scenes as projections.
- Do not introduce static caches that retain collectible game assemblies.
decisions:
- Prefab instances are stored as deltas on disk and expanded into ordinary GoDoc nodes in memory.
- Prefab-local object and component references are remapped through persistent local-to-scene IDs.
- Editor structural mutations update the document and rebuild the live projection.
facts:
- Prefab expansion and collapse are implemented by the SceneDoc partial. [evidence: engine/managed/Serialization/SceneDoc.Prefab.cs]
- The editor and engine currently target .NET 9. [evidence: editor/DigitoyEditor.csproj; engine/managed/DigitoyEngine.csproj]
open_items:
- Prefab child removal, component diffs, and nested prefab behavior remain active areas.
validations:
- dotnet build editor -v q --nologo: passed before context compiler work

## GIT CHANGES
```text
No tracked baseline; 9171 untracked paths omitted.
```

## RELEVANT CODE

### method Kill
source: engine/managed/Tween.cs:277
symbol: DigitoyEngine.TweenPool.Kill(ref DigitoyEngine.TweenPool.Slot, int)
score: 178
called_by: DigitoyEngine.TweenPool.Cancel(int, int), DigitoyEngine.TweenPool.Tick(float)
depends_on: DigitoyEngine.TweenPool.Slot
```csharp
void Kill(ref Slot s, int index)
    {
        s.Gen++; // bayat handle'lar aninda gecersiz
        s.Alive = false;
        s.Target = null;
        s.Field = null;
        s.Curve = null;
        s.OnDone = null;
        if (_freeCount == _free.Length)
            Array.Resize(ref _free, _freeCount * 2);
        _free[_freeCount++] = index;
    }
```

### method Cancel
source: engine/managed/Tween.cs:305
symbol: DigitoyEngine.TweenPool.Cancel(int, int)
score: 176
calls: DigitoyEngine.TweenPool.IsActive(int, int), DigitoyEngine.TweenPool.Kill(ref DigitoyEngine.TweenPool.Slot, int)
called_by: DigitoyEngine.TweenHandle.Cancel()
```csharp
internal void Cancel(int i, int gen)
    {
        if (IsActive(i, gen))
            Kill(ref _slots[i], i);
    }
```

### method Tick
source: engine/managed/Tween.cs:125
symbol: DigitoyEngine.TweenPool.Tick(float)
score: 158
calls: DigitoyEngine.Curve.Evaluate(float), DigitoyEngine.Easing.Evaluate(DigitoyEngine.Ease, float), DigitoyEngine.TweenPool.CaptureFrom(ref DigitoyEngine.TweenPool.Slot), DigitoyEngine.TweenPool.Kill(ref DigitoyEngine.TweenPool.Slot, int), DigitoyEngine.TweenPool.Write(ref DigitoyEngine.TweenPool.Slot, float)
called_by: DigitoyEngine.Scene.Update(float, bool)
depends_on: DigitoyEngine.Easing, DigitoyEngine.TweenPool.Slot
```csharp
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
            // Bitti: callback + zincir + slot geri.
            var done = s.OnDone;
            int next = s.Next, nextGen = s.NextGen;
            Kill(ref s, i);
            if (next >= 0 && next < _count && _slots[next].Gen == nextGen)
                _slots[next].Waiting = false;
            done?.Invoke();
        }
    }
```

### method Start
source: engine/managed/Tween.cs:87
symbol: DigitoyEngine.TweenPool.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, DigitoyEngine.SerializedType.FieldSchema, float, float, float, float, float)
score: 140
calls: DigitoyEngine.TweenHandle.TweenHandle(DigitoyEngine.TweenPool, int, int)
called_by: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float), DigitoyEngine.TweenExtensions.TweenField(DigitoyEngine.Component, string, float, float), DigitoyEngine.TweenExtensions.TweenValue(DigitoyEngine.Scene, float, float, float)
depends_on: DigitoyEngine.Component, DigitoyEngine.Ease, DigitoyEngine.SerializedType, DigitoyEngine.SerializedType.FieldSchema, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle, DigitoyEngine.TweenPool.Slot
```csharp
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
```

### struct Slot
source: engine/managed/Tween.cs:56
symbol: DigitoyEngine.TweenPool.Slot
score: 134
```csharp
struct Slot
    {
    }
MEMBERS
field int Gen
field bool Alive
field bool Waiting
field bool HasFrom
field bool Yoyo
field bool Forward
field TweenChannel Channel
field Ease Ease
field short LoopsLeft
field float Delay
field float T
field float Duration
field float FromX, FromY, FromZ, FromW
field float ToX, ToY, ToZ, ToW
field Component Target
field SerializedType.FieldSchema Field
field Curve Curve
field Action OnDone
field int Next
field int NextGen
```

### class TweenPool
source: engine/managed/Tween.cs:54
symbol: DigitoyEngine.TweenPool
score: 129
```csharp
public sealed class TweenPool
{
}
MEMBERS
struct Slot
field Slot[] _slots = new Slot[64]
field int _count
field int[] _free = new int[64]
field int _freeCount
method Start(Component target, TweenChannel ch, SerializedType.FieldSchema field,
        float toX, float toY, float toZ, float toW, float duration)
method Tick(float dt)
method CaptureFrom(ref Slot s)
method Tr(ref Slot s)
method Write(ref Slot s, float e)
method L(float a, float b, float t)
method Kill(ref Slot s, int index)
method IsActive(int i, int gen)
method ValueOf(int i, int gen)
method Cancel(int i, int gen)
method SetEase(int i, int gen, Ease e)
method SetCurve(int i, int gen, Curve c)
method SetDelay(int i, int gen, float d)
method SetFrom(int i, int gen, float x, float y, float z, float w)
method SetLoops(int i, int gen, int count, bool yoyo)
method SetOnDone(int i, int gen, Action done)
method Link(int i, int gen, int nextI, int nextGen)
```

### method Cancel
source: engine/managed/Tween.cs:36
symbol: DigitoyEngine.TweenHandle.Cancel()
score: 128
calls: DigitoyEngine.TweenPool.Cancel(int, int)
```csharp
public void Cancel() => Pool?.Cancel(Index, Gen);
```

### method CaptureFrom
source: engine/managed/Tween.cs:178
symbol: DigitoyEngine.TweenPool.CaptureFrom(ref DigitoyEngine.TweenPool.Slot)
score: 122
calls: DigitoyEngine.TweenPool.Tr(ref DigitoyEngine.TweenPool.Slot)
called_by: DigitoyEngine.TweenPool.Tick(float)
depends_on: DigitoyEngine.Color, DigitoyEngine.SpriteRenderer, DigitoyEngine.Transform.localEulerAngles, DigitoyEngine.Transform.localPosition, DigitoyEngine.Transform.localScale, DigitoyEngine.TweenChannel, DigitoyEngine.TweenPool.Slot
```csharp
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
```

### method Write
source: engine/managed/Tween.cs:202
symbol: DigitoyEngine.TweenPool.Write(ref DigitoyEngine.TweenPool.Slot, float)
score: 122
calls: DigitoyEngine.TweenPool.L(float, float, float), DigitoyEngine.TweenPool.Tr(ref DigitoyEngine.TweenPool.Slot), DigitoyEngine.Vec3.Vec3(float, float, float)
called_by: DigitoyEngine.TweenPool.Tick(float)
depends_on: DigitoyEngine.Color, DigitoyEngine.SpriteRenderer, DigitoyEngine.Transform, DigitoyEngine.Transform.localEulerAngles, DigitoyEngine.Transform.localPosition, DigitoyEngine.Transform.localScale, DigitoyEngine.TweenChannel, DigitoyEngine.TweenPool.Slot
```csharp
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
```

### method Then
source: engine/managed/Tween.cs:46
symbol: DigitoyEngine.TweenHandle.Then(DigitoyEngine.TweenHandle)
score: 116
calls: DigitoyEngine.TweenPool.Link(int, int, int, int)
depends_on: DigitoyEngine.TweenHandle
```csharp
// Zincir: bu tween bitince digeri baslar (uyuyan slot uyanir) — nesne yok.
    public TweenHandle Then(TweenHandle next)
    {
        if (Pool != null && next.Pool == Pool)
            Pool.Link(Index, Gen, next.Index, next.Gen);
        return next;
    }
```

### struct TweenHandle
source: engine/managed/Tween.cs:20
symbol: DigitoyEngine.TweenHandle
score: 105
```csharp
public readonly struct TweenHandle
{
}
MEMBERS
field TweenPool Pool
field int Index
field int Gen
constructor TweenHandle(TweenPool pool, int index, int gen)
property bool IsActive
property float Value
method Cancel()
method Ease(Ease e)
method WithCurve(Curve c)
method Delay(float seconds)
method From(float v)
method Loops(int count, bool yoyo = false)
method OnDone(Action done)
method Then(TweenHandle next)
```

### method Tr
source: engine/managed/Tween.cs:200
symbol: DigitoyEngine.TweenPool.Tr(ref DigitoyEngine.TweenPool.Slot)
score: 102
called_by: DigitoyEngine.TweenPool.CaptureFrom(ref DigitoyEngine.TweenPool.Slot), DigitoyEngine.TweenPool.Write(ref DigitoyEngine.TweenPool.Slot, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenPool.Slot
```csharp
static Transform Tr(ref Slot s) => s.Target._gameObject.transform;
```

### constructor TweenHandle
source: engine/managed/Tween.cs:26
symbol: DigitoyEngine.TweenHandle.TweenHandle(DigitoyEngine.TweenPool, int, int)
score: 102
called_by: DigitoyEngine.TweenPool.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, DigitoyEngine.SerializedType.FieldSchema, float, float, float, float, float)
depends_on: DigitoyEngine.TweenPool
```csharp
internal TweenHandle(TweenPool pool, int index, int gen)
    {
        Pool = pool;
        Index = index;
        Gen = gen;
    }
```

### method TweenMoveX
source: engine/managed/Tween.cs:360
symbol: DigitoyEngine.TweenExtensions.TweenMoveX(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenMoveX(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.PosX, to, duration);
```

### method TweenMoveY
source: engine/managed/Tween.cs:363
symbol: DigitoyEngine.TweenExtensions.TweenMoveY(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenMoveY(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.PosY, to, duration);
```

### method TweenRotation
source: engine/managed/Tween.cs:366
symbol: DigitoyEngine.TweenExtensions.TweenRotation(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenRotation(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.Rot, to, duration);
```

### method TweenScaleX
source: engine/managed/Tween.cs:369
symbol: DigitoyEngine.TweenExtensions.TweenScaleX(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenScaleX(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleX, to, duration);
```

### method TweenScaleY
source: engine/managed/Tween.cs:372
symbol: DigitoyEngine.TweenExtensions.TweenScaleY(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenScaleY(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleY, to, duration);
```

### method TweenScale
source: engine/managed/Tween.cs:375
symbol: DigitoyEngine.TweenExtensions.TweenScale(DigitoyEngine.Transform, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Transform, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenScale(this Transform tr, float to, float duration)
        => Start(tr, TweenChannel.ScaleUniform, to, duration);
```

### method TweenAlpha
source: engine/managed/Tween.cs:378
symbol: DigitoyEngine.TweenExtensions.TweenAlpha(DigitoyEngine.SpriteRenderer, float, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.SpriteRenderer, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenAlpha(this SpriteRenderer sr, float to01, float duration)
        => Start(sr, TweenChannel.Alpha, to01, duration);
```

### method TweenColor
source: engine/managed/Tween.cs:381
symbol: DigitoyEngine.TweenExtensions.TweenColor(DigitoyEngine.SpriteRenderer, DigitoyEngine.Color, float)
score: 94
calls: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float)
depends_on: DigitoyEngine.Color, DigitoyEngine.SpriteRenderer, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
public static TweenHandle TweenColor(this SpriteRenderer sr, Color to, float duration)
        => Start(sr, TweenChannel.ColorRgb, to.r, duration, to.g, to.b);
```

### method Link
source: engine/managed/Tween.cs:335
symbol: DigitoyEngine.TweenPool.Link(int, int, int, int)
score: 94
calls: DigitoyEngine.TweenPool.IsActive(int, int)
called_by: DigitoyEngine.TweenHandle.Then(DigitoyEngine.TweenHandle)
```csharp
internal void Link(int i, int gen, int nextI, int nextGen)
    {
        if (!IsActive(i, gen) || !IsActive(nextI, nextGen))
            return;
        _slots[i].Next = nextI;
        _slots[i].NextGen = nextGen;
        _slots[nextI].Waiting = true;
    }
```

### method TweenValue
source: engine/managed/Tween.cs:399
symbol: DigitoyEngine.TweenExtensions.TweenValue(DigitoyEngine.Scene, float, float, float)
score: 94
calls: DigitoyEngine.TweenHandle.From(float), DigitoyEngine.TweenPool.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, DigitoyEngine.SerializedType.FieldSchema, float, float, float, float, float)
depends_on: DigitoyEngine.Scene, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle
```csharp
// Hedefsiz deger tween'i: kullanici handle.Value okur (callback yok = alloc yok).
    public static TweenHandle TweenValue(this Scene scene, float from, float to, float duration)
    {
        var h = scene.Tweens.Start(null, TweenChannel.Value, null, to, 0, 0, 0, duration);
        return h.From(from);
    }
```

### method TweenField
source: engine/managed/Tween.cs:385
symbol: DigitoyEngine.TweenExtensions.TweenField(DigitoyEngine.Component, string, float, float)
score: 94
calls: DigitoyEngine.SerializedType.EnsureFloatAccessors(DigitoyEngine.SerializedType.FieldSchema), DigitoyEngine.SerializedType.Find(DigitoyEngine.SerializedType.FieldSchema[], string), DigitoyEngine.TweenExtensions.PoolOf(DigitoyEngine.Component), DigitoyEngine.TweenPool.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, DigitoyEngine.SerializedType.FieldSchema, float, float, float, float, float), DigitoyEngine.TypeCatalog.Find(System.Type)
depends_on: DigitoyEngine.Component, DigitoyEngine.SerializedType, DigitoyEngine.SerializedType.FieldSchema, DigitoyEngine.SerializedType.Kind, DigitoyEngine.TweenChannel, DigitoyEngine.TweenHandle, DigitoyEngine.TweenPool, DigitoyEngine.TypeCatalog
```csharp
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
```

### field _slots
source: engine/managed/Tween.cs:80
symbol: _slots
score: 90
depends_on: DigitoyEngine.TweenPool.Slot
```csharp
Slot[] _slots = new Slot[64];
```

### method PoolOf
source: engine/managed/Tween.cs:348
symbol: DigitoyEngine.TweenExtensions.PoolOf(DigitoyEngine.Component)
score: 90
called_by: DigitoyEngine.TweenExtensions.Start(DigitoyEngine.Component, DigitoyEngine.TweenChannel, float, float, float, float), DigitoyEngine.TweenExtensions.TweenField(DigitoyEngine.Component, string, float, float)
depends_on: DigitoyEngine.Component, DigitoyEngine.TweenPool
```csharp
static TweenPool PoolOf(Component c)
        => c?._gameObject?._scene?.Tweens;
```

### field Pool
source: engine/managed/Tween.cs:22
symbol: Pool
score: 68
depends_on: DigitoyEngine.TweenPool
```csharp
internal readonly TweenPool Pool;
```

## BUDGET
token_budget: 6000
