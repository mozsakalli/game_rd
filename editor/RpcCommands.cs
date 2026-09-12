using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Builtin RPC komutlari: hepsi mevcut EditorScene bogazlarina ince marshal —
// undo/redo, prefab diff, canli patch bedava (Inspector'la ayni yol).
static class RpcCommands
{
    // --- kesif ---

    [RpcCommand("rpc.describe", "Tum komutlari parametre semalariyla listeler")]
    static DocNode Describe() => RpcRegistry.Describe();

    // --- gozlem ---

    [RpcCommand("scene.dump", "Acik sahnenin doc dokumu (id/parent/ad/transform/component'ler); diff baseline'ini sifirlar")]
    static DocNode SceneDump(RpcContext ctx)
    {
        var root = DocNode.Map();
        root.Add("name", DocNode.Scal(ctx.Scene.Doc.Name));
        root.Add("path", DocNode.Scal(ctx.Scene.Path ?? ""));
        root.Add("dirty", DocNode.Scal(ctx.Scene.Dirty ? "true" : "false"));
        var objects = DocNode.Seq();
        _diffBaseline.Clear();
        foreach (var g in ctx.Scene.Doc.Objects)
        {
            var og = DumpGo(g);
            _diffBaseline[g.Id] = og.Clone();
            objects.Items.Add(og);
        }
        root.Add("objects", objects);
        return root;
    }

    static DocNode DumpGo(SceneDoc.GoDoc g)
    {
        var og = DocNode.Map();
        og.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
        og.Add("parent", DocNode.Scal(g.Parent.ToString(CultureInfo.InvariantCulture)));
        og.Add("name", DocNode.Scal(g.Name));
        if (!g.Active)
            og.Add("active", DocNode.Scal("false"));
        og.Add("pos", DocNode.Scal(V3(g.Pos)));
        og.Add("rot", DocNode.Scal(V3(g.Rot)));
        og.Add("scale", DocNode.Scal(V3(g.Scale)));
        if (g.PrefabGuid != null)
            og.Add("prefab", DocNode.Scal(g.PrefabGuid));
        if (g.Components.Count > 0)
        {
            var comps = DocNode.Seq();
            foreach (var cd in g.Components)
            {
                var oc = DocNode.Map();
                oc.Add("type", DocNode.Scal(cd.Type));
                if (!cd.Enabled)
                    oc.Add("enabled", DocNode.Scal("false"));
                if (cd.Props.Count > 0)
                {
                    var props = DocNode.Map();
                    foreach (var kv in cd.Props)
                        props.Add(kv.Key, kv.Value.Clone());
                    oc.Add("props", props);
                }
                comps.Items.Add(oc);
            }
            og.Add("components", comps);
        }
        return og;
    }

    // Son dump/diff'ten beri degisenler: baseline id->dump klonu (tam dokum yerine
    // kompakt geri bildirim — AI her komut sonrasi ucuza "ne degisti" gorur).
    static readonly Dictionary<int, DocNode> _diffBaseline = new();

    [RpcCommand("scene.diff", "Son scene.dump/diff'ten beri eklenen/silinen/degisen GO'lar")]
    static DocNode SceneDiff(RpcContext ctx)
    {
        var added = DocNode.Seq();
        var changed = DocNode.Seq();
        var removed = DocNode.Seq();
        var seen = new HashSet<int>();
        foreach (var g in ctx.Scene.Doc.Objects)
        {
            seen.Add(g.Id);
            var now = DumpGo(g);
            if (!_diffBaseline.TryGetValue(g.Id, out var was))
                added.Items.Add(now.Clone());
            else if (!DocNode.Equal(now, was))
                changed.Items.Add(now.Clone());
            _diffBaseline[g.Id] = now;
        }
        var gone = new List<int>();
        foreach (var id in _diffBaseline.Keys)
            if (!seen.Contains(id))
                gone.Add(id);
        gone.Sort();
        foreach (var id in gone)
        {
            removed.Items.Add(DocNode.Scal(id.ToString(CultureInfo.InvariantCulture)));
            _diffBaseline.Remove(id);
        }
        var root = DocNode.Map();
        root.Add("added", added);
        root.Add("changed", changed);
        root.Add("removed", removed);
        return root;
    }

