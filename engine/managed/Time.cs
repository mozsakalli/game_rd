namespace DigitoyEngine;

// Ambient zaman PENCERESI: degerler icinde kosulan SAHNEYE aittir (Scene.Update
// girisinde basilir). Component kodu icin API duz statik kalir (AOT dostu).
// frameCount global'dir (kaynak damgalari icin — Scene.UpdateAll arttirir).
public static class Time
{
    public static float time { get; internal set; }
    public static float deltaTime { get; internal set; }
    public static int frameCount { get; internal set; }
}
