using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;

// kullanim: modpak <in.pak> <out.pak> <code.dmod> <entryName>
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("kullanim: modpak <in.pak> <out.pak> <code.dmod> <entryName>"); return 2; }
        var src = new PakSource(args[0]);
        var items = new List<(string Key, string Guid, byte[] Data)>();
        var deps = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var key in src.Keys)
        {
            if (key == Module.CodeKey || key == Module.EntryKey) continue;
            items.Add((key, src.GuidOf(key) ?? "", src.ReadBytes(key) ?? Array.Empty<byte>()));
            var d = src.DepsOf(key);
            if (d != null && d.Length > 0) deps[key] = d;
        }
        items.Add((Module.CodeKey, "", File.ReadAllBytes(args[2])));
        items.Add((Module.EntryKey, "", Encoding.UTF8.GetBytes(args[3])));
        if (args.Length > 4) // start sahne override (test): .project kaydini yeniden yaz
        {
            ProjectBinary.TryRead(src.ReadBytes(ProjectBinary.PakKey), out var rec);
            for (int i = 0; i < items.Count; i++)
                if (items[i].Key == ProjectBinary.PakKey)
                    items[i] = (items[i].Key, items[i].Guid, ProjectBinary.Write(rec?.Name ?? "Module", args[4], rec?.Scenes));
        }
        var (raw, size) = PakWriter.Write(args[1], items, null, k => deps.TryGetValue(k, out var l) ? l : null);
        Console.WriteLine($"modpak -> {args[1]}: {items.Count} giris, {size / 1024} KB (acik {raw / 1024} KB)");
        return 0;
    }
}
