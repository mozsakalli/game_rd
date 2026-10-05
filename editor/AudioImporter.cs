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
}
