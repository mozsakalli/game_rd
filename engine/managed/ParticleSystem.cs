using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Editor preview kancasi: edit modunda Update kosmaz; PreviewSession sahibi bu
// arayuzu uyguluyorsa her frame kendi saatiyle adimlar (simulasyon tetiklenir).
public interface IEditorPreview
{
    void PreviewStep(float dt);
}

public enum ParticleSpace : byte { Local, World }
public enum ParticleShapeType : byte { Point, Circle, Sphere, Box, Cone, Edge }
public enum ParticleRenderMode : byte { Billboard, Stretched }
public enum ParticleStopAction : byte { None, Disable, Destroy }
public enum LifeCurveMode : byte { Constant, Ease, Curve }
public enum ParticleFrameMode : byte { Lifetime, Fps, RandomFrame }

// min..max arasi rastgele baslangic degeri (Unity MinMax sabit ikilisi).
[Serializable]
public struct FloatRange
{
    public float min, max;
    public FloatRange(float v) { min = max = v; }
    public FloatRange(float min, float max) { this.min = min; this.max = max; }
    internal readonly float Sample(ref uint rng)
        => min == max ? min : min + (max - min) * ParticleSystem.Rand01(ref rng);
}

[Serializable]
public struct Vec3Range
{
    public Vec3 min, max;
    public Vec3Range(Vec3 v) { min = max = v; }
    public Vec3Range(Vec3 min, Vec3 max) { this.min = min; this.max = max; }
    internal readonly Vec3 Sample(ref uint rng)
    {
        float x = min.x + (max.x - min.x) * ParticleSystem.Rand01(ref rng);
        float y = min.y + (max.y - min.y) * ParticleSystem.Rand01(ref rng);
        float z = min.z + (max.z - min.z) * ParticleSystem.Rand01(ref rng);
        return new Vec3(x, y, z);
    }
}

// Omur boyunca carpan: Constant=1, Ease=from->to (LUT), Curve=authored egri.
// randomize: parcacik tohumuyla 0..randomize orani kadar asagi ceker (iki egri
// arasi rastgele muadili, tek carpim).
[Serializable]
public struct LifeCurve
{
    public LifeCurveMode mode;
    public float from;
    public float to;
    public Ease ease;
    public Curve curve;
    public float randomize;

    [NonSerialized] int _curveSig;

    public static LifeCurve One => new() { from = 1f, to = 1f };
    public static LifeCurve EaseTo(float from, float to, Ease e = Ease.Linear)
        => new() { mode = LifeCurveMode.Ease, from = from, to = to, ease = e };

    public readonly bool Active => mode != LifeCurveMode.Constant || randomize != 0f;

    public readonly float Evaluate(float t, float seed)
    {
        float v = mode switch
        {
            LifeCurveMode.Ease => from + (to - from) * Easing.Evaluate(ease, t),
            LifeCurveMode.Curve => curve != null ? curve.Evaluate(t) : 1f,
            _ => 1f,
        };
        return randomize != 0f ? v * (1f - randomize * seed) : v;
    }

    // Inspector alanlara dogrudan yazar (Version artmaz): key imzasi degistiyse
    // Curve LUT'unu tazele. Frame'de bir kez, parcacik basina degil.
    internal void Refresh()
    {
        if (mode != LifeCurveMode.Curve || curve == null)
            return;
        int sig = ParticleSystem.CurveSignature(curve);
        if (sig == _curveSig)
            return;
        _curveSig = sig;
        curve.Version++;
    }
}

[Serializable]
public sealed class GradientKey
{
    public float time;
    public Color color = Color.White;
}

// Cok duraklı renk+alfa gradyani (omur boyunca renk). 64 girisli Color LUT'una
// pisirilir; key imzasi degisince (Inspector dogrudan yazar) yeniden pisirilir.
[Serializable]
public sealed class ColorGradient
{
    public List<GradientKey> keys = new();

    [NonSerialized] Color[] _lut;
    [NonSerialized] int _sig = int.MinValue;
    const int Table = 64;

    public bool Active => keys.Count > 0;

    public ColorGradient Add(float time, Color c)
    {
        keys.Add(new GradientKey { time = time, color = c });
        return this;
    }

    // Frame'de bir kez: imza kiyasi + gerekirse pisirme (hot path Evaluate'e girmez).
    internal void Refresh()
    {
        int sig = 17;
        var k = keys;
        for (int i = 0; i < k.Count; i++)
        {
            var key = k[i];
            if (key == null) continue;
            sig = sig * 31 + BitConverter.SingleToInt32Bits(key.time);
            sig = sig * 31 + (key.color.r | key.color.g << 8 | key.color.b << 16 | key.color.a << 24);
        }
        if (sig == _sig && _lut != null)
            return;
        _sig = sig;
        _lut ??= new Color[Table];
        for (int i = 0; i < Table; i++)
            _lut[i] = Exact(i / (float)(Table - 1));
    }

    public Color Evaluate(float t)
    {
        var lut = _lut;
        if (lut == null)
            return Color.White;
        float f = Math.Clamp(t, 0f, 1f) * (Table - 1);
        int i = (int)f;
        if (i >= Table - 1)
            return lut[Table - 1];
        f -= i;
        Color a = lut[i], b = lut[i + 1];
        return new(
            (byte)(a.r + (b.r - a.r) * f), (byte)(a.g + (b.g - a.g) * f),
            (byte)(a.b + (b.b - a.b) * f), (byte)(a.a + (b.a - a.a) * f));
    }

