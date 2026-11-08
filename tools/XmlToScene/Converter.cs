using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace XmlToScene;

// Eski XML dugumunden uretilen GO tarifi (scene.apply girdisi + id gerektiren 2. faz isleri).
sealed class GoSpec
{
    public XNode Node = null!;
    public string Name = "";
    public bool Active = true;
    public float ScaleX = 1, ScaleY = 1, Rotation;
    public readonly Dictionary<string, object?> Box = new();     // LayoutBox props
    public readonly List<(string type, Dictionary<string, object?> props)> Extra = new(); // RenderGroup, modul vb.
    public readonly List<GoSpec> Children = new();
    public readonly List<IncludeSpec> Includes = new();           // sirali konum: Children arasina girer
    public readonly List<int> ChildOrder = new();                 // >=0: Children idx; <0: ~Includes idx
    public readonly List<XNode> Animations = new();
    public readonly List<(string field, string source)> Binds = new();
    public string? LocaleKey;                                     // Label LocaleText
    public int Id;                                                // faz 1 sonrasi
    public GoSpec? Parent;
    public bool IsPrefabRoot;
    public bool ScaledInLayout;                                   // H/VGroup icinde Scale != 1 (sarmalayici adayi)
}

sealed class IncludeSpec
{
    public XNode Node = null!;
    public string XmlPath = "";          // layout/ui/avatar.xml
    public string Name = "";
    public readonly Dictionary<string, object?> Overrides = new(); // LayoutBox prop farklari
    public float ScaleX = 1, ScaleY = 1, Rotation;
    public bool Active = true;
    public readonly List<XNode> Animations = new();
    public readonly List<(string field, string source)> Binds = new();
    public int Id;
    public GoSpec? ChildrenHost;        // include dugumunun normal cocuklari (nadir)
    public float? NaturalW, NaturalH;   // include attribute'lariyla birlesik kok boyutu (sarmalayici icin)
}

// Dugumu cevreleyen baglam: ust bir layout grubu mu, hangi modulun icindeyiz.
sealed class Ctx
{
    public bool InLayout;        // ust HGroup/VGroup
    public bool LayoutHorizontal;
    public bool StretchZero;
    public string? Module;       // en yakin modul tag'i (MEModule, BonusModule...)
    public Ctx Child(bool inLayout, bool horizontal, bool stretchZero, string? module)
        => new() { InLayout = inLayout, LayoutHorizontal = horizontal, StretchZero = stretchZero, Module = module ?? Module };
}

sealed class Report
{
    public readonly List<string> Lines = new();
    public void Add(XNode n, string msg) => Lines.Add($"- L{n.Line} <{n.Name}{(n["Name"] != null ? " " + n["Name"] : "")}>: {msg}");
}

sealed class Converter
{
    public readonly string SrcAssets;      // eski Assets koku (layout/... buradan)
    public readonly string ProjAssets;     // yeni Projects/<oyun>/Assets
    public readonly AssetIndex Assets;
    public readonly Rpc Rpc;
    public readonly HashSet<string> CompTypes;
    public Report Report = new();
    public readonly Dictionary<string, string> PrefabPaths = new(StringComparer.OrdinalIgnoreCase); // xml yolu -> Assets/Prefabs/...prefab
    public bool ForcePrefabs;
    // Eski Stage2D tasarim kutusu (Scene2D koku bu boyutta, ekrana CONTAIN sigdirilir). Kok GO bu kutudur;
    // yalniz FullScreen/SafeScreen gruplar gercek ekran kenarina uzanir (fitScreen).
    public float DesignW = 1440, DesignH = 960;
    public bool EnsureCamera = true;   // sahnede kamera yoksa FitPerspectiveProjection(design, fov 20) ekle
    const float StageFov = 20;          // eski Stage2D.FOV
    int _aliasN;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // Modul tag'i -> binding kaynaklarinda atilacak onek (modul = model; "ME.name" -> "name")
    static readonly Dictionary<string, string> ModelPrefix = new() { ["MEModule"] = "ME." };

    public Converter(string srcAssets, string projAssets, AssetIndex assets, Rpc rpc, HashSet<string> compTypes)
    { SrcAssets = srcAssets; ProjAssets = projAssets; Assets = assets; Rpc = rpc; CompTypes = compTypes; }

    // ---------------------------------------------------------------- yukleme
    public XNode Load(string xmlRel)
    {
        string full = Path.Combine(SrcAssets, xmlRel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) throw new FileNotFoundException("xml yok: " + full);
        return LenientXml.Parse(File.ReadAllText(full), xmlRel);
    }

    static string? IncludePath(XNode n)
    {
        if (n.Name.StartsWith("T:", StringComparison.Ordinal))
            return "layout/" + n.Name[2..].ToLowerInvariant().Replace(':', '/') + ".xml";
        if (n.Name == "include") return n["path"];
        return null;
    }

    // Bagimlilik-once prefab uretimi: bu xml'in (ve altindakilerin) include ettigi her xml bir prefab.
    public void EnsurePrefabs(XNode root, HashSet<string> visiting)
    {
        foreach (var inc in EnumerateIncludes(root))
        {
            if (PrefabPaths.ContainsKey(inc)) continue;
            if (!visiting.Add(inc)) throw new Exception("include dongusu: " + inc);
            var incRoot = Load(inc);
            EnsurePrefabs(incRoot, visiting);
            PrefabPaths[inc] = BuildPrefab(inc, incRoot);
            visiting.Remove(inc);
        }
    }

    static IEnumerable<string> EnumerateIncludes(XNode n)
    {
        var p = IncludePath(n);
        if (p != null) yield return p;
        foreach (var c in n.Children)
            foreach (var x in EnumerateIncludes(c)) yield return x;
    }

