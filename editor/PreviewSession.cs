namespace DigitoyEditor;

// Preview oturumu: baslarken hicbir sey kaydedilmez, custom editor/panel CANLI
// nesneleri serbestce mutate eder; bitis = doc'tan reload (izler olur). Doc'a
// giden Commit'ler (SetProp vs.) oturum icinde de calisir ve reload'da YASAR —
// duzenlemeler kalir, preview izleri olur. Secim baska GO'ya gecince ortulu biter.
public static class PreviewSession
{
    public static bool Active { get; private set; }

    static int _ownerDocId;
    static int _ownerCompIndex;

    public static bool IsOwner(int docId, int compIndex)
        => Active && _ownerDocId == docId && _ownerCompIndex == compIndex;

    public static int OwnerDocId => Active ? _ownerDocId : 0;

    // Klip editoru bagliyken oturum secimle KAPANMAZ (kullanici sahnede nesne secip
    // kliğe ekler / duzenler). Panel her cizildiginde Pin() cagirir; tab tasima gibi
    // birkac frame cizilmeyen durumlara tolerans: son pin'den 30 frame gecince cozulur.
    static int _pinFrame = int.MinValue;
    public static void Pin() => _pinFrame = DigitoyEngine.Time.frameCount;
    static bool Pinned => DigitoyEngine.Time.frameCount - _pinFrame <= 30;

    // Sahip ya da sahibin alt agacindaki bir GO mu? (Klip katmani secmek oturumu kapatmaz.)
    public static bool InOwnerSubtree(int docId)
    {
        if (!Active || docId == 0)
            return false;
        var doc = App.EditScene?.Doc;
        for (int guard = 0; docId != 0 && guard < 64; guard++)
        {
            if (docId == _ownerDocId)
                return true;
            var g = doc?.Objects.Find(o => o.Id == docId);
            if (g == null)
                return false;
            docId = g.Parent;
        }
        return false;
    }

    public static void Begin(int docId, int compIndex)
    {
        if (Active)
            End();
        Active = true;
        _ownerDocId = docId;
        _ownerCompIndex = compIndex;
    }

    public static void End()
    {
        if (!Active)
            return;
        Active = false;
        App.EditScene.RefreshLive(); // tum preview izleri doc'tan reload ile olur
    }

    // Oturum icinde bastan izleme: izler silinir, oturum acik kalir.
    public static void Restart() => App.EditScene.RefreshLive();

    // Her frame editor cagirir: secim sahibin alt agacindan ayrildiysa ortulu bitis.
    public static void Tick(int selectedDocId)
    {
        if (Active && !Pinned && selectedDocId != _ownerDocId && !InOwnerSubtree(selectedDocId))
            End();
    }

    // Sahip component IEditorPreview ise edit modunda kendi saatiyle adimlanir
    // (parcacik gibi Update'e bagli sistemler preview'da akar).
    public static void Step(float dt)
    {
        if (!Active)
            return;
        var es = App.EditScene;
        var g = es?.Doc.Objects.Find(static o => o.Id == _ownerDocId);
        if (g == null || _ownerCompIndex < 0 || _ownerCompIndex >= g.Components.Count)
            return;
        if (es.FindLiveComponent(g, g.Components[_ownerCompIndex]) is DigitoyEngine.IEditorPreview p)
            p.PreviewStep(dt);
    }
}
