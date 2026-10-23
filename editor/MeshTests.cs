#if DE_EDITOR
using System;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Mesh/3D smoke: DMSH roundtrip, MeshRenderer encode, dinamik mesh aralikli draw,
// rotasyonlu kamera matrisi, FreePerspective isin, AABB picking, FBX parser (kaynak varsa).
public static class MeshTests
{
    static int _pass, _fail;

    public static unsafe void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;

        // --- DMSH roundtrip ---
        var white = new Color(255, 255, 255, 255);
        var v = new Vertex[]
        {
            new(new Vec3(-1, -2, -3), new Vec2(0, 0), white),
            new(new Vec3(4, 5, 6), new Vec2(1, 0), white),
            new(new Vec3(0, 7, 1), new Vec2(1, 1), white),
        };
        ushort[] idx = { 0, 1, 2 };
        var blob = MeshBinary.Write(v, idx);
        Check(MeshBinary.IsMesh(blob), "DMSH magic");
        Check(MeshBinary.Read(blob, out var rv, out var ri, out var mn, out var mx), "DMSH okunur");
        Check(rv.Length == 3 && ri.Length == 3 && rv[1].position.z == 6 && rv[2].uv.y == 1 && ri[2] == 2, "DMSH icerik birebir");
        Check(mn.x == -1 && mn.y == -2 && mn.z == -3 && mx.x == 4 && mx.y == 7 && mx.z == 6, "DMSH bounds");
        var bad = (byte[])blob.Clone(); bad[^1] ^= 0; Array.Resize(ref bad, bad.Length - 1);
        Check(!MeshBinary.Read(bad, out _, out _, out _, out _), "kirpik DMSH reddedilir");
        Check(!MeshBinary.IsMesh(new byte[] { 1, 2, 3 }), "rastgele bayt DMSH degil");

