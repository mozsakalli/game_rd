using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// FBX import ayarlari (.meta "importer:" blogu; Inspector duzenler).
[Serializable]
public sealed class FbxImportSettings
{
    // Dosya birimi (cm) -> dunya birimi carpani. 0.01 = metre (Unity useFileScale paritesi).
    public float scale = 0.01f;
    // (pos,uv) esit koseleri tekillestir (16-bit indeks butcesi; her zaman onerilir).
    public bool weld = true;
    // UV v eksenini cevir (kaynak dokusu ters pisirilmisse).
    public bool flipV = false;
}

// FBX (binary 7.x) -> DMSH: dosyadaki tum Mesh modelleri hiyerarsi donusumleriyle dunyaya
// tasinip TEK mesh'te birlestirilir. Y ekseni cevrilir (FBX/Unity y-yukari -> motor y-asagi)
// ve ucgen sarimi ters cevrilir (CullMode.Back dogru calissin). Normal/materyal/animasyon
// okunmaz: motor isiksiz, doku pismis. Runtime FBX'i hic gormez (yalniz DMSH).
[AssetImporter(".fbx", Version = 1, Settings = typeof(FbxImportSettings))]
public sealed class FbxImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
    {
        var s = ctx.Settings as FbxImportSettings ?? new FbxImportSettings();
        FbxDocument doc;
        List<FbxTriMesh> meshes;
        try
        {
            doc = FbxDocument.Parse(File.ReadAllBytes(ctx.SourcePath));
            meshes = FbxGeometry.Extract(doc);
        }
        catch (Exception e)
        {
            ctx.Fail("FBX okunamadi: " + e.Message);
            return;
        }
        if (meshes.Count == 0)
        {
            ctx.Fail("FBX'te mesh yok");
            return;
        }

        float k = s.scale * (float)doc.UnitScaleFactor;
        var white = new Color(255, 255, 255, 255);
        var verts = new List<Vertex>();
        var indices = new List<ushort>();
        var weld = s.weld ? new Dictionary<VertexKey, int>() : null;
        var names = new List<string>();
        foreach (var m in meshes)
        {
            names.Add(m.ModelName);
            for (int t = 0; t < m.TriangleCount; t++)
            {
                // Y cevrimi aynadir: sarim yonu de cevrilir (0,2,1).
                int c0 = t * 3, c1 = t * 3 + 2, c2 = t * 3 + 1;
                if (!Add(c0) || !Add(c1) || !Add(c2))
                {
                    ctx.Fail("vertex sayisi 16-bit indeks sinirini asiyor (>65536); modeli sadelestir");
                    return;
                }
            }

            bool Add(int corner)
            {
                var pos = new Vec3(
                    (float)(m.Positions[corner * 3] * k),
                    (float)(-m.Positions[corner * 3 + 1] * k),
                    (float)(m.Positions[corner * 3 + 2] * k));
                float u = m.Uvs[corner * 2];
                float v = m.Uvs[corner * 2 + 1];
                if (s.flipV)
                    v = 1f - v;
                var key = new VertexKey(pos, u, v);
                if (weld != null && weld.TryGetValue(key, out int existing))
                {
                    indices.Add((ushort)existing);
                    return true;
                }
                if (verts.Count > ushort.MaxValue)
                    return false;
                int idx = verts.Count;
                verts.Add(new Vertex(pos, new Vec2(u, v), white));
                weld?.Add(key, idx);
                indices.Add((ushort)idx);
                return true;
            }
        }

        var va = verts.ToArray();
        var blob = MeshBinary.Write(va, indices.ToArray());
        ctx.AddArtifact("main", blob);
        MeshBinary.ComputeBounds(va, out var mn, out var mx);
        EditorLog.Info($"[fbx] {ctx.AssetPath}: {meshes.Count} model ({string.Join(",", names)}), "
            + $"{indices.Count / 3} ucgen, {va.Length} vertex, bounds ({mn.x:0.##},{mn.y:0.##},{mn.z:0.##})..({mx.x:0.##},{mx.y:0.##},{mx.z:0.##}), {blob.Length / 1024} KB");
    }

    readonly struct VertexKey : IEquatable<VertexKey>
    {
        readonly float x, y, z, u, v;
        public VertexKey(Vec3 p, float u, float v) { x = p.x; y = p.y; z = p.z; this.u = u; this.v = v; }
        public bool Equals(VertexKey o) => x == o.x && y == o.y && z == o.z && u == o.u && v == o.v;
        public override bool Equals(object obj) => obj is VertexKey o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(x, y, z, u, v);
    }
}
