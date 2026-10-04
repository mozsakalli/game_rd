#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Generic animasyon altyapisi testleri (acilista, izole sahnede, Scene.Update elle).
// Kapsam: AnimRegistry (sema + ic ice yol + [Animatable] property/trigger + pseudo),
// MovieClip oynatma/loop/aksiyon/label/nested(Frame)/dedup/round-trip, Tween generic
// yol + maske, steady-state alloc=0.
public static class MovieClipTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("movieclip-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        Registry(s);
        PlaybackAndInterpolation(s);
        ActionsAndTriggersFireOnce(s);
        LabelsAndGoto(s);
        NestedViaFrameProperty(s);
        KindsAndDedup(s);
        TweenGeneric(s);
        RoundTrip(s, catalog);
        ZeroAlloc(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[movieclip] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[movieclip] FAIL: {name}"); }
    }

    // Test component'i: serilesen alanlar + ic ice + [Animatable] property + trigger.
    [Serializable]
    public sealed class Inner
    {
        public Color Tint = Color.White;
        public float Depth;
    }

    public enum Mode { A, B, C }

    public sealed class AnimTarget : Component, IClipEventHandler
    {
        public float Width = 1f;
        public int Count;
        public bool Flag;
        public Mode M;
        public Vec2 Size;
        public Inner Fill = new();
        [NonSerialized] public float Runtime;
        [Animatable] public float Intensity { get; set; }
        [Animatable] public float Hidden; // serilesmez ama animatable
        [NonSerialized] public int Emits;
        [Animatable] public void Emit() => Emits++;
        [NonSerialized] public int Events;
        public void OnClipEvent(MovieClip clip, int eventId) => Events++;
    }

    static ClipTrack Track(Component target, string path, params (int f, AnimValue v)[] keys)
    {
        var t = new ClipTrack { Target = target, Path = path };
        foreach (var (f, v) in keys)
        {
            var k = new ClipKey { Frame = f };
            k.SetAnim(v);
            t.Keys.Add(k);
        }
        return t;
    }

    static void Registry(Scene s)
    {
        var go = new GameObject("reg");
        var t = go.AddComponent<AnimTarget>();
        var cat = s.Catalog;
        var props = AnimRegistry.PropsOf(cat, typeof(AnimTarget));
        bool Has(string p) { foreach (var x in props) if (x.Path == p) return true; return false; }
        Check(Has("Width") && Has("Count") && Has("Flag") && Has("M") && Has("Size"), "serilesen skaler/enum/vektor alanlar animatable");
        Check(Has("Fill.Tint") && Has("Fill.Depth"), "ic ice yol (Fill.Tint)");
        Check(!Has("Runtime") && !Has("Fill") && !Has("Emits"), "NonSerialized/konteyner alan listede degil");
        Check(Has("Intensity") && Has("Hidden"), "[Animatable] property ve alan");
        Check(Has("Emit") && AnimRegistry.Find(cat, typeof(AnimTarget), "Emit").Kind == AnimKind.Trigger, "[Animatable] metod → Trigger");

        var tint = AnimRegistry.Find(cat, typeof(AnimTarget), "Fill.Tint");
        tint.Set(t, AnimValue.FromColor(new Color(10, 20, 30, 40)));
        Check(t.Fill.Tint.g == 20 && tint.Get(t).ToColor().a == 40, "ic ice Color Set/Get");
        AnimRegistry.Find(cat, typeof(AnimTarget), "M").Set(t, AnimValue.FromInt(2));
        Check(t.M == Mode.C, "enum Set");
        AnimRegistry.Find(cat, typeof(AnimTarget), "Count").Set(t, new AnimValue(2.6f));
        Check(t.Count == 3, "int yuvarlama");
        AnimRegistry.Find(cat, typeof(AnimTarget), "Intensity").Set(t, new AnimValue(0.5f));
        Check(t.Intensity == 0.5f, "property Set");
        AnimRegistry.Find(cat, typeof(AnimTarget), "Emit").Set(t, default);
        Check(t.Emits == 1, "trigger cagri");
        AnimRegistry.Find(cat, typeof(Transform), "Active").Set(go.transform, AnimValue.FromBool(false));
        Check(!go.activeSelf, "pseudo Transform.Active");
        AnimRegistry.Find(cat, typeof(AnimTarget), "Enabled").Set(t, AnimValue.FromBool(false));
        Check(!t.enabled, "pseudo Component.Enabled");
        Check(AnimRegistry.Find(cat, typeof(AnimTarget), "Yok") == null, "bilinmeyen yol null");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    // Kok + bir cocuk: Position.x 0→100 (frame 0→10).
    static MovieClip MakeClip(out GameObject root, out GameObject child, bool playOnStart = true)
    {
        root = new GameObject("clip");
        child = new GameObject("child");
        child.transform.parent = root.transform;
        var clip = root.AddComponent<MovieClip>();
        clip.PlayOnStart = playOnStart;
        clip.Fps = 10;
        clip.FrameCount = 11;
        clip.Loop = false;
        clip.Tracks.Add(Track(child.transform, "Position", (0, new AnimValue(0, 0, 0)), (10, new AnimValue(100, 0, 0))));
        clip.Rebuild();
        return clip;
    }

    static void PlaybackAndInterpolation(Scene s)
    {
        var clip = MakeClip(out var root, out var child);
        s.Update(0f);
        Check(clip.IsPlaying && clip.CurrentFrame == 0, "PlayOnStart frame 0");
        s.Update(0.5f);
        Check(Math.Abs(child.transform.localPosition.x - 50f) < 0.01f, "sub-frame lineer interpolasyon (5/10)");
        s.Update(0.025f);
        Check(Math.Abs(child.transform.localPosition.x - 52.5f) < 0.01f, "kesirli frame interpolasyonu");
        clip.Interpolate = false;
        s.Update(0f);
        Check(Math.Abs(child.transform.localPosition.x - 50f) < 0.01f, "Interpolate=false tam frame'e yuvarlar");
        clip.Interpolate = true;
        s.Update(1f);
        Check(!clip.IsPlaying && clip.CurrentFrame == 10, "Loop=false sonda durur");
        Check(child.transform.localPosition.x == 100f, "son key degeri");
        clip.Loop = true;
        clip.GotoAndPlay(8);
        s.Update(0.5f);
        Check(clip.IsPlaying && clip.CurrentFrame == 2, "Loop wrap (8+5 → 2)");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void ActionsAndTriggersFireOnce(Scene s)
    {
        var clip = MakeClip(out var root, out var child);
        var sink = root.AddComponent<AnimTarget>();
        var tgt = child.AddComponent<AnimTarget>();
        clip.Loop = true;
        clip.Actions.Add(new ClipAction { Frame = 3, Kind = ClipActionKind.Event, Label = "hit" });
        clip.Actions.Add(new ClipAction { Frame = 7, Kind = ClipActionKind.Stop });
        clip.Tracks.Add(Track(tgt, "Emit", (3, default)));
        clip.Rebuild();
        s.Update(0f);
        s.Update(0.1f); s.Update(0.1f); s.Update(0.1f); // frame 3'e giris
        Check(sink.Events == 1, "Event aksiyonu frame girisinde bir kez");
        Check(tgt.Emits == 1, "Trigger track frame girisinde bir kez");
        s.Update(0.1f);
        Check(sink.Events == 1 && tgt.Emits == 1, "ayni frame'de kalinca tekrar tetiklenmez");
        s.Update(0.35f);
        Check(!clip.IsPlaying && clip.CurrentFrame == 7, "Stop aksiyonu oynatmayi durdurur, kalan sure atilir");
        sink.Events = 0; tgt.Emits = 0;
        clip.GotoAndPlay(0);
        s.Update(0.5f); // 0→5, frame 3 gecildi
        Check(sink.Events == 1 && tgt.Emits == 1, "buyuk dt'de atlanan frame'in aksiyon/tetigi yine bir kez");
        clip.Loop = false;
        clip.Actions.Clear();
        clip.Rebuild();
        sink.Events = 0;
        clip.GotoAndPlay(9);
        s.Update(0.3f);
        Check(sink.Events == 1, "CompleteEvent sonda bir kez");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void LabelsAndGoto(Scene s)
    {
        var clip = MakeClip(out var root, out var child, playOnStart: false);
        clip.Labels.Add(new ClipLabel { Frame = 5, Name = "mid" });
        clip.Labels.Add(new ClipLabel { Frame = 0, Name = "start" });
        clip.Actions.Add(new ClipAction { Frame = 10, Kind = ClipActionKind.GotoAndPlay, Label = "mid" });
        clip.Rebuild();
        s.Update(0f);
        Check(!clip.IsPlaying, "PlayOnStart=false");
        Check(clip.FrameOfLabel("mid") == 5 && clip.FrameOfLabel("yok") == -1, "label → frame");
        clip.GotoAndStop("mid");
        Check(clip.CurrentFrame == 5 && !clip.IsPlaying && clip.CurrentLabel == "mid", "GotoAndStop(label)");
        Check(Math.Abs(child.transform.localPosition.x - 50f) < 0.01f, "goto sonrasi ornekleme");
        clip.GotoAndPlay(9);
        s.Update(0.1f);
        Check(clip.IsPlaying && clip.CurrentFrame == 5, "frame aksiyonu GotoAndPlay(label)");
        clip.NextFrame();
        Check(clip.CurrentFrame == 6 && !clip.IsPlaying, "NextFrame");
        clip.GotoAndPlay("yok");
        Check(clip.CurrentFrame == 6, "bilinmeyen label no-op");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    // Nested: parent, cocuk klibin "Frame" property'sini surer (Sync = lineer track).
    static void NestedViaFrameProperty(Scene s)
    {
        var parentGo = new GameObject("parent");
        var childGo = new GameObject("child");
        childGo.transform.parent = parentGo.transform;
        var leaf = new GameObject("leaf");
        leaf.transform.parent = childGo.transform;

        var child = childGo.AddComponent<MovieClip>();
        child.Fps = 10; child.FrameCount = 10; child.Loop = true; child.PlayOnStart = true;
        child.Tracks.Add(Track(leaf.transform, "Position", (0, new AnimValue(0)), (9, new AnimValue(90))));
        child.Rebuild();

        var parent = parentGo.AddComponent<MovieClip>();
        parent.Fps = 10; parent.FrameCount = 20; parent.Loop = true;
        // Sync: parent 0..19 → child 3..22 (child loop ile sarar)
        parent.Tracks.Add(Track(child, "Frame", (0, new AnimValue(3)), (19, new AnimValue(22))));
        parent.Rebuild();

        s.Update(0f);
        Check(!child.IsPlaying, "surulen child kendi basina oynamaz");
        Check(Math.Abs(leaf.transform.localPosition.x - 30f) < 0.01f, "Sync: parent 0 → child 3");
        s.Update(0.4f);
        Check(Math.Abs(leaf.transform.localPosition.x - 70f) < 0.01f, "Sync: parent 4 → child 7");
        s.Update(0.4f); // parent 8 → child 11 → wrap 1
        Check(Math.Abs(leaf.transform.localPosition.x - 10f) < 0.01f, "Sync: child loop wrap");

        parent.Tracks[0].Keys.Clear();
        var hold = new ClipKey { Frame = 0, Mode = ClipKeyMode.Hold }; hold.SetAnim(new AnimValue(9));
        parent.Tracks[0].Keys.Add(hold);
        parent.Rebuild();
        s.Update(0.1f);
        Check(leaf.transform.localPosition.x == 90f, "SingleFrame = Hold key");
        GameObject.Destroy(parentGo);
        s.Update(0f);
    }

    static void KindsAndDedup(Scene s)
    {
        var clip = MakeClip(out var root, out var child, playOnStart: false);
        var t = child.AddComponent<AnimTarget>();
        clip.Tracks.Clear();
        clip.Tracks.Add(Track(child.transform, "Active", (2, AnimValue.FromBool(true)), (5, AnimValue.FromBool(false)), (8, AnimValue.FromBool(true))));
        clip.Tracks.Add(Track(t, "Fill.Tint", (0, AnimValue.FromColor(new Color(0, 0, 0, 255))), (10, AnimValue.FromColor(new Color(200, 100, 0, 255)))));
        clip.Tracks.Add(Track(t, "Count", (0, new AnimValue(0)), (10, new AnimValue(10))));
        clip.Tracks.Add(Track(t, "M", (0, AnimValue.FromInt(0)), (10, AnimValue.FromInt(2))));
        var hold = Track(t, "Width", (2, new AnimValue(5)), (8, new AnimValue(7)));
        hold.Keys[0].Mode = ClipKeyMode.Hold;
        clip.Tracks.Add(hold);
        var masked = Track(child.transform, "Scale", (0, new AnimValue(2, 0, 0)), (10, new AnimValue(4, 0, 0)));
        masked.Mask = AnimValue.MaskX;
        clip.Tracks.Add(masked);
        clip.Rebuild();
        s.Update(0f);
        Check(child.activeSelf, "ilk key'den once ilk degere kenetlenir (Active=true)");
        clip.GotoAndStop(6);
        Check(!child.activeSelf, "Bool step: 5..7 arasi false");
        Check(t.Fill.Tint.r == 120 && t.Fill.Tint.g == 60, "Color lerp (ic ice yol)");
        Check(t.Count == 6, "Int lerp+round");
        Check(t.M == Mode.A, "Enum step (t<1 → ilk)");
        Check(t.Width == 5f, "Hold span interpolasyon yok");
        Check(Math.Abs(child.transform.localScale.x - 3.2f) < 0.001f && child.transform.localScale.y == 1f, "kanal maskesi: yalniz X yazilir");
        clip.GotoAndStop(10);
        Check(t.M == Mode.C && t.Count == 10, "son key degerleri");

        // Dedup: ayni frame'de tekrar ornekleme property'yi yeniden yazmaz.
        t.Width = 999f;
        clip.SampleFrame(10);
        Check(t.Width == 999f, "degismeyen deger yazilmaz (dedup)");
        clip.Rebuild();
        clip.SampleFrame(10);
        Check(t.Width == 7f, "Rebuild sonrasi tam yazim");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void TweenGeneric(Scene s)
    {
        var go = new GameObject("tw");
        var t = go.AddComponent<AnimTarget>();
        t.Fill.Tint = new Color(0, 0, 0, 255);
        t.Tween("Fill.Tint", new Color(100, 50, 0, 255), 1f);
        t.Tween("Width", 11f, 1f);
        t.Tween("Intensity", 2f, 1f);
        go.transform.TweenMoveX(10f, 1f);
        go.transform.localPosition = new Vec3(0, 7, 0);
        s.Update(0.5f);
        Check(t.Fill.Tint.r == 50 && t.Fill.Tint.g == 25, "Tween generic yol: ic ice Color");
        Check(Math.Abs(t.Width - 6f) < 0.01f, "Tween serilesen float");
        Check(Math.Abs(t.Intensity - 1f) < 0.01f, "Tween [Animatable] property");
        Check(Math.Abs(go.transform.localPosition.x - 5f) < 0.01f && go.transform.localPosition.y == 7f, "TweenMoveX maskesi Y'yi korur");
        var dead = t.Tween("Yok", 1f, 1f);
        Check(!dead.IsActive, "bilinmeyen yol: olu handle");
        s.Update(1f);
        Check(t.Width == 11f && t.Fill.Tint.r == 100, "tween tamamlanir");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void RoundTrip(Scene s, TypeCatalog catalog)
    {
        var src = Scene.Create("movieclip-rt-src");
        src.Catalog = catalog;
        Scene.SetActive(src);
        var clip = MakeClip(out _, out var child, playOnStart: false);
        var t = child.AddComponent<AnimTarget>();
        clip.Tracks.Add(Track(t, "Fill.Tint", (0, AnimValue.FromColor(Color.Black)), (10, AnimValue.FromColor(Color.Red))));
        clip.Tracks[1].Mask = AnimValue.MaskXYZ;
        clip.Labels.Add(new ClipLabel { Frame = 4, Name = "lbl" });
        clip.Actions.Add(new ClipAction { Frame = 10, Kind = ClipActionKind.GotoAndPlay, Label = "lbl" });
        clip.Tracks[0].Keys[1].Ease = Ease.OutBack;

        string yaml = SceneDoc.Capture(src, catalog).ToYaml();
        var dst = Scene.Create("movieclip-rt-dst");
        dst.Catalog = catalog;
        Scene.SetActive(dst);
        SceneDoc.Parse(yaml).Spawn(null, catalog, null);
        MovieClip r = null;
        for (int i = 0; i < dst.RootCount && r == null; i++)
            r = dst.GetRoot(i).GetComponent<MovieClip>();
        Check(r != null, "MovieClip round-trip");
        Check(r.Tracks.Count == 2 && r.Tracks[0].Keys.Count == 2 && r.Tracks[0].Keys[1].Frame == 10
              && r.Tracks[0].Keys[1].Ease == Ease.OutBack && r.Tracks[0].Keys[1].Value.x == 100f
              && r.Tracks[1].Mask == AnimValue.MaskXYZ && r.Tracks[1].Path == "Fill.Tint",
            "track/key/ease/mask/path round-trip");
        Check(r.Tracks[0].Target is Transform tr && tr.gameObject.name == "child" && tr.parent == r.transform,
            "track Target (Transform ref) cozuldu");
        Check(r.Tracks[1].Target is AnimTarget, "track Target (custom component ref) cozuldu");
        Check(r.Labels.Count == 1 && r.Labels[0].Name == "lbl" && r.Actions.Count == 1 && r.Actions[0].Label == "lbl",
            "label/aksiyon round-trip");
        r.GotoAndStop(10); // frame 10 aksiyonu: GotoAndPlay("lbl") → 4
        Check(r.CurrentFrame == 4 && r.IsPlaying && r.Tracks[0].Target.transform.localPosition.x == 40f,
            "yuklenen klip: hedef frame aksiyonu calisir ve ornekleme yapilir");

        Scene.SetActive(s);
        Scene.Unload(src);
        Scene.Unload(dst);
    }

    static void ZeroAlloc(Scene s)
    {
        // Karisik track'ler (Transform, Color, Int, Enum, Trigger, nested Frame) + label + aksiyon
        // + sonsuz tween: isinma sonrasi N frame oynatma hic alloc yapmamali.
        var root = new GameObject("za");
        var clip = root.AddComponent<MovieClip>();
        clip.Fps = 30; clip.FrameCount = 60; clip.Loop = true;
        root.AddComponent<AnimTarget>();
        for (int i = 0; i < 3; i++)
        {
            var g = new GameObject("l" + i);
            g.transform.parent = root.transform;
            var t = g.AddComponent<AnimTarget>();
            clip.Tracks.Add(Track(g.transform, "Position", (0, new AnimValue(0, i, 0)), (30, new AnimValue(50, i, 0)), (59, new AnimValue(0, i, 0))));
            clip.Tracks[^1].Keys[1].Ease = Ease.InOutCubic;
            clip.Tracks.Add(Track(g.transform, "Rotation", (0, new AnimValue(0, 0, 0)), (59, new AnimValue(0, 0, 360))));
            clip.Tracks.Add(Track(t, "Fill.Tint", (0, AnimValue.FromColor(Color.Black)), (59, AnimValue.FromColor(Color.White))));
            clip.Tracks.Add(Track(t, "Count", (0, new AnimValue(0)), (59, new AnimValue(100))));
            clip.Tracks.Add(Track(t, "M", (0, AnimValue.FromInt(0)), (30, AnimValue.FromInt(2))));
            clip.Tracks.Add(Track(t, "Emit", (15, default), (45, default)));
        }
        var nestedGo = new GameObject("nested");
        nestedGo.transform.parent = root.transform;
        var nested = nestedGo.AddComponent<MovieClip>();
        nested.Fps = 30; nested.FrameCount = 10;
        nested.Tracks.Add(Track(nestedGo.transform, "Scale", (0, new AnimValue(1, 1, 1)), (9, new AnimValue(2, 2, 1))));
        nested.Rebuild();
        clip.Tracks.Add(Track(nested, "Frame", (0, new AnimValue(0)), (59, new AnimValue(59))));
        clip.Labels.Add(new ClipLabel { Frame = 30, Name = "half" });
        clip.Actions.Add(new ClipAction { Frame = 15, Kind = ClipActionKind.Event, Label = "tick" });
        clip.Actions.Add(new ClipAction { Frame = 59, Kind = ClipActionKind.GotoAndPlay, Label = "half" });
        clip.Rebuild();
        root.transform.TweenMoveX(10f, 100f).Loops(-1, yoyo: true);

        for (int i = 0; i < 120; i++) s.Update(1f / 60f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) s.Update(1f / 60f);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(alloc == 0, $"steady-state oynatma alloc=0 (olculen {alloc} B)");
        GameObject.Destroy(root);
        s.Update(0f);
    }
}
#endif
