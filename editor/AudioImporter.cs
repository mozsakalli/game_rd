using System;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public enum AudioImportType
{
    Auto,   // kisa (<= ~10 sn, < 1.5 MB) = Effect, aksi Stream
    Effect, // PCM16'ya cozulur -> native mixer (SFX): runtime decode yok
    Stream, // kaynak oldugu gibi -> platform native player (muzik/jingle): HW decode
}

// Ses import ayarlari (.meta "importer:" blogu; Inspector duzenler).
[Serializable]
public sealed class AudioImportSettings
{
    public AudioImportType type = AudioImportType.Auto;
    public bool mono = true;          // Effect: tek kanal (yari bellek; SFX'te stereo nadiren gerekir)
    public int sampleRate = 48000;    // Effect: hedef hiz (cihaz 48k -> runtime resample yok); 22050 = ceyrek boyut
}

// mp3/ogg/wav -> Effect: DPCM artifact (PCM16, hedef hiz, mono/stereo) — pak zlib'ler.
//                Stream: kaynak baytlari "main" olarak kopyalanir — pak STORED yazar,
//                        native player (Audio.PlayMusic) dosya/offset ile okur.
// Decoder'lar (dr_mp3/stb_vorbis/dr_wav) yalniz editor native'inde; release'e girmez.
[AssetImporter(".mp3", ".ogg", ".wav", Version = 1, Settings = typeof(AudioImportSettings))]
public sealed class AudioImporter : AssetImporter
{
    const long StreamSizeThreshold = 1536 * 1024; // bu boyutun ustu decode edilmeden Stream sayilir
    const float StreamSeconds = 10f;

    public override void Import(ImportContext ctx)
    {
        var s = ctx.Settings as AudioImportSettings ?? new AudioImportSettings();
        byte[] src = File.ReadAllBytes(ctx.SourcePath);
        if (src.Length == 0)
        {
            ctx.Fail("bos dosya");
            return;
        }

        var type = s.type;
        if (type == AudioImportType.Auto && src.Length > StreamSizeThreshold)
            type = AudioImportType.Stream;

        if (type != AudioImportType.Stream)
        {
            int rate = Math.Clamp(s.sampleRate, 8000, 96000);
            var blob = Audio.DecodeToDpcm(src, rate, s.mono, out int frames, out int ch, out int outRate);
            if (blob == null)
            {
                ctx.Fail("ses cozulemedi (mp3/ogg/wav degil ya da bozuk)");
                return;
            }
            float seconds = (float)frames / outRate;
            if (type == AudioImportType.Auto && seconds > StreamSeconds)
                type = AudioImportType.Stream;
            else
            {
                ctx.AddArtifact("main", blob);
                EditorLog.Info($"[audio] {ctx.AssetPath}: effect {seconds:0.00}s {outRate}Hz {(ch == 1 ? "mono" : "stereo")} ({blob.Length / 1024} KB)");
                return;
            }
        }

        ctx.AddArtifact("main", src); // Stream: bayt-esit kopya; pak bunu stored tutar
        EditorLog.Info($"[audio] {ctx.AssetPath}: stream ({src.Length / 1024} KB, native player)");
    }

    // Pak builder: bu anahtar akitilacak (stored) mi? Artifact DPCM degilse Stream'dir.
    public static bool IsStream(string assetPath)
    {
        var main = ImportPipeline.GetArtifact(assetPath, "main");
        return main != null && !Audio.IsDpcm(main);
    }

    // --- Inspector onizleme: bilgi satiri + Play/Stop + ilerleme (Stream'de seek) ---
    // Effect: AssetDatabase klibi (runtime ile ayni yol), voice konumu mixer'dan okunamaz ->
    // duvar saatiyle ilerleme. Stream: Audio.OpenMusic (native player), konum/sure oradan.

    string _pvRel;
    bool _pvStream;
    byte[] _pvMain;
    long _pvSrcBytes;
    int _pvVoice = -1, _pvMusic = -1;
    float _pvDuration;
    readonly System.Diagnostics.Stopwatch _pvClock = new();
    const float PreviewRowH = 22f;

    public override float PreviewHeight(string assetPath) => PreviewRowH * 3 + 8;

    void PreviewBind(string rel)
    {
        if (_pvRel == rel)
            return;
        HidePreview();
        _pvRel = rel;
        _pvMain = ImportPipeline.GetArtifact(rel, "main");
        _pvStream = _pvMain != null && !Audio.IsDpcm(_pvMain);
        _pvDuration = 0f;
        if (_pvMain != null && !_pvStream)
        {
            int rate = BitConverter.ToInt32(_pvMain, 4), frames = BitConverter.ToInt32(_pvMain, 12);
            _pvDuration = rate > 0 ? (float)frames / rate : 0f;
        }
        try { _pvSrcBytes = new FileInfo(ImportPipeline.FullPath(rel)).Length; }
        catch { _pvSrcBytes = 0; }
    }

