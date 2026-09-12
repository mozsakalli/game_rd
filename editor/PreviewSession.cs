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

    // Her frame editor cagirir: secim sahibinden ayrildiysa ortulu bitis.
    public static void Tick(int selectedDocId)
    {
        if (Active && selectedDocId != _ownerDocId)
            End();
    }
}
