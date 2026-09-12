namespace DigitoyEngine;

// Sahne-sahipli genisletme noktasi: custom sistemler (layout, particle, physics...)
// core Scene'e dokunmadan frame fazlarina takilir. Instance-bazli (statik event
// YOK — ALC reload guvenligi); kayit sirasi = cagri sirasi. Scene.GetSystem<T>
// lazy kurar (TweenPool deseni: sahneyle dogar, sahneyle olur).
public interface ISceneSystem
{
    // Update/LateUpdate/Tween sonrasi, compaction/destroy ONCESI.
    // simulate=false (edit modu) dahil her frame kosar.
    void AfterUpdate(Scene scene);

    // Renderer encode'larindan hemen once / sonra (kamera basina cagrilir).
    void BeginRender(Scene scene, RenderQueue queue);
    void EndRender(Scene scene, RenderQueue queue);
}
