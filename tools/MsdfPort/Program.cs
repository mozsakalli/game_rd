using System.Diagnostics;
using System.Globalization;
using Digitoy.Box.Editor;
using MsdfPort;

// Kullanim: MsdfPort <font.ttf> [--size 32] [--charset "32-126,160-383"] [--dll <digitoyengine_native.dll>] [--dump <dir>] [--no-ref]
// Portun MTSDF ciktisini native referansla bayt bayt karsilastirir.

string fontPath = null;
float sdfSize = 32f;
string charset = "32-126,160-383";
string dll = null;
string dump = null;
string pngDir = null;
bool noRef = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--size": sdfSize = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--charset": charset = args[++i]; break;
        case "--dll": dll = args[++i]; break;
        case "--dump": dump = args[++i]; break;
        case "--no-ref": noRef = true; break;
        case "--png": pngDir = args[++i]; break;
        default: fontPath = args[i]; break;
    }
}
if (fontPath == null)
{
    Console.Error.WriteLine("font yolu gerekli");
    return 2;
}

const int BakePad = 6;
const float BakePixelDistScale = 21f;
const double MsdfAngleThreshold = 3.0;

dll ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "editor", "bin", "Debug", "net10.0", "digitoyengine_native.dll"));
if (!noRef && !File.Exists(dll))
{
    Console.Error.WriteLine($"referans DLL yok: {dll} (--dll ver ya da --no-ref)");
    return 2;
}

var ttf = File.ReadAllBytes(fontPath);
var cps = ParseCharset(charset);
Console.WriteLine($"font={Path.GetFileName(fontPath)} size={sdfSize} glyphs={cps.Count} ref={(noRef ? "-" : dll)}");

var baker = new MsdfFontBaker(ttf);
NativeReference.Handle native = null;
if (!noRef)
{
    NativeReference.Configure(dll);
    native = new NativeReference.Handle(ttf);
}

int identical = 0, differ = 0, metaDiff = 0, maxDiff = 0;
long totalBytes = 0;
var swPort = new Stopwatch();
var swRef = new Stopwatch();
var out5 = new float[5];
var ref5 = new float[5];
var worst = new List<string>();

// metrikler + kerning
{
    var m = baker.Metrics(sdfSize);
    if (native != null)
    {
        var rm = native.Metrics(sdfSize);
        bool eq = true;
        for (int i = 0; i < 5; i++) eq &= BitConverter.SingleToInt32Bits(m[i]) == BitConverter.SingleToInt32Bits(rm[i]);
        Console.WriteLine($"metrics {(eq ? "OK" : "DIFF")}: port=[{string.Join(", ", m)}] ref=[{string.Join(", ", rm)}]");
        var arr = cps.ToArray();
        var k = baker.KernTable(arr);
        var rk = native.KernTable(arr);
        int kd = 0;
        for (int i = 0; i < k.Length; i++) if (k[i] != rk[i]) kd++;
        int nonZero = 0;
        for (int i = 0; i < k.Length; i++) if (rk[i] != 0) nonZero++;
        Console.WriteLine($"kern {(kd == 0 ? "OK" : "DIFF " + kd)}: {k.Length} cift, referansta {nonZero} sifir-disi");
    }
    else
        Console.WriteLine($"metrics port=[{string.Join(", ", m)}]");
}

if (dump != null) Directory.CreateDirectory(dump);
if (pngDir != null) Directory.CreateDirectory(pngDir);

