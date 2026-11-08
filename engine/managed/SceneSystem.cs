namespace DigitoyEngine;

// Sahne-sahipli genisletme noktasi: custom sistemler (layout, particle, physics...)
// core Scene'e dokunmadan frame fazlarina takilir. Instance-bazli (statik event
// YOK — ALC reload guvenligi); kayit sirasi = cagri sirasi. Scene.GetSystem<T>
// lazy kurar (TweenPool deseni: sahneyle dogar, sahneyle olur).
public interface ISceneSystem
{
    // Update/LateUpdate/Tween sonrasi, FlushAnimated (OnAnimatedPropertiesApplied → OnValidate) ONCESI:
    // dis veriyi sahneye yazan sistemler (binding) buraya — yazimlar ayni frame'de dogrulanir/yerlesir.
    // simulate=false (edit modu) dahil her frame kosar.
    void Sync(Scene scene);

    // Update/LateUpdate/Tween/Sync/FlushAnimated sonrasi, compaction/destroy ONCESI.
    // simulate=false (edit modu) dahil her frame kosar.
    void AfterUpdate(Scene scene);

    // Renderer encode'larindan hemen once / sonra (kamera basina cagrilir).
    void BeginRender(Scene scene, RenderQueue queue);
    void EndRender(Scene scene, RenderQueue queue);
}
