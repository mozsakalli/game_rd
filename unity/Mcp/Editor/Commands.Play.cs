using System;
using UnityEditor;
using UnityEngine;

namespace Digitoy.Mcp
{
    // Play modu + deterministik frame adimlama. Unity'de play'e giris (domain reload aciksa) tum
    // statikleri ve TCP baglantisini sifirlar: bu durumda komut "starting" ile hemen doner, kopru
    // yeniden baglanir ve ajan komutu tekrar cagirir. Enter Play Mode Options'ta domain reload
    // kapaliysa play'e giris beklenir ve komut tek seferde tamamlanir (onerilen).
    public static partial class Commands
    {
        public static bool DomainReloadOnPlay =>
            !EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        // EditorApplication.Step() update icinden frame uretmiyor; adimlama = cozup N frame akit, sonra yeniden duraklat.
        public static void Advance()
        {
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        }

        // Play degilse baslatir. Donus: null = zaten play'de (devam et); JNode.Null = Defer kuruldu (play'e girince
        // then() kosar, o da Defer kurabilir); baska JNode = hemen donulecek "starting" yaniti (domain reload gelecek).
        static JNode EnsurePlaying(bool pausedStart, Func<JNode> then)
        {
            if (Application.isPlaying) return null;
            if (EditorApplication.isCompiling) throw new RpcError("derleme suruyor; bitince tekrar dene (code.build durumu)");
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPaused = pausedStart;
                EditorApplication.isPlaying = true;
                FocusGameView();
            }
            if (DomainReloadOnPlay)
            {
                var r = JNode.Map();
                r.Add("state", "starting");
                r.Add("note", "play moduna geciliyor; domain reload baglantiyi dusurur — ayni komutu tekrar cagir. Hizli dongu icin Edit > Project Settings > Editor > Enter Play Mode Options: Reload Domain KAPAT");
                return r;
            }
            RpcHost.Defer(() => Application.isPlaying ? then() : null);
            return JNode.Null();
        }

        static bool IsDeferMarker(JNode n) => n != null && n.IsNull;

        [RpcCommand("play.start", "Play moduna girer (paused=true: ilk frame'de duraklatilmis — sim.run ile adimla)")]
        static JNode PlayStart(bool paused = false)
        {
            var r = EnsurePlaying(paused, () => PlayState());
            if (r == null) return PlayState();
            return IsDeferMarker(r) ? null : r;
        }

        [RpcCommand("play.stop", "Play modundan cikar")]
        static JNode PlayStop()
        {
            if (!EditorApplication.isPlaying) return PlayState();
            Time.captureDeltaTime = 0f;
            EditorApplication.isPlaying = false;
            RpcHost.Defer(() => EditorApplication.isPlaying ? null : PlayState());
            return null;
        }

        [RpcCommand("play.pause", "Play'i duraklat/devam ettir (paused). Duraklatilmisken sim.run frame adimlar")]
        static JNode PlayPause(bool paused = true)
        {
            if (!Application.isPlaying) throw new RpcError("play modunda degil");
            EditorApplication.isPaused = paused;
            if (!paused) Time.captureDeltaTime = 0f;
            return PlayState();
        }

        [RpcCommand("play.state", "Play durumu (playing/paused/frame/time)")]
        static JNode PlayState()
        {
            var r = JNode.Map();
            r.Add("playing", Application.isPlaying);
            r.Add("paused", EditorApplication.isPaused);
            r.Add("compiling", EditorApplication.isCompiling);
            r.Add("frame", Time.frameCount);
            r.Add("time", Time.time);
            r.Add("domainReloadOnPlay", DomainReloadOnPlay);
            return r;
        }

        [RpcCommand("sim.run", "N frame simule eder (SABIT ADIM dt=1/fps, deterministik), canli dokumle doner. Play degilse baslatir (domain reload varsa 'starting' doner → tekrar cagir). Sonda play DURAKLATILMIS kalir (pause=false → serbest akar); duraklatma frame sinirinda oldugundan ±1 frame sapabilir ('simulated'). screenshot=true sonda PNG; captureEvery=K → her K frame'de PNG (frames listesi); stop=true sonda play'den cikar; dump=false → dokum yok (hizli)")]
        static JNode SimRun(int frames = 60, int fps = 60, bool screenshot = false, int captureEvery = 0, bool stop = false, bool pause = true, bool dump = true, bool fixedPhysics = true, int maxSize = 1024)
        {
            if (frames < 1 || frames > 100000) throw new RpcError("frames 1..100000 olmali");
            if (fps < 1 || fps > 1000) throw new RpcError("fps 1..1000 olmali");
            if (captureEvery < 0 || (captureEvery > 0 && frames / captureEvery > 500)) throw new RpcError("captureEvery: en fazla 500 kare yakalanir");

            Func<JNode> body = () =>
            {
                Time.captureDeltaTime = 1f / fps;
                if (fixedPhysics) Time.fixedDeltaTime = 1f / fps;
                int start = Time.frameCount;
                int target = start + frames;
                JNode captures = captureEvery > 0 ? JNode.Seq() : null;
                int nextCapture = start + captureEvery;
                bool restarted = _lastRunFrame > Time.frameCount; // play yeniden basladi
                _lastRunFrame = Time.frameCount;
                Capture.Request pendingShot = null;
                Capture.Request finalShot = null;
                Advance();

                RpcHost.Defer(() =>
                {
                    if (!Application.isPlaying) throw new RpcError("play bitti");
                    if (pendingShot != null)
                    {
                        if (!pendingShot.Done) return null;
                        var shot = pendingShot.Error != null ? JNode.Map().Add("error", pendingShot.Error) : pendingShot.Result;
                        shot.Fields.Insert(0, new System.Collections.Generic.KeyValuePair<string, JNode>("frame", JNode.Num(pendingShot.AtFrame - start)));
                        captures.Push(shot);
                        pendingShot = null;
                    }
                    int now = Time.frameCount;
                    if (captures != null && now >= nextCapture && now <= target)
                    {
                        pendingShot = Capture.Begin(1f, null, maxSize);
                        nextCapture += captureEvery;
                        return null;
                    }
                    if (now < target) return null;
                    if (screenshot)
                    {
                        if (finalShot == null) { finalShot = Capture.Begin(1f, null, maxSize); return null; }
                        if (!finalShot.Done) return null;
                    }
                    if (pause || stop) EditorApplication.isPaused = true;
                    Time.captureDeltaTime = 0f;
                    var r = dump ? DumpLive() : JNode.Map().Add("playing", true).Add("frame", Time.frameCount);
                    r.Add("simulated", Time.frameCount - start);
                    if (restarted) r.Add("restarted", true);
                    if (finalShot != null) r.Add("screenshot", CaptureResult(finalShot));
                    if (captures != null) r.Add("frames", captures);
                    if (stop) { EditorApplication.isPlaying = false; r.Add("stopped", true); }
                    return r;
                });
                return null;
            };

            var pre = EnsurePlaying(true, body);
            if (pre == null) return body();
            return IsDeferMarker(pre) ? null : pre;
        }

        static int _lastRunFrame;

        static void FocusGameView()
        {
            // Game View gorunur degilse oyun render etmez (screenshot kararir): sekmeyi one getir.
            try { EditorApplication.ExecuteMenuItem("Window/General/Game"); } catch { }
        }
    }
}
