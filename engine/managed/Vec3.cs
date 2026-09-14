namespace DigitoyEngine;

public struct Vec3
{
    public float x;
    public float y;
    public float z;

    public readonly Vec3 One => new Vec3(1, 1, 1);
    public readonly Vec3 Zero => new Vec3(0, 0, 0);
    public Vec3(float x = 0, float y = 0, float z = 0)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vec3 operator *(Vec3 a, float s) => new(a.x * s, a.y * s, a.z * s);

    public readonly float Length() => System.MathF.Sqrt(x * x + y * y + z * z);

    public static float Distance(Vec3 a, Vec3 b) => (b - a).Length();

    public static Vec3 Normalize(Vec3 v)
    {
        float len = v.Length();
        return len > 1e-12f ? v * (1f / len) : v;
    }
}