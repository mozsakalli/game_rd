#if DE_EDITOR
using System;
using DigitoyEngine;

namespace DigitoyEditor;

// ParticleSystem smoke testleri (acilista, izole sahne): emisyon sayimi, burst,
// omur/olum, dongu, stopAction, dunya/lokal uzay, draw sayisi ve SIFIR alloc.
public static class ParticleTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("particle-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        RateAndLifetime(s);
        Bursts(s);
        LoopAndStop(s);
        WorldSpace(s);
        DrawCountAndZeroAlloc(s);
        SubEmitter(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[particle] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[particle] FAIL: {name}"); }
    }

    static ParticleSystem Make(Scene s, string name)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.playOnAwake = false;
        ps.randomSeed = 1234;
        ps.emission.rateOverTime = 0f;
        ps.shape.type = ParticleShapeType.Point;
        ps.startSpeed = new FloatRange(0f);
        return ps;
    }

    static void RateAndLifetime(Scene s)
    {
        var ps = Make(s, "rate");
        ps.emission.rateOverTime = 100f;
        ps.startLifetime = new FloatRange(0.5f);
        ps.Play();
        for (int i = 0; i < 10; i++) ps.Simulate(0.01f); // 0.1s -> 10 parcacik
        Check(ps.ParticleCount == 10, $"rate 10 ({ps.ParticleCount})");
        for (int i = 0; i < 100; i++) ps.Simulate(0.01f); // toplam 1.1s: hepsi 0.5s yasar
        Check(ps.ParticleCount >= 49 && ps.ParticleCount <= 51, $"steady ~50 ({ps.ParticleCount})");
        ps.Stop();
        for (int i = 0; i < 60; i++) ps.Simulate(0.01f);
        Check(ps.ParticleCount == 0, "stop -> olumler");
        Check(!ps.IsAlive, "stop sonrasi olu");
        ps.maxParticles = 5;
        ps.Play();
        ps.Emit(100);
        Check(ps.ParticleCount == 5, "maxParticles kirpar");
        Component.Destroy(ps.gameObject);
    }

    static void Bursts(Scene s)
    {
        var ps = Make(s, "burst");
        ps.startLifetime = new FloatRange(10f);
        ps.emission.bursts = new()
        {
            new ParticleBurst { time = 0f, count = 7 },
            new ParticleBurst { time = 0.5f, count = 3, cycles = 3, interval = 0.1f },
        };
        ps.Play();
        Check(ps.ParticleCount == 7, $"t=0 burst ({ps.ParticleCount})");
        ps.Simulate(0.45f);
        Check(ps.ParticleCount == 7, "0.45: ikinci henuz yok");
        ps.Simulate(0.1f); // 0.55: 1. dongu
        Check(ps.ParticleCount == 10, $"0.55 ({ps.ParticleCount})");
        ps.Simulate(0.3f); // 0.85: 0.6 + 0.7 -> toplam 3 dongu
        Check(ps.ParticleCount == 16, $"3 dongu ({ps.ParticleCount})");
        ps.Simulate(1f);
        Check(ps.ParticleCount == 16, "dongu biter");
        Component.Destroy(ps.gameObject);
    }

    static void LoopAndStop(Scene s)
    {
        var ps = Make(s, "loop");
        ps.duration = 1f;
        ps.looping = true;
        ps.startLifetime = new FloatRange(0.2f);
        ps.emission.bursts = new() { new ParticleBurst { time = 0f, count = 4 } };
        ps.Play();
        ps.Simulate(0.3f);
        Check(ps.ParticleCount == 0, "ilk burst oldu");
        ps.Simulate(0.75f); // t=1.05 -> sarma, burst tekrar
        Check(ps.ParticleCount == 4, $"loop burst ({ps.ParticleCount})");
        ps.looping = false;
        ps.Simulate(1f);
        ps.Simulate(0.1f);
        Check(!ps.IsEmitting && !ps.IsAlive, "non-loop biter");

        ps.stopAction = ParticleStopAction.Disable;
        ps.Play();
        ps.Simulate(1.5f);
        s.Update(0f);
        Check(!ps.gameObject.activeSelf, "stopAction Disable");
        Component.Destroy(ps.gameObject);
    }

    static void WorldSpace(Scene s)
    {
        var ps = Make(s, "world");
        ps.transform.localPosition = new Vec3(100f, 50f, 0f);
        ps.startLifetime = new FloatRange(10f);
        ps.simulationSpace = ParticleSpace.World;
        ps.Play();
        ps.Emit(1);
        // Dunya uzayinda parcacik emitter'in dogum anindaki konumunda kalir.
        ps.transform.localPosition = new Vec3(0f, 0f, 0f);
        ps.Simulate(0.1f);
        Check(ps.ParticleCount == 1, "world: 1 parcacik");
        // Mesafe emisyonu: emitter 100 birim hareket, 0.1/birim -> 10 parcacik
        ps.emission.rateOverDistance = 0.1f;
        ps.Simulate(0.01f); // ilk konum kaydi
        ps.transform.localPosition = new Vec3(100f, 0f, 0f);
        ps.Simulate(0.01f);
        Check(ps.ParticleCount == 11, $"rateOverDistance ({ps.ParticleCount})");
        Component.Destroy(ps.gameObject);
    }

    static void DrawCountAndZeroAlloc(Scene s)
    {
        s.Update(0f); // onceki testlerin Destroy'lari flush olsun (draw sayimi temiz)
        var ps = Make(s, "draw");
        ps.maxParticles = 2000;
        ps.startLifetime = new FloatRange(5f);
        ps.emission.rateOverTime = 500f;
        ps.velocity.gravity = new Vec3(0f, 100f, 0f);
        ps.velocity.drag = 0.5f;
        ps.velocity.orbitalSpeed = 30f;
        ps.noise.strength = 50f;
        ps.sizeOverLifetime = LifeCurve.EaseTo(1f, 0f, Ease.OutQuad);
        ps.colorOverLifetime.Add(0f, Color.White).Add(1f, new Color(255, 255, 255, 0));
        ps.rotation.angularVelocity = new Vec3Range(new Vec3(0, 0, -90), new Vec3(10, 20, 90));
        // Iki kare, iki ayri sayfa: kare animasyonu + sayfa degisimi yolu da alloc'suz olmali.
        ps.sprites.Add(Sprite.FromTexture(Texture.FromColor(2, 2, Color.White)));
        ps.sprites.Add(Sprite.FromTexture(Texture.FromColor(2, 4, Color.Red)));
        ps.frames.mode = ParticleFrameMode.Fps;
        ps.Play();
        var cam = new Camera();
        for (int i = 0; i < 30; i++) ps.Simulate(1f / 60f);
        cam.Queue.Begin();
        s.Render(cam.Queue);
        Check(cam.Queue.Count == ps.ParticleCount && ps.ParticleCount > 100,
            $"draw = parcacik ({cam.Queue.Count}/{ps.ParticleCount})");

        // Isinma sonrasi: simulasyon + encode SIFIR alloc.
        for (int i = 0; i < 3; i++) { ps.Simulate(1f / 60f); cam.Queue.Begin(); s.Render(cam.Queue); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) { ps.Simulate(1f / 60f); cam.Queue.Begin(); s.Render(cam.Queue); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(alloc == 0, $"zero alloc ({alloc} B)");
        cam.Queue.Begin();
        Component.Destroy(ps.gameObject);
    }

    static void SubEmitter(Scene s)
    {
        var parent = Make(s, "sub-parent");
        var child = Make(s, "sub-child");
        child.startLifetime = new FloatRange(10f);
        parent.startLifetime = new FloatRange(0.1f);
        parent.subEmitterOnDeath = child;
        parent.subEmitterCount = 3;
        parent.Play();
        child.Play();
        parent.Emit(2);
        parent.Simulate(0.2f);
        Check(parent.ParticleCount == 0 && child.ParticleCount == 6, $"sub emitter ({child.ParticleCount})");
        Component.Destroy(parent.gameObject);
        Component.Destroy(child.gameObject);
    }
}
#endif
