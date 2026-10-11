using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

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

    // Faz 0 regresyon araci (docs/registry-removal.md): player'daki Build/scene-dump.flag ciktisiyla birebir karsilastirilir.
    [RpcCommand("scene.dumpFields", "Canli sahnenin katalog semasindaki TUM alanlarini metne doker (SceneDump; which: edit|play)")]
    static DocNode DumpFields(RpcContext ctx, string which = "edit")
    {
        var scene = which switch
        {
            "edit" => ctx.Scene.LiveScene,
            "play" => PlayMode.PlayScene,
            _ => throw new RpcError("which 'edit' ya da 'play' olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}");
        var r = DocNode.Map();
        r.Add("text", DocNode.Scal(DigitoyEngine.SceneDump.Write(scene, ctx.Catalog)));
        return r;
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

    [RpcCommand("comp.schema", "Component tipinin serilesen alan semasi (ic ice nesne/liste elemani alanlari 'fields' altinda; ref biciminde 'format')")]
    static DocNode CompSchema(RpcContext ctx, string type)
    {
        var entry = ctx.Catalog.Find(type)
            ?? throw new RpcError($"bilinmeyen component tipi: '{type}'");
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(entry.Name));
        root.Add("fields", SchemaFields(entry.Schema));
        root.Add("hint", DocNode.Scal("prop.set 'prop' ic ice yol alir: Tracks[0].Keys[1].Value (index == eleman sayisi = sona ekle). "
            + "Animatable yol listesi: anim.props(type)."));
        return root;
    }

    static DocNode SchemaFields(SerializedType.FieldSchema[] schema)
    {
        var fields = DocNode.Seq();
        foreach (var f in schema)
        {
            var fd = DocNode.Map();
            fd.Add("name", DocNode.Scal(f.Name));
            fd.Add("kind", DocNode.Scal(f.Kind.ToString()));
            var leaf = f.Kind;
            if (f.Kind == SerializedType.Kind.List)
            {
                fd.Add("elementKind", DocNode.Scal(f.ElementKind.ToString()));
                leaf = f.ElementKind;
            }
            var leafType = f.Kind == SerializedType.Kind.List ? f.ElementType : f.FieldType;
            if (leaf == SerializedType.Kind.Enum && leafType != null)
                fd.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(leafType))));
            string format = RefFormat(leaf, leafType);
            if (format != null)
                fd.Add("format", DocNode.Scal(format));
            if (f.Nested != null && f.Nested.Length > 0)
                fd.Add("fields", SchemaFields(f.Nested));
            fields.Items.Add(fd);
        }
        return fields;
    }

    // Ajanin tahmin etmek zorunda kalmamasi icin skaler bicim sozlesmesi (doc kanonik).
    static string RefFormat(SerializedType.Kind kind, Type type) => kind switch
    {
        SerializedType.Kind.CompRef => "\"goId:TypeName:ordinal\" — ayni GO'daki ayni tipten kacinci (0 tabanli); Transform icin \"goId:Transform:0\""
            + (type != null && type != typeof(Component) ? $"; tip: {type.Name}" : ""),
        SerializedType.Kind.GoRef => "\"goId\"",
        SerializedType.Kind.Asset => "asset guid ya da Assets'e goreli yol (asset.list)" + (type != null ? $"; tip: {type.Name}" : ""),
        SerializedType.Kind.Vec2 => "\"x y\"",
        SerializedType.Kind.Vec3 => "\"x y z\"",
        SerializedType.Kind.Vec4 => "\"x y z w\"",
        SerializedType.Kind.Color => "\"r g b a\" 0..255",
        SerializedType.Kind.Bool => "true|false",
        _ => null,
    };

    [RpcCommand("anim.props", "Tipin animatable property yollari (Tween path / MovieClip ClipTrack.Path): Transform dahil; kind=Float|Vec3|Color|Trigger... ve kanal sayisi")]
    static DocNode AnimProps(RpcContext ctx, string type)
    {
        Type t = type == "Transform" ? typeof(Transform) : ctx.Catalog.Find(type)?.Type;
        if (t == null)
            throw new RpcError($"bilinmeyen tip: '{type}' (comp.types ya da Transform)");
        var seq = DocNode.Seq();
        void Add(AnimProperty p)
        {
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(p.Path));
            m.Add("kind", DocNode.Scal(p.Kind.ToString()));
            m.Add("channels", DocNode.Scal(Channels(p.Kind).ToString(CultureInfo.InvariantCulture)));
            if (p.Kind == AnimKind.Enum && p.ValueType != null)
                m.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(p.ValueType))));
            if (p.Kind == AnimKind.Ref && p.ValueType != null)
                m.Add("refType", DocNode.Scal(p.ValueType.Name));
            seq.Items.Add(m);
        }
        foreach (var p in AnimRegistry.PropsOf(ctx.Catalog, t))
            Add(p);
        if (t != typeof(Transform))
            Add(AnimRegistry.Enabled);
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(t.Name));
        root.Add("props", seq);
        root.Add("hint", DocNode.Scal("ClipKey.Value = \"x y z w\" (Float/Int/Bool/Enum: x; Vec2: x y; Vec3: x y z; Color: r g b a 0..255); "
            + "Ref turleri ClipKey.Ref; Trigger'da deger yok. ClipTrack.Mask bit: 1=X 2=Y 4=Z 8=W (15=hepsi). Frame = Fps * saniye."));
        return root;
    }

    static int Channels(AnimKind k) => k switch
    {
        AnimKind.Vec2 => 2,
        AnimKind.Vec3 => 3,
        AnimKind.Vec4 or AnimKind.Color => 4,
        AnimKind.Trigger => 0,
        _ => 1,
    };

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

    // Motor API'si (oyun kodu yazarken): engine assembly'sinin PUBLIC yuzeyi reflection'la.
    // type bos = tip listesi (ad + kind + base); dolu = o tipin uyeleri (C# imzasi). Component
    // alt tiplerinde protected virtual lifecycle/pointer kancalari da listelenir (override edilecekler).
    [RpcCommand("engine.api", "Motor API'si: type bos = public tip listesi (filter ad icerir); type dolu = uyeler (imza + protected override kancalari)")]
    static DocNode EngineApi(string type = "", string filter = "")
    {
        var asm = typeof(GameObject).Assembly;
        var root = DocNode.Map();
        if (type.Length == 0)
        {
            var seq = DocNode.Seq();
            var types = new List<Type>();
            foreach (var t in asm.GetExportedTypes())
                if (!t.IsNested && (filter.Length == 0 || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    types.Add(t);
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (var t in types)
            {
                var m = DocNode.Map();
                m.Add("name", DocNode.Scal(t.Name));
                m.Add("kind", DocNode.Scal(t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static" : "class"));
                if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum))
                    m.Add("base", DocNode.Scal(t.BaseType.Name));
                seq.Items.Add(m);
            }
            root.Add("types", seq);
            return root;
        }
        Type target = null;
        foreach (var t in asm.GetExportedTypes())
            if (string.Equals(t.Name, type, StringComparison.Ordinal)) { target = t; break; }
        if (target == null)
            throw new RpcError($"motor tipi yok: '{type}' (engine.api ile listele)");
        root.Add("type", DocNode.Scal(target.Name));
        if (target.BaseType != null && target.BaseType != typeof(object))
            root.Add("base", DocNode.Scal(target.BaseType.Name));
        var members = DocNode.Seq();
        if (target.IsEnum)
        {
            root.Add("values", DocNode.Scal(string.Join(" ", Enum.GetNames(target))));
            return root;
        }
        const System.Reflection.BindingFlags Pub = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        // Yuru: tip + ata zinciri (engine'de tanimli olanlar), tekrar eden imzalar atlanir.
        var seen = new HashSet<string>();
        for (var t = target; t != null && t.Assembly == asm; t = t.BaseType)
        {
            foreach (var f in t.GetFields(Pub))
                Add(members, seen, $"{(f.IsStatic ? "static " : "")}{Sig(f.FieldType)} {f.Name}", "field");
            foreach (var p in t.GetProperties(Pub))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string acc = (p.CanRead ? "get;" : "") + (p.CanWrite && p.SetMethod.IsPublic ? " set;" : "");
                Add(members, seen, $"{(p.GetMethod?.IsStatic == true ? "static " : "")}{Sig(p.PropertyType)} {p.Name} {{ {acc} }}", "property");
            }
            foreach (var mi in t.GetMethods(Pub | System.Reflection.BindingFlags.NonPublic))
            {
                if (mi.IsSpecialName) continue;
                bool hook = mi.IsFamily || mi.IsFamilyOrAssembly; // protected (internal) virtual kanca
                if (!mi.IsPublic && !(hook && mi.IsVirtual)) continue;
                if (mi.DeclaringType == typeof(object)) continue;
                string ps = string.Join(", ", Array.ConvertAll(mi.GetParameters(), q => $"{(q.ParameterType.IsByRef ? (q.IsIn ? "in " : q.IsOut ? "out " : "ref ") : "")}{Sig(q.ParameterType.IsByRef ? q.ParameterType.GetElementType() : q.ParameterType)} {q.Name}{(q.HasDefaultValue ? " = " + (q.DefaultValue is null ? "null" : Convert.ToString(q.DefaultValue, CultureInfo.InvariantCulture)) : "")}"));
                string pre = hook ? "protected override " : (mi.IsStatic ? "static " : "") + (mi.IsVirtual && !mi.IsFinal ? "virtual " : "");
                Add(members, seen, $"{pre}{Sig(mi.ReturnType)} {mi.Name}({ps})", hook ? "hook" : (mi.IsStatic && mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false) ? "extension" : "method"));
            }
        }
        // Extension metodlar (Tween vb.): bu tipi ilk parametre alan public static'ler.
        foreach (var t in asm.GetExportedTypes())
        {
            if (!(t.IsAbstract && t.IsSealed)) continue;
            foreach (var mi in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)) continue;
                var ps0 = mi.GetParameters();
                if (ps0.Length == 0 || !ps0[0].ParameterType.IsAssignableFrom(target)) continue;
                string ps = string.Join(", ", Array.ConvertAll(ps0[1..], q => $"{Sig(q.ParameterType.IsByRef ? q.ParameterType.GetElementType() : q.ParameterType)} {q.Name}{(q.HasDefaultValue ? " = " + (q.DefaultValue is null ? "null" : Convert.ToString(q.DefaultValue, CultureInfo.InvariantCulture)) : "")}"));
                Add(members, seen, $"{Sig(mi.ReturnType)} {mi.Name}({ps})  // extension ({t.Name})", "extension");
            }
        }
        root.Add("members", members);
        return root;

        static void Add(DocNode seq, HashSet<string> seen, string sig, string kind)
        {
            if (!seen.Add(sig)) return;
            var m = DocNode.Map();
            m.Add("kind", DocNode.Scal(kind));
            m.Add("sig", DocNode.Scal(sig));
            seq.Items.Add(m);
        }

        static string Sig(Type t)
        {
            if (t == typeof(void)) return "void";
            if (t == typeof(float)) return "float";
            if (t == typeof(int)) return "int";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(object)) return "object";
            if (t.IsArray) return Sig(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                string n = t.Name[..t.Name.IndexOf('`')];
                return n + "<" + string.Join(", ", Array.ConvertAll(t.GetGenericArguments(), Sig)) + ">";
            }
            return t.Name;
        }
    }

    // --- yapisal mutasyonlar ---

    [RpcCommand("scene.addGo", "Yeni GameObject ekler, id doner. insertBefore = onune gelecegi kardes id (0 = sona). Paralel cagrilarda varis sirasi belirsizdir: sira onemliyse ardisik cagir veya insertBefore ver")]
    static DocNode AddGo(RpcContext ctx, int parent = 0, string name = "", int insertBefore = 0)
    {
        if (parent != 0)
            RequireGo(ctx, parent); // parent yoksa erken ve net hata
        if (insertBefore != 0 && RequireGo(ctx, insertBefore).Parent != parent)
            throw new RpcError($"insertBefore={insertBefore} hedef parent'in ({parent}) cocugu degil");
        var g = ctx.Scene.AddGameObject(parent, insertBefore);
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

    [RpcCommand("scene.reparent", "GameObject'i parent'a tasir (0 = kok) ve/veya kardes sirasini degistirir: insertBefore = onune gelecegi kardes id (0 = sona). Ayni parent + insertBefore = yalniz siralama. Liste sirasi = hiyerarsi = ayni SortingOrder'da cizim sirasi (sonraki ustte)")]
    static DocNode ReparentGo(RpcContext ctx, int id, int parent, int insertBefore = -1)
    {
        var g = RequireGo(ctx, id);
        if (parent != 0)
            RequireGo(ctx, parent);
        if (insertBefore < 0)
            ctx.Scene.Reparent(g, parent);
        else
        {
            if (insertBefore != 0 && RequireGo(ctx, insertBefore).Parent != parent)
                throw new RpcError($"insertBefore={insertBefore} hedef parent'in ({parent}) cocugu degil");
            ctx.Scene.MoveGameObject(g, parent, insertBefore);
        }
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

    [RpcCommand("prop.set", "Component alanina deger yazar (kanonik doc formati; null = default'a don). prop ic ice yol alabilir: 'Tracks[0].Keys[1].Value' — liste index'i eleman sayisina esitse SONA EKLER, ic ice yolda null liste elemanini SILER")]
    static DocNode PropSet(RpcContext ctx, int go, int comp, string prop, DocNode value = null)
    {
        var g = RequireGo(ctx, go);
        var cd = RequireComp(g, comp);
        var entry = ctx.Catalog.Find(cd.Type)
            ?? throw new RpcError($"component tipi katalogda yok: '{cd.Type}'");
        int cut = prop.IndexOfAny(new[] { '.', '[' });
        string rootName = cut < 0 ? prop : prop[..cut];
        var f = SerializedType.Find(entry.Schema, rootName)
            ?? throw new RpcError($"'{cd.Type}' tipinde alan yok: '{rootName}' (comp.schema ile listele)");
        var old = EditorScene.FindProp(cd, f);
        DocNode next = value;
        if (cut >= 0)
        {
            // Ic ice yol: kok dugumu klonla, yolu izleyerek yaprağı degistir, kokun tamamini yaz
            // (tek PropOp = tek undo; canli patch kok alani yeniden kurar → OnValidate/Rebuild).
            next = (old ?? InspectorPanel.CreateDefaultFieldNode(f)).Clone();
            SetNested(next, f.Kind, f.FieldType, f.ElementKind, f.ElementType, f.Nested,
                prop, cut, value, prop);
        }
        ctx.Scene.SetProp(g, cd, f, old?.Clone(), next);
        ctx.Scene.History.SealTop(); // her RPC yazimi ayri undo girisi (jest yok)
        return null;
    }

    // Yol grameri: ('.' ad | '[' index ']')*. Sema ile birlikte yurunur ki eksik liste
    // elemani / ic nesne dogru varsayilanla yaratilsin ve yanlis yol erken hata versin.
    static void SetNested(DocNode node, SerializedType.Kind kind, Type valueType,
        SerializedType.Kind elementKind, Type elementType, SerializedType.FieldSchema[] nested,
        string path, int pos, DocNode value, string fullPath)
    {
        if (path[pos] == '[')
        {
            if (kind != SerializedType.Kind.List)
                throw new RpcError($"'{fullPath}': '{path[..pos]}' liste degil");
            int close = path.IndexOf(']', pos);
            if (close < 0 || !int.TryParse(path.AsSpan(pos + 1, close - pos - 1), out int index))
                throw new RpcError($"'{fullPath}': gecersiz index");
            node.Items ??= new List<DocNode>();
            if (index < 0 || index > node.Items.Count)
                throw new RpcError($"'{fullPath}': index {index} aralik disi (eleman sayisi {node.Items.Count}; sona eklemek icin {node.Items.Count})");
            int after = close + 1;
            if (after >= path.Length)
            {
                // Yaprak = liste elemani: null siler, index==count ekler, aksi degistirir.
                if (value == null) { if (index < node.Items.Count) node.Items.RemoveAt(index); return; }
                if (index == node.Items.Count) node.Items.Add(value); else node.Items[index] = value;
                return;
            }
            if (index == node.Items.Count)
                node.Items.Add(InspectorPanel.CreateDefaultNode(elementKind, elementType, default, null, nested));
            SetNested(node.Items[index], elementKind, elementType, default, null, nested, path, after, value, fullPath);
            return;
        }
        if (path[pos] != '.')
            throw new RpcError($"'{fullPath}': beklenmeyen karakter '{path[pos]}'");
        if (kind != SerializedType.Kind.Object || nested == null)
            throw new RpcError($"'{fullPath}': '{path[..pos]}' ic ice nesne degil");
        int end = path.IndexOfAny(new[] { '.', '[' }, pos + 1);
        string name = end < 0 ? path[(pos + 1)..] : path[(pos + 1)..end];
        var field = SerializedType.Find(nested, name)
            ?? throw new RpcError($"'{fullPath}': '{valueType?.Name}' tipinde alan yok: '{name}'");
        node.Fields ??= new List<KeyValuePair<string, DocNode>>();
        int slot = -1;
        for (int i = 0; i < node.Fields.Count; i++)
            if (node.Fields[i].Key == field.Name || (field.FormerName != null && node.Fields[i].Key == field.FormerName))
            { slot = i; break; }
        if (end < 0)
        {
            var leaf = value ?? InspectorPanel.CreateDefaultFieldNode(field);
            if (slot < 0) node.Fields.Add(new KeyValuePair<string, DocNode>(field.Name, leaf));
            else node.Fields[slot] = new KeyValuePair<string, DocNode>(field.Name, leaf);
            return;
        }
        if (slot < 0)
        {
            node.Fields.Add(new KeyValuePair<string, DocNode>(field.Name, InspectorPanel.CreateDefaultFieldNode(field)));
            slot = node.Fields.Count - 1;
        }
        SetNested(node.Fields[slot].Value, field.Kind, field.FieldType, field.ElementKind, field.ElementType, field.Nested,
            path, end, value, fullPath);
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

    [RpcCommand("select.set", "Editor secimini ayarlar (0 = temizle); paneller bu secime baglanir")]
    static DocNode SelectSet(RpcContext ctx, int id)
    {
        if (id != 0)
            RequireGo(ctx, id);
        Selection.DocId = id;
        return null;
    }

    [RpcCommand("select.asset", "Project panelinde asset secer (Assets'e goreli yol ya da guid; bos = temizle) — Inspector import ayarlari/onizlemeyi gosterir")]
    static DocNode SelectAsset(RpcContext ctx, string path = "")
    {
        if (string.IsNullOrEmpty(path))
        {
            Selection.AssetPath = null;
            return null;
        }
        string rel = App.Assets.ResolvePath(path);
        if (!App.Assets.Exists(rel))
            throw new RpcError("asset yok: " + path);
        Selection.AssetPath = System.IO.Path.Combine(App.Assets.Root, rel);
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

    [RpcCommand("sim.run", "N frame simule eder, canli dokumle doner. Play degilse baslatir; play'deyken doc DEGISTIYSE oyun sahnesi otomatik yeniden dogar (frame 0'dan — bayat goruntu olmaz; restart=true her zaman yeniden baslatir). screenshot=true sonda PNG; stop=true sonda durdurur")]
    static DocNode SimRun(RpcContext ctx, int frames = 60, bool stop = false, bool screenshot = false, bool restart = false)
    {
        if (frames < 1 || frames > 100000)
            throw new RpcError("frames 1..100000 olmali");
        bool restarted = false;
        if (PlayMode.State == PlayState.Editing)
            PlayMode.Play();
        else if (restart || PlayMode.PlaySceneStale)
        {
            PlayMode.DoHotReload();
            restarted = true;
        }
        int target = RpcHost.FrameCount + frames;
        RpcHost.Defer(() =>
        {
            if (RpcHost.FrameCount < target)
                return null; // bekle
            var dump = DumpLive(ctx, "play");
            if (restarted)
                dump.Add("restarted", DocNode.Scal("true"));
            if (screenshot)
                dump.Add("screenshot", RenderScreenshot());
            if (stop)
                PlayMode.Stop();
            return dump;
        });
        return null;
    }

    // --- goruntu + cihaz profili + pointer (AI'in gozu ve eli) ---
    // KOORDINAT SOZLESMESI: her sey MANTIKSAL nokta (Screen.width x height, sol-ust
    // orijin, y asagi). render.screenshot varsayilan olarak bu boyutta PNG yazar ->
    // goruntu pikseli == input.* koordinati birebir.

    [RpcCommand("view.get", "Game View boyutu/cihaz profili (mantiksal + fiziksel + pixelRatio)")]
    static DocNode ViewGet()
    {
        var r = DocNode.Map();
        r.Add("mode", DocNode.Scal(GameOutput.Forced ? "device" : "panel"));
        if (GameOutput.ForcedName.Length > 0)
            r.Add("device", DocNode.Scal(GameOutput.ForcedName));
        r.Add("width", DocNode.Scal(F(GameOutput.ViewW)));
        r.Add("height", DocNode.Scal(F(GameOutput.ViewH)));
        r.Add("physicalW", DocNode.Scal(GameOutput.PhysicalW.ToString(CultureInfo.InvariantCulture)));
        r.Add("physicalH", DocNode.Scal(GameOutput.PhysicalH.ToString(CultureInfo.InvariantCulture)));
        r.Add("pixelRatio", DocNode.Scal(F(GameOutput.PixelRatio)));
        return r;
    }

    [RpcCommand("view.set", "Game View'a FIZIKSEL cozunurluk + pixelRatio dayatir (mantiksal = w/pixelRatio); width=0 = panel takibine don")]
    static DocNode ViewSet(int width = 0, int height = 0, float pixelRatio = 1f)
    {
        if (width > 0 && (width > 8192 || height <= 0 || height > 8192))
            throw new RpcError("width/height 1..8192 olmali");
        if (pixelRatio <= 0f || pixelRatio > 8f)
            throw new RpcError("pixelRatio 0..8 araliginda olmali");
        GameOutput.SetDevice(width, height, pixelRatio);
        App.SaveSettings();
        return ViewGet();
    }

    [RpcCommand("view.device", "Cihaz preset'i uygular (name: view.devices listesinden). Yon PlayerSettings.orientation'dan (Auto = preset'in dogal yonu); landscape/portrait ile zorlanabilir")]
    static DocNode ViewDevice(string name, string orientation = "")
    {
        var dv = GameOutput.FindDevice(name) ?? throw new RpcError($"bilinmeyen cihaz: {name} (view.devices)");
        var o = orientation.Length > 0
            ? (Enum.TryParse<ScreenOrientation>(orientation, true, out var oe) ? oe : throw new RpcError("orientation landscape|portrait|auto olmali"))
            : App.Project?.Player.orientation ?? ScreenOrientation.Auto;
        bool portraitPreset = dv.H >= dv.W;
        bool landscape = o switch
        {
            ScreenOrientation.Landscape => true,
            ScreenOrientation.Portrait => false,
            _ => !portraitPreset,
        };
        bool swap = landscape == portraitPreset; // preset yonu istenen yonle uyusmuyorsa cevir
        GameOutput.SetDevice(swap ? dv.H : dv.W, swap ? dv.W : dv.H, dv.PixelRatio, dv.Name);
        App.SaveSettings();
        var r = ViewGet();
        r.Add("orientation", DocNode.Scal(landscape ? "landscape" : "portrait"));
        return r;
    }

    [RpcCommand("view.devices", "Kullanilabilir cihaz preset'leri")]
    static DocNode ViewDevices()
    {
        var seq = DocNode.Seq();
        foreach (var d in GameOutput.Devices)
        {
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(d.Name));
            m.Add("physicalW", DocNode.Scal(d.W.ToString(CultureInfo.InvariantCulture)));
            m.Add("physicalH", DocNode.Scal(d.H.ToString(CultureInfo.InvariantCulture)));
            m.Add("pixelRatio", DocNode.Scal(F(d.PixelRatio)));
            m.Add("width", DocNode.Scal(F(d.W / d.PixelRatio)));
            m.Add("height", DocNode.Scal(F(d.H / d.PixelRatio)));
            seq.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("devices", seq);
        return r;
    }

    static int _shotSeq;

    [RpcCommand("render.screenshot", "Game View'in son frame'ini PNG yazar. scale: mantiksal boyuta carpan (1 = Screen boyutu, tiklama koordinatiyla birebir; pixelRatio = tam fiziksel). path bos = Library/Screenshots/shot_N.png")]
    static DocNode RenderScreenshot(string path = "", float scale = 1f)
    {
        if (scale <= 0f || scale > 8f)
            throw new RpcError("scale 0..8 araliginda olmali");
        if (path.Length == 0)
            path = Path.Combine("Library", "Screenshots", $"shot_{++_shotSeq:D4}.png");
        string full = Path.IsPathRooted(path) ? path : Path.Combine(App.Project.Root, path);
        var (w, h) = GameOutput.Screenshot(full, scale);
        var r = DocNode.Map();
        r.Add("path", DocNode.Scal(Path.GetFullPath(full)));
        r.Add("w", DocNode.Scal(w.ToString(CultureInfo.InvariantCulture)));
        r.Add("h", DocNode.Scal(h.ToString(CultureInfo.InvariantCulture)));
        r.Add("scale", DocNode.Scal(F(scale)));
        r.Add("screenW", DocNode.Scal(F(GameOutput.ViewW)));
        r.Add("screenH", DocNode.Scal(F(GameOutput.ViewH)));
        r.Add("pixelRatio", DocNode.Scal(F(GameOutput.PixelRatio)));
        r.Add("playing", DocNode.Scal(PlayMode.PlayScene != null ? "true" : "false"));
        if (PlayMode.PlaySceneStale)
            r.Add("stale", DocNode.Scal("true")); // doc degisti, oyun sahnesi eski: sim.run ile tazele
        return r;
    }

    static Scene RequirePlayScene()
    {
        if (PlayMode.State == PlayState.Editing)
            PlayMode.Play();
        else if (PlayMode.PlaySceneStale)
            PlayMode.DoHotReload(); // input bayat sahneye gitmesin
        return PlayMode.PlayScene ?? throw new RpcError("oyun sahnesi yok (play baslatilamadi)");
    }

    [RpcCommand("input.pointer", "Oyun sahnesine ham pointer olayi (action: down|move|up; x,y mantiksal). Play degilse baslatir")]
    static DocNode InputPointer(string action, float x, float y)
    {
        var s = RequirePlayScene();
        switch (action)
        {
            case "down": s.Pointer.Down(x, y); break;
            case "move": s.Pointer.Move(x, y); break;
            case "up": s.Pointer.Up(x, y); break;
            default: throw new RpcError("action down|move|up olmali");
        }
        return null;
    }

    [RpcCommand("input.tap", "Tiklama: down, holdFrames frame sonra up; up'tan 1 frame sonra doner (handler'lar kostu)")]
    static DocNode InputTap(float x, float y, int holdFrames = 2)
    {
        var s = RequirePlayScene();
        s.Pointer.Down(x, y);
        int upAt = RpcHost.FrameCount + Math.Max(1, holdFrames);
        bool upped = false;
        RpcHost.Defer(() =>
        {
            if (!upped)
            {
                if (RpcHost.FrameCount < upAt)
                    return null;
                PlayMode.PlayScene?.Pointer.Up(x, y);
                upped = true;
                return null;
            }
            if (RpcHost.FrameCount <= upAt)
                return null;
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal("true"));
            return r;
        });
        return null;
    }

    [RpcCommand("input.drag", "Surukleme: (x0,y0) down, frames boyunca lineer move, (x1,y1) up; bitince doner")]
    static DocNode InputDrag(float x0, float y0, float x1, float y1, int frames = 10)
    {
        if (frames < 1 || frames > 10000)
            throw new RpcError("frames 1..10000 olmali");
        var s = RequirePlayScene();
        s.Pointer.Down(x0, y0);
        int start = RpcHost.FrameCount;
        bool upped = false;
        RpcHost.Defer(() =>
        {
            var ps = PlayMode.PlayScene;
            if (ps == null)
                throw new RpcError("oyun sahnesi drag sirasinda kapandi");
            int i = RpcHost.FrameCount - start;
            if (!upped)
            {
                if (i < frames)
                {
                    float t = (i + 1) / (float)frames;
                    ps.Pointer.Move(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
                    return null;
                }
                ps.Pointer.Up(x1, y1);
                upped = true;
                return null;
            }
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal("true"));
            return r;
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

    [RpcCommand("asset.list", "Projedeki asset'ler (yol/guid/tip/bayt; gorsellerde width/height + atlas uyeligi). filter: yolda gecen metin (buyuk-kucuk harf duyarsiz), type: Sprite|Font|Prefab|AudioClip|PixelEffect|... ya da uzanti (.png)")]
    static DocNode AssetList(RpcContext ctx, string filter = "", string type = "")
    {
        var seq = DocNode.Seq();
        var paths = new List<string>(ctx.Assets.AllAssets.Keys);
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in paths)
        {
            if (filter.Length > 0 && rel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            string typeName = AssetTypeName(rel);
            if (type.Length > 0 && !type.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                && !rel.EndsWith(type, StringComparison.OrdinalIgnoreCase))
                continue;
            seq.Items.Add(AssetEntry(ctx, rel, typeName));
        }
        var r = DocNode.Map();
        r.Add("count", DocNode.Scal(seq.Items.Count.ToString(CultureInfo.InvariantCulture)));
        r.Add("assets", seq);
        return r;
    }

    [RpcCommand("asset.info", "Tek asset'in bilgisi (yol ya da guid): tip/bayt/guid; gorselde width/height (dosya basligindan, yukleme gerekmez) + atlas bolgesi; import ayarlari (varsa)")]
    static DocNode AssetInfo(RpcContext ctx, string path)
    {
        string rel = ctx.Assets.ResolvePath(path).Replace('\\', '/');
        GuardAssetPath(rel);
        if (!ctx.Assets.AllAssets.ContainsKey(rel))
            throw new RpcError($"asset yok: {path} (asset.list ile ara)");
        var r = AssetEntry(ctx, rel, AssetTypeName(rel));
        var importer = ImportPipeline.ImporterFor(rel);
        if (importer?.SettingsType != null)
        {
            object settings = ImportPipeline.LoadSettings(rel, importer);
            if (settings != null)
                r.Add("importSettings", ObjectSerializer.ToNode(settings, ctx.Assets));
        }
        return r;
    }

    // Uzanti -> tip adi: importer kaydi (AssetType) oncelikli, yoksa AssetDatabase tohumu.
    static string AssetTypeName(string rel)
        => ImportPipeline.ImporterFor(rel)?.AssetType ?? AssetDatabase.ImportTypeOf(rel)?.Name ?? "";

    static DocNode AssetEntry(RpcContext ctx, string rel, string typeName)
    {
        var m = DocNode.Map();
        m.Add("path", DocNode.Scal(rel));
        m.Add("guid", DocNode.Scal(ctx.Assets.PathToGuid(rel) ?? ""));
        m.Add("type", DocNode.Scal(typeName));
        string full = ImportPipeline.FullPath(rel);
        if (File.Exists(full))
            m.Add("bytes", DocNode.Scal(new FileInfo(full).Length.ToString(CultureInfo.InvariantCulture)));
        // Gorsel boyutu: once atlas bolgesi (mantiksal OrigW/OrigH), yoksa dosya basligi.
        // Doku yuklemesi tetiklenmez (RPC sahneye yan etki yapmaz).
        if (ctx.Assets.TryGetRegion(rel, out var region))
        {
            m.Add("width", DocNode.Scal(region.OrigW.ToString(CultureInfo.InvariantCulture)));
            m.Add("height", DocNode.Scal(region.OrigH.ToString(CultureInfo.InvariantCulture)));
            m.Add("atlas", DocNode.Scal("true"));
            var trimmed = DocNode.Map();
            trimmed.Add("x", DocNode.Scal(region.OffX.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("y", DocNode.Scal(region.OffY.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("w", DocNode.Scal(region.W.ToString(CultureInfo.InvariantCulture)));
            trimmed.Add("h", DocNode.Scal(region.H.ToString(CultureInfo.InvariantCulture)));
            m.Add("trimmed", trimmed);
        }
        else if (AssetDatabase.ImportTypeOf(rel) == typeof(Sprite)
            && ImageHeader.TryReadSize(full, out int w, out int h))
        {
            m.Add("width", DocNode.Scal(w.ToString(CultureInfo.InvariantCulture)));
            m.Add("height", DocNode.Scal(h.ToString(CultureInfo.InvariantCulture)));
            m.Add("atlas", DocNode.Scal("false"));
        }
        return m;
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
        AtlasSystem.RefreshAll(); // bu kaynagi alan atlas gruplari (stamp) yeniden paketlenir
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

    [RpcCommand("project.open", "Baska bir projeyi acar: editor o yolla yeniden baslar, bu surec kapanir (RPC baglantisi kopar)")]
    static DocNode ProjectOpen(string path)
    {
        string full = Path.GetFullPath(path);
        if (!ProjectSwitcher.IsProjectRoot(full))
            throw new RpcError($"proje klasoru degil: {path}");
        ProjectSwitcher.Open(full);
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal("true"));
        r.Add("project", DocNode.Scal(full));
        return r;
    }

    [RpcCommand("project.recent", "Son acilan projeler (kullanici tercihleri) + repo Projects/* adaylari")]
    static DocNode ProjectRecent()
    {
        var prefs = EditorPrefs.Load();
        var r = DocNode.Map();
        r.Add("current", DocNode.Scal(App.Project?.Root ?? ""));
        var seq = DocNode.Seq();
        foreach (var p in prefs.recentProjects)
            seq.Items.Add(DocNode.Scal(p));
        r.Add("recent", seq);
        return r;
    }

    [RpcCommand("module.build", "Acik projeyi dinamik modul (.module.pak) olarak publish eder (Project > Build Module)")]
    static DocNode ModuleBuild()
    {
        bool ok = ModuleBuilder.Build();
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal(ok ? "true" : "false"));
        return r;
    }

    [RpcCommand("asset.buildPak", "Release asset paketini (Build/game.pak) uretir; sonuc log.tail'de")]
    static DocNode AssetBuildPak()
    {
        bool ok = AssetPackBuilder.Build();
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal(ok ? "true" : "false"));
        return r;
    }

    [RpcCommand("player.build", "game.pak + aotcompiler ile player uretir (target: windows=exe | android=Android Studio projesi | wasm=Build/wasm web cikti); sonuc build bitince doner")]
    static DocNode PlayerBuild(string target = "windows")
    {
        int v0 = PlayerBuilder.Version;
        if (!PlayerBuilder.Start(target))
        {
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal("false"));
            r.Add("error", DocNode.Scal("baslatilamadi; log.tail'e bakin"));
            return r;
        }
        RpcHost.Defer(() =>
        {
            if (PlayerBuilder.Version == v0)
                return null; // aotcompiler suruyor
            var r = DocNode.Map();
            r.Add("ok", DocNode.Scal(PlayerBuilder.LastOk ? "true" : "false"));
            r.Add("exe", DocNode.Scal(PlayerBuilder.LastExe));
            var tail = DocNode.Seq();
            var lines = PlayerBuilder.LastOutput.Split('\n');
            for (int i = Math.Max(0, lines.Length - 40); i < lines.Length; i++)
                if (lines[i].Length > 0)
                    tail.Items.Add(DocNode.Scal(lines[i]));
            r.Add("output", tail);
            return r;
        });
        return null;
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

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

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