    [RpcCommand("scene.dumpLive", "CANLI sahne dokumu (runtime degerler; which: edit|play)")]
    static DocNode DumpLive(RpcContext ctx, string which = "edit")
    {
        var scene = which switch
        {
            "edit" => ctx.Scene.LiveScene,
            "play" => PlayMode.PlayScene,
            _ => throw new RpcError("which 'edit' ya da 'play' olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}" + (which == "play" ? " (play.start ile baslat)" : ""));
        var root = DocNode.Map();
        root.Add("name", DocNode.Scal(scene.Name));
        var objects = DocNode.Seq();
        for (int i = 0; i < scene.RootCount; i++)
            DumpLiveGo(scene.GetRoot(i), 0, objects);
        root.Add("objects", objects);
        return root;
    }

    static void DumpLiveGo(GameObject go, int depth, DocNode into)
    {
        var og = DocNode.Map();
        og.Add("name", DocNode.Scal(go.name));
        og.Add("depth", DocNode.Scal(depth.ToString(CultureInfo.InvariantCulture)));
        if (!go.activeSelf)
            og.Add("active", DocNode.Scal("false"));
        og.Add("pos", DocNode.Scal(V3(go.transform.localPosition)));
        og.Add("rot", DocNode.Scal(V3(go.transform.localEulerAngles)));
        og.Add("scale", DocNode.Scal(V3(go.transform.localScale)));
        var comps = DocNode.Seq();
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var c = go.ComponentAt(i);
            if (c != null && c is not Transform)
                comps.Items.Add(DocNode.Scal(c.GetType().Name));
        }
        if (comps.Items.Count > 0)
            og.Add("components", comps);
        into.Items.Add(og);
        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t.gameObject != null)
                DumpLiveGo(t.gameObject, depth + 1, into);
    }

    [RpcCommand("comp.schema", "Component tipinin serilesen alan semasi")]
    static DocNode CompSchema(RpcContext ctx, string type)
    {
        var entry = ctx.Catalog.Find(type)
            ?? throw new RpcError($"bilinmeyen component tipi: '{type}'");
        var fields = DocNode.Seq();
        foreach (var f in entry.Schema)
        {
            var fd = DocNode.Map();
            fd.Add("name", DocNode.Scal(f.Name));
            fd.Add("kind", DocNode.Scal(f.Kind.ToString()));
            if (f.Kind == SerializedType.Kind.List)
                fd.Add("elementKind", DocNode.Scal(f.ElementKind.ToString()));
            if (f.Kind == SerializedType.Kind.Enum && f.Info != null)
                fd.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(f.Info.FieldType))));
            fields.Items.Add(fd);
        }
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(entry.Name));
        root.Add("fields", fields);
        return root;
    }

    [RpcCommand("comp.types", "Katalogdaki tum component tipleri")]
    static DocNode CompTypes(RpcContext ctx)
    {
        var seq = DocNode.Seq();
        var names = new System.Collections.Generic.List<string>();
        foreach (var e in ctx.Catalog.Entries)
            names.Add(e.Name);
        names.Sort(StringComparer.Ordinal);
        foreach (var n in names)
            seq.Items.Add(DocNode.Scal(n));
        var root = DocNode.Map();
        root.Add("types", seq);
        return root;
    }

    // --- yapisal mutasyonlar ---

    [RpcCommand("scene.addGo", "Yeni GameObject ekler, id doner")]
    static DocNode AddGo(RpcContext ctx, int parent = 0, string name = "")
    {
        if (parent != 0)
            RequireGo(ctx, parent); // parent yoksa erken ve net hata
        var g = ctx.Scene.AddGameObject(parent);
        if (name.Length > 0)
            ctx.Scene.SetName(g, name);
        var r = DocNode.Map();
        r.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
        r.Add("name", DocNode.Scal(g.Name));
        return r;
    }

    [RpcCommand("scene.removeGo", "GameObject'i (alt agaciyla) siler")]
    static DocNode RemoveGo(RpcContext ctx, int id)
    {
        ctx.Scene.RemoveGameObject(RequireGo(ctx, id));
        return null;
    }

    [RpcCommand("scene.rename", "GameObject'i yeniden adlandirir")]
    static DocNode Rename(RpcContext ctx, int id, string name)
    {
        ctx.Scene.SetName(RequireGo(ctx, id), name);
        return null;
    }

    [RpcCommand("scene.reparent", "GameObject'i baska parent'a tasir (0 = kok)")]
    static DocNode ReparentGo(RpcContext ctx, int id, int parent)
    {
        var g = RequireGo(ctx, id);
        if (parent != 0)
            RequireGo(ctx, parent);
        ctx.Scene.Reparent(g, parent);
        return null;
    }

    [RpcCommand("comp.add", "Component ekler, index doner")]
    static DocNode CompAdd(RpcContext ctx, int go, string type)
    {
        var g = RequireGo(ctx, go);
        if (ctx.Catalog.Find(type) == null)
            throw new RpcError($"bilinmeyen component tipi: '{type}' (comp.types ile listele)");
        var cd = ctx.Scene.AddComponent(g, type);
        var r = DocNode.Map();
        r.Add("index", DocNode.Scal(g.Components.IndexOf(cd).ToString(CultureInfo.InvariantCulture)));
        return r;
    }

    [RpcCommand("comp.remove", "Component'i index'iyle kaldirir")]
    static DocNode CompRemove(RpcContext ctx, int go, int comp)
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.RemoveComponent(g, RequireComp(g, comp));
        return null;
    }

    // --- property mutasyonlari ---

    [RpcCommand("prop.set", "Component alanina deger yazar (kanonik doc formati; null = default'a don)")]
    static DocNode PropSet(RpcContext ctx, int go, int comp, string prop, DocNode value = null)
    {
        var g = RequireGo(ctx, go);
        var cd = RequireComp(g, comp);
        var entry = ctx.Catalog.Find(cd.Type)
            ?? throw new RpcError($"component tipi katalogda yok: '{cd.Type}'");
        var f = SerializedType.Find(entry.Schema, prop)
            ?? throw new RpcError($"'{cd.Type}' tipinde alan yok: '{prop}' (comp.schema ile listele)");
        var old = EditorScene.FindProp(cd, f);
        ctx.Scene.SetProp(g, cd, f, old?.Clone(), value);
        ctx.Scene.History.SealTop(); // her RPC yazimi ayri undo girisi (jest yok)
        return null;
    }

    [RpcCommand("transform.set", "Pos/rot/scale yazar ('x y z' formati; bos = dokunma)")]
    static DocNode TransformSet(RpcContext ctx, int go, string pos = "", string rot = "", string scale = "")
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.SetTransform(g,
            pos.Length > 0 ? ParseV3(pos, "pos") : g.Pos,
            rot.Length > 0 ? ParseV3(rot, "rot") : g.Rot,
            scale.Length > 0 ? ParseV3(scale, "scale") : g.Scale);
        ctx.Scene.History.SealTop();
        return null;
    }

    [RpcCommand("go.setActive", "GameObject aktifligini degistirir")]
    static DocNode SetActive(RpcContext ctx, int id, bool active)
    {
        ctx.Scene.SetActive(RequireGo(ctx, id), active);
        return null;
    }

    [RpcCommand("comp.setEnabled", "Component enabled durumunu degistirir")]
    static DocNode SetEnabled(RpcContext ctx, int go, int comp, bool enabled)
    {
        var g = RequireGo(ctx, go);
        ctx.Scene.SetEnabled(g, RequireComp(g, comp), enabled);
        return null;
    }

    // --- oturum ---

    [RpcCommand("edit.undo", "Son islemi geri alir")]
    static DocNode Undo(RpcContext ctx)
    {
        ctx.Scene.DoUndo();
        return null;
    }

    [RpcCommand("edit.redo", "Geri alinan islemi yineler")]
    static DocNode Redo(RpcContext ctx)
    {
        ctx.Scene.DoRedo();
        return null;
    }

    [RpcCommand("scene.save", "Acik sahneyi diske yazar")]
    static DocNode Save(RpcContext ctx)
    {
        if (ctx.Scene.Path == null)
            throw new RpcError("acik sahne yok");
        ctx.Scene.Save();
        return null;
    }

    [RpcCommand("scene.new", "Yeni bos sahne yaratir ve acar (yol Assets'e goreli, .scene)")]
    static DocNode SceneNew(RpcContext ctx, string path)
    {
        string full = GuardAssetPath(path);
        if (File.Exists(full))
            throw new RpcError($"dosya zaten var: {path} (scene.open kullan)");
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        App.OpenScene(full); // dosya yoksa bos doc acilir
        App.EditScene.Save(); // dosyayi yarat
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("scene.open", "Sahne acar (yol Assets'e goreli)")]
    static DocNode SceneOpen(RpcContext ctx, string path)
    {
        string full = GuardAssetPath(path);
        if (!File.Exists(full))
            throw new RpcError($"sahne yok: {path}");
        App.OpenScene(full);
        _diffBaseline.Clear();
        return null;
    }

    [RpcCommand("play.start", "Play moduna gecer")]
    static DocNode PlayStart()
    {
        PlayMode.Play();
        return null;
    }

    [RpcCommand("play.stop", "Play'i durdurur (doc'a doner)")]
    static DocNode PlayStop()
    {
        PlayMode.Stop();
        return null;
    }

    [RpcCommand("sim.run", "N frame simule eder (play degilse baslatir), canli dokumle doner; stop=true sonda durdurur")]
    static DocNode SimRun(RpcContext ctx, int frames = 60, bool stop = false)
    {
        if (frames < 1 || frames > 100000)
            throw new RpcError("frames 1..100000 olmali");
        if (PlayMode.State == PlayState.Editing)
            PlayMode.Play();
        int target = RpcHost.FrameCount + frames;
        RpcHost.Defer(() =>
        {
            if (RpcHost.FrameCount < target)
                return null; // bekle
            var dump = DumpLive(ctx, "play");
            if (stop)
                PlayMode.Stop();
            return dump;
        });
        return null;
    }

    // --- dosya/kod kanali (toptan YAML uretimi + kod yazimi dosyadan; watcher gerisini yapar) ---

    [RpcCommand("asset.write", "Assets altina dosya yazar (yol Assets'e goreli); watcher derleme/reload tetikler")]
    static DocNode AssetWrite(RpcContext ctx, string path, string content)
    {
        string full = GuardAssetPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, content);
        var r = DocNode.Map();
        r.Add("path", DocNode.Scal(full));
        return r;
    }

    [RpcCommand("asset.read", "Assets altindan dosya okur")]
    static DocNode AssetRead(RpcContext ctx, string path)
    {
        string full = GuardAssetPath(path);
        if (!File.Exists(full))
            throw new RpcError($"dosya yok: {path}");
        var r = DocNode.Map();
        r.Add("content", DocNode.Scal(File.ReadAllText(full)));
        return r;
    }

    [RpcCommand("asset.importers", "Kayitli asset importer'lari (uzanti/tip/versiyon)")]
    static DocNode AssetImporters()
    {
        var r = DocNode.Map();
        var seq = DocNode.Seq();
        foreach (var e in ImportPipeline.Entries)
        {
            var m = DocNode.Map();
            m.Add("type", DocNode.Scal(e.Type.Name));
            m.Add("extensions", DocNode.Scal(string.Join(" ", e.Extensions)));
            m.Add("version", DocNode.Scal(e.Version.ToString(CultureInfo.InvariantCulture)));
            seq.Items.Add(m);
        }
        r.Add("importers", seq);
        return r;
    }

    [RpcCommand("asset.reimport", "Kaynagi zorla yeniden import eder, artifact listesi doner")]
    static DocNode AssetReimport(RpcContext ctx, string path)
    {
        GuardAssetPath(path); // traversal guard'i (yol Assets'e goreli)
        string rel = path.Replace('\\', '/');
        if (ImportPipeline.ImporterFor(rel) == null)
            throw new RpcError($"bu uzanti icin importer yok: {rel}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string dir = ImportPipeline.EnsureImported(rel, force: true);
        if (dir == null)
            throw new RpcError($"import basarisiz: {rel} (log.tail ile detay)");
        ctx.Assets.InvalidateImported(rel);
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal("true"));
        r.Add("ms", DocNode.Scal(sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)));
        var arts = DocNode.Seq();
        foreach (var f in Directory.GetFiles(dir))
        {
            string name = Path.GetFileName(f);
            if (name == ".stamp")
                continue;
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(name));
            m.Add("bytes", DocNode.Scal(new FileInfo(f).Length.ToString(CultureInfo.InvariantCulture)));
            arts.Items.Add(m);
        }
        r.Add("artifacts", arts);
        return r;
    }

    [RpcCommand("code.build", "Oyun kodunu derler; sonuc (ok + hatalar) derleme bitince doner")]
    static DocNode CodeBuild()
    {
        int v0 = AssetWatcher.BuildVersion;
        AssetWatcher.RequestBuild();
        RpcHost.Defer(() =>
        {
            if (AssetWatcher.BuildVersion == v0)
                return null; // derleme suruyor
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal(AssetWatcher.LastBuildOk ? "true" : "false"));
            if (!AssetWatcher.LastBuildOk)
            {
                var errs = DocNode.Seq();
                foreach (var line in AssetWatcher.LastBuildOutput.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Contains(": error ") && errs.Items.Count < 30)
                        errs.Items.Add(DocNode.Scal(t));
                }
                r.Add("errors", errs);
            }
            return r;
        });
        return null;
    }

    [RpcCommand("log.tail", "Editor log'unun son N girisi (derleme/import hatalari burada)")]
    static DocNode LogTail(int count = 20)
    {
        var snap = new List<EditorLog.Entry>();
        EditorLog.Snapshot(snap);
        var seq = DocNode.Seq();
        for (int i = Math.Max(0, snap.Count - count); i < snap.Count; i++)
        {
            var e = DocNode.Map();
            e.Add("sev", DocNode.Scal(snap[i].Sev.ToString()));
            e.Add("msg", DocNode.Scal(snap[i].Message));
            seq.Items.Add(e);
        }
        var r = DocNode.Map();
        r.Add("entries", seq);
        return r;
    }

    // --- yardimcilar ---

    // Yol Assets/ disina kacamaz (path traversal guard'i).
    static string GuardAssetPath(string rel)
    {
        string root = Path.GetFullPath(App.Project.AssetsPath);
        string full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new RpcError($"yol Assets disinda: {rel}");
        return full;
    }

    static SceneDoc.GoDoc RequireGo(RpcContext ctx, int id)
        => ctx.Scene.FindGo(id) ?? throw new RpcError($"GameObject yok: id={id}");

    static SceneDoc.CompDoc RequireComp(SceneDoc.GoDoc g, int index)
        => index >= 0 && index < g.Components.Count
            ? g.Components[index]
            : throw new RpcError($"component index gecersiz: {index} (go '{g.Name}' {g.Components.Count} component)");

    static string V3(Vec3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.x:R} {v.y:R} {v.z:R}");

    static Vec3 ParseV3(string s, string label)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            throw new RpcError($"'{label}' 'x y z' formatinda degil: '{s}'");
        return new Vec3(x, y, z);
    }
}
