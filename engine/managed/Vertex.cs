namespace DigitoyEngine;

// C '_engine_Vertex' ile birebir bellek duzeni:
//   { Vec3 position; Vec2 uv; Color color }  (Color = 4 x byte)
// GPU'ya oldugu gibi (blittable) yuklenir; alan sirasi/degistirilmemeli.
public struct Vertex
{
    public Vec3 position;
    public Vec2 uv;
    public Color color;

    public Vertex(Vec3 position, Vec2 uv, Color color)
    {
        this.position = position;
        this.uv = uv;
        this.color = color;
    }
}
