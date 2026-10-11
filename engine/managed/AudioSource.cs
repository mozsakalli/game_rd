namespace DigitoyEngine;

// Unity AudioSource'un SFX dilimi: sahnedeki bir GO'ya bagli klip + oynatma ayarlari.
// Mixer'da tek voice tutar (_voice); Play yeniden cagrilirsa onceki voice durur (Unity:
// AudioSource ayni anda tek ses). Muzik (Stream tipli kaynak) burada DEGIL: Audio.PlayMusic
// (platform native player, sahneden bagimsiz yasam) — AudioClip alanina Stream asset atanirsa
// AssetDatabase null doner, kaynak sessiz kalir (uyari log'u AssetDatabase'ten).
// Edit sahnesinde yasam dongusu kesilir ([ExecuteInEditMode] yok) -> editorde ses yok;
// onizleme Inspector'in isidir.
public sealed class AudioSource : Component
{
    public AudioClip Clip;
    public bool PlayOnAwake = true; // Unity paritesi: ilk Start'ta (simule sahnede) caliyor
    public bool Loop;
    public float Volume = 1f;       // 0..1; animasyon/inspector yazimi OnValidate ile canli voice'a iner

    int _voice = -1;

    // Voice bitince mixer -1/not-playing doner; handle'i o anda birakiriz (poll, callback yok).
    public bool IsPlaying
    {
        get
        {
            if (_voice < 0)
                return false;
            if (Audio.IsPlaying(_voice))
                return true;
            _voice = -1;
            return false;
        }
    }

    public void Play() => Play(Clip);

    // Tek seferlik baska klip (ayni voice slotunda; Unity PlayOneShot gibi ek voice ACMAZ).
    public void Play(AudioClip clip)
    {
        Stop();
        if (clip == null || !isActiveAndEnabled)
            return;
        _voice = Audio.Play(clip, Volume, Loop);
    }

    public void Stop()
    {
        if (_voice < 0)
            return;
        Audio.Stop(_voice);
        _voice = -1;
    }

    protected internal override void Start()
    {
        if (PlayOnAwake && _gameObject?._scene != null && _gameObject._scene.Simulating)
            Play();
    }

    protected internal override void OnValidate()
    {
        Volume = Volume < 0f ? 0f : Volume > 1f ? 1f : Volume;
        if (_voice >= 0)
            Audio.SetVolume(_voice, Volume);
    }

    protected internal override void OnDisable() => Stop();
    protected internal override void OnDestroy() => Stop();
}
