using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// Kisa ses efekti: import'ta PCM16'ya cozulmus (DPCM artifact), native mixer'da
// (audio_shim.c) yasar. Managed tarafta yalniz bir int handle — Play/Stop/Volume
// her biri tek extern cagri, allocation yok. Yukleme: AssetDatabase.LoadAudioClip.
public sealed class AudioClip : IAsset
{
    public string Name { get; internal set; }
    internal int Handle = -1;

    public bool IsLoaded => Handle >= 0;
    public int Frames => Audio.ClipFrames(Handle);
    public int SampleRate => Audio.ClipRate(Handle);
    public float Duration
    {
        get
        {
            int r = SampleRate;
            return r > 0 ? (float)Frames / r : 0f;
        }
    }

    // Donus: voice handle (Audio.Stop/SetVolume/IsPlaying ile), calmadiysa -1.
    public int Play(float volume = 1f, bool loop = false) => Audio.Play(this, volume, loop);

    // DPCM blobundan (importer ciktisi / pak girisi). Gecersiz blob = null.
    internal static unsafe AudioClip FromArtifact(byte[] blob, string name)
    {
        if (blob == null || blob.Length < 16)
            return null;
        int h;
        fixed (byte* p = blob)
            h = Audio.ClipCreate(p, blob.Length);
        return h >= 0 ? new AudioClip { Name = name, Handle = h } : null;
    }

    internal void Destroy()
    {
        if (Handle < 0)
            return;
        Audio.ClipDestroy(Handle);
        Handle = -1;
    }
}

public enum MusicState
{
    Opening = 0,
    Ready = 1,   // hazir / duraklatilmis
    Playing = 2,
    Ended = 3,
    Error = -1,
}

// Ses yuzeyi. IKI yol:
//   SFX  : Play/Stop/SetVolume/IsPlaying — native PCM mixer, int voice handle.
//   MUZIK: PlayMusic/StopMusic/MusicVolume (eski Digiplay API'si, tek ana kanal) +
//          OpenMusic/MusicPlay/... (coklu kanal, radyo/crossfade). Kaynak: asset
//          anahtari (loose dosya ya da pak araligi) YA DA http(s) URL — platformun
//          native player'i calar (decode OS'ta), mixer'dan bagimsiz.
// Hepsi poll tabanli, callback yok; managed tek thread sozlesmesi korunur.
public static unsafe class Audio
{
    const string Lib = "digitoyengine_native";

    // Muzik anahtarlarini (pak araligi / dosya yolu) cozmek icin; host kurar
    // (PlayerApp/EditorApp). URL'ler kaynaga bakmadan gecer.
    public static AssetSource Source;

    // --- SFX ---

    public static int Play(AudioClip clip, float volume = 1f, bool loop = false)
        => clip != null && clip.Handle >= 0 ? PlayNative(clip.Handle, volume, loop ? 1 : 0) : -1;

    [DllImport(Lib, EntryPoint = "de_audio_stop")]
    extern public static void Stop(int voice);

    [DllImport(Lib, EntryPoint = "de_audio_set_volume")]
    extern public static void SetVolume(int voice, float volume);

    [DllImport(Lib, EntryPoint = "de_audio_is_playing")]
    extern static int IsPlayingNative(int voice);
    public static bool IsPlaying(int voice) => IsPlayingNative(voice) != 0;

    [DllImport(Lib, EntryPoint = "de_audio_stop_all")]
    extern public static void StopAll();

    static float _master = 1f;
    public static float MasterVolume
    {
        get => _master;
        set
        {
            _master = value;
            SetMasterNative(value);
        }
    }

    // Uygulama arka plana/one: SFX cihazi kapanir/acilir (voice durumu korunur),
    // ana muzik duraklar/surer. Host'un pause/resume olayina baglanir.
    public static void Suspend()
    {
        SuspendNative();
        _musicWasPlaying = _music >= 0 && MusicStateOf(_music) == MusicState.Playing;
        if (_musicWasPlaying)
            MusicPause(_music);
    }

    public static void Resume()
    {
        ResumeNative();
        if (_music >= 0 && _musicWasPlaying)
            MusicPlay(_music);
        _musicWasPlaying = false;
    }

    public static void Shutdown()
    {
        StopMusic();
        MusicShutdownNative();
        ShutdownNative();
    }

    [DllImport(Lib, EntryPoint = "de_audio_play")]
    extern static int PlayNative(int clip, float volume, int loop);
    [DllImport(Lib, EntryPoint = "de_audio_set_master_volume")]
    extern static void SetMasterNative(float v);
    [DllImport(Lib, EntryPoint = "de_audio_suspend")]
    extern static void SuspendNative();
    [DllImport(Lib, EntryPoint = "de_audio_resume")]
    extern static void ResumeNative();
    [DllImport(Lib, EntryPoint = "de_audio_shutdown")]
    extern static void ShutdownNative();
    [DllImport(Lib, EntryPoint = "de_audio_clip_create")]
    extern internal static int ClipCreate(byte* blob, int length);
    [DllImport(Lib, EntryPoint = "de_audio_clip_destroy")]
    extern internal static void ClipDestroy(int clip);
    [DllImport(Lib, EntryPoint = "de_audio_clip_frames")]
    extern internal static int ClipFrames(int clip);
    [DllImport(Lib, EntryPoint = "de_audio_clip_rate")]
    extern internal static int ClipRate(int clip);
    [DllImport(Lib, EntryPoint = "de_audio_sample_rate")]
    extern public static int DeviceSampleRate();
    // Teshis: SFX cihazi acik mi (hic ses yokken ~3 sn sonra kapanir; pil/CPU sifir).
    [DllImport(Lib, EntryPoint = "de_audio_device_open")]
    extern static int DeviceOpenNative();
    public static bool IsDeviceOpen => DeviceOpenNative() != 0;

