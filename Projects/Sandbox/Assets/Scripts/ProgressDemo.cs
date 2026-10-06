using System;
using DigitoyEngine;

// Sandbox demo: ProgressRenderer degerini zamana gore surer.
// Sine: yumusak gidip gelme; StepDown: her adimda keskin dusus (ghost izi gorunsun).
public sealed class ProgressDemo : Component
{
    public enum Mode : byte { Sine, StepDown }

    public Mode Animation;
    public float Speed = 0.5f;
    public float Phase;
    ProgressRenderer _pr;

    protected override void Awake() => _pr = GetComponent<ProgressRenderer>();

    protected override void Update()
    {
        if (_pr == null)
            return;
        float t = Time.time * Speed + Phase;
        _pr.Value = Animation == Mode.Sine
            ? 0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f)
            : 1f - MathF.Floor((t - MathF.Floor(t)) * 6f) / 6f;
    }
}
