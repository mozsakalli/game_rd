namespace DigitoyEngine;

public struct Color
{
    public byte r;
    public byte g;
    public byte b;
    public byte a;

    public readonly static Color White = new Color(255, 255, 255, 255);
    public readonly static Color Black = new Color(0, 0, 0, 255);
    public readonly static Color Red = new Color(255, 0, 0, 255);
    public readonly static Color Green = new Color(0, 255, 0, 255);
    public readonly static Color Blue = new Color(0, 0, 255, 255);
    public readonly static Color Transparent = new Color(0, 0, 0, 0);
    public Color(byte r = 0, byte g = 0, byte b = 0, byte a = 255)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }
}