    // Siralanmamis key listesinde t'yi saran ikiliyi tarar (yalniz pisirmede).
    Color Exact(float t)
    {
        GradientKey lo = null, hi = null;
        var k = keys;
        for (int i = 0; i < k.Count; i++)
        {
            var key = k[i];
            if (key == null) continue;
            if (key.time <= t && (lo == null || key.time > lo.time)) lo = key;
            if (key.time > t && (hi == null || key.time < hi.time)) hi = key;
        }
        if (lo == null && hi == null) return Color.White;
        if (lo == null) return hi.color;
        if (hi == null) return lo.color;
        float span = hi.time - lo.time;
        float u = span > 1e-6f ? (t - lo.time) / span : 0f;
        Color a = lo.color, b = hi.color;
        return new(
            (byte)(a.r + (b.r - a.r) * u), (byte)(a.g + (b.g - a.g) * u),
            (byte)(a.b + (b.b - a.b) * u), (byte)(a.a + (b.a - a.a) * u));
    }
}

[Serializable]
public sealed class ParticleBurst
{
    public float time;
    public int count = 10;
    public int countMax;        // >count ise count..countMax rastgele
    public int cycles = 1;      // 0 = sonsuz (interval ile)
    public float interval = 0.1f;
    public float probability = 1f;
}

[Serializable]
public struct EmissionModule
{
    public float rateOverTime;
    public float rateOverDistance;  // dunya birimi basina (emitter hareketi)
    public List<ParticleBurst> bursts;

    public static EmissionModule Default => new() { rateOverTime = 10f };
}

// Emisyon sekli. "Ileri" yon -Y'dir (y-asagi dunyada ekran yukarisi): Point/Box/
// Edge bu yone, Cone bu eksen etrafinda aciyla, Circle/Sphere merkezden disa firlatir.
// randomDirection 0..1 sekil yonunu rastgele yonle harmanlar (1 = tamamen rastgele).
[Serializable]
public struct ShapeModule
{
    public ParticleShapeType type;
    public float radius;
    public float radiusThickness;   // 0 = yalniz yuzey/kenar, 1 = tum hacim
    public float arc;               // Circle: derece
    public Vec3 boxSize;
    public float coneAngle;         // yarim aci, derece
    public float coneLength;        // >0: taban diskinden bu kadar ileriye kadar hacimden
    public Vec3 offset;
    public Vec3 rotation;           // sekil euler (derece)
    public float randomDirection;
    public bool emit3D;             // yonler/konumlar z eksenini de kullanir
    public bool alignToDirection;   // dogum rotasyonu z = firlatma yonu

    public static ShapeModule Default => new()
    {
        type = ParticleShapeType.Cone,
        radius = 4f,
        radiusThickness = 1f,
        arc = 360f,
        boxSize = new Vec3(100f, 100f, 0f),
        coneAngle = 25f,
    };
}

[Serializable]
public struct VelocityModule
{
    public Vec3 gravity;            // birim/s^2 (y-asagi dunyada +y asagi duser)
    public Vec3 force;              // sabit ivme (ruzgar)
    public float drag;              // 1/s: hiz *= (1 - drag*dt)
    public float orbitalSpeed;      // derece/s, emitter merkezi etrafinda (z ekseni)
    public float radialSpeed;       // birim/s, merkezden disa (+) / ice (-)
    public Vec3Range linear;        // doguma eklenen rastgele hiz
    public LifeCurve speed;         // omur boyunca hiz carpani

    public static VelocityModule Default => new() { speed = LifeCurve.One };
}

// Ucuz prosedurel turbulans: konum+zaman tabanli sin/cos alanı (gercek curl
// noise degil; parcacik basina 4 trig). strength 0 = kapali (sifir maliyet).
[Serializable]
public struct NoiseModule
{
    public float strength;          // birim/s^2
    public float frequency;         // 1/birim (piksel dunyada ~0.01)
    public float scrollSpeed;       // alanin zamanla kaymasi
    public static NoiseModule Default => new() { frequency = 0.01f, scrollSpeed = 1f };
}

[Serializable]
public struct RotationModule
{
    public Vec3Range angularVelocity;   // derece/s, xyz
    public LifeCurve speed;             // acisal hiz carpani
    public bool alignToVelocity;        // z rotasyonu her frame hiz yonune
    public float alignOffset;           // derece (sprite yukari ciziliyse 90)

    public static RotationModule Default => new() { speed = LifeCurve.One };
}

// Kare animasyonu: ParticleSystem.sprites listesi uzerinde (her kare ayri Sprite —
// atlas bolgesi olabilir; farkli sayfalar yalniz draw'i boler, calismaya devam eder).
[Serializable]
public struct FrameModule
{
    public ParticleFrameMode mode;
    public float cycles;            // Lifetime: omur boyunca tur sayisi
    public float fps;               // Fps modu
    public FloatRange startFrame;

    public static FrameModule Default => new() { cycles = 1f, fps = 12f };
}