    // ---------------------------------------------------------------- prefab
    string BuildPrefab(string xmlRel, XNode root)
    {
        string prefabRel = "Prefabs/" + Regex.Replace(xmlRel, "^layout/", "") ;
        prefabRel = Regex.Replace(prefabRel, @"\.xml$", "", RegexOptions.IgnoreCase) + ".prefab";
        string full = Path.Combine(ProjAssets, prefabRel.Replace('/', Path.DirectorySeparatorChar));
        bool existed = File.Exists(full);
        if (existed && !ForcePrefabs)
        {
            Console.WriteLine($"  prefab var, korunuyor: {prefabRel} (--force-prefabs ile yeniden uret)");
            return prefabRel;
        }
        Console.WriteLine($"  prefab: {xmlRel} -> {prefabRel}{(existed ? " (yeniden)" : "")}");
        var savedReport = Report;
        Report = new Report();
        var spec = BuildGo(root, new Ctx(), null);
        spec.IsPrefabRoot = true;
        spec.Name = root["Name"] ?? Path.GetFileNameWithoutExtension(xmlRel);
        int id = Apply(spec, 0);
        if (!Rpc.Dry)
        {
            if (existed)
            {
                // .meta korunur → guid sabit kalir (referanslar kopmaz); sadece govde yenilenir.
                File.Delete(full);
            }
            var r = Rpc.Call("prefab.create", new JsonObject { ["id"] = id, ["path"] = prefabRel });
            Console.WriteLine($"    guid {r["guid"]}");
            Rpc.Call("scene.removeGo", new JsonObject { ["id"] = id });
        }
        WriteReport(xmlRel, Report);
        Report = savedReport;
        return prefabRel;
    }

    public void WriteReport(string xmlRel, Report rep)
    {
        string outPath = Path.Combine(ProjAssets, "Scenes", "port-reports", Regex.Replace(xmlRel, @"[/\\]", "_") + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var sb = new StringBuilder();
        sb.AppendLine($"# Port report: {xmlRel}");
        sb.AppendLine($"Generated by xml2scene at {DateTime.Now:yyyy-MM-dd HH:mm}. Regenerated on every run; do not edit.");
        sb.AppendLine();
        if (rep.Lines.Count == 0) sb.AppendLine("No open items.");
        foreach (var l in rep.Lines.Distinct()) sb.AppendLine(l);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"    rapor: {Path.GetRelativePath(ProjAssets, outPath)} ({rep.Lines.Distinct().Count()} madde)");
    }

