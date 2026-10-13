using System;
using DigitoyEngine;

// Plugin component'i: Assets/Plugins/SandboxPlugin.dll icinden gelir (kaynak PluginSrc/). Sahnede
// kullanilabilmesi katalog + ALC + AOT plugin yolunun calistigini kanitlar.
public sealed class PluginPulse : Component
{
    public float Speed = 2f;
    public float Amount = 0.25f;

    protected override void Update()
    {
        float s = 1f + MathF.Sin(Time.time * Speed) * Amount;
        transform.localScale = new Vec3(s, s, 1f);
    }
}
