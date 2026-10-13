namespace DigitoyEngine;

// Hiyerarsik grup alfasi (Unity CanvasGroup / Flash DisplayObject.alpha): bu GO ve tum
// alt agacindaki renderer'larin kose alfalari Alpha ile carpilir; ic ice gruplar
// carpilarak iner. Durum/cache yok: Scene.Render'in zaten yaptigi DFS'te
// (SortingOrder cozumuyle ayni yuruyus) parentAlpha * Alpha hesaplanir, renderer
// _effectiveAlpha okur, RenderQueue.DrawMesh tek dalla uygular — renderer tipleri
// bu component'i bilmez. Alpha<=0: encode edilmez, pointer icin gorunmez.
// Fade-in/out = kok GO'ya tek Tween/MovieClip track'i ("Alpha").
[AddComponentMenu("Rendering/Render Group")]
public sealed class RenderGroup : Component
{
    public float Alpha = 1f;
    // true: atalarin grup alfasi yok sayilir (SortingSpace.Absolute paritesi) — solan
    // panelin icinde sabit kalmasi gereken uyari/spinner gibi.
    public bool IgnoreParentAlpha;
}