// Yuksek performansli 2D/3D parcacik sistemi: tek component, modul struct'lari.
// Parcaciklar onceden ayrilmis duz dizide (AoS, swap-remove), simulasyon ve
// emisyon frame'de SIFIR alloc; cizim = parcacik basina Mesh.Quad instanced
// DrawMesh (ayni materyal/doku = RenderQueue merge ile TEK draw call).
// Rotasyon xyz (3D), blend/efekt Renderer alanlarindan, ozel Material serbest.
[Previewable]
public sealed unsafe class ParticleSystem : Renderer, IEditorPreview
{
    // --- Ana ---
    public float duration = 5f;
    public bool looping = true;
    public bool prewarm;
    public float startDelay;
    public bool playOnAwake = true;
    public float simulationSpeed = 1f;
    public ParticleSpace simulationSpace;
    public int maxParticles = 1000;
    public int randomSeed;                  // 0 = her Play'de farkli
    public ParticleStopAction stopAction;
    public FloatRange startLifetime = new(1f, 1.5f);
    public FloatRange startSpeed = new(80f, 120f);
    public FloatRange startSize = new(16f, 24f);
    public float aspect = 1f;               // yukseklik = boyut * aspect; 0 = sprite'in dogal orani
    public Vec3Range startRotation;         // derece
    public Gradient startColor = Color.White; // Linear: color..color2 arasi rastgele

    // --- Cizim ---
    // Kare listesi: bos = beyaz kare, 1 = sabit sprite, >1 = frames modulune gore animasyon.
    public List<Sprite> sprites = new();
    public Material material;               // null = paylasilan varsayilan (sprite sayfasi)
    public ParticleRenderMode renderMode;
    public float lengthScale = 1f;          // Stretched: boyut carpani (hiz yonunde)
    public float speedScale;                // Stretched: + hiz * bu

    // --- Moduller ---
    public EmissionModule emission = EmissionModule.Default;
    public ShapeModule shape = ShapeModule.Default;
    public VelocityModule velocity = VelocityModule.Default;
    public NoiseModule noise = NoiseModule.Default;
    public ColorGradient colorOverLifetime = new();
    public LifeCurve sizeOverLifetime = LifeCurve.One;
    public RotationModule rotation = RotationModule.Default;
    public FrameModule frames = FrameModule.Default;
    public ParticleSystem subEmitterOnDeath;
    public int subEmitterCount = 5;

    // Kare basina cozulmus cizim verisi (Encode basinda sprites'tan doldurulur;
    // yalniz liste uzunlugu degisince yeniden ayrilir). Trim: SpriteRenderer paritesi.
    struct FrameUv
    {
        public Texture Page;
        public float U0, V0, U1, V1;
        public float Sx, Sy, Ox, Oy;    // kirpilmis parca olcek + merkez ofseti (mantiksal birim)
        public float Aspect;            // OrigH/OrigW
    }
    FrameUv[] _frames = Array.Empty<FrameUv>();

    struct Particle
    {
        public Vec3 pos, vel, rot, angVel;
        public float age, life, invLife;
        public float size, seed, frame0;
        public Color color;
    }

    Particle[] _p = Array.Empty<Particle>();
    int _count;
    bool _playing, _emitting, _paused, _startedOnce;
    float _time, _delayLeft, _emitAcc, _distAcc;
    Vec3 _lastWorldPos;
    bool _hasLastPos;
    int[] _burstFired = Array.Empty<int>();
    uint _rng = 0x9E3779B9u;
    Mat4 _shapeM;
    Vec3 _shapeRotCached = new(float.NaN, 0, 0);

    static Material _shared;
    static Material Shared => _shared ??= new Material();
    static Texture _white;
    static Texture White => _white ??= MakeWhite();
    static Texture MakeWhite()
    {
        var t = Texture.FromColor(1, 1, Color.White);
        t.Persistent = true;
        return t;
    }

    const float Deg2Rad = MathF.PI / 180f;
    const float Rad2Deg = 180f / MathF.PI;

    // --- Durum ---
    public bool IsPlaying => _playing && !_paused;
    public bool IsPaused => _paused;
    public bool IsEmitting => _playing && _emitting;
    public int ParticleCount => _count;
    public float PlaybackTime => _time;
    public bool IsAlive => _playing || _count > 0;

    // --- Kontrol ---
    public void Play()
    {
        EnsureCapacity();
        _playing = true;
        _emitting = true;
        _paused = false;
        _time = 0f;
        _delayLeft = startDelay;
        _emitAcc = 0f;
        _distAcc = 0f;
        _hasLastPos = false;
        _rng = randomSeed != 0 ? (uint)randomSeed : (uint)Environment.TickCount * 2654435761u | 1u;
        ResetBursts();
        FireBursts(-1e-6f, 0f);
        if (prewarm && looping && duration > 0f)
        {
            const float step = 1f / 30f;
            float left = duration;
            while (left > 0f)
            {
                float d = left < step ? left : step;
                Step(d);
                left -= d;
            }
        }
    }

    // clear=false: emisyon durur, canli parcaciklar omurlerini tamamlar.
    public void Stop(bool clear = false)
    {
        _emitting = false;
        if (clear)
        {
            _count = 0;
            _playing = false;
        }
    }

    public void Pause() => _paused = _playing;
    public void Resume() => _paused = false;
    public void Clear() => _count = 0;

    public void Emit(int count)
    {
        EnsureCapacity();
        for (int i = 0; i < count && _count < _p.Length; i++)
            Spawn(ref _p[_count++], 0f, false, default);
    }

    // Belirli dunya noktasindan (sekil ofseti o noktaya gore).
    public void Emit(int count, Vec3 worldPos)
    {
        EnsureCapacity();
        for (int i = 0; i < count && _count < _p.Length; i++)
            Spawn(ref _p[_count++], 0f, true, worldPos);
    }

    // Elle adimlama (test/preview/ozel saat). dt saniye, simulationSpeed uygulanir.
    public void Simulate(float dt) => Step(dt * simulationSpeed);

    void IEditorPreview.PreviewStep(float dt)
    {
        if (!_playing)
            Play();
        Simulate(dt);
    }