        // --- Sahne: MeshRenderer encode + picking ---
        var prev = Scene.Active;
        var s = Scene.Create("mesh-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        s.ScreenWidth = 800;
        s.ScreenHeight = 600;

        var mesh = Mesh.FromArtifact(blob, "test.fbx");
        Check(mesh != null && mesh.IndexCount == 3 && mesh.VertexCount == 3 && mesh.HasBounds, "Mesh.FromArtifact");

        var go = new GameObject("m");
        var mr = go.AddComponent<MeshRenderer>();
        mr.Mesh = mesh;
        s.Update(0f);
        var q = new RenderQueue();
        q.Begin(); s.Render(q);
        Check(q.Count == 1, "MeshRenderer tek draw");
        mr.Mesh = null;
        q.Begin(); s.Render(q);
        Check(q.Count == 0, "mesh yokken draw yok");
        mr.Mesh = mesh;

        // --- Sprite: tam sayfa 0..1; atlas bolgesi trim-bilincli uvRect ---
        var pageTex = Texture.FromColor(256, 256, white);
        var full = Sprite.FromTexture(pageTex, "page");
        mr.Sprite = full;
        q.Begin(); s.Render(q);
        var uv = q.UvRectAt(0);
        Check(uv.x == 0 && uv.y == 0 && uv.z == 1 && uv.w == 1, "tam sayfa sprite: uvRect 0..1");
        var reg = Sprite.FromTexture(null, "reg");
        reg.BindRegion(pageTex, 64, 32, 100, 50, 10, 5, 120, 60);
        mr.Sprite = reg;
        q.Begin(); s.Render(q);
        uv = q.UvRectAt(0);
        // u0 = 64/256 - 10/256 = 54/256; genislik = 120/256; v0 = 32/256 - 5/256 = 27/256; yukseklik 60/256
        Check(Math.Abs(uv.x - 54f / 256f) < 1e-5f && Math.Abs(uv.z - 120f / 256f) < 1e-5f
            && Math.Abs(uv.y - 27f / 256f) < 1e-5f && Math.Abs(uv.w - 60f / 256f) < 1e-5f,
            $"atlas bolgesi: trim-bilincli uvRect ({uv.x * 256:0.#},{uv.y * 256:0.#},{uv.z * 256:0.#},{uv.w * 256:0.#})");
        mr.Sampler = SamplerType.Trilinear;
        q.Begin(); s.Render(q);
        Check(!pageTex.HasMipmaps, "atlas uyesinde Trilinear sayfaya mip istemez");
        mr.Sprite = full;
        q.Begin(); s.Render(q);
        Check(pageTex.HasMipmaps, "tam sayfa sprite + Trilinear mip ister");
        mr.Sampler = SamplerType.Linear;
        mr.Sprite = null;
        pageTex.Destroy();

        // AABB picking: -z'den +z'ye bakan isin kutuya girer; disaridan gecen girmez.
        var ray = new Ray { Origin = new Vec3(1, 3, -100), Dir = new Vec3(0, 0, 1) };
        Check(mr.HitTest(in ray, out float dist, out var pt) && Math.Abs(dist - 97f) < 0.01f && Math.Abs(pt.z + 3f) < 0.01f, "AABB isin kesisimi (giris -3)");
        var miss = new Ray { Origin = new Vec3(50, 3, -100), Dir = new Vec3(0, 0, 1) };
        Check(!mr.HitTest(in miss, out _, out _), "AABB iskalama");
        var inside = new Ray { Origin = new Vec3(1, 3, 0), Dir = new Vec3(0, 0, 1) };
        Check(mr.HitTest(in inside, out dist, out _) && dist == 0f, "isin kutu icinde: mesafe 0");

        // --- Dinamik mesh: aralikli draw ---
        var dyn = Mesh.CreateDynamic(8, 12);
        var mat = new Material { MainTexture = SpriteRenderer.White };
        var id = new Mat4(); id.Identity();
        q.Begin();
        q.DrawMeshRange(dyn, mat, in id, white, 0, 6, 0);
        Check(q.Count == 0, "yazilmamis dinamik mesh cizilmez");
        var dv = new Vertex[4];
        for (int i = 0; i < 4; i++) dv[i] = new Vertex(new Vec3(i, 0, 0), new Vec2(0, 0), white);
        ushort[] di = { 0, 1, 2, 0, 2, 3, 1, 2, 3, 0, 1, 3 };
        dyn.UpdateDynamic(dv, 4, di, 12);
        Check(dyn.IndexCount == 12 && dyn.VertexCount == 4, "UpdateDynamic sayaclar");
        q.Begin();
        q.DrawMeshRange(dyn, mat, in id, white, 0, 6, 0);
        q.DrawMeshRange(dyn, mat, in id, white, 6, 6, 0);
        q.DrawMeshRange(dyn, mat, in id, white, 6, 12, 0); // aralik disi
        Check(q.Count == 2, "iki aralik = iki item, tasan aralik atilir");
        dyn.EnsureDynamicCapacity(100, 300);
        Check(dyn.VertexCapacity >= 100 && dyn.IndexCapacity >= 300 && dyn.Vbuf != 0, "dinamik kapasite buyur");
        dyn.Destroy();

        // --- Kamera: rotasyonsuz SetPerspectiveView == SetPerspective ---
        var c1 = new Camera(); var c2 = new Camera();
        c1.SetPerspective(45f, 1.5f, 1f, 1000f, 10f, 20f, -300f);
        var world = new Mat4(); world.Identity(); world.m[12] = 10f; world.m[13] = 20f; world.m[14] = -300f;
        c2.SetPerspectiveView(45f, 1.5f, 1f, 1000f, ref world);
        bool same = true;
        for (int i = 0; i < 16; i++) same &= Math.Abs(c1.ViewProj.m[i] - c2.ViewProj.m[i]) < 1e-4f;
        Check(same, "SetPerspectiveView(rotasyonsuz) == SetPerspective");
        c1.GetWorldRect(out float ax, out float ay, out float aw, out float ah);
        c2.GetWorldRect(out float bx, out float by, out float bw, out float bh);
        Check(ax == bx && ay == by && aw == bw && ah == bh, "rect esit");
        int ver = c2.RectVersion;
        c2.SetPerspectiveView(45f, 1.5f, 1f, 1000f, ref world);
        Check(c2.RectVersion == ver, "ayni matris no-op");

        // --- FreePerspective: kamera 90 derece x ekseninde (yukaridan asagi bakar) ---
        var camGo = new GameObject("cam");
        camGo.transform.localPosition = new Vec3(0, -100, 0); // y-asagi dunyada -100 = "yukarida"
        camGo.transform.localEulerAngles = new Vec3(90, 0, 0); // Rx(+90): lokal +z -> dunya +y (y-asagi dunyada asagi bakar)
        var cc = camGo.AddComponent<CameraComponent>();
        var fp = camGo.AddComponent<FreePerspectiveProjection>();
        fp.fov = 60; fp.nearClip = 1; fp.farClip = 1000;
        s.Update(0f);
        var cam = new Camera();
        cc.ApplyTo(cam, 800, 600);
        // Kameranin tam altindaki (0,0,0) noktasi ekran merkezine duser.
        Mat4.TransformPointW(ref cam.ViewProj, new Vec3(0, 0, 0), out var ndc);
        Check(Math.Abs(ndc.x) < 1e-3f && Math.Abs(ndc.y) < 1e-3f && ndc.z > -1f && ndc.z < 1f, $"rotasyonlu kamera: altindaki nokta merkezde ve onde ({ndc.x:0.###},{ndc.y:0.###},{ndc.z:0.###})");
        // Isin: viewport merkezi -> dunyada +y yonune (kameradan asagi).
        Check(cc.ScreenPointToRay(400, 300, 800, 600, out var r) && r.Dir.y > 0.999f && Math.Abs(r.Origin.x) < 1e-2f,
            $"FreePerspective merkez isini kamera bakisinda ({r.Dir.x:0.##},{r.Dir.y:0.##},{r.Dir.z:0.##})");
        // Kenar noktasi: ekranin sagina dusen isin +x'e sapar.
        cc.ScreenPointToRay(800, 300, 800, 600, out var rr);
        Check(rr.Dir.x > 0.3f, "sag kenar isini +x'e sapar");

        // fovAxis Horizontal: yatay 60 derece, aspect 2 -> dikey ~33.4 derece.
        var pp = new PerspectiveProjection { fov = 60, fovAxis = FovAxis.Horizontal };
        var c3 = new Camera();
        pp.Apply(c3, new Vec3(0, 0, -100), 200, 100);
        c3.GetWorldRect(out _, out _, out float hw, out float hh);
        Check(Math.Abs(hw - 2f * 100f * MathF.Tan(30f * MathF.PI / 180f)) < 0.05f, $"fovAxis Horizontal: genislik yatay fov'dan ({hw:0.##})");

        // --- FBX parser (kaynak repo disinda; varsa) ---
        string fbx = @"C:\Work\digitoygames\ginrummyextra.unity\Assets\GinRummyExtra\Table\3DModel\TableModel.fbx";
        if (File.Exists(fbx))
        {
            try
            {
                var doc = FbxDocument.Parse(File.ReadAllBytes(fbx));
                var meshes = FbxGeometry.Extract(doc);
                Check(doc.Version == 7400 && meshes.Count == 1 && meshes[0].TriangleCount == 3146, $"FBX TableModel: {meshes.Count} mesh, {(meshes.Count > 0 ? meshes[0].TriangleCount : 0)} ucgen");
            }
            catch (Exception e) { Check(false, "FBX parse: " + e.Message); }
        }
        try { FbxDocument.Parse(System.Text.Encoding.ASCII.GetBytes("; FBX 7.4.0 project file\nFBXHeaderExtension: {")); Check(false, "ASCII FBX reddedilmeli"); }
        catch (InvalidDataException) { Check(true, "ASCII FBX reddedilir"); }

        GameObject.Destroy(go);
        GameObject.Destroy(camGo);
        s.Update(0f);
        mesh.Destroy();
        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[mesh] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[mesh] FAIL: {name}"); }
    }
}
#endif
