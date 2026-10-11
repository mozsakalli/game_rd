using System;
using DigitoyEngine;

// Generic buton davranisi: basinca kucul (press), birakinca OutBack ile geri gel (release),
// tiklamada Clicked event'i ve buttonId ile haber ver. Gorsel LayoutBox/SpriteRenderer'dan
// bagimsiz; sadece transform olcegine dokunur. Animasyon motorun Tween havuzuyla (sifir alloc,
// destroy-guvenli, yarida kesilince mevcut degerden devam). Taban olcek Awake'te yakalanir.
// NOT: Motion gibi olcek yazan baska bir component ayni GO'da ayni anda oynarsa son yazan kazanir.
public class Button : Component
{
    public string buttonId = "";          // dinleyicinin hangi buton oldugunu ayirt etmesi icin
    public float pressScale = 0.94f;      // basili olcek carpani
    public float pressDuration = 0.08f;   // basma suresi (OutCubic)
    public float releaseDuration = 0.25f; // birakma suresi (OutBack)
    public bool interactable = true;      // false: olay yutulur, animasyon oynamaz

    // Tiklama olayi: (buton, pointer olayi). Oyun kodu Start'ta abone olur.
    public event Action<Button, PointerEvent> Clicked;
    // Tum butonlar icin tek nokta (analytics/ses gibi global dinleyiciler).
    public static event Action<Button, PointerEvent> AnyClicked;

    Vec3 _baseScale;
    bool _captured;
    bool _pressed;
    TweenHandle _scale;

    public bool IsPressed => _pressed;

    protected override void Awake()
    {
        Capture();
    }

    void Capture()
    {
        if (_captured) return;
        _captured = true;
        _baseScale = transform.localScale;
    }

    void ScaleTo(float k, float dur, Ease ease)
    {
        _scale.Cancel(); // onceki yarim tween olurse ikisi birden yazmasin
        var to = new Vec2(_baseScale.x * k, _baseScale.y * k);
        if (dur <= 0f)
        {
            transform.localScale = new Vec3(to.x, to.y, _baseScale.z);
            return;
        }
        _scale = transform.TweenScale(to, dur).Ease(ease);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (!interactable) { e.Use(); return; }
        Capture();
        _pressed = true;
        ScaleTo(pressScale, pressDuration, Ease.OutCubic);
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (!_pressed) return;
        _pressed = false;
        ScaleTo(1f, releaseDuration, Ease.OutBack);
    }

    protected override void OnDragStart(PointerEvent e)
    {
        // Surukleme basladiysa buton iptal: olcegi geri al, tiklama sayilmaz.
        if (!_pressed) return;
        _pressed = false;
        ScaleTo(1f, releaseDuration, Ease.OutCubic);
    }

    protected override void OnPointerClick(PointerEvent e)
    {
        if (!interactable || e.dragged) return;
        Clicked?.Invoke(this, e);
        AnyClicked?.Invoke(this, e);
        e.Use();
    }

    protected override void OnDisable()
    {
        // Devre disi kalirken basili/animasyonda kalmasin.
        _pressed = false;
        _scale.Cancel();
        if (_captured) transform.localScale = _baseScale;
    }
}