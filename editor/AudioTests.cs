#if DE_EDITOR
using System;
using System.Threading;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: ses hatti (DPCM blob, native mixer klip/voice, suspend, AudioSource yasam dongusu,
// importer decode). Zamanlamaya bagli adimlar (voice bitisi) KISA poll ile ve yalniz cihaz acildiysa
// kontrol edilir — ses karti olmayan makinede (CI) callback kosmaz, o adimlar atlanir.
public static class AudioTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var oldWarn = AssetDatabase.LogWarning;
        try
        {
            DpcmBlob();
            ClipAndVoice();
            SuspendBlocksPlay();
            SourceLifecycle(catalog);
            SourceSleepsInEditMode(catalog);
            DecodeWavRoundTrip();
        }
        finally
        {
            Audio.StopAll();
            AssetDatabase.LogWarning = oldWarn;
        }
        Console.WriteLine($"[audio] {_pass} PASS, {_fail} FAIL");
    }

    // 440 Hz sinus, verilen uzunlukta mono PCM16 DPCM blobu (decoder gerektirmez).
    static byte[] MakeDpcm(int rate, int frames, int channels = 1)
    {
        var blob = new byte[Audio.DpcmHeaderSize + frames * channels * 2];
        BitConverter.TryWriteBytes(blob.AsSpan(0), Audio.DpcmMagic);
        BitConverter.TryWriteBytes(blob.AsSpan(4), rate);
        BitConverter.TryWriteBytes(blob.AsSpan(8), channels);
        BitConverter.TryWriteBytes(blob.AsSpan(12), frames);
        for (int i = 0; i < frames; i++)
        {
            short s = (short)(MathF.Sin(i * 440f * 2f * MathF.PI / rate) * 8000f);
            for (int c = 0; c < channels; c++)
                BitConverter.TryWriteBytes(blob.AsSpan(Audio.DpcmHeaderSize + (i * channels + c) * 2), s);
        }
        return blob;
    }

    static AudioClip Clip(int rate, int frames, int channels = 1)
        => AudioClip.FromArtifact(MakeDpcm(rate, frames, channels), "test");

    static void DpcmBlob()
    {
        var blob = MakeDpcm(48000, 100);
        Check(Audio.IsDpcm(blob), "DPCM magic taninir");
        Check(!Audio.IsDpcm(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }), "rastgele blob DPCM degil");
        // Bozuk uzunluk (header frames ile gövde uyusmaz) -> klip olusmaz.
        var bad = MakeDpcm(48000, 100);
        BitConverter.TryWriteBytes(bad.AsSpan(12), 999);
        Check(Clip(48000, 0) == null, "sifir frame klip reddedilir");
        var badClip = AudioClip.FromArtifact(bad, "bad");
        Check(badClip == null, "govde/frames uyusmazligi reddedilir");
    }

    static void ClipAndVoice()
    {
        var clip = Clip(48000, 4800); // 100 ms
        Check(clip != null && clip.IsLoaded, "klip native'e yuklenir");
        Check(clip.Frames == 4800 && clip.SampleRate == 48000, "frame/rate header'dan okunur");
        Check(MathF.Abs(clip.Duration - 0.1f) < 1e-4f, "Duration = frames/rate");

        int v = clip.Play(0.5f);
        Check(v >= 0, "Play voice handle doner");
        Check(Audio.IsPlaying(v), "Play sonrasi IsPlaying (RESERVED/PLAYING)");
        Audio.SetVolume(v, 0.1f); // gecersiz handle degil: sessizce kabul
        Check(!Audio.IsPlaying(-1) && !Audio.IsPlaying(0x7FFFFFF), "gecersiz handle calmiyor");

        if (Audio.IsDeviceOpen)
        {
            // 100 ms klip: ~300 ms icinde biter, voice serbest kalir (poll).
            Check(WaitUntil(() => !Audio.IsPlaying(v), 600), "kisa klip biter, voice serbest kalir");
            int loop = clip.Play(0.2f, loop: true);
            Thread.Sleep(250);
            Check(Audio.IsPlaying(loop), "loop klip surer");
            Audio.Stop(loop);
            Check(WaitUntil(() => !Audio.IsPlaying(loop), 300), "Stop sonrasi voice serbest (ramp)");
        }
        else
            Console.WriteLine("[audio] cihaz acilamadi (ses karti yok?) — zamanlama adimlari atlandi");

        // Klip yok edilince handle'lar gecersiz; Play -1.
        clip.Destroy();
        Check(!clip.IsLoaded && clip.Play() == -1, "yok edilen klip calmaz");
    }

    static void SuspendBlocksPlay()
    {
        var clip = Clip(48000, 480);
        Audio.Suspend();
        Check(clip.Play() == -1, "suspend'de Play -1");
        Check(!Audio.IsDeviceOpen, "suspend'de cihaz kapali");
        Audio.Resume();
        int v = clip.Play();
        Check(v >= 0, "resume sonrasi Play calisir");
        Audio.Stop(v);
        clip.Destroy();
    }

    static void SourceLifecycle(TypeCatalog catalog)
    {
        var prev = Scene.Active;
        var s = Scene.Create("audio-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        var clip = Clip(48000, 48000); // 1 sn: test boyunca biter sanilmasin
        var go = new GameObject("src");
        var src = go.AddComponent<AudioSource>();
        src.Clip = clip;
        src.Loop = true;
        Check(!src.IsPlaying, "Start oncesi calmiyor");
        s.Update(0f);
        Check(src.IsPlaying, "PlayOnAwake: ilk Update'te (Start) calar");
        src.Volume = 0.3f;
        src.OnValidate(); // inspector/animasyon yazimi yolu — crash yok, canli voice'a iner
        src.Volume = 7f;
        src.OnValidate();
        Check(src.Volume == 1f, "OnValidate Volume'u 0..1'e clamp'ler");
        go.SetActive(false);
        Check(!src.IsPlaying, "OnDisable: durur");
        go.SetActive(true);
        s.Update(0f);
        Check(!src.IsPlaying, "yeniden enable: Start tekrar kosmaz, kendiliginden calmaz");
        src.Play();
        Check(src.IsPlaying, "Play() calar");
        src.Play(); // ikinci Play oncekini durdurur, tek voice
        Check(src.IsPlaying, "ikinci Play tek voice ile surer");
        src.enabled = false;
        Check(!src.IsPlaying, "component disable: durur");
        src.enabled = true;
        src.Play();
        GameObject.Destroy(go);
        s.Update(0f);
        Check(!src.IsPlaying, "Destroy: durur");

        var noClip = new GameObject("empty").AddComponent<AudioSource>();
        s.Update(0f);
        Check(!noClip.IsPlaying, "Clip null: sessiz, hata yok");

        Scene.SetActive(prev);
        Scene.Unload(s);
        clip.Destroy();
    }

    // Edit sahnesi: AudioSource [ExecuteInEditMode] degil -> Start kosmaz, editorde ses yok.
    static void SourceSleepsInEditMode(TypeCatalog catalog)
    {
        var prev = Scene.Active;
        var es = Scene.Create("audio-edit");
        es.Catalog = catalog;
        es.EditMode = true;
        Scene.SetActive(es);
        var clip = Clip(48000, 4800);
        var src = new GameObject("src").AddComponent<AudioSource>();
        src.Clip = clip;
        es.Update(0f, simulate: false);
        Check(!src.IsPlaying, "edit modunda PlayOnAwake uyur");
        Scene.SetActive(prev);
        Scene.Unload(es);
        clip.Destroy();
    }

    // Importer yolu: bellekte WAV (PCM16 mono 22050) -> DecodeToDpcm 48k mono -> frame sayisi orantili.
    static void DecodeWavRoundTrip()
    {
        const int rate = 22050, frames = 2205; // 100 ms
        var wav = new byte[44 + frames * 2];
        void W32(int o, int v) => BitConverter.TryWriteBytes(wav.AsSpan(o), v);
        void W16(int o, short v) => BitConverter.TryWriteBytes(wav.AsSpan(o), v);
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0); W32(4, 36 + frames * 2);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12); W32(16, 16); W16(20, 1); W16(22, 1);
        W32(24, rate); W32(28, rate * 2); W16(32, 2); W16(34, 16);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36); W32(40, frames * 2);
        for (int i = 0; i < frames; i++)
            W16(44 + i * 2, (short)(MathF.Sin(i * 0.1f) * 10000f));

        var blob = Audio.DecodeToDpcm(wav, 48000, true, out int f, out int ch, out int r);
        Check(blob != null && Audio.IsDpcm(blob), "wav decode -> DPCM");
        Check(ch == 1 && r == 48000, "hedef mono/48k uygulanir");
        Check(Math.Abs(f - 4800) <= 2, "resample frame sayisi orantili (" + f + ")");
        var same = Audio.DecodeToDpcm(wav, 0, false, out int f2, out int ch2, out int r2);
        Check(same != null && f2 == frames && r2 == rate && ch2 == 1, "targetRate<=0 kaynak hizini korur");
        Check(Audio.DecodeToDpcm(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 48000, true, out _, out _, out _) == null, "bozuk kaynak null");
    }

    static bool WaitUntil(Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (cond()) return true;
            Thread.Sleep(5);
        }
        return cond();
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[audio] FAIL: {name}"); }
    }
}
#endif
