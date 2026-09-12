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

}