    // ---------------------------------------------------------------- sahne
    public void ConvertScene(string xmlRel, int parent)
    {
        var root = Load(xmlRel);
        EnsurePrefabs(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { xmlRel });
        string rootName = "xml:" + xmlRel;
        // Ayni xml'in onceki kopyasini kaldir (yeniden donusum = degistir); kamera var mi bak.
        bool hasCamera = false;
        if (!Rpc.Dry)
        {
            var tree = Rpc.Call("scene.tree", new JsonObject { ["root"] = parent == 0 ? "" : parent.ToString(), ["depth"] = parent == 0 ? 1 : 2, ["comps"] = true });
            var lines = tree["lines"] is JsonArray la ? la.Select(x => x!.ToString()) : Regex.Unescape(tree.ToString()).Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("CameraComponent")) hasCamera = true;
                var m = Regex.Match(line, @"^\s*(\d+)\s+(.*)$");
                if (!m.Success) continue;
                string rest = m.Groups[2].Value.Trim();
                if (rest == rootName || rest.StartsWith(rootName + " ", StringComparison.Ordinal))
                {
                    Console.WriteLine($"  eski kopya siliniyor: id {m.Groups[1].Value}");
                    Rpc.Call("scene.removeGo", new JsonObject { ["id"] = int.Parse(m.Groups[1].Value) });
                }
            }
        }
        if (EnsureCamera && !hasCamera && parent == 0)
        {
            // Eski Stage2D: FOV 20 perspektif, tasarim kutusu ekrana contain. Kamera (0,0) = tasarim merkezi.
            var cam = new JsonObject
            {
                ["name"] = "Camera",
                ["components"] = new JsonArray(
                    new JsonObject { ["type"] = "CameraComponent", ["props"] = new JsonObject() },
                    new JsonObject { ["type"] = "FitPerspectiveProjection", ["props"] = new JsonObject { ["fov"] = StageFov, ["referenceResolution"] = $"{V(DesignW)} {V(DesignH)}" } }),
            };
            Rpc.Call("scene.apply", new JsonObject { ["objects"] = new JsonArray(cam) });
            Console.WriteLine($"  kamera eklendi: FitPerspectiveProjection {V(DesignW)}x{V(DesignH)} fov {V(StageFov)}");
        }
        var spec = BuildGo(root, new Ctx(), null);
        spec.Name = rootName;
        // Kok = tasarim kutusu: dunya orijininde ortalanmis, DesignW x DesignH (eski Scene2D koku; FullScreen degilse).
        if (!spec.Box.ContainsKey("fitScreen"))
        {
            spec.Box["anchorMin"] = "0.5 0.5"; spec.Box["anchorMax"] = "0.5 0.5"; spec.Box["pivot"] = "0.5 0.5";
            spec.Box.Remove("anchoredPos");
            if (!spec.Box.ContainsKey("width")) spec.Box["width"] = DesignW;
            if (!spec.Box.ContainsKey("height")) spec.Box["height"] = DesignH;
        }
        int id = Apply(spec, parent);
        Console.WriteLine($"  sahne koku: {rootName} id {id}");
        WriteReport(xmlRel, Report);
    }

    // ---------------------------------------------------------------- dugum -> spec
    GoSpec BuildGo(XNode n, Ctx ctx, GoSpec? parent)
    {
        var g = new GoSpec { Node = n, Parent = parent };
        string tag = n.Name;
        g.Name = n["Name"] ?? DefaultName(n);
        var box = g.Box;

        // --- modul / bilinen component
        string? module = null;
        if (IsModuleTag(tag))
        {
            module = tag;
            if (CompTypes.Contains(tag)) g.Extra.Add((tag, new()));
            else Report.Add(n, $"component type '{tag}' not in project yet (attrs: {AttrSummary(n)}) — GO+LayoutBox created");
        }
        else if (!KnownTags.Contains(tag))
            Report.Add(n, $"unknown tag — mapped to plain LayoutBox (attrs: {AttrSummary(n)})");

        // --- gorunurluk
        if (n["Hidden"] == "true" || n["Visible"] == "false") g.Active = false;

        // --- transform
        ParseTransform(n, out g.ScaleX, out g.ScaleY, out g.Rotation);

        // --- boyut
        float? w = F(n["Width"]), h = F(n["Height"]);
        string stretch = n["Stretch"] ?? "None";
        bool stretchH = stretch is "Horizontal" or "Both", stretchV = stretch is "Vertical" or "Both";

        // --- gorsel
        AssetIndex.Tex? tex = null;
        if (tag is "Image" or "Image9" or "MaskSprite")
        {
            string? src = n["Src"];
            if (src == null && !n.Attrs.Any(a => a.Key == "bind:Src"))
                Report.Add(n, "no Src");
            if (src != null)
            {
                tex = Assets.Resolve(src, out var amb);
                if (tex == null) Report.Add(n, $"texture not found: '{src}'");
                else
                {
                    if (amb != null) Report.Add(n, $"ambiguous texture '{src}' → used {tex.RelPath} (candidates: {amb})");
                    box["sprite"] = tex.RelPath;
                    box["spriteFit"] = "Stretch";
                    if (tag == "Image9")
                    {
                        int[]? s9 = tex.Slice9;
                        var grid = n["Scale9Grid"];
                        if (grid != null)
                        {
                            var p = grid.Split(',').Select(x => int.Parse(x.Trim(), Inv)).ToArray();
                            if (p.Length == 4) s9 = new[] { p[0], p[1], tex.Width - p[0] - p[2], tex.Height - p[1] - p[3] };
                        }
                        if (s9 != null) box["slice9"] = $"{s9[0]} {s9[1]} {s9[2]} {s9[3]}";
                        else Report.Add(n, $"Image9 without slice data ({tex.RelPath}) — drawn stretched");
                    }
                    // Eski Image dogal boyutu = texture; yalniz verilen eksen ezilir.
                    if (w == null && !stretchH) w = tex.Width;
                    if (h == null && !stretchV) h = tex.Height;
                }
            }
            if (tag == "MaskSprite") Report.Add(n, "MaskSprite: masking not ported (plain image)");
        if (tag == "Html") Report.Add(n, $"Html content not ported: \"{Regex.Replace(n.Text.Trim(), @"\s+", " ")}\" + markup children");
        }
        if (tag == "Fill")
        {
            box["fill.color"] = Color(n["Color"] ?? "#fff");
        }
        else if (n["Color"] != null && tag != "Label")
        {
            box["fill.color"] = Color(n["Color"]!);   // sprite tint
        }

        // --- Label
        if (tag == "Label" || tag == "NumberLabel")
            BuildLabel(n, g);

        // --- grup/layout
        bool isLayout = tag is "HGroup" or "VGroup";
        if (isLayout)
        {
            box["layout"] = tag == "HGroup" ? "Horizontal" : "Vertical";
            if (F(n["Spacing"]) is float sp) box["spacing"] = sp;
            if (n["Reverse"] == "true") box["reverse"] = true;
            string? align = n["Align"];
            if (align is "Center") box["alignChildren"] = "Center";
            else if (align is "Bottom" or "Right") box["alignChildren"] = "End";
            string? justify = tag == "HGroup" ? n["HAlign"] : n["VAlign"];
            if (justify is "Center") box["justifyChildren"] = "Center";
            else if (justify is "Right" or "Bottom") box["justifyChildren"] = "End";
            if (n["HasBackground"] == "true") Report.Add(n, "HasBackground not ported");
        }
        else if (n["Align"] != null || n["HAlign"] != null || n["VAlign"] != null)
            Report.Add(n, $"Align/HAlign/VAlign on non-layout group ignored");
        if (n["Padding"] is string pad)
        {
            var p = pad.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, Inv)).ToArray();
            float t = p[0], r = p.Length > 1 ? p[1] : t, b = p.Length > 2 ? p[2] : t, l = p.Length > 3 ? p[3] : t;
            box["padding"] = $"{V(l)} {V(t)} {V(r)} {V(b)}";
        }

        // --- yerlesim
        if (n["FullScreen"] == "true" || n["SafeScreen"] == "true")
        {
            box["fitScreen"] = true;
            if (n["SafeScreen"] == "true") Report.Add(n, "SafeScreen → fitScreen (safe-area insets not applied)");
        }
        else if (ctx.InLayout)
        {
            // Layout cocugu: Snap ezilir; Margin yer kaplamayan kaydirma (l,t,r,b = mx,my,-mx,-my).
            var (mx, my) = Margin(n);
            if (mx != 0 || my != 0) box["margin"] = $"{V(mx)} {V(my)} {V(-mx)} {V(-my)}";
            if (ctx.StretchZero && ((ctx.LayoutHorizontal && w == 0) || (!ctx.LayoutHorizontal && h == 0)))
                box["grow"] = 1f;
            if (stretch != "None")
            {
                // Cross eksende stretch: parent boyutu; layout ekseninde eski motor da parent'a esitlerdi.
                if (stretchH && !ctx.LayoutHorizontal) box["alignSelf"] = "Stretch";
                else if (stretchV && ctx.LayoutHorizontal) box["alignSelf"] = "Stretch";
                else Report.Add(n, $"Stretch={stretch} along layout axis — check");
            }
            if (g.ScaleX != 1 || g.ScaleY != 1)
                g.ScaledInLayout = true; // ebeveyn karar verir: boyut biliniyorsa sarmalayici, degilse rapor
        }
        else
        {
            var (mx, my) = Margin(n);
            string? snap = n["Snap"];
            float px = F(n["PivotX"]) ?? 0.5f, py = F(n["PivotY"]) ?? 0.5f;
            if (snap != null)
            {
                var (ax, ay) = SnapPoint(snap, n);
                box["anchorMin"] = $"{V(stretchH ? 0 : ax)} {V(stretchV ? 0 : ay)}";
                box["anchorMax"] = $"{V(stretchH ? 1 : ax)} {V(stretchV ? 1 : ay)}";
                box["pivot"] = $"{V(ax)} {V(ay)}";
                if (mx != 0 || my != 0) box["anchoredPos"] = $"{V(mx)} {V(my)}";
                if (n["PivotX"] != null || n["PivotY"] != null) Report.Add(n, "Snap + PivotX/Y combined — pivot taken from Snap, check");
            }
            else
            {
                float x = F(n["X"]) ?? 0, y = F(n["Y"]) ?? 0;
                box["anchorMin"] = "0 0";
                box["anchorMax"] = $"{V(stretchH ? 1 : 0)} {V(stretchV ? 1 : 0)}";
                box["pivot"] = $"{V(px)} {V(py)}";
                if (x != 0 || y != 0 || mx != 0 || my != 0) box["anchoredPos"] = $"{V(x + mx)} {V(y + my)}";
                if (stretch != "None") Report.Add(n, "Stretch without Snap — anchored at 0,0; check");
            }
            if (stretch != "None")
            {
                var (dx, dy) = Pair(n["StretchDelta"]);
                if (stretchH) { box["width"] = -dx; w = null; }
                if (stretchV) { box["height"] = -dy; h = null; }
            }
        }
        if (w != null) box["width"] = w;
        if (h != null) box["height"] = h;
        if (F(n["MinWidth"]) is float mnw) box["minWidth"] = mnw;
        if (F(n["MinHeight"]) is float mnh) box["minHeight"] = mnh;
        if (F(n["MaxWidth"]) is float mxw) box["maxWidth"] = mxw;
        if (F(n["MaxHeight"]) is float mxh) box["maxHeight"] = mxh;
        if (n["MaxWidthPercent"] != null) Report.Add(n, "MaxWidthPercent not ported");

        // --- siralama / etkilesim
        if (F(n["BatchOrder"]) is float bo) box["SortingOrder"] = (int)bo;
        else if (F(n["Depth"]) is float dp) { box["SortingOrder"] = (int)dp; Report.Add(n, $"Depth={dp} mapped to SortingOrder (sibling depth semantics differ)"); }
        if (n["Interactive"] == "false") box["BlocksRaycast"] = false;

        // --- alfa
        float? alpha = F(n["Alpha"]);
        bool needsGroup = alpha != null || n.Children.Any(c => c.Name == "Animation" && c.Children.Any(t => t.Name is "Alpha" or "AnimAlpha"));
        if (needsGroup) g.Extra.Add(("RenderGroup", new() { ["Alpha"] = alpha ?? 1f }));

        // --- bind:* ve action:*
        foreach (var a in n.Attrs)
        {
            if (a.Key.StartsWith("action:")) { Report.Add(n, $"{a.Key}=\"{a.Value}\" — action not ported"); continue; }
            if (a.Key.StartsWith(":") || a.Key.StartsWith("bindOnce:")) { Report.Add(n, $"{a.Key}=\"{a.Value}\" — nested/once binding not ported"); continue; }
            if (a.Key == "bind") { Report.Add(n, $"bind=\"{a.Value}\" (named ref for module code) — expose via component field"); continue; }
            if (a.Key.StartsWith("bind:"))
            {
                string field = a.Key[5..];
                string src = a.Value.Trim();
                string? prefix = ctx.Module != null && ModelPrefix.TryGetValue(ctx.Module, out var pf) ? pf : null;
                if (prefix != null && src.StartsWith(prefix, StringComparison.Ordinal)) src = src[prefix.Length..];
                if (Regex.IsMatch(src, @"^[A-Za-z_][\w\.]*$") && BindField(field) is string bf)
                    g.Binds.Add((bf, src));
                else
                    Report.Add(n, $"bind:{field}=\"{a.Value}\" — expression/field not auto-bound");
            }
        }
        if (n["Xml"] != null) Report.Add(n, $"Xml=\"{n["Xml"]}\" (module template) — handled by module code");

        // --- bilinmeyen attribute'lar
        foreach (var a in n.Attrs)
            if (!HandledAttr(a.Key, tag)) Report.Add(n, $"attribute {a.Key}=\"{a.Value}\" not mapped");

        // --- cocuklar
        var childCtx = ctx.Child(isLayout, tag == "HGroup", n["StretchZero"] == "true", module);
        foreach (var c in n.Children)
        {
            switch (c.Name)
            {
                case "Animation": g.Animations.Add(c); break;
                case "Button": Report.Add(c, $"Button ({AttrSummary(c)}) — click handling not ported"); break;
                case "Sound": Report.Add(c, "Sound not ported"); break;
                case "Repeat": Report.Add(c, "Repeat not ported"); break;
                default:
                    if (IncludePath(c) is string ip)
                    {
                        var inc = BuildInclude(c, ip, childCtx, g);
                        if (childCtx.InLayout && WrapScaledInclude(inc, g) is GoSpec hostW)
                        {
                            g.Children.Add(hostW);
                            g.ChildOrder.Add(g.Children.Count - 1);
                        }
                        else
                        {
                            g.Includes.Add(inc);
                            g.ChildOrder.Add(~(g.Includes.Count - 1));
                        }
                    }
                    else
                    {
                        g.Children.Add(WrapScaled(BuildGo(c, childCtx, g)));
                        g.ChildOrder.Add(g.Children.Count - 1);
                    }
                    break;
            }
        }
        return g;
    }

    // Eski H/VGroup cocugun yer kaplayan boyutu = dogal boyut x |scale|; yeni LayoutBox transform scale'i olcmez.
    // Cozum: olcekli dugum, olceklenmis boyutta bir sarmalayici kutuya ortalanir (negatif scale icin de guvenli);
    // layout tarafi anahtarlar (margin/grow/alignSelf) sarmalayiciya gecer. Boyut bilinmiyorsa yalniz rapor.
    static readonly string[] LayoutSideKeys = { "margin", "grow", "alignSelf" };

    GoSpec WrapScaled(GoSpec inner)
    {
        if (!inner.ScaledInLayout) return inner;
        if (inner.Box.TryGetValue("grow", out _) || inner.Box.TryGetValue("alignSelf", out _)
            || inner.Box.GetValueOrDefault("width") is not float w || inner.Box.GetValueOrDefault("height") is not float h)
        {
            Report.Add(inner.Node, "scaled child inside H/VGroup with unknown/stretched size: old layout used scaled size — check spacing");
            return inner;
        }
        var wrap = new GoSpec { Node = inner.Node, Parent = inner.Parent, Name = inner.Name + "#fit" };
        wrap.Box["width"] = w * Math.Abs(inner.ScaleX);
        wrap.Box["height"] = h * Math.Abs(inner.ScaleY);
        foreach (var k in LayoutSideKeys)
            if (inner.Box.Remove(k, out var v)) wrap.Box[k] = v;
        inner.Box["anchorMin"] = "0.5 0.5"; inner.Box["anchorMax"] = "0.5 0.5"; inner.Box["pivot"] = "0.5 0.5";
        inner.Box.Remove("anchoredPos");
        inner.Parent = wrap;
        wrap.Children.Add(inner);
        wrap.ChildOrder.Add(0);
        return wrap;
    }

    GoSpec? WrapScaledInclude(IncludeSpec inc, GoSpec parent)
    {
        if (inc.ScaleX == 1 && inc.ScaleY == 1) return null;
        if (inc.NaturalW is not float w || inc.NaturalH is not float h
            || inc.Overrides.GetValueOrDefault("grow") != null || inc.Overrides.GetValueOrDefault("alignSelf") != null)
        {
            Report.Add(inc.Node, "scaled include inside H/VGroup with unknown/stretched size: old layout used scaled size — check spacing");
            return null;
        }
        var wrap = new GoSpec { Node = inc.Node, Parent = parent, Name = inc.Name + "#fit" };
        wrap.Box["width"] = w * Math.Abs(inc.ScaleX);
        wrap.Box["height"] = h * Math.Abs(inc.ScaleY);
        foreach (var k in LayoutSideKeys)
            if (inc.Overrides.Remove(k, out var v)) { if (v != null) wrap.Box[k] = v; }
        inc.Overrides["anchorMin"] = "0.5 0.5"; inc.Overrides["anchorMax"] = "0.5 0.5"; inc.Overrides["pivot"] = "0.5 0.5";
        inc.Overrides["anchoredPos"] = null;
        wrap.Includes.Add(inc);
        wrap.ChildOrder.Add(~0);
        return wrap;
    }

    IncludeSpec BuildInclude(XNode c, string xmlPath, Ctx ctx, GoSpec parent)
    {
        var inc = new IncludeSpec { Node = c, XmlPath = xmlPath };
        var tplRoot = Load(xmlPath);
        inc.Name = c["Name"] ?? tplRoot["Name"] ?? Path.GetFileNameWithoutExtension(xmlPath);
        // Sablon koku tek basina (A) ve include attribute'lariyla birlesik (B): fark = override.
        var merged = new XNode { Name = tplRoot.Name, Line = c.Line };
        foreach (var a in tplRoot.Attrs) merged.Attrs.Add(a);
        foreach (var a in c.Attrs) if (a.Key != "path") merged.Set(a.Key, a.Value);
        var savedRep = Report; Report = new Report();
        var a0 = BuildGo(StripChildren(tplRoot), new Ctx(), null);
        Report = new Report();
        var b0 = BuildGo(StripChildren(merged), ctx, parent);
        var incRep = Report; Report = savedRep;
        foreach (var l in incRep.Lines) if (l.Contains("action:") || l.Contains("bind")) Report.Lines.Add(l);
        foreach (var kv in b0.Box)
            if (!a0.Box.TryGetValue(kv.Key, out var old) || !Equals(Norm(old), Norm(kv.Value))) inc.Overrides[kv.Key] = kv.Value;
        foreach (var kv in a0.Box)
            if (!b0.Box.ContainsKey(kv.Key)) inc.Overrides[kv.Key] = null; // default'a don
        inc.ScaleX = b0.ScaleX; inc.ScaleY = b0.ScaleY; inc.Rotation = b0.Rotation;
        inc.NaturalW = b0.Box.GetValueOrDefault("width") as float?; inc.NaturalH = b0.Box.GetValueOrDefault("height") as float?;
        inc.Active = b0.Active;
        inc.Binds.AddRange(b0.Binds);
        foreach (var ch in c.Children)
        {
            if (ch.Name == "Animation") inc.Animations.Add(ch);
            else Report.Add(ch, $"child <{ch.Name}> under include/template node not ported (instance children)");
        }
        // Sablon kokundeki Animation'lar prefab'in icinde; include'daki ek Animation'lar instance'a eklenir.
        return inc;
    }

    static XNode StripChildren(XNode n)
    {
        var x = new XNode { Name = n.Name, Line = n.Line };
        foreach (var a in n.Attrs) x.Attrs.Add(a);
        return x;
    }

    static object? Norm(object? v) => v is float f ? (object)Math.Round(f, 4) : v is int i ? (object)(float)i : v;

    void BuildLabel(XNode n, GoSpec g)
    {
        var box = g.Box;
        if (n["Text"] is string t) box["text"] = t;
        if (n["LocaleText"] is string lt)
        {
            var m = Regex.Match(lt, @"^\{locale:([^}]+)\}$");
            if (m.Success) { g.LocaleKey = m.Groups[1].Value; box["text"] = m.Groups[1].Value; }
            else Report.Add(n, $"LocaleText=\"{lt}\" not a plain {{locale:key}}");
        }
        if (n["Face"] is string face) box["font"] = face;
        if (F(n["Size"]) is float sz) box["textSize"] = sz;
        if (n["HAlign"] is string ha) box["textAlign"] = ha switch { "0" or "Left" => "Left", "1" or "Center" => "Center", "2" or "Right" => "Right", _ => "Left" };
        if (n["VAlign"] is string va) box["textAlignV"] = va switch { "0" or "Top" => "Start", "1" or "Center" => "Center", "2" or "Bottom" => "End", _ => "Start" };
        if (n["Multiline"] == "true") box["textWrap"] = true;
        if (n["Ellipsis"] != null) box["textEllipsis"] = true;
        if (F(n["Kerning"]) is float k) box["textSpacing"] = k;
        if (n["Color"] is string col) box["textFill.color"] = Color(col);
        if (n["Shadow"] is string sh)
        {
            // "#AARRGGBB dx dy blur"
            var p = sh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            box["textShadowColor"] = Color(p[0]);
            if (p.Length > 2) box["textShadowOffset"] = $"{p[1]} {p[2]}";
            if (p.Length > 3) box["textShadowBlur"] = float.Parse(p[3], Inv);
        }
        if (n["LineHeight"] != null) Report.Add(n, "LineHeight not mapped");
        if (n.Name == "NumberLabel") Report.Add(n, "NumberLabel: number formatting/animation not ported");
    }

    // ---------------------------------------------------------------- uygulama (RPC)
    // Faz 1: scene.apply ile iskelet (LayoutBox/RenderGroup/modul). Faz 2: id gerektirenler
    // (prefab instance, MovieClip, Binding) tek edit.batch. Faz 3: loc.bind (batch disi).
    int Apply(GoSpec root, int parent)
    {
        var obj = ToApply(root);
        if (parent != 0) obj["parent"] = parent;
        var res = Rpc.Call("scene.apply", new JsonObject { ["objects"] = new JsonArray(obj) });
        if (!Rpc.Dry) AssignIds(root, res[0]!);

        var ops = new JsonArray();
        var locBinds = new List<(int id, string key)>();
        int aliasN = 0;
        Phase2(root, ops, locBinds, ref aliasN);
        if (ops.Count > 0)
        {
            try { Rpc.Call("edit.batch", new JsonObject { ["ops"] = ops }); }
            catch (RpcException)
            {
                if (!Rpc.Dry) Rpc.Call("scene.removeGo", new JsonObject { ["id"] = root.Id });
                throw;
            }
        }
        foreach (var (id, key) in locBinds)
            Rpc.Call("loc.bind", new JsonObject { ["go"] = id, ["comp"] = 0, ["field"] = "text", ["key"] = key });
        return root.Id;
    }

    JsonObject ToApply(GoSpec g)
    {
        var o = new JsonObject { ["name"] = g.Name };
        if (!g.Active) o["active"] = false;
        if (g.ScaleX != 1 || g.ScaleY != 1) o["scale"] = $"{V(g.ScaleX)} {V(g.ScaleY)} 1";
        if (g.Rotation != 0) o["rot"] = $"0 0 {V(g.Rotation)}";
        var comps = new JsonArray();
        comps.Add(Comp("LayoutBox", g.Box));
        foreach (var (type, props) in g.Extra) comps.Add(Comp(type, props));
        o["components"] = comps;
        if (g.Children.Count > 0)
        {
            var ch = new JsonArray();
            foreach (var c in g.Children) ch.Add(ToApply(c));
            o["children"] = ch;
        }
        return o;
    }

    static JsonObject Comp(string type, Dictionary<string, object?> props)
    {
        var p = new JsonObject();
        foreach (var kv in props) p[kv.Key] = ToJson(kv.Value);
        return new JsonObject { ["type"] = type, ["props"] = p };
    }

    static JsonNode? ToJson(object? v) => v switch
    {
        null => null,
        float f => JsonValue.Create(Math.Round(f, 4)),
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        _ => JsonValue.Create(v.ToString()),
    };

    static void AssignIds(GoSpec g, JsonNode res)
    {
        g.Id = int.Parse(res["id"]!.ToString());
        var ch = res["children"] as JsonArray;
        for (int i = 0; i < g.Children.Count; i++) AssignIds(g.Children[i], ch![i]!);
    }

    void Phase2(GoSpec g, JsonArray ops, List<(int, string)> loc, ref int aliasN)
    {
        string self = g.Id.ToString();
        if (g.LocaleKey != null) loc.Add((g.Id, g.LocaleKey));
        EmitAnimations(g.Node, g.Animations, self, g.ScaleX, g.ScaleY, g.Box, ops, 0);
        EmitBinds(g.Binds, self, ops);

        // include'lar: instantiate + siraya sok + override
        for (int oi = 0; oi < g.ChildOrder.Count; oi++)
        {
            int k = g.ChildOrder[oi];
            if (k >= 0) continue;
            var inc = g.Includes[~k];
            if (!PrefabPaths.TryGetValue(inc.XmlPath, out var prefab)) { Report.Add(inc.Node, $"prefab missing for {inc.XmlPath}"); continue; }
            string alias = "inc" + (aliasN++);
            ops.Add(Op("prefab.instantiate", new JsonObject { ["prefab"] = prefab, ["parent"] = g.Id }, alias));
            // onune gelecegi kardes: siradaki normal cocuk (include'lar instantiate sirasiyla zaten arkaya eklenir)
            int before = 0;
            for (int j = oi + 1; j < g.ChildOrder.Count; j++)
                if (g.ChildOrder[j] >= 0) { before = g.Children[g.ChildOrder[j]].Id; break; }
            if (before != 0)
                ops.Add(Op("scene.reparent", new JsonObject { ["id"] = "$" + alias, ["parent"] = g.Id, ["insertBefore"] = before }));
            if (inc.Name != "")
                ops.Add(Op("scene.rename", new JsonObject { ["id"] = "$" + alias, ["name"] = inc.Name }));
            if (!inc.Active)
                ops.Add(Op("go.setActive", new JsonObject { ["id"] = "$" + alias, ["active"] = false }));
            if (inc.ScaleX != 1 || inc.ScaleY != 1 || inc.Rotation != 0)
                ops.Add(Op("transform.set", new JsonObject { ["go"] = "$" + alias, ["scale"] = $"{V(inc.ScaleX)} {V(inc.ScaleY)} 1", ["rot"] = $"0 0 {V(inc.Rotation)}" }));
            foreach (var kv in inc.Overrides)
                ops.Add(Op("prop.set", new JsonObject { ["go"] = "$" + alias, ["comp"] = 0, ["prop"] = kv.Key, ["value"] = ToJson(kv.Value) }));
            EmitAnimations(inc.Node, inc.Animations, "$" + alias, inc.ScaleX, inc.ScaleY, inc.Overrides, ops, CountTemplateAnims(inc.XmlPath));
            EmitBinds(inc.Binds, "$" + alias, ops);
        }
        foreach (var c in g.Children) Phase2(c, ops, loc, ref aliasN);
    }

    int CountTemplateAnims(string xmlPath) => Load(xmlPath).Children.Count(c => c.Name == "Animation");

    static JsonObject Op(string cmd, JsonObject args, string? alias = null)
    {
        var o = new JsonObject { ["cmd"] = cmd, ["args"] = args };
        if (alias != null) o["as"] = alias;
        return o;
    }

    void EmitBinds(List<(string field, string source)> binds, string go, JsonArray ops)
    {
        if (binds.Count == 0) return;
        string bAlias = "bind" + (_aliasN++);
        ops.Add(Op("comp.add", new JsonObject { ["go"] = go, ["type"] = "Binding" }, bAlias));
        for (int i = 0; i < binds.Count; i++)
        {
            ops.Add(Op("prop.set", new JsonObject { ["go"] = go, ["comp"] = "$" + bAlias, ["prop"] = $"Binds[{i}].Source", ["value"] = binds[i].source }));
            ops.Add(Op("prop.set", new JsonObject { ["go"] = go, ["comp"] = "$" + bAlias, ["prop"] = $"Binds[{i}].Target", ["value"] = go + ":LayoutBox:0" }));
            ops.Add(Op("prop.set", new JsonObject { ["go"] = go, ["comp"] = "$" + bAlias, ["prop"] = $"Binds[{i}].Path", ["value"] = binds[i].field }));
        }
    }

    // <Animation> → MovieClip (biri per Animation; sira = component ordinal'i; firstOrdinal = zaten var olanlar).
    // Eski Anim* track'leri authored degere GORELI offset; MovieClip mutlak → authored + offset.
    void EmitAnimations(XNode owner, List<XNode> anims, string go, float sx, float sy, Dictionary<string, object?> box, JsonArray ops, int firstOrdinal)
    {
        for (int ai = 0; ai < anims.Count; ai++)
        {
            var a = anims[ai];
            string comp = "$mc" + (_aliasN++);
            ops.Add(Op("comp.add", new JsonObject { ["go"] = go, ["type"] = "MovieClip" }, comp[1..]));
            void Set(string prop, JsonNode? val) => ops.Add(Op("prop.set", new JsonObject { ["go"] = go, ["comp"] = comp, ["prop"] = prop, ["value"] = val }));
            float delayMs = F(a["Delay"]) ?? 0;
            bool onShow = a["OnShow"] == "true";
            string? name = a["Name"];
            Set("PlayOnStart", onShow);
            Set("Loop", false);
            if (name != null) { Set("Labels[0].Frame", 0); Set("Labels[0].Name", name); }
            foreach (var k in a.Attrs)
                if (k.Key is not ("OnShow" or "Name" or "Delay")) Report.Add(a, $"Animation attr {k.Key}=\"{k.Value}\" not ported");
            int maxFrame = 0, ti = 0;
            foreach (var t in a.Children)
            {
                if (t["Keys"] == null) { Report.Add(t, "timeline without Keys"); continue; }
                string tl = t.Name;
                bool rel = tl.StartsWith("Anim");
                string baseName = rel ? tl[4..] : tl;
                string? target = null, path = null; int channels = 1, mask = 0;
                Func<float[], float[]> toAbs = v => v;
                switch (baseName)
                {
                    case "Scale":
                        target = "Transform:0"; path = "Scale"; channels = 3;
                        toAbs = v => new[] { (rel ? sx : 0) + v[0], (rel ? sy : 0) + v[1], 1 };
                        break;
                    case "Alpha":
                        target = "RenderGroup:0"; path = "Alpha";
                        toAbs = v => new[] { (rel ? 1 : 0) + v[0] };
                        break;
                    case "Position":
                        {
                            var (bx, by) = Pair(box.TryGetValue("anchoredPos", out var ap) ? ap as string : null);
                            target = "LayoutBox:0"; path = "anchoredPos"; channels = 2;
                            toAbs = v => new[] { (rel ? bx : 0) + v[0], (rel ? by : 0) + v[1] };
                            break;
                        }
                    case "X":
                        {
                            var (bx, _) = Pair(box.TryGetValue("anchoredPos", out var ap) ? ap as string : null);
                            target = "LayoutBox:0"; path = "anchoredPos"; mask = 1;
                            toAbs = v => new[] { (rel ? bx : 0) + v[0], 0 };
                            break;
                        }
                    case "Y":
                        {
                            var (_, by) = Pair(box.TryGetValue("anchoredPos", out var ap) ? ap as string : null);
                            target = "LayoutBox:0"; path = "anchoredPos"; mask = 2;
                            toAbs = v => new[] { 0, (rel ? by : 0) + v[0] };
                            break;
                        }
                    case "Width": target = "LayoutBox:0"; path = "width"; if (rel) Report.Add(t, "AnimWidth relative to authored width — treated absolute"); break;
                    case "Height": target = "LayoutBox:0"; path = "height"; if (rel) Report.Add(t, "AnimHeight relative — treated absolute"); break;
                    case "Rotation": case "RotationZ":
                        target = "Transform:0"; path = "Rotation"; mask = 4;
                        toAbs = v => new[] { 0, 0, v[0] };
                        break;
                    default:
                        Report.Add(t, $"timeline <{tl}> not ported");
                        continue;
                }
                var keys = ParseKeys(t["Keys"]!, channels, t);
                if (keys == null) continue;
                string tp = $"Tracks[{ti}]";
                Set(tp + ".Target", go + ":" + target);
                Set(tp + ".Path", path);
                if (mask != 0) Set(tp + ".Mask", mask);
                for (int ki = 0; ki < keys.Count; ki++)
                {
                    var (ms, vals, ease) = keys[ki];
                    int frame = (int)Math.Round((ms + delayMs) * 30f / 1000f);
                    maxFrame = Math.Max(maxFrame, frame);
                    var abs = toAbs(vals);
                    Set($"{tp}.Keys[{ki}].Frame", frame);
                    Set($"{tp}.Keys[{ki}].Value", string.Join(" ", abs.Select(V)) + (abs.Length < 4 ? string.Concat(Enumerable.Repeat(" 0", 4 - abs.Length)) : ""));
                    // Eski: ease N. key'e GIRIS; MovieClip: key'den CIKIS → bir onceki key'e yaz.
                    string? nextEase = ki + 1 < keys.Count ? keys[ki + 1].ease : null;
                    if (nextEase != null) Set($"{tp}.Keys[{ki}].Ease", MapEase(nextEase, t));
                }
                ti++;
            }
            Set("FrameCount", Math.Max(1, maxFrame));
            if (ti == 0 && a.Children.Count == 0 && name != null) Report.Add(a, $"empty named Animation '{name}' (action hook only)");
        }
    }

    // "0:-1,0 400:0,0,BackOut" → (ms, degerler, ease)
    List<(float ms, float[] v, string? ease)>? ParseKeys(string keys, int channels, XNode t)
    {
        var list = new List<(float, float[], string?)>();
        foreach (var k in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tmp = k.Split(':', 2);
            if (tmp.Length < 2) continue;
            float ms = float.Parse(tmp[0], Inv);
            var parts = tmp[1].Split(',');
            int idx = 0;
            if (parts[0].StartsWith('^')) { Report.Add(t, $"keys targeting other sprite ('{parts[0]}') not ported"); return null; }
            var vals = new List<float>();
            while (idx < parts.Length && float.TryParse(parts[idx], NumberStyles.Float, Inv, out float f)) { vals.Add(f); idx++; }
            string? ease = idx < parts.Length ? parts[idx].Trim() : null;
            if (vals.Count == 0) vals.Add(0);
            var v = new float[Math.Max(channels, 1)];
            for (int i = 0; i < v.Length; i++) v[i] = i < vals.Count ? vals[i] : vals[0];
            if (channels >= 2 && vals.Count == 1) v[1] = vals[0];            // eski: tek deger → v2=v1
            if (channels >= 3 && vals.Count < 3) v[2] = 0;
            list.Add((ms, v, ease));
        }
        return list;
    }

    static readonly HashSet<string> EaseNames = new(Enum.GetNames(typeof(EaseEnum)));
    enum EaseEnum { Linear, InSine, OutSine, InOutSine, InQuad, OutQuad, InOutQuad, InCubic, OutCubic, InOutCubic, InQuart, OutQuart, InOutQuart, InQuint, OutQuint, InOutQuint, InExpo, OutExpo, InOutExpo, InCirc, OutCirc, InOutCirc, InElastic, OutElastic, InOutElastic, InBack, OutBack, InOutBack, InBounce, OutBounce, InOutBounce }

    string MapEase(string old, XNode t)
    {
        if (EaseNames.Contains(old)) return old;
        var m = Regex.Match(old, @"^(\w+?)(InOut|In|Out)$");
        if (m.Success)
        {
            string c = m.Groups[2].Value + m.Groups[1].Value;
            if (EaseNames.Contains(c)) return c;
        }
        Report.Add(t, $"ease '{old}' unknown → Linear");
        return "Linear";
    }

    // ---------------------------------------------------------------- yardimcilar
    static readonly HashSet<string> KnownTags = new() { "Group2D", "UISprite", "Image", "Image9", "Fill", "Label", "NumberLabel", "HGroup", "VGroup", "Scroller", "MaskSprite", "Html", "SpriteSheet", "ParticleSprite", "FlowLayout", "RepeatSprite", "ListSprite", "Sprite2D" };
    static readonly HashSet<string> BehaviourTags = new() { "Button", "Sound", "AnyMotion", "BackgroundImage", "DoubleSide", "FollowPointer", "Periodic", "Shake", "SpriteSheet", "TimerSpriteUpdate" };
    static bool IsModuleTag(string tag) => tag.EndsWith("Module") || tag.EndsWith("Scene") || (!KnownTags.Contains(tag) && char.IsUpper(tag[0]) && tag != "include" && !tag.StartsWith("T:"));

    static readonly HashSet<string> HandledCommon = new() { "Name", "Hidden", "Visible", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Stretch", "StretchDelta", "Snap", "Margin", "X", "Y", "PivotX", "PivotY", "Scale", "ScaleX", "ScaleY", "Rotation", "Alpha", "Color", "BatchOrder", "Depth", "Interactive", "FullScreen", "SafeScreen", "Padding", "Batch", "InteractiveChildren", "NoParentLayout", "Xml", "bind" };
    static bool HandledAttr(string key, string tag)
    {
        if (key.StartsWith("bind:") || key.StartsWith("action:") || key.StartsWith(":") || key.StartsWith("bindOnce:") || HandledCommon.Contains(key)) return true;
        return tag switch
        {
            "Image" or "Image9" or "MaskSprite" => key is "Src" or "Scale9Grid",
            "Label" or "NumberLabel" => key is "Text" or "LocaleText" or "Face" or "Size" or "HAlign" or "VAlign" or "Multiline" or "Ellipsis" or "Kerning" or "Shadow" or "LineHeight",
            "HGroup" or "VGroup" => key is "Spacing" or "Reverse" or "Align" or "HAlign" or "VAlign" or "StretchZero" or "HasBackground",
            "Group2D" => key is "Align" or "HAlign" or "VAlign",
            _ => IsModuleTag(tag),   // modul attribute'lari component alanlarina gider (ayrica raporlanir)
        };
    }

    static string? BindField(string f) => f switch
    {
        "Text" => "text",
        "Width" => "width",
        "Height" => "height",
        "BatchOrder" => "SortingOrder",
        "Alpha" => null,
        _ => null,
    };

    static string DefaultName(XNode n) => n.Name switch
    {
        "Image" or "Image9" => n["Src"] is string s ? Path.GetFileName(s) : n.Name,
        "Label" => n["Text"] ?? (n["LocaleText"] is string lt ? Regex.Replace(lt, @"[{}]|locale:", "") : "Label"),
        _ => n.Name,
    };

    static string AttrSummary(XNode n) => string.Join(" ", n.Attrs.Where(a => a.Key != "Name").Select(a => $"{a.Key}=\"{a.Value}\""));

    static void ParseTransform(XNode n, out float sx, out float sy, out float rot)
    {
        sx = sy = 1; rot = 0;
        if (F(n["Scale"]) is float s) sx = sy = s;
        if (F(n["ScaleX"]) is float x) sx = x;
        if (F(n["ScaleY"]) is float y) sy = y;
        if (F(n["Rotation"]) is float r) rot = r;
    }

    static (float, float) Margin(XNode n) => Pair(n["Margin"]);

    static (float, float) Pair(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (0, 0);
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float a = float.Parse(p[0], Inv);
        float b = p.Length > 1 ? float.Parse(p[1], Inv) : a;
        return (a, b);
    }

    (float, float) SnapPoint(string snap, XNode n) => snap switch
    {
        "TopLeft" => (0, 0), "TopCenter" => (.5f, 0), "TopRight" => (1, 0),
        "CenterLeft" => (0, .5f), "Center" => (.5f, .5f), "CenterRight" => (1, .5f),
        "BottomLeft" => (0, 1), "BottomCenter" => (.5f, 1), "BottomRight" => (1, 1),
        _ => Warn(n, $"unknown Snap '{snap}'"),
    };
    (float, float) Warn(XNode n, string m) { Report.Add(n, m); return (.5f, .5f); }

    static float? F(string? s) => s != null && float.TryParse(s, NumberStyles.Float, Inv, out var f) ? f : null;
    static string V(float f) { var r = Math.Round(f, 4); return (r == 0 ? 0 : r).ToString(Inv); }

    // Eski renk: "#RGB" | "#RRGGBB" | "#AARRGGBB" → yeni "#RRGGBBAA"
    static string Color(string c)
    {
        c = c.Trim().TrimStart('#');
        if (c.Length == 3) c = $"{c[0]}{c[0]}{c[1]}{c[1]}{c[2]}{c[2]}";
        if (c.Length == 6) return "#" + c.ToUpperInvariant() + "FF";
        if (c.Length == 8) return "#" + c[2..].ToUpperInvariant() + c[..2].ToUpperInvariant();
        return "#FFFFFFFF";
    }
}
