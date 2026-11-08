using System.Text.Json;

namespace XmlToScene;

// Projects/<oyun>/Assets/Textures altindaki parca gorselleri indeksler: eski "Src" adi ->
// Assets'e goreli yol + piksel boyutu (+ slices.json'dan 9-slice kenarlari).
sealed class AssetIndex
{
    public sealed record Tex(string RelPath, int Width, int Height, int[]? Slice9);

    readonly Dictionary<string, List<Tex>> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Tex> _byRel = new(StringComparer.OrdinalIgnoreCase);
    public int Count => _byRel.Count;

    public AssetIndex(string assetsRoot)
    {
        string texRoot = Path.Combine(assetsRoot, "Textures");
        var slices = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        string slicePath = Path.Combine(texRoot, "slices.json");
        if (File.Exists(slicePath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(slicePath));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var e = p.Value;
                slices[p.Name] = new[]
                {
                    e.GetProperty("left").GetInt32(), e.GetProperty("top").GetInt32(),
                    e.GetProperty("right").GetInt32(), e.GetProperty("bottom").GetInt32(),
                };
            }
        }
        if (!Directory.Exists(texRoot)) return;
        foreach (var f in Directory.EnumerateFiles(texRoot, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
            var (w, h) = ImageSize.Read(f);
            string rel = Path.GetRelativePath(assetsRoot, f).Replace('\\', '/');
            // "Textures/common/bg-bar.png" -> grup "common", ad "bg-bar"
            string noExt = rel.Substring("Textures/".Length);
            noExt = noExt[..noExt.LastIndexOf('.')];
            slices.TryGetValue(noExt, out var s9);
            var t = new Tex(rel, w, h, s9);
            _byRel[noExt] = t;
            string name = noExt.Contains('/') ? noExt[(noExt.LastIndexOf('/') + 1)..] : noExt;
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = new();
            list.Add(t);
        }
    }

    // Eski Src cozumu: "grp/ad" tam yol; yalin "ad" -> once common, sonra tek aday; coklu aday -> ambiguous.
    public Tex? Resolve(string src, out string? ambiguity)
    {
        ambiguity = null;
        if (string.IsNullOrEmpty(src)) return null;
        if (src.Contains('/'))
            return _byRel.TryGetValue(src, out var t) ? t : null;
        if (!_byName.TryGetValue(src, out var list)) return null;
        if (list.Count == 1) return list[0];
        var common = list.FirstOrDefault(x => x.RelPath.StartsWith("Textures/common/", StringComparison.OrdinalIgnoreCase));
        if (common != null) return common;
        ambiguity = string.Join(", ", list.Select(x => x.RelPath));
        return list[0];
    }
}

static class ImageSize
{
    // PNG IHDR / JPEG SOFn basligindan boyut; decode yok.
    public static (int w, int h) Read(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> b = stackalloc byte[32];
        int n = fs.Read(b);
        if (n >= 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G')
            return (BE(b, 16), BE(b, 20));
        if (n >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            fs.Position = 2;
            var br = new BinaryReader(fs);
            while (fs.Position < fs.Length)
            {
                if (br.ReadByte() != 0xFF) continue;
                byte marker = br.ReadByte();
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                int len = (br.ReadByte() << 8) | br.ReadByte();
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    br.ReadByte();
                    int h = (br.ReadByte() << 8) | br.ReadByte();
                    int w = (br.ReadByte() << 8) | br.ReadByte();
                    return (w, h);
                }
                fs.Position += len - 2;
            }
        }
        return (0, 0);
    }
    static int BE(Span<byte> b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
}
