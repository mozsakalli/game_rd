namespace DigitoyEngine;

// Unity Rect karsiligi (piksel uzayi, y asagi). Blittable struct.
public struct Rect
{
    public float x, y, width, height;

    public Rect(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public float xMin { get => x; set { width += x - value; x = value; } }
    public float yMin { get => y; set { height += y - value; y = value; } }
    public float xMax { get => x + width; set => width = value - x; }
    public float yMax { get => y + height; set => height = value - y; }

    public Vec2 Position => new Vec2(x, y);
    public Vec2 Size => new Vec2(width, height);
    public Vec2 Center => new Vec2(x + width * 0.5f, y + height * 0.5f);

    public bool Contains(Vec2 p)
        => p.x >= x && p.x < x + width && p.y >= y && p.y < y + height;

    public bool Overlaps(in Rect r)
        => r.x < x + width && r.x + r.width > x && r.y < y + height && r.y + r.height > y;

    public static Rect MinMaxRect(float xMin, float yMin, float xMax, float yMax)
        => new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

    // Kesisim; kesismiyorsa genislik/yukseklik 0'a kirpilir.
    public static Rect Intersect(in Rect a, in Rect b)
    {
        float x0 = a.x > b.x ? a.x : b.x;
        float y0 = a.y > b.y ? a.y : b.y;
        float x1 = a.xMax < b.xMax ? a.xMax : b.xMax;
        float y1 = a.yMax < b.yMax ? a.yMax : b.yMax;
        return new Rect(x0, y0, x1 > x0 ? x1 - x0 : 0f, y1 > y0 ? y1 - y0 : 0f);
    }

    public override string ToString() => $"(x:{x} y:{y} w:{width} h:{height})";
}
