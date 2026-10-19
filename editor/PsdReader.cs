using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigitoyEditor;

// PSD / PSB katman okuyucu (editor-only, saf C#, Adobe "Photoshop File Format" spec'i). Yalniz katman
// piksellerini verir: 8-bit RGB(A)/Gray, raw ve RLE (PackBits) sikistirma, PSB (v2: 64-bit uzunluklar).
// Composite goruntu, maske, efekt, ayarlama katmani OKUNMAZ (piksel katmani olmayanlar atlanir).
// 16/32-bit derinlik, CMYK/Lab, zip sikistirma -> hata (kaynak 8-bit RGB'ye donusturulmeli).
// Cikti RGBA8, satir 0 USTTE (dosya duzeni); cagiran motor duzenine (satir 0 altta) cevirir.
public sealed class PsdReader
{
    public sealed class Layer
    {
        public int Id = -1;            // 'lyid' (yoksa -1)
        public string Name = "";       // 'luni' varsa unicode, yoksa pascal
        public int Left, Top, Right, Bottom;
        public bool Visible;
        public int SectionType;        // 'lsct': 0 piksel, 1 acik grup, 2 kapali grup, 3 grup sonu
        public string GroupPath = "";  // "A/B" (gruplardaki katman icin)
        public byte[] Rgba;            // W*H*4, satir 0 ustte; piksel katmani degilse null
        public int W => Right - Left;
        public int H => Bottom - Top;

        // Kanal kayitlari (okuma sirasinda)
        internal readonly List<(short Id, long Length)> Channels = new();
        internal byte Opacity;
    }

    public int Width, Height, Depth, ColorMode, ChannelCount;
    public bool IsPsb;
    public readonly List<Layer> Layers = new();

