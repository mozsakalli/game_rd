using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Digitoy.Mcp
{
    // Game View goruntusu. Play'de: gizli bir MonoBehaviour end-of-frame'de back buffer'i okur (overlay UI dahil);
    // edit modunda: kameralar RT'ye render edilir (Screen Space Overlay canvas'lar dahil DEGIL).
    public static class Capture
    {
        public sealed class Request
        {
            public float Scale = 1f;
            public int MaxSize = 1024;   // uzun kenar siniri (0 = sinirsiz); AI'in gorsel token maliyeti piksel sayisiyla olcekleniyor
            public bool Jpg = true;
            public int Quality = 80;
            public Rect? Region;         // ekran koordinati (sol-ust, y asagi)
            public string Path;
            public bool Done;
            public JNode Result;
            public int AtFrame;
            public string Error;
        }

        static int _shotSeq;

        public static string DefaultPath(bool jpg)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "McpScreenshots");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"shot_{++_shotSeq:D4}.{(jpg ? "jpg" : "png")}");
        }

        public static Request Begin(float scale, string path = null, int maxSize = 1024, string format = "jpg", Rect? region = null)
        {
            bool jpg = format != "png";
            var req = new Request { Scale = scale, MaxSize = maxSize, Jpg = jpg, Region = region, Path = string.IsNullOrEmpty(path) ? DefaultPath(jpg) : Path.GetFullPath(path) };
            if (Application.isPlaying)
                CaptureAgent.Enqueue(req);
            else
                RenderCameras(req);
            return req;
        }

        public static void Finish(Request req, Texture2D tex, int screenW, int screenH, string mode)
        {
            try
            {
                var bytes = req.Jpg ? ImageConversion.EncodeToJPG(tex, req.Quality) : ImageConversion.EncodeToPNG(tex);
                Directory.CreateDirectory(Path.GetDirectoryName(req.Path));
                File.WriteAllBytes(req.Path, bytes);
                var r = JNode.Map();
                r.Add("path", req.Path);
                r.Add("w", tex.width);
                r.Add("h", tex.height);
                r.Add("screenW", screenW);
                r.Add("screenH", screenH);
                var reg = req.Region ?? new Rect(0, 0, screenW, screenH);
                if (req.Region.HasValue) r.Add("region", $"{JNode.F(reg.x)} {JNode.F(reg.y)} {JNode.F(reg.width)} {JNode.F(reg.height)}");
                // Tiklama koordinati = regionX + px * toScreen (goruntu kucultulmus olabilir).
                r.Add("toScreen", reg.width / tex.width);
                r.Add("bytes", bytes.Length);
                r.Add("playing", Application.isPlaying);
                r.Add("mode", mode);
                if (mode == "camera") r.Add("note", "edit modu: kamera render'i; Screen Space Overlay UI dahil degil — UI icin play.start/sim.run");
                req.Result = r;
            }
            catch (Exception ex) { req.Error = ex.Message; }
            finally
            {
                req.AtFrame = Time.frameCount;
                req.Done = true;
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // Kaynak RT/ekrani (istenirse bolgesini) olcekleyerek Texture2D'ye okur. Olcek = scale * maxSize siniri.
        public static Texture2D Read(RenderTexture src, int w, int h, Request req)
        {
            var reg = req.Region ?? new Rect(0, 0, w, h);
            reg.x = Mathf.Clamp(reg.x, 0, w - 1); reg.y = Mathf.Clamp(reg.y, 0, h - 1);
            reg.width = Mathf.Clamp(reg.width, 1, w - reg.x); reg.height = Mathf.Clamp(reg.height, 1, h - reg.y);
            float eff = req.Scale;
            if (req.MaxSize > 0)
            {
                float longEdge = Mathf.Max(reg.width, reg.height) * eff;
                if (longEdge > req.MaxSize) eff *= req.MaxSize / longEdge;
            }
            int tw = Math.Max(1, Mathf.RoundToInt(reg.width * eff)), th = Math.Max(1, Mathf.RoundToInt(reg.height * eff));
            RenderTexture owned = null;
            var prev = RenderTexture.active;
            try
            {
                if (src == null)
                {
                    // Back buffer: birebir oku, RT'ye tasi (blit ile bolge/olcek).
                    var full = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    RenderTexture.active = null;
                    full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    full.Apply();
                    bool identity = tw == w && th == h && !req.Region.HasValue;
                    if (identity) return full;
                    owned = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(full, owned);
                    UnityEngine.Object.DestroyImmediate(full);
                    src = owned;
                }
                var dst = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                // Bolge: sol-ust y-asagi → RT uv (sol-alt y-yukari).
                var uvScale = new Vector2(reg.width / w, reg.height / h);
                var uvOffset = new Vector2(reg.x / w, (h - reg.y - reg.height) / h);
                Graphics.Blit(src, dst, uvScale, uvOffset);
                var tex = new Texture2D(tw, th, TextureFormat.RGB24, false);
                RenderTexture.active = dst;
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                tex.Apply();
                RenderTexture.ReleaseTemporary(dst);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                if (owned != null) RenderTexture.ReleaseTemporary(owned);
            }
        }

        static void RenderCameras(Request req)
        {
            Commands.GetViewSize(out int w, out int h);
            var cams = new List<Camera>(Camera.allCameras);
            if (cams.Count == 0)
            {
                foreach (var go in Commands.AllGos())
                    foreach (var c in go.GetComponents<Camera>()) cams.Add(c);
            }
            if (cams.Count == 0) { req.Error = "sahnede kamera yok"; req.Done = true; return; }
            cams.Sort((a, b) => a.depth.CompareTo(b.depth));
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                foreach (var cam in cams)
                {
                    if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                    var prevT = cam.targetTexture;
                    cam.targetTexture = rt;
                    try { cam.Render(); } finally { cam.targetTexture = prevT; }
                }
                var tex = Read(rt, w, h, req);
                Finish(req, tex, w, h, "camera");
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }
    }

    // Play'de yasayan gizli yakalayici: istekleri end-of-frame'de okur.
    public sealed class CaptureAgent : MonoBehaviour
    {
        static CaptureAgent _inst;
        readonly Queue<Capture.Request> _queue = new Queue<Capture.Request>();
        bool _running;

        public static void Enqueue(Capture.Request req)
        {
            if (_inst == null)
            {
                var go = new GameObject("__mcp_capture") { hideFlags = HideFlags.HideAndDontSave };
                _inst = go.AddComponent<CaptureAgent>();
            }
            _inst._queue.Enqueue(req);
            if (!_inst._running) _inst.StartCoroutine(_inst.Loop());
        }

        IEnumerator Loop()
        {
            _running = true;
            while (_queue.Count > 0)
            {
                yield return new WaitForEndOfFrame();
                var req = _queue.Dequeue();
                int w = Screen.width, h = Screen.height;
                Texture2D tex = null;
                try { tex = Capture.Read(null, w, h, req); }
                catch (Exception ex) { req.Error = ex.Message; req.Done = true; req.AtFrame = Time.frameCount; continue; }
                Capture.Finish(req, tex, w, h, "frame");
            }
            _running = false;
        }
    }

    public static partial class Commands
    {
        public static void GetViewSize(out int w, out int h)
        {
            PlayModeWindow.GetRenderingResolution(out uint uw, out uint uh);
            w = (int)uw; h = (int)uh;
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
        }

        sealed class Device { public string Name; public int W, H; public float Ratio; public Device(string n, int w, int h, float r) { Name = n; W = w; H = h; Ratio = r; } }

        static readonly Device[] Devices =
        {
            new Device("iPhone SE", 750, 1334, 2f),
            new Device("iPhone 8 Plus", 1080, 1920, 3f),
            new Device("iPhone 13", 1170, 2532, 3f),
            new Device("iPhone 15 Pro Max", 1290, 2796, 3f),
            new Device("iPad 10", 1640, 2360, 2f),
            new Device("iPad Pro 12.9", 2048, 2732, 2f),
            new Device("Pixel 4a", 1080, 2340, 2.75f),
            new Device("Pixel 7", 1080, 2400, 2.625f),
            new Device("Galaxy S21", 1080, 2400, 3f),
            new Device("Galaxy Tab S7", 1600, 2560, 2f),
            new Device("HD", 1280, 720, 1f),
            new Device("FullHD", 1920, 1080, 1f),
            new Device("Square", 1080, 1080, 1f),
        };

        [RpcCommand("view.get", "Game View render cozunurlugu (screenshot/input koordinat uzayi)")]
        static JNode ViewGet()
        {
            GetViewSize(out int w, out int h);
            var r = JNode.Map();
            r.Add("width", w);
            r.Add("height", h);
            r.Add("orientation", w >= h ? "landscape" : "portrait");
            if (_device != null) r.Add("device", _device);
            return r;
        }

        static string _device;

        [RpcCommand("view.set", "Game View'a sabit cozunurluk dayatir (width x height)")]
        static JNode ViewSet(int width, int height)
        {
            if (width <= 0 || width > 8192 || height <= 0 || height > 8192) throw new RpcError("width/height 1..8192 olmali");
            FocusGameView();
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, $"MCP {width}x{height}");
            _device = null;
            return ViewGet();
        }

        [RpcCommand("view.device", "Cihaz preset'i uygular (name: view.devices). orientation: portrait|landscape (bos = preset'in dogal yonu)")]
        static JNode ViewDevice(string name, string orientation = "")
        {
            Device dv = null;
            foreach (var d in Devices) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) dv = d;
            if (dv == null) throw new RpcError($"bilinmeyen cihaz: {name} (view.devices)");
            int w = dv.W, h = dv.H;
            if (orientation == "landscape" && w < h || orientation == "portrait" && w > h) { int t = w; w = h; h = t; }
            else if (orientation.Length > 0 && orientation != "landscape" && orientation != "portrait") throw new RpcError("orientation portrait|landscape olmali");
            FocusGameView();
            PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
            PlayModeWindow.SetCustomRenderingResolution((uint)w, (uint)h, dv.Name);
            _device = dv.Name;
            var r = ViewGet();
            r.Add("pixelRatio", dv.Ratio);
            return r;
        }

        [RpcCommand("view.devices", "Kullanilabilir cihaz preset'leri (fiziksel piksel)")]
        static JNode ViewDevices()
        {
            var seq = JNode.Seq();
            foreach (var d in Devices)
                seq.Push(JNode.Map().Add("name", d.Name).Add("width", d.W).Add("height", d.H).Add("pixelRatio", d.Ratio));
            return JNode.Map().Add("devices", seq);
        }

        [RpcCommand("render.screenshot", "Game View goruntusu (play'de tam frame; edit'te kamera render'i). VARSAYILAN KUCULTULMUS: maxSize=1024 uzun kenar, jpg q80 (hizli yorum). Tiklama koordinati = region.x + px * toScreen (sonuctaki carpan). scale: carpan (maxSize ile sinirli); maxSize=0 sinirsiz; format png|jpg; region 'x y w h' (ekran koordinati) → yalniz o bolge, detay icin. path bos = Library/McpScreenshots/shot_N")]
        static JNode RenderScreenshot(string path = "", float scale = 1f, int maxSize = 1024, string format = "jpg", string region = "")
        {
            if (scale <= 0f || scale > 4f) throw new RpcError("scale 0..4 araliginda olmali");
            if (format != "jpg" && format != "png") throw new RpcError("format jpg|png olmali");
            Rect? reg = null;
            if (region.Length > 0) { var f = ParseFloats(region, 4, "region"); reg = new Rect(f[0], f[1], f[2], f[3]); }
            FocusGameView();
            var req = Capture.Begin(scale, path.Length > 0 ? path : null, maxSize, format, reg);
            if (req.Done) return CaptureResult(req);
            bool wasPaused = EditorApplication.isPaused;
            Advance();
            int waited = 0;
            RpcHost.Defer(() =>
            {
                if (req.Done)
                {
                    if (wasPaused) EditorApplication.isPaused = true;
                    return CaptureResult(req);
                }
                if (!Application.isPlaying) throw new RpcError("play bitti, kare alinamadi");
                if (++waited > 600) throw new RpcError("kare alinamadi (Game View gorunur mu? oyun render ediyor mu?)");
                return null;
            });
            return null;
        }

        static JNode CaptureResult(Capture.Request req)
        {
            if (req.Error != null) throw new RpcError("screenshot: " + req.Error);
            return req.Result;
        }
    }
}
