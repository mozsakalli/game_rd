using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DigitoyEditor;

// Binary FBX (Kaydara 7.x) okuyucu — YALNIZ geometri alt kumesi. Motor bagimsiz:
// node agaci + tipli property'ler, Geometry (Vertices/PolygonVertexIndex/UV),
// Model yerel donusumu (Lcl T/R/S, PreRotation, pivot/offset, Geometric*),
// Connections (OO hiyerarsisi), GlobalSettings.UnitScaleFactor. Normal, materyal,
// animasyon, deformer, ASCII FBX yok sayilir / reddedilir. Cikti FbxImporter'da
// motorun DMSH mesh'ine cevrilir — runtime FBX'i hic bilmez.
public sealed class FbxNode
{
    public string Name;
    public object[] Properties;
    public readonly List<FbxNode> Children = new();

    public FbxNode Find(string name)
    {
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Name == name)
                return Children[i];
        return null;
    }

    public IEnumerable<FbxNode> FindAll(string name)
    {
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Name == name)
                yield return Children[i];
    }

    public long Long(int i) => Properties[i] switch
    {
        long l => l, int n => n, short s => s, _ => 0
    };

    public double Double(int i) => Properties[i] switch
    {
        double d => d, float f => f, long l => l, int n => n, short s => s, _ => 0
    };

    public string String(int i) => i < Properties.Length ? Properties[i] as string : null;
}

public sealed class FbxDocument
{
    public int Version;
    public FbxNode Root;

    // Dosya birimi -> cm carpani (FBX ic birimi cm; Unity useFileScale: * 0.01 ile metre).
    public double UnitScaleFactor = 1.0;

    public static FbxDocument Parse(byte[] data)
    {
        if (data.Length < 27 || Encoding.ASCII.GetString(data, 0, 18) != "Kaydara FBX Binary")
            throw new InvalidDataException("binary FBX degil (ASCII FBX desteklenmiyor)");
        var doc = new FbxDocument { Version = BitConverter.ToInt32(data, 23), Root = new FbxNode { Name = "" } };
        bool wide = doc.Version >= 7500; // 7.5+: offset/sayac alanlari 64-bit
        int p = 27;
        while (p < data.Length)
        {
            var n = ReadNode(data, ref p, wide);
            if (n == null)
                break;
            doc.Root.Children.Add(n);
        }
        var gs = doc.Root.Find("GlobalSettings")?.Find("Properties70");
        if (gs != null)
        {
            var usf = FindP(gs, "UnitScaleFactor");
            if (usf != null)
                doc.UnitScaleFactor = usf.Double(4);
        }
        return doc;
    }

    static FbxNode ReadNode(byte[] d, ref int p, bool wide)
    {
        long end, numProps, propLen;
        if (wide)
        {
            end = BitConverter.ToInt64(d, p); numProps = BitConverter.ToInt64(d, p + 8); propLen = BitConverter.ToInt64(d, p + 16); p += 24;
        }
        else
        {
            end = BitConverter.ToUInt32(d, p); numProps = BitConverter.ToUInt32(d, p + 4); propLen = BitConverter.ToUInt32(d, p + 8); p += 12;
        }
        int nameLen = d[p++];
        if (end == 0)
            return null; // null kayit (cocuk listesi sonu)
        var node = new FbxNode { Name = Encoding.ASCII.GetString(d, p, nameLen) };
        p += nameLen;
        node.Properties = new object[numProps];
        for (int i = 0; i < numProps; i++)
            node.Properties[i] = ReadProperty(d, ref p);
        while (p < end)
        {
            var c = ReadNode(d, ref p, wide);
            if (c == null)
                break;
            node.Children.Add(c);
        }
        p = (int)end;
        return node;
    }