    public override void HidePreview()
    {
        if (_pvVoice >= 0) Audio.Stop(_pvVoice);
        if (_pvMusic >= 0) Audio.MusicClose(_pvMusic);
        _pvVoice = _pvMusic = -1;
        _pvClock.Reset();
        _pvRel = null;
        _pvMain = null;
    }

    bool PreviewPlaying
    {
        get
        {
            if (_pvStream)
                return _pvMusic >= 0 && Audio.MusicStateOf(_pvMusic) is MusicState.Playing or MusicState.Opening;
            if (_pvVoice < 0) return false;
            if (Audio.IsPlaying(_pvVoice)) return true;
            _pvVoice = -1;
            return false;
        }
    }

    void PreviewPlay()
    {
        if (_pvStream)
        {
            if (_pvMusic < 0)
                _pvMusic = Audio.OpenMusic(_pvRel);
            if (_pvMusic >= 0)
                Audio.MusicPlay(_pvMusic);
            return;
        }
        var clip = App.Assets?.LoadAudioClip(_pvRel);
        if (clip == null)
            return;
        _pvVoice = clip.Play();
        _pvClock.Restart();
    }

    void PreviewStop()
    {
        if (_pvStream)
        {
            if (_pvMusic >= 0) Audio.MusicClose(_pvMusic);
            _pvMusic = -1;
            return;
        }
        if (_pvVoice >= 0) Audio.Stop(_pvVoice);
        _pvVoice = -1;
        _pvClock.Reset();
    }

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        PreviewBind(assetPath);
        bool repaint = Event.Current.Type == EventType.Repaint;
        var dim = new Color(160, 168, 182, 255);
        float y = rect.y;

        // Satir 1: format bilgisi (artifact'ten; import edilmemisse uyari).
        string info;
        if (_pvMain == null)
            info = "(not imported)";
        else if (_pvStream)
            info = $"Stream  ·  {_pvSrcBytes / 1024} KB  ·  native player (HW decode)";
        else
        {
            int rate = BitConverter.ToInt32(_pvMain, 4), ch = BitConverter.ToInt32(_pvMain, 8);
            info = $"Effect  ·  {_pvDuration:0.00} s  ·  {rate} Hz  ·  {(ch == 1 ? "mono" : "stereo")}  ·  {(_pvMain.Length - Audio.DpcmHeaderSize) / 1024} KB PCM";
        }
        if (repaint)
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PreviewRowH), info, Gui.FontSize - 1, dim);
        y += PreviewRowH;

        // Satir 2: Play/Stop + ilerleme cubugu + zaman. Kontrol sayisi sabit (tek buton).
        bool playing = PreviewPlaying;
        if (Gui.Button(new Rect(rect.x, y + 1, 60, PreviewRowH - 2), playing ? "Stop" : "Play"))
        {
            if (playing) PreviewStop(); else PreviewPlay();
            playing = !playing;
        }
        float pos = 0f, dur = _pvDuration;
        if (_pvStream && _pvMusic >= 0)
        {
            double d = Audio.MusicDuration(_pvMusic);
            dur = d > 0 ? (float)d : 0f;
            pos = (float)Audio.MusicPosition(_pvMusic);
        }
        else if (_pvVoice >= 0)
            pos = (float)_pvClock.Elapsed.TotalSeconds;
        if (dur > 0 && pos > dur) pos = dur;
        var bar = new Rect(rect.x + 68, y + 7, rect.width - 68 - 96, PreviewRowH - 14);
        if (repaint)
        {
            GuiRenderer.DrawRect(bar, new Color(32, 36, 46, 255));
            if (dur > 0)
                GuiRenderer.DrawRect(new Rect(bar.x, bar.y, bar.width * (pos / dur), bar.height), new Color(90, 150, 230, 255));
            GuiRenderer.DrawTextIn(new Rect(bar.xMax + 4, y, 92, PreviewRowH),
                $"{Fmt(pos)} / {(dur > 0 ? Fmt(dur) : "--:--")}", Gui.FontSize - 1, dim);
        }
        y += PreviewRowH;

        // Satir 3: Stream'de seek (slider daima cizilir: kontrol sayisi sabit; Effect'te pasif).
        bool canSeek = _pvStream && _pvMusic >= 0 && dur > 0;
        float t = canSeek ? pos / dur : 0f;
        float nt = Gui.HorizontalSlider(new Rect(rect.x, y + 4, rect.width, PreviewRowH - 8), t, 0f, 1f);
        if (canSeek && MathF.Abs(nt - t) > 0.002f)
            Audio.MusicSeek(_pvMusic, nt * dur);
        if (!_pvStream && repaint)
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PreviewRowH), "seek: stream only", Gui.FontSize - 2, new Color(90, 96, 110, 255), true);
    }

    static string Fmt(float s)
    {
        int m = (int)(s / 60), sec = (int)(s % 60);
        return $"{m}:{sec:00}";
    }
}