foreach (int cp in cps)
{
    swPort.Start();
    byte[] a = baker.Glyph(cp, sdfSize, BakePad, BakePixelDistScale, MsdfAngleThreshold, out5);
    swPort.Stop();
    if (pngDir != null && a != null)
    {
        DumpPng(pngDir, cp, a, (int)out5[3], (int)out5[4]);
        int gw = (int)out5[3], gh = (int)out5[4], maxA = 0, maxM = 0, aHi = 0, mHi = 0, artOut = 0, artIn = 0;
        for (int i = 0; i < gw * gh; i++)
        {
            int r = a[i * 4], g = a[i * 4 + 1], b2 = a[i * 4 + 2], al = a[i * 4 + 3];
            int m = Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b2));
            if (al > maxA) maxA = al; if (m > maxM) maxM = m; if (al >= 200) aHi++; if (m >= 200) mHi++;
            if (al < 128 - 21 && m >= 128) artOut++;   // gercek mesafe 1px+ DISARIDA ama median "icerde"
            if (al > 128 + 21 && m < 128) artIn++;     // gercek mesafe 1px+ ICERDE ama median "disarida"
        }
        Console.WriteLine($"U+{cp:X4} {gw}x{gh} maxA={maxA} maxMed={maxM} px(a>=200)={aHi} px(med>=200)={mHi} medianArtifact out={artOut} in={artIn}");
    }
    if (native == null)
    {
        if (a != null) totalBytes += a.Length;
        continue;
    }
    swRef.Start();
    byte[] b = native.Glyph(cp, sdfSize, BakePad, BakePixelDistScale, MsdfAngleThreshold, ref5, out int ok);
    swRef.Stop();

    bool metaEq = true;
    for (int i = 0; i < 5; i++) metaEq &= BitConverter.SingleToInt32Bits(out5[i]) == BitConverter.SingleToInt32Bits(ref5[i]);
    if (!metaEq)
    {
        metaDiff++;
        worst.Add($"U+{cp:X4} META port=[{string.Join(",", out5)}] ref=[{string.Join(",", ref5)}] ok={ok}");
        continue;
    }
    if (a == null && b == null) { identical++; continue; }
    if (a == null || b == null || a.Length != b.Length)
    {
        differ++;
        worst.Add($"U+{cp:X4} SIZE port={(a?.Length ?? -1)} ref={(b?.Length ?? -1)}");
        continue;
    }
    totalBytes += a.Length;
    int w = (int)out5[3];
    int cnt = 0, md = 0, firstIdx = -1;
    for (int i = 0; i < a.Length; i++)
    {
        int d = Math.Abs(a[i] - b[i]);
        if (d != 0)
        {
            cnt++;
            if (firstIdx < 0) firstIdx = i;
            if (d > md) md = d;
        }
    }
    if (cnt == 0)
        identical++;
    else
    {
        differ++;
        if (md > maxDiff) maxDiff = md;
        int px = firstIdx / 4, ch = firstIdx % 4;
        worst.Add($"U+{cp:X4} ({(char)cp}) {cnt}/{a.Length} bayt farkli, max {md}, ilk ({px % w},{px / w}) kanal {ch}: port={a[firstIdx]} ref={b[firstIdx]}");
        if (dump != null)
        {
            File.WriteAllBytes(Path.Combine(dump, $"U+{cp:X4}_{w}x{(int)out5[4]}_port.rgba"), a);
            File.WriteAllBytes(Path.Combine(dump, $"U+{cp:X4}_{w}x{(int)out5[4]}_ref.rgba"), b);
        }
    }
}

Console.WriteLine($"port {swPort.ElapsedMilliseconds} ms, ref {swRef.ElapsedMilliseconds} ms, {totalBytes} bayt");
if (native != null)
{
    Console.WriteLine($"SONUC: {identical} ayni, {differ} farkli, {metaDiff} metrik farki, max bayt farki {maxDiff}");
    foreach (var s in worst.Take(40))
        Console.WriteLine("  " + s);
    native.Dispose();
    return differ + metaDiff == 0 ? 0 : 1;
}
return 0;

// Glyph'in A (gercek SDF), median(rgb) ve esiklenmis kontur goruntuleri, 4x buyutulmus PNG (gorsel teshis).
static void DumpPng(string dir, int cp, byte[] rgba, int w, int h)
{
    const int S = 4;
    var alpha = new byte[w * S * h * S * 3];
    var med = new byte[alpha.Length];
    var outl = new byte[alpha.Length];
    for (int y = 0; y < h * S; y++)
        for (int x = 0; x < w * S; x++)
        {
            int i = ((y / S) * w + x / S) * 4;
            byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2], a = rgba[i + 3];
            byte m = Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));
            int o = (y * w * S + x) * 3;
            alpha[o] = alpha[o + 1] = alpha[o + 2] = a;
            med[o] = med[o + 1] = med[o + 2] = m;
            // kontur: median >= 128 beyaz dolgu; alpha >= 128-2*21 (2 SDF px disari) kirmizi kontur
            bool fill = m >= 128, ring = a >= 128 - 42;
            outl[o] = (byte)(fill ? 255 : ring ? 220 : 40);
            outl[o + 1] = (byte)(fill ? 255 : ring ? 30 : 40);
            outl[o + 2] = (byte)(fill ? 255 : ring ? 30 : 40);
        }
    File.WriteAllBytes(Path.Combine(dir, $"U+{cp:X4}_alpha.png"), Png.Encode(alpha, w * S, h * S));
    File.WriteAllBytes(Path.Combine(dir, $"U+{cp:X4}_median.png"), Png.Encode(med, w * S, h * S));
    File.WriteAllBytes(Path.Combine(dir, $"U+{cp:X4}_outline.png"), Png.Encode(outl, w * S, h * S));
}

static List<int> ParseCharset(string s)
{
    var set = new SortedSet<int> { ' ', '?' };
    foreach (var raw in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var part = raw;
        int dash = part.IndexOf('-', 1);
        if (dash > 0 && TryCp(part[..dash], out int a) && TryCp(part[(dash + 1)..], out int b))
        {
            for (int c = a; c <= b; c++) set.Add(c);
        }
        else if (TryCp(part, out int single))
            set.Add(single);
        else
            foreach (var r in part.EnumerateRunes()) set.Add(r.Value);
    }
    return set.ToList();

    static bool TryCp(string t, out int v)
    {
        if (t.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }
}
