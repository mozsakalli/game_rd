using System;
using DigitoyEngine;

// Perspektif kamerayi referans cozunurluk ekrani KAPLAYACAK sekilde konumlar:
// en veya boydan biri ekrana tam oturur, digeri tasar (bosluk kalmaz).
public class CameraScaler : Component
{
    public CameraComponent camera;
    public Vec2 referenceResolution = new Vec2(1080, 1920);

    protected override void Update()
    {
        if (camera == null || Screen.width <= 0 || Screen.height <= 0)
            return;
        float aspect = (float)Screen.width / Screen.height;
        // Dunya birimi = tasarim pikseli (Unity CanvasScaler): referans ekranin tamamina
        // oranlanir, pixelRatio'ya BOLUNMEZ (bolunce yogun ekranda icerik tasar).
        float refW = referenceResolution.x, refH = referenceResolution.y;
        // Gorunur yukseklik = 2*d*tan(fov/2); genislik = yukseklik*aspect.
        float needH = MathF.Max(refH, refW / aspect);
        float d = needH / (2f * MathF.Tan(camera.fov * MathF.PI / 360f));
        var p = camera.transform.position;
        if (p.z != -d)
            camera.transform.position = new Vec3(p.x, p.y, -d);
    }
}