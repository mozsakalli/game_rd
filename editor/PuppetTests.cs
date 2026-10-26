#if DE_EDITOR
using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Puppet/Cue testleri (acilista, izole sahnede). Kapsam: isimle oynatma + tamamlanma, PARAMETRELI key (tek Deal cue'su
// farkli hedeflere), kesme = hedefe zipla, Sample (scrub), Reparent/Event aksiyonlari + Current key, round-trip, alloc=0.
public static class PuppetTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("puppet-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        PlayAndComplete(s);
        ParametricKeys(s);
        InterruptJumpsToEnd(s);
        SampleForEditor(s);
        ReparentAndEvents(s);
        RoundTrip(s, catalog);
        ZeroAlloc(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[puppet] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[puppet] FAIL: {name}"); }
    }

    // Gercekci adimlama: tek Update'te MaxCatchUpFrames'ten fazla frame atlanmaz (spike korumasi).
    static void Run(Scene s, float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += 0.1f)
            s.Update(MathF.Min(0.1f, seconds - t));
    }

    [AddComponentMenu("")]
    public sealed class CueSink : Component, ICueHandler
    {
        [NonSerialized] public int Completed;
        [NonSerialized] public int LastCue = -1;
        [NonSerialized] public int Events;
        [NonSerialized] public int LastEvent;
        public void OnCueComplete(Puppet p, int cue) { Completed++; LastCue = cue; }
        public void OnCueEvent(Puppet p, int cue, int id) { Events++; LastEvent = id; }
    }

    static CueKey K(int frame, AnimValue v) { var k = new CueKey { Frame = frame }; k.SetAnim(v); return k; }
    static CueKey KParam(int frame, int param) => new() { Frame = frame, Source = KeySource.Param, Param = param };
    static CueKey KCurrent(int frame) => new() { Frame = frame, Source = KeySource.Current };

    static CueTrack Track(Component node, string prop, params CueKey[] keys)
    {
        var t = new CueTrack { Node = node, Property = prop };
        t.Keys.AddRange(keys);
        return t;
    }

    // Card puppet'i: "Deal(to: Vec3)" (Position 0 → $to, 10 frame) ve "Flip(up: Bool)" (Scale.x 1→0→1 + Face.Active=$up @5).
    static Puppet MakeCard(out GameObject root, out GameObject face)
    {
        root = new GameObject("card");
        face = new GameObject("face");
        face.transform.parent = root.transform;
        var p = root.AddComponent<Puppet>();

        var deal = new Cue { Name = "Deal", Fps = 10, Frames = 11 };
        deal.Params.Add(new CueParam { Name = "to", Kind = ParamKind.Vec3, Default = new Vec4(100, 0, 0, 0) });
        deal.Tracks.Add(Track(root.transform, "Position", K(0, new AnimValue(0, 0, 0)), KParam(10, 0)));
        p.Cues.Add(deal);

        var flip = new Cue { Name = "Flip", Fps = 10, Frames = 11 };
        flip.Params.Add(new CueParam { Name = "up", Kind = ParamKind.Bool, Default = new Vec4(1, 0, 0, 0) });
        flip.Tracks.Add(Track(root.transform, "Scale", K(0, new AnimValue(1, 1, 1)), K(5, new AnimValue(0, 1, 1)), K(10, new AnimValue(1, 1, 1))));
        flip.Tracks.Add(Track(face.transform, "Active", K(0, AnimValue.FromBool(false)), KParam(5, 0)));
        p.Cues.Add(flip);
        p.Rebuild();
        return p;
    }

    static void PlayAndComplete(Scene s)
    {
        var p = MakeCard(out var root, out _);
        var sink = root.AddComponent<CueSink>();
        p.Rebuild();
        int evt = -1;
        p.CueCompleted += (_, c) => evt = c;
        s.Update(0f);
        Check(!p.IsPlaying && root.transform.localPosition.x == 0f, "cue'suz puppet dokunmaz (doc pozu)");
        Check(p.Has("Deal") && p.Has("Flip") && !p.Has("Yok"), "Has/IndexOf");
        Check(p.Play("Deal") && p.IsPlaying && p.CurrentCue == 0, "Play(isim)");
        s.Update(0.5f);
        Check(Math.Abs(root.transform.localPosition.x - 50f) < 0.01f, "Default arguman (to=100) ile yarida 50");
        Run(s, 1f);
        Check(!p.IsPlaying && root.transform.localPosition.x == 100f, "sonda durur, hedef degerde");
        Check(sink.Completed == 1 && sink.LastCue == 0 && evt == 0, "ICueHandler + CueCompleted bir kez");
        Check(!p.Play("Yok"), "bilinmeyen cue false");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void ParametricKeys(Scene s)
    {
        var p = MakeCard(out var root, out var face);
        s.Update(0f);
        p.Play("Deal", AnimValue.FromVec3(new Vec3(0, 200, 0)));
        Run(s, 1.2f);
        Check(root.transform.localPosition.y == 200f && root.transform.localPosition.x == 0f, "Deal(to=(0,200,0)) tek klip, farkli hedef");
        p.Play("Deal", AnimValue.FromVec3(new Vec3(-40, 0, 0)));
        s.Update(0.5f);
        Check(Math.Abs(root.transform.localPosition.x - (-20f)) < 0.01f && root.transform.localPosition.y == 0f, "ayni cue ikinci kez farkli argumanla; Const baslangic key'i (0,0,0) → y sifirlandi");
        Run(s, 1f);
        Check(root.transform.localPosition.x == -40f, "ikinci hedefe vardi");

        p.Play("Flip", AnimValue.FromBool(true));
        s.Update(0.2f);
        Check(!face.activeSelf, "Flip: frame 5 oncesi yuz kapali");
        s.Update(0.4f);
        Check(face.activeSelf && Math.Abs(root.transform.localScale.x - 0.2f) < 0.01f, "Flip(up=true): frame 5'te Bool arguman yazildi");
        Run(s, 1f);
        p.Play("Flip", AnimValue.FromBool(false));
        Run(s, 0.6f);
        Check(!face.activeSelf, "Flip(up=false): ayni cue, ters sonuc");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void InterruptJumpsToEnd(Scene s)
    {
        var p = MakeCard(out var root, out _);
        var sink = root.AddComponent<CueSink>();
        p.Rebuild();
        s.Update(0f);
        p.Play("Deal", AnimValue.FromVec3(new Vec3(100, 0, 0)));
        s.Update(0.3f);
        Check(Math.Abs(root.transform.localPosition.x - 30f) < 0.01f, "Deal yarida");
        p.Play("Flip", AnimValue.FromBool(true));
        Check(root.transform.localPosition.x == 100f && p.CurrentCue == 1 && p.IsPlaying, "kesme: Deal hedefe zipladi, Flip basladi");
        Check(sink.Completed == 0, "kesilen cue tamamlanma olayi uretmez");
        Run(s, 1.2f);
        Check(sink.Completed == 1 && sink.LastCue == 1, "Flip tamamlandi");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void SampleForEditor(Scene s)
    {
        var p = MakeCard(out var root, out _);
        s.Update(0f, simulate: false);
        p.SetArg(0, 0, AnimValue.FromVec3(new Vec3(0, 0, 80)));
        p.Sample(0, 5f);
        Check(!p.IsPlaying && Math.Abs(root.transform.localPosition.z - 40f) < 0.01f, "Sample(cue, frame) oynatmadan ornekler, argumani kullanir");
        Check(p.TryEvaluate(0, 0, 2.5f, out var v) && Math.Abs(v.Z - 20f) < 0.01f, "TryEvaluate yazmadan degerlendirir");
        Check(p.FramesOf(0) == 11 && p.FramesOf(1) == 11, "FramesOf");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    // Deck → Hand ucusu: "Fly(to: Target)" = frame 0 Reparent(kok → $to, keepWorld) + Position Current → (0,0,0),
    // frame 5 Event("landed"). Kart yeni ebeveynin YEREL orijinine ucar; hedef tasinirsa kart onunla gider.
    static Puppet MakeFlyer(out GameObject deck, out GameObject hand, out GameObject card, out CueSink sink)
    {
        deck = new GameObject("deck");
        hand = new GameObject("hand"); hand.transform.localPosition = new Vec3(300, 0, 0);
        card = new GameObject("card"); card.transform.parent = deck.transform; card.transform.localPosition = new Vec3(10, 0, 0);
        var p = card.AddComponent<Puppet>();
        sink = card.AddComponent<CueSink>();
        var fly = new Cue { Name = "Fly", Fps = 10, Frames = 11 };
        fly.Params.Add(new CueParam { Name = "to", Kind = ParamKind.Target });
        fly.Actions.Add(new CueAction { Frame = 0, Kind = ActionKind.Reparent, Param = 0, KeepWorld = true });
        fly.Actions.Add(new CueAction { Frame = 5, Kind = ActionKind.Event, Label = "landed" });
        fly.Tracks.Add(Track(card.transform, "Position", KCurrent(0), K(10, new AnimValue(0, 0, 0))));
        p.Cues.Add(fly);
        p.Rebuild();
        return p;
    }

    static void ReparentAndEvents(Scene s)
    {
        var p = MakeFlyer(out var deck, out var hand, out var card, out var sink);
        s.Update(0f);
        Check(p.HasReparent(0), "HasReparent");
        p.Play("Fly", hand.transform);
        Check(card.transform.parent == hand.transform, "frame 0 Reparent: kart Hand'in cocugu");
        Check(Math.Abs(card.transform.localPosition.x - (-290f)) < 0.01f, "KeepWorld: dunya (10,0,0) → Hand yerel (-290,0,0)");
        Check(Math.Abs(card.transform.position.x - 10f) < 0.01f, "dunya pozu korunmus (zipla yok)");
        s.Update(0.5f);
        Check(Math.Abs(card.transform.localPosition.x - (-145f)) < 0.01f, "Current key'den (-290) hedefe (0) yarida -145");
        Check(sink.Events == 1 && sink.LastEvent == CueHash.Of("landed"), "frame 5 Event bir kez, hash ile");
        Run(s, 0.7f);
        Check(!p.IsPlaying && card.transform.localPosition.x == 0f && sink.Completed == 1, "yerel orijine vardi, tamamlandi");
        hand.transform.localPosition = new Vec3(500, 50, 0);
        Check(Math.Abs(card.transform.position.x - 500f) < 0.01f, "hedef tasininca kart onunla gider");

        p.Play("Fly", deck.transform);
        Check(card.transform.parent == deck.transform && Math.Abs(card.transform.localPosition.x - 500f) < 0.01f, "ikinci ucus: Deck'e reparent, yerel (500,50) yakalandi");
        s.Update(0.2f);
        p.Play("Fly", hand.transform);
        Check(card.transform.parent == hand.transform && sink.Completed == 1, "kesme: yeni hedefe reparent, tamamlanma olayi yok");
        Check(Math.Abs(card.transform.localPosition.x - (-500f)) < 0.01f, "kesilen cue hedefe (deck yerel 0) zipladi → hand yerel (-500)");
        Run(s, 1.2f);
        Check(card.transform.localPosition.x == 0f && sink.Completed == 2, "ucuncu ucus tamamlandi");

        // Scrub: editor akisi = arguman degisikligi Rebuild'den gecer, geri sarma reload.
        p.Play("Fly", deck.transform); Run(s, 1.2f);
        int ev = sink.Events;
        p.SetArg(0, 0, AnimValue.FromRef(hand.transform));
        p.Rebuild();
        p.Sample(0, 7f);
        Check(card.transform.parent == hand.transform && sink.Events == ev, $"Sample(7): reparent uygulandi, Event susuyor (events={sink.Events}/{ev})");
        Check(Math.Abs(card.transform.localPosition.x - (-150f)) < 0.01f, $"Sample(7): Current reparent SONRASI yakalandi (-500→0 @0.7 = -150; olculen {card.transform.localPosition.x})");
        float atSeven = card.transform.localPosition.x;
        p.Sample(0, 8f);
        Check(card.transform.localPosition.x > atSeven && card.transform.localPosition.x < 0f, "ileri scrub: Current yeniden yakalanmadi (surunme yok)");

        GameObject.Destroy(deck); GameObject.Destroy(hand);
        s.Update(0f);
    }

    static void RoundTrip(Scene s, TypeCatalog catalog)
    {
        var src = Scene.Create("puppet-rt-src");
        src.Catalog = catalog;
        Scene.SetActive(src);
        var p = MakeCard(out _, out _);
        p.Cues[0].Tracks[0].Keys[1].Ease = Ease.OutBack;
        var handGo = new GameObject("hand");
        p.Cues[0].Params.Add(new CueParam { Name = "slot", Kind = ParamKind.Target, DefaultTarget = handGo.transform });
        p.Cues[0].Actions.Add(new CueAction { Frame = 0, Kind = ActionKind.Reparent, Param = 1, KeepWorld = false });
        p.Cues[0].Actions.Add(new CueAction { Frame = 3, Kind = ActionKind.Event, Label = "hit" });

        string yaml = SceneDoc.Capture(src, catalog).ToYaml();
        var dst = Scene.Create("puppet-rt-dst");
        dst.Catalog = catalog;
        Scene.SetActive(dst);
        SceneDoc.Parse(yaml).Spawn(null, catalog, null);
        Puppet r = null;
        for (int i = 0; i < dst.RootCount && r == null; i++)
            r = dst.GetRoot(i).GetComponent<Puppet>();
        Check(r != null, "Puppet round-trip");
        var d = r.Cues[0];
        Check(r.Cues.Count == 2 && d.Name == "Deal" && d.Params.Count == 2 && d.Params[0].Kind == ParamKind.Vec3 && d.Params[0].Default.x == 100f
              && d.Tracks[0].Keys[1].Source == KeySource.Param && d.Tracks[0].Keys[1].Param == 0 && d.Tracks[0].Keys[1].Ease == Ease.OutBack,
            "cue/param/key(Source=Param) round-trip");
        Check(r.Cues[1].Tracks[1].Node is Transform tr && tr.gameObject.name == "face", "track Node (cocuk Transform) cozuldu");
        Check(d.Params[1].Kind == ParamKind.Target && d.Params[1].DefaultTarget?.gameObject.name == "hand"
              && d.Actions.Count == 2 && d.Actions[0].Kind == ActionKind.Reparent && d.Actions[0].Param == 1 && !d.Actions[0].KeepWorld
              && d.Actions[1].Label == "hit",
            "Target param / aksiyon round-trip");
        r.Play("Deal", AnimValue.FromVec3(new Vec3(7, 0, 0)));
        Check(r.transform.parent?.gameObject.name == "hand", "yuklenen puppet: Reparent aksiyonu DefaultTarget'a (hand) calisti");
        Run(dst, 1.2f);
        Check(r.transform.localPosition.x == 7f, "yuklenen puppet argumanla oynar");

        Scene.SetActive(s);
        Scene.Unload(src);
        Scene.Unload(dst);
    }

    static void ZeroAlloc(Scene s)
    {
        var p = MakeCard(out var root, out _);
        root.AddComponent<CueSink>();
        p.Rebuild();
        p.CueCompleted += (pp, c) => { if (c == 0) pp.Play("Flip", AnimValue.FromBool(true)); else pp.Play("Deal", AnimValue.FromVec3(new Vec3(c * 10, 0, 0))); };
        p.Play("Deal", AnimValue.FromVec3(new Vec3(50, 0, 0)));
        for (int i = 0; i < 120; i++) s.Update(1f / 60f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) s.Update(1f / 60f);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(alloc == 0, $"steady-state cue zinciri alloc=0 (olculen {alloc} B)");
        GameObject.Destroy(root);
        s.Update(0f);
    }
}
#endif
