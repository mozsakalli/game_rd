using System;
using DigitoyEngine;

// Stres testi script'i: Seed'den deterministik yorunge (alloc'suz).
public sealed class StressOrb : Component
{
    public int Seed;

    protected override void Update()
    {
        float speed = 0.3f + (Seed % 17) * 0.09f;
        float radius = 260f + (Seed % 231);
        float phase = Seed * 2.399f; // altin aci: halka homojen dolsun
        float ang = Time.time * speed + phase;
        transform.localPosition = new Vec3(
            Screen.width * 0.5f + MathF.Cos(ang) * radius,
            Screen.height * 0.5f + MathF.Sin(ang) * radius, 0f);
        transform.localEulerAngles = new Vec3(0f, 0f, ang * 57.29578f);
    }
}
