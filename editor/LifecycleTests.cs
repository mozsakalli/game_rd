#if DE_EDITOR
using System;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Yasam dongusu kenar durum smoke testleri (yalniz editor build'inde, acilista).
// Izole bir sahnede kosar, Scene.Update elle surulur, sonuc konsola yazilir.
// Kayit private StringBuilder'da: Instantiate'in public-alan kopyasi log'u
// PAYLASAMASIN (clone kendi initializer'iyla taze log alir).
public static class LifecycleTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("lifecycle-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        AwakeAddsComponent(s);
        DestroyDuringUpdate(s);
        StartOnceOnReEnable(s);
        SelfDisableInOnEnable(s);
        InactiveParentChain(s);
        InstantiateDuringUpdate(s);
        TweenChainSurvivesKill(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[lifecycle] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[lifecycle] FAIL: {name}"); }
    }

    // Cagri sirasini harflerle kaydeder: A=Awake E=OnEnable S=Start U=Update D=OnDisable X=OnDestroy
    sealed class Recorder : Component
    {
        readonly StringBuilder _log = new();
        public string Log => _log.ToString();
        protected internal override void Awake() => _log.Append('A');
        protected internal override void OnEnable() => _log.Append('E');
        protected internal override void Start() => _log.Append('S');
        protected internal override void Update() => _log.Append('U');
        protected internal override void OnDisable() => _log.Append('D');
        protected internal override void OnDestroy() => _log.Append('X');
    }

    sealed class AwakeAdder : Component
    {
        public Recorder Added;
        protected internal override void Awake() => Added = gameObject.AddComponent<Recorder>();
    }

    static void AwakeAddsComponent(Scene s)
    {
        var go = new GameObject("t1");
        var a = go.AddComponent<AwakeAdder>();
        Check(a.Added != null && a.Added.Log == "AE", "awake-icinde-addcomponent: aninda Awake+OnEnable");
        s.Update(0f);
        Check(a.Added.Log == "AESU", "eklenen component Start sonra Update");
        GameObject.Destroy(go);
        s.Update(0f);
        Check(a.Added.Log == "AESUUDX", "destroy frame sonunda: son Update sonra OnDisable+OnDestroy");
    }

    sealed class SelfDestroyer : Component
    {
        protected internal override void Update() => GameObject.Destroy(gameObject);
    }

    static void DestroyDuringUpdate(Scene s)
    {
        var go = new GameObject("t2");
        var r = go.AddComponent<Recorder>();
        go.AddComponent<SelfDestroyer>();
        s.Update(0f);
        Check(r.Log == "AESUDX", "update icinde self-destroy: ayni frame OnDisable+OnDestroy");
        Check(!go.activeInHierarchy, "destroy sonrasi activeInHierarchy false");
        s.Update(0f);
        Check(r.Log == "AESUDX", "olu component tekrar Update almaz");
    }

    static void StartOnceOnReEnable(Scene s)
    {
        var go = new GameObject("t3");
        var r = go.AddComponent<Recorder>();
        go.SetActive(false);
        s.Update(0f);
        Check(!r.Log.Contains('S'), "inaktifken Start kosmaz");
        go.SetActive(true);
        s.Update(0f);
        Check(r.Log == "AEDESU", "yeniden aktif: Start bir kez, dogru sirada");
        go.SetActive(false);
        go.SetActive(true);
        s.Update(0f);
        int startCount = 0;
        foreach (var ch in r.Log)
            if (ch == 'S')
                startCount++;
        Check(startCount == 1, "Start omur boyu tek sefer");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    sealed class SelfDisabler : Component
    {
        public bool Updated;
        protected internal override void OnEnable() => enabled = false;
        protected internal override void Update() => Updated = true;
    }

    static void SelfDisableInOnEnable(Scene s)
    {
        var go = new GameObject("t4");
        var c = go.AddComponent<SelfDisabler>();
        s.Update(0f);
        Check(!c.Updated && !c.enabled, "OnEnable icinde self-disable: Update hic kosmaz");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void InactiveParentChain(Scene s)
    {
        var p = new GameObject("t5-parent");
        p.SetActive(false);
        var cgo = new GameObject("t5-child");
        var r = cgo.AddComponent<Recorder>(); // aktif dogar: AE
        cgo.transform.SetParent(p.transform); // inaktif altina girer
        Check(r.Log == "AED" && !cgo.activeInHierarchy, "inaktif parent altina reparent: OnDisable");
        p.SetActive(true);
        Check(r.Log == "AEDE" && cgo.activeInHierarchy, "parent aktiflesince zincir OnEnable");
        GameObject.Destroy(p);
        s.Update(0f);
        Check(r.Log.EndsWith("DX"), "parent destroy alt agaci da yok eder");
    }

    sealed class SpawnerOnce : Component
    {
        public GameObject Template;
        public GameObject Spawned;
        protected internal override void Update()
        {
            if (Spawned == null)
                Spawned = GameObject.Instantiate(Template);
        }
    }

    static void InstantiateDuringUpdate(Scene s)
    {
        var template = new GameObject("t6-template");
        template.AddComponent<Recorder>();
        var go = new GameObject("t6");
        var sp = go.AddComponent<SpawnerOnce>();
        sp.Template = template;
        s.Update(0f);
        var cloneRec = sp.Spawned?.GetComponent<Recorder>();
        Check(cloneRec != null && cloneRec.Log == "AE", "update icinde Instantiate: klon aninda Awake+OnEnable, Update ayni frame degil");
        s.Update(0f);
        Check(cloneRec.Log == "AESU", "klon sonraki frame Start+Update alir");
        GameObject.Destroy(template);
        GameObject.Destroy(go);
        GameObject.Destroy(sp.Spawned);
        s.Update(0f);
    }

    // Zincir politikasi: erken olen tween (olu hedef veya Cancel) sonrakini uyandirir,
    // zincirdeki slot sonsuza dek Waiting kalip sizamaz.
    static void TweenChainSurvivesKill(Scene s)
    {
        var go = new GameObject("t7");
        var chained = s.TweenValue(0f, 1f, 0.1f);
        go.transform.TweenMoveX(10f, 1f).Then(chained);
        GameObject.Destroy(go);
        s.Update(0.05f); // destroy frame sonunda uygulanir
        s.Update(0.05f); // olu hedefli tween Kill olur, zincir uyanir
        s.Update(0.05f); // uyanan zincir bu frame ilerler (slotu daha once tarandi)
        Check(chained.Value > 0f, "olu hedefle olen tween zinciri uyandirir");
        s.Update(0.2f);
        Check(!chained.IsActive, "uyanan zincir tamamlanip slotu geri verir");

        var go2 = new GameObject("t8");
        var chained2 = s.TweenValue(0f, 1f, 0.1f);
        var first = go2.transform.TweenMoveX(10f, 1f);
        first.Then(chained2);
        first.Cancel();
        s.Update(0.05f);
        Check(chained2.Value > 0f, "Cancel edilen tween zinciri uyandirir");
        GameObject.Destroy(go2);
        s.Update(0.2f);
    }
}
#endif