    // --- Muzik: tek ana kanal (eski Digiplay API'si) ---

    static int _music = -1;
    static float _musicVolume = 1f;
    static bool _musicWasPlaying;

    public static void PlayMusic(string keyOrUrl, float volume = 1f, bool loop = false)
    {
        StopMusic();
        _music = OpenMusic(keyOrUrl);
        if (_music < 0)
            return;
        _musicVolume = volume;
        MusicSetVolume(_music, volume);
        MusicSetLoop(_music, loop);
        MusicPlay(_music); // acilis bitince native kendisi baslar
    }

    public static void StopMusic()
    {
        if (_music < 0)
            return;
        MusicClose(_music);
        _music = -1;
    }

    public static void PauseMusic() { if (_music >= 0) MusicPause(_music); }
    public static void ResumeMusic() { if (_music >= 0) MusicPlay(_music); }
    public static bool IsMusicPlaying => _music >= 0 && MusicStateOf(_music) == MusicState.Playing;
    public static MusicState MainMusicState => _music >= 0 ? MusicStateOf(_music) : MusicState.Error;

    public static float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = value;
            if (_music >= 0)
                MusicSetVolume(_music, value);
        }
    }

    // --- Muzik: coklu kanal (radyo, crossfade) ---

    // keyOrUrl: "http(s)://..." -> ag akisi; degilse Source'tan (loose dosya / pak araligi).
    // Donus: muzik handle (>=0) ya da -1. Acilis async: MusicStateOf ile izlenir.
    public static int OpenMusic(string keyOrUrl)
    {
        if (string.IsNullOrEmpty(keyOrUrl))
            return -1;
        if (keyOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || keyOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return MusicOpenNative(keyOrUrl, 0, 0);
        if (Source == null || !Source.TryGetRange(keyOrUrl, out var path, out long off, out long len))
            return -1;
        return MusicOpenNative(path, off, len);
    }

    [DllImport(Lib, EntryPoint = "de_music_open")]
    extern static int MusicOpenNative([MarshalAs(UnmanagedType.LPUTF8Str)] string pathOrUrl, long offset, long length);
    [DllImport(Lib, EntryPoint = "de_music_close")]
    extern public static void MusicClose(int music);
    [DllImport(Lib, EntryPoint = "de_music_play")]
    extern public static void MusicPlay(int music);
    [DllImport(Lib, EntryPoint = "de_music_pause")]
    extern public static void MusicPause(int music);
    [DllImport(Lib, EntryPoint = "de_music_set_volume")]
    extern public static void MusicSetVolume(int music, float volume);
    [DllImport(Lib, EntryPoint = "de_music_set_loop")]
    extern static void MusicSetLoopNative(int music, int loop);
    public static void MusicSetLoop(int music, bool loop) => MusicSetLoopNative(music, loop ? 1 : 0);
    [DllImport(Lib, EntryPoint = "de_music_state")]
    extern static int MusicStateNative(int music);
    public static MusicState MusicStateOf(int music) => (MusicState)MusicStateNative(music);
    [DllImport(Lib, EntryPoint = "de_music_position")]
    extern public static double MusicPosition(int music);
    // Canli akis (radyo): -1.
    [DllImport(Lib, EntryPoint = "de_music_duration")]
    extern public static double MusicDuration(int music);
    [DllImport(Lib, EntryPoint = "de_music_seek")]
    extern public static void MusicSeek(int music, double seconds);
    [DllImport(Lib, EntryPoint = "de_music_shutdown")]
    extern static void MusicShutdownNative();

    // --- Editor decode (importer): mp3/ogg/wav -> PCM16. Release native'de stub. ---

    [DllImport(Lib, EntryPoint = "de_audio_decode")]
    extern internal static int DecodeNative(byte* data, int length, int targetRate, int mono,
        out IntPtr pcm, out int frames, out int channels, out int rate);
    [DllImport(Lib, EntryPoint = "de_audio_decode_free")]
    extern internal static void DecodeFree(IntPtr pcm);

    public const int DpcmMagic = 0x4D435044; // "DPCM"
    public const int DpcmHeaderSize = 16;

    // Kaynak baytlarini DPCM blobuna cevirir (targetRate<=0: kaynak hizi). Hata = null.
    public static byte[] DecodeToDpcm(byte[] src, int targetRate, bool mono, out int frames, out int channels, out int rate)
    {
        frames = channels = rate = 0;
        if (src == null || src.Length == 0)
            return null;
        IntPtr pcm;
        int ok;
        fixed (byte* p = src)
            ok = DecodeNative(p, src.Length, targetRate, mono ? 1 : 0, out pcm, out frames, out channels, out rate);
        if (ok == 0 || pcm == IntPtr.Zero)
            return null;
        try
        {
            int bytes = frames * channels * 2;
            var blob = new byte[DpcmHeaderSize + bytes];
            BitConverter.TryWriteBytes(blob.AsSpan(0), DpcmMagic);
            BitConverter.TryWriteBytes(blob.AsSpan(4), rate);
            BitConverter.TryWriteBytes(blob.AsSpan(8), channels);
            BitConverter.TryWriteBytes(blob.AsSpan(12), frames);
            new ReadOnlySpan<byte>((void*)pcm, bytes).CopyTo(blob.AsSpan(DpcmHeaderSize));
            return blob;
        }
        finally
        {
            DecodeFree(pcm);
        }
    }

    public static bool IsDpcm(byte[] blob)
        => blob != null && blob.Length >= DpcmHeaderSize && BitConverter.ToInt32(blob, 0) == DpcmMagic;
}