    static object ReadProperty(byte[] d, ref int p)
    {
        char t = (char)d[p++];
        switch (t)
        {
            case 'Y': { short v = BitConverter.ToInt16(d, p); p += 2; return v; }
            case 'C': { bool v = d[p] != 0; p += 1; return v; }
            case 'I': { int v = BitConverter.ToInt32(d, p); p += 4; return v; }
            case 'F': { float v = BitConverter.ToSingle(d, p); p += 4; return v; }
            case 'D': { double v = BitConverter.ToDouble(d, p); p += 8; return v; }
            case 'L': { long v = BitConverter.ToInt64(d, p); p += 8; return v; }
            case 'S':
            case 'R':
            {
                int len = BitConverter.ToInt32(d, p); p += 4;
                object v = t == 'S' ? Encoding.UTF8.GetString(d, p, len) : d.AsSpan(p, len).ToArray();
                p += len;
                return v;
            }
            case 'f': case 'd': case 'l': case 'i': case 'b':
            {
                int count = BitConverter.ToInt32(d, p);
                int encoding = BitConverter.ToInt32(d, p + 4);
                int compLen = BitConverter.ToInt32(d, p + 8);
                p += 12;
                int elem = t switch { 'f' => 4, 'd' => 8, 'l' => 8, 'i' => 4, _ => 1 };
                byte[] raw;
                if (encoding == 0)
                    raw = d.AsSpan(p, count * elem).ToArray();
                else
                {
                    raw = new byte[count * elem];
                    using var ms = new MemoryStream(d, p, compLen);
                    using var z = new ZLibStream(ms, CompressionMode.Decompress);
                    int got = 0;
                    while (got < raw.Length)
                    {
                        int r = z.Read(raw, got, raw.Length - got);
                        if (r <= 0)
                            throw new InvalidDataException("FBX zlib dizisi eksik");
                        got += r;
                    }
                }
                p += compLen;
                switch (t)
                {
                    case 'f': { var a = new float[count]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length); return a; }
                    case 'd': { var a = new double[count]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length); return a; }
                    case 'l': { var a = new long[count]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length); return a; }
                    case 'i': { var a = new int[count]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length); return a; }
                    default: { var a = new bool[count]; for (int i = 0; i < count; i++) a[i] = raw[i] != 0; return a; }
                }
            }
            default:
                throw new InvalidDataException("FBX bilinmeyen property tipi '" + t + "'");
        }
    }

    // Properties70 icinde "P" kaydi: [0]=ad, [1]=tip, [2]=etiket, [3]=flags, [4..]=deger.
    internal static FbxNode FindP(FbxNode props70, string name)
    {
        if (props70 == null)
            return null;
        foreach (var p in props70.FindAll("P"))
            if (p.String(0) == name)
                return p;
        return null;
    }

    internal static FbxVec3 Vec3P(FbxNode props70, string name, double dx, double dy, double dz)
    {
        var p = FindP(props70, name);
        return p == null || p.Properties.Length < 7 ? new FbxVec3(dx, dy, dz) : new FbxVec3(p.Double(4), p.Double(5), p.Double(6));
    }
}

public struct FbxVec3
{
    public double X, Y, Z;
    public FbxVec3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

// 4x4 kolon-major double matris (donusum bilesimi icin; hassasiyet dosya kadar).
public struct FbxMat4
{
    public double[] M; // 16, kolon-major

    public static FbxMat4 Identity()
    {
        var m = new FbxMat4 { M = new double[16] };
        m.M[0] = m.M[5] = m.M[10] = m.M[15] = 1;
        return m;
    }

    public static FbxMat4 Translation(FbxVec3 t)
    {
        var m = Identity();
        m.M[12] = t.X; m.M[13] = t.Y; m.M[14] = t.Z;
        return m;
    }

    public static FbxMat4 Scale(FbxVec3 s)
    {
        var m = Identity();
        m.M[0] = s.X; m.M[5] = s.Y; m.M[10] = s.Z;
        return m;
    }

    // Euler derece, FBX varsayilan sira XYZ: R = Rz * Ry * Rx (X once uygulanir).
    public static FbxMat4 RotationXYZ(FbxVec3 e)
    {
        double rx = e.X * Math.PI / 180, ry = e.Y * Math.PI / 180, rz = e.Z * Math.PI / 180;
        var x = Identity(); x.M[5] = Math.Cos(rx); x.M[6] = Math.Sin(rx); x.M[9] = -Math.Sin(rx); x.M[10] = Math.Cos(rx);
        var y = Identity(); y.M[0] = Math.Cos(ry); y.M[2] = -Math.Sin(ry); y.M[8] = Math.Sin(ry); y.M[10] = Math.Cos(ry);
        var z = Identity(); z.M[0] = Math.Cos(rz); z.M[1] = Math.Sin(rz); z.M[4] = -Math.Sin(rz); z.M[5] = Math.Cos(rz);
        return Mul(Mul(z, y), x);
    }

    public static FbxMat4 Mul(FbxMat4 a, FbxMat4 b)
    {
        var r = new FbxMat4 { M = new double[16] };
        for (int c = 0; c < 4; c++)
            for (int rw = 0; rw < 4; rw++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++)
                    s += a.M[k * 4 + rw] * b.M[c * 4 + k];
                r.M[c * 4 + rw] = s;
            }
        return r;
    }

    public FbxVec3 TransformPoint(FbxVec3 p) => new(
        M[0] * p.X + M[4] * p.Y + M[8] * p.Z + M[12],
        M[1] * p.X + M[5] * p.Y + M[9] * p.Z + M[13],
        M[2] * p.X + M[6] * p.Y + M[10] * p.Z + M[14]);

