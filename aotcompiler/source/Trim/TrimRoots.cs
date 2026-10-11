using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigitoyEngine.Language.Trim
{
    // Trim kokleri. Kaynaklar: giris metotlari (host ABI), pak'taki pismis sahne/prefab component tipleri (adlar),
    // koddan otomatik ([Preserve], [Animatable], typeof/AddComponent<T>) — TrimAnalysis toplar.
    public sealed class TrimRoots
    {
        public readonly List<Code> Entry = new List<Code>();
        public readonly Dictionary<Primitive, MemberKinds> Reflection = new Dictionary<Primitive, MemberKinds>();
        public readonly List<string> ComponentNames = new List<string>(); // pak'tan (kisa tip adi: TypeCatalog.Entry.Name)
        public readonly List<string> Notes = new List<string>();          // rapor: kok kaynaklari / uyarilar

        public void AddReflection(Primitive p, MemberKinds kinds)
        {
            if (p == null || kinds == MemberKinds.None) return;
            Reflection.TryGetValue(p, out var cur);
            Reflection[p] = cur | kinds;
        }
    }

    // game.pak okuyucu (aotcompiler tarafi): pismis sahne/prefab girislerindeki component tip adlarini cikarir.
    // Format: engine/managed/AssetSource.cs (DPK3) + Serialization/SceneBinary.cs (DSCN v2). Pak = tek gercek kaynak;
    // ayri kok dosyasi yok.
    public static class PakRoots
    {
        const int PakMagic = 0x334B5044;   // "DPK3"
        const int SceneMagic = 0x4E435344; // "DSCN"

        public static List<string> ComponentTypeNames(string pakPath, List<string> notes)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var bytes = File.ReadAllBytes(pakPath);
            if (bytes.Length < 12 || BitConverter.ToInt32(bytes, 0) != PakMagic)
                throw new Exception("pak magic uyusmuyor: " + pakPath);
            int count = BitConverter.ToInt32(bytes, 4), indexLen = BitConverter.ToInt32(bytes, 8);
            int p = 12, scenes = 0;
            for (int i = 0; i < count; i++)
            {
                string key = Str(bytes, ref p);
                Str(bytes, ref p); // guid
                long off = BitConverter.ToInt64(bytes, p); p += 8;
                long stored = BitConverter.ToInt64(bytes, p); p += 8;
                long raw = BitConverter.ToInt64(bytes, p); p += 8;
                int nDep = bytes[p] | (bytes[p + 1] << 8); p += 2;
                for (int d = 0; d < nDep; d++) Str(bytes, ref p);
                if (stored <= 0 || !(key.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var data = new byte[stored];
                Array.Copy(bytes, off, data, 0, stored);
                if (raw != stored)
                {
                    var inflated = new byte[raw];
                    using var z = new System.IO.Compression.ZLibStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress);
                    z.ReadExactly(inflated);
                    data = inflated;
                }
                if (data.Length < 8 || BitConverter.ToInt32(data, 0) != SceneMagic) continue;
                ReadSceneTypes(data, names);
                scenes++;
            }
            notes?.Add($"pak kokleri: {pakPath} -> {scenes} sahne/prefab, {names.Count} component tipi");
            return new List<string>(names);
        }

        // DSCN v2: magic, version, strings, aliases, types(name + alan tablosu) ... yalniz tip adlari + alias yeni adlari.
        static void ReadSceneTypes(byte[] b, SortedSet<string> names)
        {
            int p = 8;
            int nStr = BitConverter.ToInt32(b, p); p += 4;
            var strs = new string[nStr];
            for (int i = 0; i < nStr; i++) strs[i] = Str(b, ref p);
            int nAlias = BitConverter.ToInt32(b, p); p += 4;
            for (int i = 0; i < nAlias; i++) { p += 4; names.Add(strs[BitConverter.ToInt32(b, p)]); p += 4; }
            int nType = BitConverter.ToInt32(b, p); p += 4;
            for (int i = 0; i < nType; i++)
            {
                names.Add(strs[BitConverter.ToInt32(b, p)]); p += 4;
                SkipFieldTable(b, ref p);
            }
        }

        static void SkipFieldTable(byte[] b, ref int p)
        {
            int n = b[p] | (b[p + 1] << 8); p += 2;
            for (int i = 0; i < n; i++)
            {
                p += 4 + 1 + 1; // nameStr, kind, elemKind
                bool nested = b[p++] != 0;
                if (nested) SkipFieldTable(b, ref p);
            }
        }

        static string Str(byte[] b, ref int p)
        {
            int len = b[p] | (b[p + 1] << 8); p += 2;
            var s = Encoding.UTF8.GetString(b, p, len); p += len;
            return s;
        }
    }
}