    public static PsdReader Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        var r = new PsdReader();
        r.Parse(br);
        return r;
    }

    // --- big-endian yardimcilar ---
    static ushort U16(BinaryReader b) { var d = b.ReadBytes(2); return (ushort)(d[0] << 8 | d[1]); }
    static short I16(BinaryReader b) => (short)U16(b);
    static uint U32(BinaryReader b) { var d = b.ReadBytes(4); return (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]); }
    static int I32(BinaryReader b) => (int)U32(b);
    static ulong U64(BinaryReader b) { ulong hi = U32(b); ulong lo = U32(b); return hi << 32 | lo; }
    long Len(BinaryReader b) => IsPsb ? (long)U64(b) : U32(b);

    void Parse(BinaryReader b)
    {
        if (Encoding.ASCII.GetString(b.ReadBytes(4)) != "8BPS")
            throw new InvalidDataException("PSD imzasi yok");
        ushort version = U16(b);
        IsPsb = version == 2;
        if (version != 1 && version != 2)
            throw new InvalidDataException("bilinmeyen PSD surumu: " + version);
        b.ReadBytes(6);
        ChannelCount = U16(b);
        Height = I32(b);
        Width = I32(b);
        Depth = U16(b);
        ColorMode = U16(b);
        if (Depth != 8)
            throw new InvalidDataException($"desteklenmeyen bit derinligi: {Depth} (8-bit gerekli)");
        if (ColorMode != 3 && ColorMode != 1)
            throw new InvalidDataException($"desteklenmeyen renk modu: {ColorMode} (RGB ya da Grayscale gerekli)");

        // Color mode data + image resources: atla.
        b.BaseStream.Seek(U32(b), SeekOrigin.Current);
        b.BaseStream.Seek(U32(b), SeekOrigin.Current);

        // Layer & mask info
        long lmLen = Len(b);
        long lmEnd = b.BaseStream.Position + lmLen;
        if (lmLen <= 0)
            return; // katmansiz (duz) dosya
        long layerInfoLen = Len(b);
        long layerInfoStart = b.BaseStream.Position;
        if (layerInfoLen > 0)
            ParseLayerInfo(b);
        b.BaseStream.Seek(layerInfoStart + layerInfoLen, SeekOrigin.Begin);
        // Global layer mask + ek bloklar ('Lr16' vb.) — 16-bit zaten reddedildi; gerisi atlanir.
        b.BaseStream.Seek(lmEnd, SeekOrigin.Begin);
    }

    void ParseLayerInfo(BinaryReader b)
    {
        int count = I16(b);
        if (count < 0) count = -count; // negatif: ilk alfa kanali birlesik seffaflik (bizi etkilemez)
        for (int i = 0; i < count; i++)
        {
            var L = new Layer();
            L.Top = I32(b); L.Left = I32(b); L.Bottom = I32(b); L.Right = I32(b);
            int nch = U16(b);
            for (int c = 0; c < nch; c++)
            {
                short id = I16(b);
                long len = Len(b);
                L.Channels.Add((id, len));
            }
            if (Encoding.ASCII.GetString(b.ReadBytes(4)) != "8BIM")
                throw new InvalidDataException("katman blend imzasi bozuk");
            b.ReadBytes(4); // blend key
            L.Opacity = b.ReadByte();
            b.ReadByte(); // clipping
            byte flags = b.ReadByte();
            L.Visible = (flags & 2) == 0;
            b.ReadByte(); // filler
            uint extraLen = U32(b);
            long extraEnd = b.BaseStream.Position + extraLen;
            // Layer mask data
            uint maskLen = U32(b);
            b.BaseStream.Seek(maskLen, SeekOrigin.Current);
            // Blending ranges
            uint rangesLen = U32(b);
            b.BaseStream.Seek(rangesLen, SeekOrigin.Current);
            // Pascal ad (4'e yuvarlanir)
            int nameLen = b.ReadByte();
            var nameBytes = b.ReadBytes(nameLen);
            L.Name = Encoding.Latin1.GetString(nameBytes);
            int pad = (4 - ((nameLen + 1) % 4)) % 4;
            b.ReadBytes(pad);
            // Ek katman bilgisi bloklari
            while (b.BaseStream.Position + 12 <= extraEnd)
            {
                string sig = Encoding.ASCII.GetString(b.ReadBytes(4));
                if (sig != "8BIM" && sig != "8B64")
                    break;
                string key = Encoding.ASCII.GetString(b.ReadBytes(4));
                long len = IsPsb && IsLongKey(key) ? (long)U64(b) : U32(b);
                long start = b.BaseStream.Position;
                switch (key)
                {
                    case "luni":
                        {
                            int n = I32(b);
                            var u = b.ReadBytes(n * 2);
                            for (int k = 0; k + 1 < u.Length; k += 2) (u[k], u[k + 1]) = (u[k + 1], u[k]); // BE -> LE
                            L.Name = Encoding.Unicode.GetString(u).TrimEnd('\0');
                            break;
                        }
                    case "lyid":
                        L.Id = I32(b);
                        break;
                    case "lsct":
                        L.SectionType = I32(b);
                        break;
                }
                // Katman icinde bloklar 2'ye yuvarlanir (bazi yazicilar 4): sonraki imzayi arayarak toparla.
                long next = start + len;
                if ((next & 1) != 0) next++;
                b.BaseStream.Seek(next, SeekOrigin.Begin);
            }
            b.BaseStream.Seek(extraEnd, SeekOrigin.Begin);
            Layers.Add(L);
        }

        // Kanal goruntu verisi: katman sirasiyla, her kanal kendi uzunlugu kadar.
        foreach (var L in Layers)
        {
            int w = L.W, h = L.H;
            bool pixelLayer = L.SectionType == 0 && w > 0 && h > 0;
            byte[] r = null, g = null, bl = null, a = null;
            foreach (var (id, clen) in L.Channels)
            {
                long end = b.BaseStream.Position + clen;
                if (pixelLayer && id >= -1 && id <= 2)
                {
                    var plane = ReadChannel(b, w, h);
                    if (id == 0) r = plane; else if (id == 1) g = plane; else if (id == 2) bl = plane; else a = plane;
                }
                b.BaseStream.Seek(end, SeekOrigin.Begin); // maske kanallari (-2/-3) ve fazlalik atlanir
            }
            if (!pixelLayer || r == null)
                continue;
            var rgba = new byte[w * h * 4];
            bool gray = ColorMode == 1;
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4] = r[i];
                rgba[i * 4 + 1] = gray ? r[i] : g?[i] ?? r[i];
                rgba[i * 4 + 2] = gray ? r[i] : bl?[i] ?? r[i];
                rgba[i * 4 + 3] = a?[i] ?? (byte)255;
            }
            L.Rgba = rgba;
        }
        AssignGroupPaths();
    }

    // Grup yolu: kayitlar ALTTAN USTE siralidir; ustten alta gezince 'lsct' 1/2 = grup basligi (acar), 3 = grup sonu (kapar).
    void AssignGroupPaths()
    {
        var names = new List<string>();
        var hidden = new List<bool>();
        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            var L = Layers[i];
            if (L.SectionType == 1 || L.SectionType == 2) { names.Add(L.Name); hidden.Add(!L.Visible); continue; }
            if (L.SectionType == 3) { if (names.Count > 0) { names.RemoveAt(names.Count - 1); hidden.RemoveAt(hidden.Count - 1); } continue; }
            L.GroupPath = string.Join("/", names.ToArray(), 0, names.Count);
            if (hidden.Contains(true))
                L.Visible = false; // gizli grup icindeki katman gorunmez
        }
    }

    static bool IsLongKey(string key) => key is "LMsk" or "Lr16" or "Lr32" or "Layr" or "Mt16" or "Mt32" or "Mtrn"
        or "Alph" or "FMsk" or "lnk2" or "FEid" or "FXid" or "PxSD";

    byte[] ReadChannel(BinaryReader b, int w, int h)
    {
        int comp = U16(b);
        var plane = new byte[w * h];
        if (comp == 0)
        {
            int got = b.Read(plane, 0, plane.Length);
            if (got != plane.Length) throw new EndOfStreamException("katman verisi kisa");
            return plane;
        }
        if (comp != 1)
            throw new InvalidDataException("desteklenmeyen kanal sikistirmasi: " + comp + " (raw/RLE gerekli; ZIP yok)");
        // RLE: satir bayt sayilari (PSD u16, PSB u32), sonra PackBits satirlari.
        var rowLen = new int[h];
        for (int y = 0; y < h; y++)
            rowLen[y] = IsPsb ? (int)U32(b) : U16(b);
        for (int y = 0; y < h; y++)
        {
            var row = b.ReadBytes(rowLen[y]);
            int o = y * w, end = o + w, p = 0;
            while (p < row.Length && o < end)
            {
                int n = (sbyte)row[p++];
                if (n >= 0)
                {
                    int cnt = Math.Min(n + 1, end - o);
                    Buffer.BlockCopy(row, p, plane, o, cnt);
                    p += n + 1; o += cnt;
                }
                else if (n != -128)
                {
                    int cnt = Math.Min(1 - n, end - o);
                    byte v = row[p++];
                    for (int k = 0; k < cnt; k++) plane[o++] = v;
                }
            }
        }
        return plane;
    }
}