    // --- Yasam dongusu ---
    protected internal override void Start()
    {
        _startedOnce = true;
        if (playOnAwake && _gameObject._scene.Simulating)
            Play();
    }

    protected internal override void OnEnable()
    {
        if (_startedOnce && playOnAwake && _gameObject._scene.Simulating)
            Play();
    }

    protected internal override void OnDisable()
    {
        _count = 0;
        _playing = false;
        _paused = false;
    }

    protected internal override void Update()
    {
        if (!_playing || _paused)
            return;
        Step(Time.deltaTime * simulationSpeed);
    }

    protected internal override void OnValidate()
    {
        if (maxParticles < 1) maxParticles = 1;
        if (simulationSpeed < 0f) simulationSpeed = 0f;
    }

    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = new Vec2(shape.offset.x, shape.offset.y);
        float r = shape.type switch
        {
            ParticleShapeType.Box => MathF.Max(shape.boxSize.x, shape.boxSize.y) * 0.5f,
            ParticleShapeType.Point => 0f,
            _ => shape.radius,
        };
        if (r < 20f) r = 20f;
        halfSize = new Vec2(r, r);
        return true;
    }

    // --- Simulasyon ---
    void EnsureCapacity()
    {
        int cap = maxParticles < 1 ? 1 : maxParticles;
        if (_p.Length == cap)
            return;
        var np = new Particle[cap];
        if (_count > cap) _count = cap;
        Array.Copy(_p, np, _count);
        _p = np;
    }

    void ResetBursts()
    {
        int n = emission.bursts?.Count ?? 0;
        if (_burstFired.Length != n)
            _burstFired = n == 0 ? Array.Empty<int>() : new int[n];
        else
            Array.Clear(_burstFired);
    }

    // (t0, t1] araligina dusen burst dongulerini atesler.
    void FireBursts(float t0, float t1)
    {
        var bursts = emission.bursts;
        if (bursts == null)
            return;
        if (_burstFired.Length != bursts.Count)
            ResetBursts();
        for (int i = 0; i < bursts.Count; i++)
        {
            var b = bursts[i];
            if (b == null) continue;
            int fired = _burstFired[i];
            // cycles<=0 = sonsuz; sifir aralikla sonsuz dongu olmasin diye tek atis.
            int cycles = b.cycles > 0 ? b.cycles : (b.interval > 0f ? int.MaxValue : 1);
            while (fired < cycles)
            {
                float at = b.time + fired * b.interval;
                if (at > t1) break;
                fired++;
                if (at <= t0) continue;
                if (b.probability < 1f && Rand01(ref _rng) > b.probability) continue;
                int n = b.countMax > b.count ? b.count + (int)(Rand01(ref _rng) * (b.countMax - b.count + 1)) : b.count;
                float ageOff = t1 - at; // frame icinde erken dogan parcacik o kadar yol alir
                for (int k = 0; k < n && _count < _p.Length; k++)
                    Spawn(ref _p[_count++], ageOff, false, default);
            }
            _burstFired[i] = fired;
        }
    }

    void Step(float dt)
    {
        if (dt <= 0f)
            return;
        if (_p.Length == 0)
            EnsureCapacity();
        // Omur carpanlari + gradyan frame'de bir kez tazelenir (Inspector dogrudan yazar).
        velocity.speed.Refresh();
        rotation.speed.Refresh();

        // ONCE mevcut parcaciklar ilerler, SONRA yeni dogumlar: Spawn dogum yasini
        // (frame ici ofset) kendisi uygular, ayni frame'de ikinci kez ilerlemez.
        Advance(dt);

        if (_playing && _emitting)
        {
            if (_delayLeft > 0f)
            {
                _delayLeft -= dt;
                if (_delayLeft > 0f) return;
                dt = -_delayLeft;
                _delayLeft = 0f;
                if (dt <= 0f) return;
            }
            float t0 = _time;
            float t1 = t0 + dt;
            // Zamana bagli emisyon (frame icine yayilmis dogum yasi)
            float rate = emission.rateOverTime;
            if (rate > 0f)
            {
                _emitAcc += rate * dt;
                int n = (int)_emitAcc;
                if (n > 0)
                {
                    _emitAcc -= n;
                    float inv = dt / n;
                    for (int i = 0; i < n && _count < _p.Length; i++)
                        Spawn(ref _p[_count++], dt - (i + 0.5f) * inv, false, default);
                }
            }
            // Mesafeye bagli emisyon (emitter dunya konumu)
            if (emission.rateOverDistance > 0f)
            {
                ref var w = ref transform._getWorldMatrix();
                var wp = new Vec3(w.m[12], w.m[13], w.m[14]);
                if (_hasLastPos)
                {
                    _distAcc += Vec3.Distance(wp, _lastWorldPos) * emission.rateOverDistance;
                    int n = (int)_distAcc;
                    if (n > 0)
                    {
                        _distAcc -= n;
                        for (int i = 0; i < n && _count < _p.Length; i++)
                            Spawn(ref _p[_count++], 0f, false, default);
                    }
                }
                _lastWorldPos = wp;
                _hasLastPos = true;
            }
            if (duration > 0f && t1 >= duration)
            {
                FireBursts(t0, duration);
                if (looping)
                {
                    ResetBursts();
                    t1 -= duration;
                    if (t1 >= duration) t1 = 0f; // asiri buyuk dt guard'i
                    FireBursts(-1e-6f, t1);
                }
                else
                {
                    _emitting = false;
                    t1 = duration;
                }
            }
            else
            {
                FireBursts(t0, t1);
            }
            _time = t1;
        }

        if (_playing && !_emitting && _count == 0)
        {
            _playing = false;
            switch (stopAction)
            {
                case ParticleStopAction.Disable: gameObject.SetActive(false); break;
                case ParticleStopAction.Destroy: Destroy(gameObject); break;
            }
        }
    }

    // Tum canli parcaciklari dt kadar ilerletir (olum = swap-remove).
    void Advance(float dt)
    {
        int count = _count;
        if (count == 0)
            return;
        var p = _p;
        var vel = velocity;
        Vec3 acc = vel.gravity + vel.force;
        float ax = acc.x * dt, ay = acc.y * dt, az = acc.z * dt;
        bool hasAcc = ax != 0f || ay != 0f || az != 0f;
        float dragK = 1f - vel.drag * dt;
        if (dragK < 0f) dragK = 0f;
        bool hasDrag = dragK != 1f;
        bool useSpd = vel.speed.Active;
        bool useRotSpd = rotation.speed.Active;

        bool orbital = vel.orbitalSpeed != 0f;
        bool radial = vel.radialSpeed != 0f;
        float cx = 0f, cy = 0f;
        if ((orbital || radial) && simulationSpace == ParticleSpace.World)
        {
            ref var w = ref transform._getWorldMatrix();
            cx = w.m[12]; cy = w.m[13];
        }
        float oc = 1f, os = 0f;
        if (orbital)
        {
            float a = vel.orbitalSpeed * Deg2Rad * dt;
            oc = MathF.Cos(a); os = MathF.Sin(a);
        }
        float radStep = vel.radialSpeed * dt;

        bool useNoise = noise.strength != 0f;
        float nf = noise.frequency, nt = _time * noise.scrollSpeed, ns = noise.strength * dt;
        bool noise3D = shape.emit3D;

        bool hasSub = subEmitterOnDeath != null && !ReferenceEquals(subEmitterOnDeath, this) && subEmitterCount > 0;

        int i = 0;
        while (i < count)
        {
            ref var q = ref p[i];
            q.age += dt;
            if (q.age >= q.life)
            {
                if (hasSub)
                    subEmitterOnDeath.Emit(subEmitterCount, ToWorld(q.pos));
                count--;
                p[i] = p[count];
                continue;
            }
            float t = q.age * q.invLife;

            if (hasAcc) { q.vel.x += ax; q.vel.y += ay; q.vel.z += az; }
            if (useNoise)
            {
                float ph = q.seed * 6.2831853f;
                float px = q.pos.x * nf, py = q.pos.y * nf;
                float nx = MathF.Sin(py + nt + ph) * MathF.Cos(px * 0.5f + nt * 0.7f);
                float ny = MathF.Cos(px + nt * 1.1f + ph) * MathF.Sin(py * 0.5f - nt * 0.8f);
                q.vel.x += nx * ns;
                q.vel.y += ny * ns;
                if (noise3D)
                    q.vel.z += MathF.Sin(px * 0.7f + py * 0.3f + nt * 0.9f + ph) * ns;
            }
            if (hasDrag) { q.vel.x *= dragK; q.vel.y *= dragK; q.vel.z *= dragK; }

            float s = useSpd ? vel.speed.Evaluate(t, q.seed) * dt : dt;
            q.pos.x += q.vel.x * s;
            q.pos.y += q.vel.y * s;
            q.pos.z += q.vel.z * s;

            if (orbital || radial)
            {
                float dx = q.pos.x - cx, dy = q.pos.y - cy;
                if (orbital)
                {
                    float rx = dx * oc - dy * os, ry = dx * os + dy * oc;
                    dx = rx; dy = ry;
                }
                if (radial)
                {
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len > 1e-6f)
                    {
                        float k = radStep / len;
                        dx += dx * k; dy += dy * k;
                    }
                }
                q.pos.x = cx + dx; q.pos.y = cy + dy;
            }

            float rs = useRotSpd ? rotation.speed.Evaluate(t, q.seed) * dt : dt;
            q.rot.x += q.angVel.x * rs;
            q.rot.y += q.angVel.y * rs;
            q.rot.z += q.angVel.z * rs;
            i++;
        }
        _count = count;
    }

    Vec3 ToWorld(Vec3 local)
    {
        if (simulationSpace == ParticleSpace.World)
            return local;
        ref var w = ref transform._getWorldMatrix();
        return Mat4.TransformPoint(ref w, local);
    }

    // --- Dogum ---
    void Spawn(ref Particle q, float ageOffset, bool atWorld, Vec3 worldPos)
    {
        ref uint rng = ref _rng;
        q.seed = Rand01(ref rng);
        q.life = startLifetime.Sample(ref rng);
        if (q.life < 1e-4f) q.life = 1e-4f;
        q.invLife = 1f / q.life;
        q.age = ageOffset >= q.life ? q.life - 1e-5f : ageOffset;
        q.size = startSize.Sample(ref rng);
        q.color = startColor.type == GradientType.Solid ? startColor.color : startColor.At(Rand01(ref rng));
        q.rot = startRotation.Sample(ref rng);
        q.angVel = rotation.angularVelocity.Sample(ref rng);
        q.frame0 = frames.startFrame.Sample(ref rng);

        SampleShape(ref rng, out Vec3 pos, out Vec3 dir);
        if (shape.rotation.x != 0f || shape.rotation.y != 0f || shape.rotation.z != 0f)
        {
            EnsureShapeMatrix();
            pos = Mat4.TransformVector(ref _shapeM, pos);
            dir = Mat4.TransformVector(ref _shapeM, dir);
        }
        pos += shape.offset;
        float speed = startSpeed.Sample(ref rng);
        Vec3 v = dir * speed;
        var lin = velocity.linear;
        if (lin.min.x != 0f || lin.max.x != 0f || lin.min.y != 0f || lin.max.y != 0f || lin.min.z != 0f || lin.max.z != 0f)
            v += lin.Sample(ref rng);

        if (simulationSpace == ParticleSpace.World)
        {
            ref var w = ref transform._getWorldMatrix();
            if (atWorld)
            {
                pos = Mat4.TransformVector(ref w, pos) + worldPos;
            }
            else
                pos = Mat4.TransformPoint(ref w, pos);
            v = Mat4.TransformVector(ref w, v);
        }
        else if (atWorld)
        {
            ref var w = ref transform._getWorldMatrix();
            if (Mat4.Inverse(ref w, out var inv))
                pos += Mat4.TransformPoint(ref inv, worldPos);
        }

        if (shape.alignToDirection && (v.x != 0f || v.y != 0f))
            q.rot.z += MathF.Atan2(-v.y, v.x) * Rad2Deg;

        q.pos = pos + v * q.age;
        q.vel = v;
    }

    void EnsureShapeMatrix()
    {
        var r = shape.rotation;
        if (r.x == _shapeRotCached.x && r.y == _shapeRotCached.y && r.z == _shapeRotCached.z)
            return;
        _shapeRotCached = r;
        _shapeM = default;
        RotScale(r.x * Deg2Rad, r.y * Deg2Rad, r.z * Deg2Rad, 1f, 1f, ref _shapeM);
        _shapeM.m[15] = 1f;
    }

    void SampleShape(ref uint rng, out Vec3 pos, out Vec3 dir)
    {
        var sh = shape;
        bool d3 = sh.emit3D;
        switch (sh.type)
        {
            default:
            case ParticleShapeType.Point:
                pos = default;
                dir = new Vec3(0f, -1f, 0f);
                break;
            case ParticleShapeType.Edge:
                pos = new Vec3((Rand01(ref rng) * 2f - 1f) * sh.radius, 0f, 0f);
                dir = new Vec3(0f, -1f, 0f);
                break;
            case ParticleShapeType.Box:
                pos = new Vec3(
                    (Rand01(ref rng) - 0.5f) * sh.boxSize.x,
                    (Rand01(ref rng) - 0.5f) * sh.boxSize.y,
                    d3 ? (Rand01(ref rng) - 0.5f) * sh.boxSize.z : 0f);
                dir = new Vec3(0f, -1f, 0f);
                break;
            case ParticleShapeType.Circle:
                {
                    float a = Rand01(ref rng) * sh.arc * Deg2Rad;
                    float ca = MathF.Cos(a), sa = MathF.Sin(a);
                    float r = sh.radius * RadiusFactor(ref rng, sh.radiusThickness);
                    pos = new Vec3(ca * r, sa * r, 0f);
                    dir = new Vec3(ca, sa, 0f);
                    break;
                }
            case ParticleShapeType.Sphere:
                {
                    dir = d3 ? RandomUnit3(ref rng) : RandomUnit2(ref rng);
                    float r = sh.radius * RadiusFactor(ref rng, sh.radiusThickness);
                    pos = dir * r;
                    break;
                }
            case ParticleShapeType.Cone:
                {
                    float half = sh.coneAngle * Deg2Rad;
                    if (d3)
                    {
                        float th = half * MathF.Sqrt(Rand01(ref rng));
                        float ph = Rand01(ref rng) * 6.2831853f;
                        float st = MathF.Sin(th);
                        dir = new Vec3(st * MathF.Cos(ph), -MathF.Cos(th), st * MathF.Sin(ph));
                        float ba = Rand01(ref rng) * 6.2831853f;
                        float br = sh.radius * MathF.Sqrt(Rand01(ref rng));
                        pos = new Vec3(MathF.Cos(ba) * br, 0f, MathF.Sin(ba) * br);
                    }
                    else
                    {
                        float th = (Rand01(ref rng) * 2f - 1f) * half;
                        dir = new Vec3(MathF.Sin(th), -MathF.Cos(th), 0f);
                        pos = new Vec3((Rand01(ref rng) * 2f - 1f) * sh.radius, 0f, 0f);
                    }
                    if (sh.coneLength > 0f)
                        pos += dir * (Rand01(ref rng) * sh.coneLength);
                    break;
                }
        }
        if (sh.randomDirection > 0f)
        {
            Vec3 rd = d3 ? RandomUnit3(ref rng) : RandomUnit2(ref rng);
            if (sh.randomDirection >= 1f)
                dir = rd;
            else
            {
                float k = sh.randomDirection;
                dir = Vec3.Normalize(new Vec3(
                    dir.x + (rd.x - dir.x) * k, dir.y + (rd.y - dir.y) * k, dir.z + (rd.z - dir.z) * k));
            }
        }
    }

    // Alan-duzgun yaricap: thickness 0 = yuzey, 1 = tum disk (sqrt dagilimi).
    static float RadiusFactor(ref uint rng, float thickness)
    {
        if (thickness <= 0f) return 1f;
        float inner = 1f - (thickness > 1f ? 1f : thickness);
        float i2 = inner * inner;
        return MathF.Sqrt(i2 + (1f - i2) * Rand01(ref rng));
    }

    static Vec3 RandomUnit2(ref uint rng)
    {
        float a = Rand01(ref rng) * 6.2831853f;
        return new Vec3(MathF.Cos(a), MathF.Sin(a), 0f);
    }

    static Vec3 RandomUnit3(ref uint rng)
    {
        float z = Rand01(ref rng) * 2f - 1f;
        float a = Rand01(ref rng) * 6.2831853f;
        float r = MathF.Sqrt(1f - z * z);
        return new Vec3(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    // xorshift32: [0,1) 24-bit hassasiyet, alloc'suz, deterministik (seed).
    internal static float Rand01(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return (s >> 8) * (1f / 16777216f);
    }

    internal static int CurveSignature(Curve c)
    {
        int sig = 23;
        var keys = c.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            if (k == null) continue;
            sig = sig * 31 + BitConverter.SingleToInt32Bits(k.Time);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(k.Value);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(k.InTangent);
            sig = sig * 31 + BitConverter.SingleToInt32Bits(k.OutTangent);
        }
        return sig;
    }

    // --- Cizim ---
    internal override void Encode(RenderQueue queue)
    {
        int count = _count;
        if (count == 0)
            return;
        var mat = (material ?? Shared).ForBlend(BlendMode).ForEffects(Effects);
        bool ownMat = material != null;

        // Kare tablosu: sprites -> UV/trim/sayfa (ozel materyalde tam doku, trim yok).
        int nFrames = ResolveFrames(ownMat);
        var fr = _frames;
        if (!ownMat)
            mat.MainTexture = nFrames > 0 ? fr[0].Page : White;
        var frm = frames;
        bool animate = nFrames > 1;
        float fps = frm.fps, cyclesN = frm.cycles * nFrames;
        var fmode = frm.mode;
        bool multiPage = false;
        for (int i = 1; i < nFrames && !multiPage; i++)
            multiPage = !ReferenceEquals(fr[i].Page, fr[0].Page);

        colorOverLifetime.Refresh();
        sizeOverLifetime.Refresh();
        bool useColor = colorOverLifetime.Active;
        bool useSize = sizeOverLifetime.Active;
        bool stretched = renderMode == ParticleRenderMode.Stretched;
        bool alignVel = stretched || rotation.alignToVelocity;
        float alignC = 1f, alignS = 0f;
        if (alignVel && rotation.alignOffset != 0f)
        {
            float a = rotation.alignOffset * Deg2Rad;
            alignC = MathF.Cos(a); alignS = MathF.Sin(a);
        }
        bool local = simulationSpace == ParticleSpace.Local;
        Mat4 world = default;
        if (local)
            world = transform._getWorldMatrix();

        var quad = Mesh.Quad();
        int layer = _effectiveOrder;
        float asp = aspect;
        var p = _p;
        Mat4 model = default;
        model.m[15] = 1f;

        for (int i = 0; i < count; i++)
        {
            ref var q = ref p[i];
            float t = q.age * q.invLife;

            int fi = 0;
            if (animate)
            {
                float f = fmode switch
                {
                    ParticleFrameMode.Fps => q.frame0 + q.age * fps,
                    ParticleFrameMode.RandomFrame => q.frame0,
                    _ => q.frame0 + t * cyclesN,
                };
                fi = (int)f % nFrames;
                if (fi < 0) fi += nFrames;
            }
            float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
            float fsx = 1f, fsy = 1f, fox = 0f, foy = 0f, fasp = 1f;
            if (nFrames > 0)
            {
                ref var F = ref fr[fi];
                u0 = F.U0; v0 = F.V0; u1 = F.U1; v1 = F.V1;
                fsx = F.Sx; fsy = F.Sy; fox = F.Ox; foy = F.Oy; fasp = F.Aspect;
                if (multiPage)
                    mat.MainTexture = F.Page; // DrawMesh aninda yakalanir; ayni sayfa = merge
            }

            float size = useSize ? q.size * sizeOverLifetime.Evaluate(t, q.seed) : q.size;
            float sx = size, sy = size * (asp != 0f ? asp : fasp);
            Color c = q.color;
            if (useColor)
                c = Mul(c, colorOverLifetime.Evaluate(t));

            float cz, sz;
            bool only2D = q.rot.x == 0f && q.rot.y == 0f;
            if (alignVel && (q.vel.x != 0f || q.vel.y != 0f))
            {
                float vl = MathF.Sqrt(q.vel.x * q.vel.x + q.vel.y * q.vel.y);
                float inv = 1f / vl;
                // Kolon-0 = (cz, -sz): x ekseni hiz yonune.
                cz = q.vel.x * inv; sz = -q.vel.y * inv;
                if (alignS != 0f)
                {
                    float c2 = cz * alignC - sz * alignS;
                    sz = sz * alignC + cz * alignS;
                    cz = c2;
                }
                if (stretched)
                    sx = size * lengthScale + vl * speedScale;
                only2D = true;
            }
            else
            {
                float rz = q.rot.z * Deg2Rad;
                cz = MathF.Cos(rz); sz = MathF.Sin(rz);
            }

            if (only2D)
            {
                model.m[0] = cz * sx; model.m[1] = -sz * sx; model.m[2] = 0f;
                model.m[4] = sz * sy; model.m[5] = cz * sy; model.m[6] = 0f;
                model.m[8] = 0f; model.m[9] = 0f; model.m[10] = 1f;
            }
            else
            {
                RotScaleCS(q.rot.x * Deg2Rad, q.rot.y * Deg2Rad, cz, sz, sx, sy, ref model);
            }
            // Trim: mantiksal kutu icinde kirpilmis parcayi otele + olcekle (SpriteRenderer paritesi).
            if (fox != 0f || foy != 0f)
            {
                // Kolonlar boyutu zaten tasir: ofset normalize (0..1) mantiksal kutu orani.
                model.m[12] = q.pos.x + model.m[0] * fox + model.m[4] * foy;
                model.m[13] = q.pos.y + model.m[1] * fox + model.m[5] * foy;
                model.m[14] = q.pos.z + model.m[2] * fox + model.m[6] * foy;
            }
            else
            {
                model.m[12] = q.pos.x; model.m[13] = q.pos.y; model.m[14] = q.pos.z;
            }
            if (fsx != 1f) { model.m[0] *= fsx; model.m[1] *= fsx; model.m[2] *= fsx; }
            if (fsy != 1f) { model.m[4] *= fsy; model.m[5] *= fsy; model.m[6] *= fsy; }
            if (local)
                MulAffine(ref world, ref model, out model);

            queue.DrawMesh(quad, mat, in model, c, c, c, c, default, u0, v0, u1, v1, layer);
        }
    }

    // sprites listesini _frames tablosuna cozer; kare sayisini doner (null/sayfasiz atlanir).
    int ResolveFrames(bool ownMat)
    {
        var list = sprites;
        int n = list?.Count ?? 0;
        if (_frames.Length < n)
            _frames = new FrameUv[n];
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            var s = list[i];
            if (s?.Page == null)
                continue;
            ref var F = ref _frames[k++];
            F.Page = s.Page;
            F.Sx = F.Sy = 1f; F.Ox = F.Oy = 0f; F.Aspect = 1f;
            if (!ownMat && s.IsRegion)
            {
                float aw = s.Page.Width, ah = s.Page.Height;
                F.U0 = s.X / aw; F.V0 = s.Y / ah;
                F.U1 = (s.X + s.W) / aw; F.V1 = (s.Y + s.H) / ah;
                F.Sx = s.W / (float)s.OrigW;
                F.Sy = s.H / (float)s.OrigH;
                F.Ox = (s.OffX + s.W * 0.5f) / s.OrigW - 0.5f;
                F.Oy = 0.5f - (s.OffY + s.H * 0.5f) / s.OrigH;
                F.Aspect = s.OrigH / (float)s.OrigW;
            }
            else
            {
                F.U0 = 0f; F.V0 = 0f; F.U1 = 1f; F.V1 = 1f;
                if (s.Page.Width > 0)
                    F.Aspect = s.Page.Height / (float)s.Page.Width;
            }
        }
        return k;
    }

    static Color Mul(Color a, Color b) => new(
        (byte)((a.r * b.r + 127) / 255), (byte)((a.g * b.g + 127) / 255),
        (byte)((a.b * b.b + 127) / 255), (byte)((a.a * b.a + 127) / 255));

    // Transform ile ayni euler kurali (bkz. Transform._getLocalMatrix 3D dali), z olcegi 1.
    static void RotScale(float rx, float ry, float rz, float sx, float sy, ref Mat4 m)
        => RotScaleCS(rx, ry, MathF.Cos(rz), MathF.Sin(rz), sx, sy, ref m);

    static void RotScaleCS(float rx, float ry, float cz, float sz, float sx, float sy, ref Mat4 m)
    {
        float cx = MathF.Cos(rx), sxn = MathF.Sin(rx);
        float cy = MathF.Cos(ry), syn = MathF.Sin(ry);
        m.m[0] = cy * cz * sx;
        m.m[1] = -cy * sz * sx;
        m.m[2] = syn * sx;
        m.m[4] = (cx * sz + sxn * syn * cz) * sy;
        m.m[5] = (cx * cz - sxn * syn * sz) * sy;
        m.m[6] = -sxn * cy * sy;
        m.m[8] = sxn * sz - cx * syn * cz;
        m.m[9] = sxn * cz + cx * syn * sz;
        m.m[10] = cx * cy;
    }

    // Afin carpim (a,b: son satir 0 0 0 1) — tam Mat4.Multiply'in yarisi kadar is.
    static void MulAffine(ref Mat4 a, ref Mat4 b, out Mat4 o)
    {
        float a0 = a.m[0], a1 = a.m[1], a2 = a.m[2];
        float a4 = a.m[4], a5 = a.m[5], a6 = a.m[6];
        float a8 = a.m[8], a9 = a.m[9], a10 = a.m[10];
        float b0 = b.m[0], b1 = b.m[1], b2 = b.m[2];
        float b4 = b.m[4], b5 = b.m[5], b6 = b.m[6];
        float b8 = b.m[8], b9 = b.m[9], b10 = b.m[10];
        float b12 = b.m[12], b13 = b.m[13], b14 = b.m[14];
        o = default;
        o.m[0] = a0 * b0 + a4 * b1 + a8 * b2;
        o.m[1] = a1 * b0 + a5 * b1 + a9 * b2;
        o.m[2] = a2 * b0 + a6 * b1 + a10 * b2;
        o.m[4] = a0 * b4 + a4 * b5 + a8 * b6;
        o.m[5] = a1 * b4 + a5 * b5 + a9 * b6;
        o.m[6] = a2 * b4 + a6 * b5 + a10 * b6;
        o.m[8] = a0 * b8 + a4 * b9 + a8 * b10;
        o.m[9] = a1 * b8 + a5 * b9 + a9 * b10;
        o.m[10] = a2 * b8 + a6 * b9 + a10 * b10;
        o.m[12] = a0 * b12 + a4 * b13 + a8 * b14 + a.m[12];
        o.m[13] = a1 * b12 + a5 * b13 + a9 * b14 + a.m[13];
        o.m[14] = a2 * b12 + a6 * b13 + a10 * b14 + a.m[14];
        o.m[15] = 1f;
    }
}