    public double Determinant3() =>
        M[0] * (M[5] * M[10] - M[9] * M[6]) - M[4] * (M[1] * M[10] - M[9] * M[2]) + M[8] * (M[1] * M[6] - M[5] * M[2]);
}

// Uc'genlenmis, dunya uzayina tasinmis tek Model geometrisi. Kose basina pozisyon + uv
// (indeks YOK: kaynaklama importer'da). UV yoksa (0,0).
public sealed class FbxTriMesh
{
    public string ModelName;
    public double[] Positions; // 3 * corner
    public float[] Uvs;        // 2 * corner
    public int CornerCount => Positions.Length / 3;
    public int TriangleCount => CornerCount / 3;
}

public static class FbxGeometry
{
    // Dosyadaki tum Mesh tipi Model'leri (hiyerarsi donusumu uygulanmis, cm biriminde) dondurur.
    public static List<FbxTriMesh> Extract(FbxDocument doc)
    {
        var result = new List<FbxTriMesh>();
        var objects = doc.Root.Find("Objects");
        var connections = doc.Root.Find("Connections");
        if (objects == null)
            return result;

        var geometries = new Dictionary<long, FbxNode>();
        var models = new Dictionary<long, FbxNode>();
        foreach (var n in objects.Children)
        {
            if (n.Name == "Geometry" && n.String(2) == "Mesh")
                geometries[n.Long(0)] = n;
            else if (n.Name == "Model")
                models[n.Long(0)] = n;
        }

        // OO baglantilari: child -> parent (0 = sahne koku).
        var parentOf = new Dictionary<long, long>();
        var geomOfModel = new Dictionary<long, long>();
        if (connections != null)
            foreach (var c in connections.FindAll("C"))
            {
                if (c.String(0) != "OO")
                    continue;
                long child = c.Long(1), parent = c.Long(2);
                if (geometries.ContainsKey(child) && models.ContainsKey(parent))
                    geomOfModel[parent] = child;
                else if (models.ContainsKey(child))
                    parentOf[child] = parent;
            }

        var worldCache = new Dictionary<long, FbxMat4>();
        foreach (var kv in geomOfModel)
        {
            var model = models[kv.Key];
            var geom = geometries[kv.Value];
            var world = WorldOf(kv.Key, models, parentOf, worldCache);
            var geoTx = GeometricTransform(model.Find("Properties70"));
            var tri = Triangulate(geom, FbxMat4.Mul(world, geoTx));
            if (tri == null)
                continue;
            tri.ModelName = ModelName(model);
            result.Add(tri);
        }
        return result;
    }

    static string ModelName(FbxNode model)
    {
        var s = model.String(1) ?? "";
        int i = s.IndexOf("\0\u0001", StringComparison.Ordinal); // "Ad\0\u0001Model"
        return i >= 0 ? s.Substring(0, i) : s;
    }

    static FbxMat4 WorldOf(long id, Dictionary<long, FbxNode> models, Dictionary<long, long> parentOf, Dictionary<long, FbxMat4> cache)
    {
        if (cache.TryGetValue(id, out var w))
            return w;
        var local = LocalTransform(models[id].Find("Properties70"));
        w = parentOf.TryGetValue(id, out var pid) && models.ContainsKey(pid)
            ? FbxMat4.Mul(WorldOf(pid, models, parentOf, cache), local)
            : local;
        cache[id] = w;
        return w;
    }

    // FBX SDK yerel donusum: T * Roff * Rp * Rpre * R * Rpost^-1 * Rp^-1 * Soff * Sp * S * Sp^-1.
    static FbxMat4 LocalTransform(FbxNode p70)
    {
        var t = FbxDocument.Vec3P(p70, "Lcl Translation", 0, 0, 0);
        var r = FbxDocument.Vec3P(p70, "Lcl Rotation", 0, 0, 0);
        var s = FbxDocument.Vec3P(p70, "Lcl Scaling", 1, 1, 1);
        var roff = FbxDocument.Vec3P(p70, "RotationOffset", 0, 0, 0);
        var rp = FbxDocument.Vec3P(p70, "RotationPivot", 0, 0, 0);
        var rpre = FbxDocument.Vec3P(p70, "PreRotation", 0, 0, 0);
        var rpost = FbxDocument.Vec3P(p70, "PostRotation", 0, 0, 0);
        var soff = FbxDocument.Vec3P(p70, "ScalingOffset", 0, 0, 0);
        var sp = FbxDocument.Vec3P(p70, "ScalingPivot", 0, 0, 0);

        var m = FbxMat4.Translation(t);
        m = FbxMat4.Mul(m, FbxMat4.Translation(roff));
        m = FbxMat4.Mul(m, FbxMat4.Translation(rp));
        m = FbxMat4.Mul(m, FbxMat4.RotationXYZ(rpre));
        m = FbxMat4.Mul(m, FbxMat4.RotationXYZ(r));
        m = FbxMat4.Mul(m, FbxMat4.RotationXYZ(new FbxVec3(-rpost.X, -rpost.Y, -rpost.Z)));
        m = FbxMat4.Mul(m, FbxMat4.Translation(new FbxVec3(-rp.X, -rp.Y, -rp.Z)));
        m = FbxMat4.Mul(m, FbxMat4.Translation(soff));
        m = FbxMat4.Mul(m, FbxMat4.Translation(sp));
        m = FbxMat4.Mul(m, FbxMat4.Scale(s));
        m = FbxMat4.Mul(m, FbxMat4.Translation(new FbxVec3(-sp.X, -sp.Y, -sp.Z)));
        return m;
    }

