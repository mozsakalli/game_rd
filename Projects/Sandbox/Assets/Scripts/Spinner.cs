using System;
using DigitoyEngine;

// Sandbox oyun script'i — editor tarafindan Assets/'ten derlenir (oyun assembly'si).
// NOT: yasam dongusu override'lari farkli assembly'den 'protected override' yazilir
// (protected internal cross-assembly kurali).
[Previewable]
public sealed class Spinner : Component
{
    public int Index;
    public float Speed = 1f;
    public SpriteRenderer Target;
    SpriteRenderer _sr;

    protected override void Awake() => _sr = Target ?? GetComponent<SpriteRenderer>();

    protected override void Update()
    {
        if (_sr == null)
            return;
        float ang = Time.time * Speed + Index * (MathF.PI * 2f / 8f) * 100;
        transform.localPosition = new Vec3(
            Screen.width * 0.5f + MathF.Cos(ang) * 220f,
            Screen.height * 0.5f + MathF.Sin(ang) * 220f, 0f);
        transform.localEulerAngles = new Vec3(0f, 0f, (Time.time * 2f + Index) * (180f / MathF.PI));
        _sr.Color = new Color(
            (byte)(128 + 0 * MathF.Sin(ang)),
            (byte)(128 + 0 * MathF.Sin(ang + 2.1f)),
            (byte)(128 + 127 * MathF.Sin(ang + 4.2f)), 255);
    }
}
