using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC komut telemetrisi: komut basina sayi, hata, toplam/ortalama/maks duvar-saati (deferred bekleme dahil),
// donen bayt ve goruntu sayisi. Amac: AI'in dogrulama dongusunun gercek maliyet profilini OLCMEK
// (tahminle optimize etmemek). Bellekte yasar; rpc.stats ile okunur/sifirlanir; editor kapanirken ozet loglanir.
public static class RpcStats
{
    public sealed class Row
    {
        public string Cmd;
        public int Count, Errors, Images;
        public long TotalMs, MaxMs, Bytes;
    }

    static readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static long _sinceMs;
    public static int TotalCommands { get; private set; }
    public static int TotalImages { get; private set; }

    // Istek alinirken: baslangic damgasi (ms).
    public static long Begin() => _clock.ElapsedMilliseconds;

    // Yanit yazilirken (hemen ya da deferred). response = JSON satiri (bayt/goruntu sayimi icin).
    public static void End(string cmd, long beginMs, string response, bool error)
    {
        if (string.IsNullOrEmpty(cmd))
            return;
        long ms = _clock.ElapsedMilliseconds - beginMs;
        if (!_rows.TryGetValue(cmd, out var r))
            _rows[cmd] = r = new Row { Cmd = cmd };
        r.Count++;
        if (error) r.Errors++;
        r.TotalMs += ms;
        if (ms > r.MaxMs) r.MaxMs = ms;
        int bytes = response?.Length ?? 0;
        r.Bytes += bytes;
        int imgs = CountImages(response);
        r.Images += imgs;
        TotalCommands++;
        TotalImages += imgs;
    }

    // Goruntu yuku isareti: screenshot/capture sonuclari "toScreen" carpanini tasir (tek yer: RenderScreenshot);
    // ui.screenshot "scale" + png yolu doner → ayrica sayilir.
    static int CountImages(string response)
    {
        if (string.IsNullOrEmpty(response))
            return 0;
        int n = 0, i = 0;
        while ((i = response.IndexOf("\"toScreen\"", i, StringComparison.Ordinal)) >= 0) { n++; i += 10; }
        if (n == 0 && response.Contains("Screenshots\\\\ui_", StringComparison.Ordinal))
            n = 1;
        return n;
    }

    public static void Reset()
    {
        _rows.Clear();
        TotalCommands = 0;
        TotalImages = 0;
        _sinceMs = _clock.ElapsedMilliseconds;
    }

    // Toplam sureye gore azalan tablo.
    public static DocNode Report(int top)
    {
        var rows = new List<Row>(_rows.Values);
        rows.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
        var r = DocNode.Map();
        r.Add("sinceSec", DocNode.Scal(((_clock.ElapsedMilliseconds - _sinceMs) / 1000).ToString(CultureInfo.InvariantCulture)));
        r.Add("commands", DocNode.Scal(TotalCommands.ToString(CultureInfo.InvariantCulture)));
        r.Add("images", DocNode.Scal(TotalImages.ToString(CultureInfo.InvariantCulture)));
        long totalMs = 0, totalBytes = 0;
        foreach (var x in rows) { totalMs += x.TotalMs; totalBytes += x.Bytes; }
        r.Add("totalSec", DocNode.Scal((totalMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)));
        r.Add("totalKB", DocNode.Scal((totalBytes / 1024).ToString(CultureInfo.InvariantCulture)));
        var seq = DocNode.Seq();
        for (int i = 0; i < rows.Count && (top <= 0 || i < top); i++)
        {
            var x = rows[i];
            var m = DocNode.Map();
            m.Add("cmd", DocNode.Scal(x.Cmd));
            m.Add("n", DocNode.Scal(x.Count.ToString(CultureInfo.InvariantCulture)));
            if (x.Errors > 0) m.Add("errors", DocNode.Scal(x.Errors.ToString(CultureInfo.InvariantCulture)));
            m.Add("totalMs", DocNode.Scal(x.TotalMs.ToString(CultureInfo.InvariantCulture)));
            m.Add("avgMs", DocNode.Scal((x.TotalMs / Math.Max(1, x.Count)).ToString(CultureInfo.InvariantCulture)));
            m.Add("maxMs", DocNode.Scal(x.MaxMs.ToString(CultureInfo.InvariantCulture)));
            m.Add("KB", DocNode.Scal((x.Bytes / 1024).ToString(CultureInfo.InvariantCulture)));
            if (x.Images > 0) m.Add("images", DocNode.Scal(x.Images.ToString(CultureInfo.InvariantCulture)));
            seq.Items.Add(m);
        }
        r.Add("rows", seq);
        return r;
    }

    // Kapanista tek satirlik ozet (Console/EditorLog): "cmd n/ms" ilk 8.
    public static string Summary()
    {
        if (TotalCommands == 0)
            return null;
        var rows = new List<Row>(_rows.Values);
        rows.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
        var sb = new System.Text.StringBuilder();
        sb.Append("[rpc.stats] ").Append(TotalCommands).Append(" komut, ").Append(TotalImages).Append(" goruntu:");
        for (int i = 0; i < rows.Count && i < 8; i++)
            sb.Append(' ').Append(rows[i].Cmd).Append(' ').Append(rows[i].Count).Append('/').Append(rows[i].TotalMs).Append("ms");
        return sb.ToString();
    }
}