    // Yalniz bu node'un geometrisine uygulanan (miras almayan) donusum.
    static FbxMat4 GeometricTransform(FbxNode p70)
    {
        var t = FbxDocument.Vec3P(p70, "GeometricTranslation", 0, 0, 0);
        var r = FbxDocument.Vec3P(p70, "GeometricRotation", 0, 0, 0);
        var s = FbxDocument.Vec3P(p70, "GeometricScaling", 1, 1, 1);
        return FbxMat4.Mul(FbxMat4.Mul(FbxMat4.Translation(t), FbxMat4.RotationXYZ(r)), FbxMat4.Scale(s));
    }

    static FbxTriMesh Triangulate(FbxNode geom, FbxMat4 world)
    {
        var verts = geom.Find("Vertices")?.Properties[0] as double[];
        var polyIdx = geom.Find("PolygonVertexIndex")?.Properties[0] as int[];
        if (verts == null || polyIdx == null || polyIdx.Length < 3)
            return null;

        // UV katmani 0: ByPolygonVertex (Direct / IndexToDirect) ya da ByControlPoint.
        double[] uv = null; int[] uvIndex = null; string uvMapping = null, uvRef = null;
        var uvLayer = geom.Find("LayerElementUV");
        if (uvLayer != null)
        {
            uv = uvLayer.Find("UV")?.Properties[0] as double[];
            uvIndex = uvLayer.Find("UVIndex")?.Properties[0] as int[];
            uvMapping = uvLayer.Find("MappingInformationType")?.String(0);
            uvRef = uvLayer.Find("ReferenceInformationType")?.String(0);
        }

        // Poligonlari fan ile uc'genle; polygon-vertex indeksi (corner) uv eslemesinde korunur.
        var corners = new List<(int cp, int pv)>(polyIdx.Length * 2);
        int start = 0;
        for (int i = 0; i < polyIdx.Length; i++)
        {
            if (polyIdx[i] >= 0)
                continue;
            int end = i; // son kose: ~index
            int n = end - start + 1;
            for (int k = 1; k + 1 <= n - 1; k++)
            {
                corners.Add((Cp(polyIdx[start]), start));
                corners.Add((Cp(polyIdx[start + k]), start + k));
                corners.Add((Cp(polyIdx[start + k + 1]), start + k + 1));
            }
            start = i + 1;
        }

        var tri = new FbxTriMesh { Positions = new double[corners.Count * 3], Uvs = new float[corners.Count * 2] };
        // Negatif determinant (ayna olcegi) sarim yonunu cevirir; dunyada tutarli kalsin.
        bool flip = world.Determinant3() < 0;
        for (int c = 0; c < corners.Count; c++)
        {
            int dst = flip ? (c / 3) * 3 + (2 - c % 3) : c;
            var (cp, pv) = corners[c];
            var p = world.TransformPoint(new FbxVec3(verts[cp * 3], verts[cp * 3 + 1], verts[cp * 3 + 2]));
            tri.Positions[dst * 3] = p.X; tri.Positions[dst * 3 + 1] = p.Y; tri.Positions[dst * 3 + 2] = p.Z;
            if (uv != null)
            {
                int ui = uvMapping == "ByControlPoint" ? cp : pv;
                if (uvRef == "IndexToDirect" && uvIndex != null && ui < uvIndex.Length)
                    ui = uvIndex[ui];
                if (ui >= 0 && ui * 2 + 1 < uv.Length)
                {
                    tri.Uvs[dst * 2] = (float)uv[ui * 2];
                    tri.Uvs[dst * 2 + 1] = (float)uv[ui * 2 + 1];
                }
            }
        }
        return tri;
    }

    static int Cp(int polygonVertexIndex) => polygonVertexIndex < 0 ? ~polygonVertexIndex : polygonVertexIndex;
}
