// ---------------------------------------------------------------------------
// audio_shim.c — DigitoyEngine.Audio (engine/managed/Audio.cs) icin C govdeleri
// ---------------------------------------------------------------------------
//
// IKI AYRI YOL, IKI AYRI AMAC:
//
//  1) SFX (de_audio_*): sokol_audio uzerinde ~300 satirlik "aptal" PCM mixer.
//     Klipler import'ta (editor) PCM16'ya cozulmus ve cihaz hizina (48k) getirilmis
//     gelir -> runtime'da decode YOK, pitch YOK, resample yalniz hiz uyusmazsa
//     (lineer). Ic dongu: out += src * gain. Hic ses calmiyorken cihaz KAPATILIR
//     (idle gating) -> CPU/pil sifir; ilk Play'de yeniden acilir.
//
//     Thread sozlesmesi: managed taraf TEK thread (main). Mixer callback'i ses
//     thread'inde kosar. Aralarinda mutex/malloc YOK:
//       - yapisal komutlar (play/stop/clip-free) SPSC lock-free ring'den gider,
//         callback blok basinda tuketir;
//       - parametre (volume) atomic float-bit store, callback blok basinda okur
//         ve gain RAMP'iyle uygular (click yok);
//       - voice'un bitisini callback atomic yazar, managed IsPlaying ile poll eder.
//     Cihaz kapaliyken (idle/suspend) callback yok -> komutlar main thread'de
//     yerinde islenir (ayni fonksiyon, tek yazar).
//
//  2) MUZIK / RADYO (de_music_*): platformun native player'i. Decode OS/DSP'de,
//     bizim mixer'dan tamamen bagimsiz akis; dosya, pak araligi (offset+len) ve
//     http(s) URL ayni API'den. Windows: Media Foundation IMFMediaEngine (audio-
//     only). Diger platformlar (AVPlayer / MediaPlayer / HTMLAudio) sonraki dilim —
//     burada -1 donen stub'lar.
//
//  3) EDITOR DECODE (de_audio_decode): mp3/ogg/wav -> PCM16 (dr_mp3 / stb_vorbis /
//     dr_wav). Yalniz importer kullanir; DE_NO_DECODERS ile release'ten dusurulur.
//
// SEMBOL ADLARI: Audio.cs'teki [DllImport(..., EntryPoint="de_audio_*|de_music_*")]
// ile birebir (langtest AOT dogrudan bu adlari emit eder).
// ---------------------------------------------------------------------------

#include <stdatomic.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "sokol/sokol_audio.h"

#if defined(DE_BUILD_DLL) && defined(_WIN32)
#define DE_API __declspec(dllexport)
#else
#define DE_API
#endif

// ===========================================================================
// SFX MIXER
// ===========================================================================

#define DE_AUDIO_RATE 48000      // istenen cihaz hizi (import varsayilani ile ayni)
#define DE_AUDIO_CHANNELS 2
#define DE_AUDIO_BUFFER_FRAMES 2048 // ~43 ms @48k: saniyede ~23 uyanma (casual icin yeter)
#define DE_AUDIO_MAX_CLIPS 512
#define DE_AUDIO_MAX_VOICES 32
#define DE_AUDIO_RING 256        // komut ring'i (2'nin kuvveti)
#define DE_AUDIO_RAMP 256        // gain rampasi (ornek) ~5 ms
#define DE_AUDIO_IDLE_BLOCKS 70  // ~3 sn sessizlikten sonra cihaz kapanir
#define DE_AUDIO_GRAVE 64

// DPCM blob (importer uretir): [magic 'DPCM'][rate i32][channels i32][frames i32][int16 interleaved]
#define DE_DPCM_MAGIC 0x4D435044
#define DE_DPCM_HEADER 16

typedef struct
{
    int16_t *samples; // interleaved
    int frames;
    int channels; // 1 | 2
    int rate;
    int alive;
} de_clip_t;

enum
{
    DE_VOICE_FREE = 0,
    DE_VOICE_RESERVED = 1, // main thread ayirdi, PLAY komutu yolda
    DE_VOICE_PLAYING = 2,
};

typedef struct
{
    _Atomic int state;
    _Atomic uint32_t gain_bits; // hedef gain (float bits); main yazar, callback okur
    unsigned gen;               // handle nesli (main yazar; callback okumaz)
    unsigned seq;               // stealing icin yas (main yazar)
    int clip;
    int loop;
    int stopping; // hedef 0'a inince serbest
    double pos;   // frame konumu (resample icin kesirli)
    float gain;   // anlik (ramp) gain
} de_voice_t;

enum
{
    DE_CMD_PLAY = 1,
    DE_CMD_STOP,       // fade-out ile durdur
    DE_CMD_STOP_CLIP,  // klibi kullanan tum voice'lari hemen birak
    DE_CMD_STOP_ALL,
};

typedef struct
{
    int type;
    int voice;
    int clip;
    int loop;
    float gain;
    unsigned gen;
} de_cmd_t;

static de_clip_t de_clips[DE_AUDIO_MAX_CLIPS];
static de_voice_t de_voices[DE_AUDIO_MAX_VOICES];
static de_cmd_t de_ring[DE_AUDIO_RING];
static _Atomic unsigned de_ring_head; // main yazar
static _Atomic unsigned de_ring_tail; // callback (veya kapaliyken main) yazar
static _Atomic int de_idle_blocks;    // callback: ust uste sessiz blok sayisi
static _Atomic uint32_t de_master_bits;
static unsigned de_voice_seq;
static int de_device_running;
static int de_suspended;
static int de_device_rate = DE_AUDIO_RATE;

#if defined(_WIN32)
// Windows basliklari decoder'lardan ONCE: stb_vorbis L/C/R makrolari SDK ile cakisir.
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objidl.h>
#include <mfapi.h>
#include <mfmediaengine.h>
#include <mferror.h>
#if defined(__MINGW32__)
// mingw-w64 mfidl.h bu prototipi tasimaz; mfplat.dll export'u (libmfplat.a'da var). Imza MS SDK ile ayni.
HRESULT WINAPI MFCreateMFByteStreamOnStream(IStream *pStream, IMFByteStream **ppByteStream);
#endif
static void Sleep_ms_1(void) { Sleep(1); }
#else
#include <time.h>
static void Sleep_ms_1(void) { struct timespec ts = { 0, 1000000 }; nanosleep(&ts, NULL); }
#endif

// Ertelenmis klip serbest birakma: callback STOP_CLIP'i isleyene kadar bellek
// yasar. Komut ring'i tuketilince (tail >= seq) free edilir.
typedef struct
{
    int16_t *samples;
    unsigned seq;
} de_grave_t;
static de_grave_t de_grave[DE_AUDIO_GRAVE];
static int de_grave_count;

static inline float de_bits_to_f(uint32_t b)
{
    float f;
    memcpy(&f, &b, 4);
    return f;
}
static inline uint32_t de_f_to_bits(float f)
{
    uint32_t b;
    memcpy(&b, &f, 4);
    return b;
}

static inline int de_handle_idx(int h) { return h & 0xFF; }
static inline unsigned de_handle_gen(int h) { return (unsigned)h >> 8; }
static inline int de_make_handle(int idx, unsigned gen) { return (int)((gen << 8) | (unsigned)idx); }

// --- komut islemcisi (callback'te ya da cihaz kapaliyken main'de) ---------

static void de_voice_release(de_voice_t *v)
{
    v->clip = -1;
    atomic_store_explicit(&v->state, DE_VOICE_FREE, memory_order_release);
}

static void de_process_cmd(const de_cmd_t *c)
{
    switch (c->type)
    {
    case DE_CMD_PLAY:
    {
        de_voice_t *v = &de_voices[c->voice];
        if (c->clip < 0 || c->clip >= DE_AUDIO_MAX_CLIPS || !de_clips[c->clip].alive)
        {
            de_voice_release(v);
            break;
        }
        v->clip = c->clip;
        v->loop = c->loop;
        v->stopping = 0;
        v->pos = 0;
        v->gain = 0; // ramp ile girer
        atomic_store_explicit(&v->gain_bits, de_f_to_bits(c->gain), memory_order_relaxed);
        atomic_store_explicit(&v->state, DE_VOICE_PLAYING, memory_order_release);
        break;
    }
    case DE_CMD_STOP:
    {
        de_voice_t *v = &de_voices[c->voice];
        if (v->gen != c->gen)
            break; // bayat handle
        int s = atomic_load_explicit(&v->state, memory_order_acquire);
        if (s == DE_VOICE_PLAYING)
        {
            v->stopping = 1;
            atomic_store_explicit(&v->gain_bits, 0, memory_order_relaxed);
        }
        else if (s == DE_VOICE_RESERVED)
            de_voice_release(v);
        break;
    }
    case DE_CMD_STOP_CLIP:
        for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
            if (de_voices[i].clip == c->clip && atomic_load_explicit(&de_voices[i].state, memory_order_acquire) != DE_VOICE_FREE)
                de_voice_release(&de_voices[i]);
        break;
    case DE_CMD_STOP_ALL:
        for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
            if (atomic_load_explicit(&de_voices[i].state, memory_order_acquire) != DE_VOICE_FREE)
                de_voice_release(&de_voices[i]);
        break;
    }
}

static void de_drain_cmds(void)
{
    unsigned tail = atomic_load_explicit(&de_ring_tail, memory_order_relaxed);
    unsigned head = atomic_load_explicit(&de_ring_head, memory_order_acquire);
    while (tail != head)
    {
        de_process_cmd(&de_ring[tail & (DE_AUDIO_RING - 1)]);
        tail++;
    }
    atomic_store_explicit(&de_ring_tail, tail, memory_order_release);
}

// Main thread: ring'e yazar; cihaz kapaliysa hemen isler. Donus 0 = ring dolu.
static int de_push_cmd(const de_cmd_t *c)
{
    unsigned head = atomic_load_explicit(&de_ring_head, memory_order_relaxed);
    unsigned tail = atomic_load_explicit(&de_ring_tail, memory_order_acquire);
    if (head - tail >= DE_AUDIO_RING)
        return 0;
    de_ring[head & (DE_AUDIO_RING - 1)] = *c;
    atomic_store_explicit(&de_ring_head, head + 1, memory_order_release);
    if (!de_device_running)
        de_drain_cmds();
    return 1;
}

// --- mixer ------------------------------------------------------------------

// Tek voice'u 'out'a toplar. Fast path: klip hizi == cihaz hizi (resample yok).
// Donus 0 = voice bitti (serbest birakildi).
static int de_mix_voice(de_voice_t *v, float *out, int frames, int ch)
{
    const de_clip_t *clip = &de_clips[v->clip];
    const int16_t *src = clip->samples;
    const int cf = clip->frames;
    const int cc = clip->channels;
    float target = de_bits_to_f(atomic_load_explicit(&v->gain_bits, memory_order_relaxed)) *
                   de_bits_to_f(atomic_load_explicit(&de_master_bits, memory_order_relaxed));
    float g = v->gain;
    const float dg = (target - g) * (1.0f / DE_AUDIO_RAMP);
    const float k = 1.0f / 32768.0f;
    int done = 0;

    if (clip->rate == de_device_rate)
    {
        int pos = (int)v->pos;
        for (int i = 0; i < frames; i++)
        {
            if (pos >= cf)
            {
                if (!v->loop) { done = 1; break; }
                pos = 0;
            }
            if (g != target)
            {
                g += dg;
                if ((dg > 0 && g > target) || (dg < 0 && g < target))
                    g = target;
            }
            float gk = g * k;
            if (cc == 1)
            {
                float s = src[pos] * gk;
                out[i * ch] += s;
                out[i * ch + 1] += s;
            }
            else
            {
                out[i * ch] += src[pos * 2] * gk;
                out[i * ch + 1] += src[pos * 2 + 1] * gk;
            }
            pos++;
        }
        v->pos = pos;
    }
    else
    {
        // Hiz uyusmazligi (22k import gibi): lineer interpolasyon.
        const double step = (double)clip->rate / de_device_rate;
        double pos = v->pos;
        for (int i = 0; i < frames; i++)
        {
            if (pos >= cf)
            {
                if (!v->loop) { done = 1; break; }
                pos -= cf;
            }
            if (g != target)
            {
                g += dg;
                if ((dg > 0 && g > target) || (dg < 0 && g < target))
                    g = target;
            }
            int p0 = (int)pos;
            int p1 = p0 + 1 < cf ? p0 + 1 : (v->loop ? 0 : p0);
            float t = (float)(pos - p0);
            float gk = g * k;
            if (cc == 1)
            {
                float s = (src[p0] + (src[p1] - src[p0]) * t) * gk;
                out[i * ch] += s;
                out[i * ch + 1] += s;
            }
            else
            {
                out[i * ch] += (src[p0 * 2] + (src[p1 * 2] - src[p0 * 2]) * t) * gk;
                out[i * ch + 1] += (src[p0 * 2 + 1] + (src[p1 * 2 + 1] - src[p0 * 2 + 1]) * t) * gk;
            }
            pos += step;
        }
        v->pos = pos;
    }
    v->gain = g;
    if (done || (v->stopping && g <= 0.0f))
    {
        de_voice_release(v);
        return 0;
    }
    return 1;
}

static void de_audio_stream_cb(float *buffer, int frames, int ch, void *ud)
{
    (void)ud;
    de_drain_cmds();
    memset(buffer, 0, (size_t)frames * ch * sizeof(float));
    int any = 0;
    for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
    {
        de_voice_t *v = &de_voices[i];
        if (atomic_load_explicit(&v->state, memory_order_acquire) != DE_VOICE_PLAYING)
            continue;
        de_mix_voice(v, buffer, frames, ch);
        any = 1;
    }
    if (any)
    {
        atomic_store_explicit(&de_idle_blocks, 0, memory_order_relaxed);
        // sert clip: coklu voice toplaminda tasma
        int n = frames * ch;
        for (int i = 0; i < n; i++)
        {
            float s = buffer[i];
            buffer[i] = s > 1.0f ? 1.0f : (s < -1.0f ? -1.0f : s);
        }
    }
    else
        atomic_fetch_add_explicit(&de_idle_blocks, 1, memory_order_relaxed);
}

// --- cihaz yasam dongusu -----------------------------------------------------

static int de_audio_inited;

static void de_audio_init_once(void)
{
    if (de_audio_inited)
        return;
    de_audio_inited = 1;
    atomic_store(&de_master_bits, de_f_to_bits(1.0f));
    for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
        de_voices[i].clip = -1;
}

static void de_device_open(void)
{
    if (de_device_running || de_suspended)
        return;
    saudio_desc d;
    memset(&d, 0, sizeof(d));
    d.sample_rate = DE_AUDIO_RATE;
    d.num_channels = DE_AUDIO_CHANNELS;
    d.buffer_frames = DE_AUDIO_BUFFER_FRAMES;
    d.stream_userdata_cb = de_audio_stream_cb;
    saudio_setup(&d);
    if (!saudio_isvalid())
        return;
    de_device_rate = saudio_sample_rate();
    atomic_store_explicit(&de_idle_blocks, 0, memory_order_relaxed);
    de_device_running = 1;
}

static void de_device_close(void)
{
    if (!de_device_running)
        return;
    saudio_shutdown(); // callback thread'i durur (join)
    de_device_running = 0;
    de_drain_cmds();   // callback'in goremedigi kalanlar
}

static int de_any_voice_active(void)
{
    for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
        if (atomic_load_explicit(&de_voices[i].state, memory_order_acquire) != DE_VOICE_FREE)
            return 1;
    return 0;
}

static void de_grave_sweep(void)
{
    unsigned tail = atomic_load_explicit(&de_ring_tail, memory_order_acquire);
    for (int i = de_grave_count - 1; i >= 0; i--)
    {
        if ((int)(tail - de_grave[i].seq) >= 0)
        {
            free(de_grave[i].samples);
            de_grave[i] = de_grave[--de_grave_count];
        }
    }
}

// Frame'de bir kez (de_sokol_commit cagirir): idle gating + ertelenmis free.
// Managed tarafin ek bir cagri yapmasi gerekmez.
DE_API void de_audio_tick(void)
{
    if (!de_audio_inited)
        return;
    if (de_grave_count)
        de_grave_sweep();
    if (de_device_running &&
        atomic_load_explicit(&de_idle_blocks, memory_order_relaxed) > DE_AUDIO_IDLE_BLOCKS &&
        !de_any_voice_active())
        de_device_close();
}

// Uygulama arka plana: cihaz kapanir, voice durumu korunur (loop'lar donuste surer).
DE_API void de_audio_suspend(void)
{
    de_audio_init_once();
    de_suspended = 1;
    de_device_close();
}

DE_API void de_audio_resume(void)
{
    de_audio_init_once();
    de_suspended = 0;
    if (de_any_voice_active())
        de_device_open();
}

DE_API void de_audio_shutdown(void)
{
    if (!de_audio_inited)
        return;
    de_device_close();
    for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
        de_voice_release(&de_voices[i]);
    for (int i = 0; i < DE_AUDIO_MAX_CLIPS; i++)
    {
        free(de_clips[i].samples);
        de_clips[i].samples = NULL;
        de_clips[i].alive = 0;
    }
    for (int i = 0; i < de_grave_count; i++)
        free(de_grave[i].samples);
    de_grave_count = 0;
}

DE_API void de_audio_set_master_volume(float v)
{
    de_audio_init_once();
    atomic_store_explicit(&de_master_bits, de_f_to_bits(v < 0 ? 0 : v), memory_order_relaxed);
}

DE_API int de_audio_sample_rate(void) { return de_device_rate; }

// Teshis: SFX cihazi su an acik mi (idle gating / suspend dogrulamasi).
DE_API int de_audio_device_open(void) { return de_device_running; }

// --- klipler -------------------------------------------------------------------

// DPCM blobundan klip: veri KOPYALANIR (cagiran blob'u serbest birakabilir).
// Donus: clip id, hata -1 (bozuk blob / havuz dolu).
DE_API int de_audio_clip_create(const void *blob, int len)
{
    de_audio_init_once();
    if (!blob || len < DE_DPCM_HEADER)
        return -1;
    const unsigned char *p = (const unsigned char *)blob;
    int32_t magic, rate, channels, frames;
    memcpy(&magic, p, 4);
    memcpy(&rate, p + 4, 4);
    memcpy(&channels, p + 8, 4);
    memcpy(&frames, p + 12, 4);
    if (magic != DE_DPCM_MAGIC || rate <= 0 || (channels != 1 && channels != 2) || frames <= 0)
        return -1;
    long long bytes = (long long)frames * channels * 2;
    if (bytes != len - DE_DPCM_HEADER)
        return -1;
    for (int i = 0; i < DE_AUDIO_MAX_CLIPS; i++)
    {
        if (de_clips[i].alive)
            continue;
        int16_t *s = (int16_t *)malloc((size_t)bytes);
        if (!s)
            return -1;
        memcpy(s, p + DE_DPCM_HEADER, (size_t)bytes);
        de_clips[i].samples = s;
        de_clips[i].frames = frames;
        de_clips[i].channels = channels;
        de_clips[i].rate = rate;
        de_clips[i].alive = 1;
        return i;
    }
    return -1;
}

DE_API void de_audio_clip_destroy(int clip)
{
    if (clip < 0 || clip >= DE_AUDIO_MAX_CLIPS || !de_clips[clip].alive)
        return;
    de_cmd_t c = { .type = DE_CMD_STOP_CLIP, .clip = clip };
    de_push_cmd(&c);
    unsigned seq = atomic_load_explicit(&de_ring_head, memory_order_relaxed);
    de_clips[clip].alive = 0;
    int16_t *samples = de_clips[clip].samples;
    de_clips[clip].samples = NULL;
    if (!de_device_running)
    {
        free(samples); // callback yok, komut yerinde islendi
        return;
    }
    if (de_grave_count >= DE_AUDIO_GRAVE)
        de_grave_sweep();
    if (de_grave_count >= DE_AUDIO_GRAVE)
    {
        // mezarlik hala dolu (ayni frame'de 64+ klip silme): callback'in ring'i
        // tuketmesini bekle — nadir, yalniz sahne gecisinde.
        while (atomic_load_explicit(&de_ring_tail, memory_order_acquire) != seq)
            Sleep_ms_1();
        free(samples);
        return;
    }
    de_grave[de_grave_count].samples = samples;
    de_grave[de_grave_count].seq = seq;
    de_grave_count++;
}

DE_API int de_audio_clip_frames(int clip)
{
    return (clip >= 0 && clip < DE_AUDIO_MAX_CLIPS && de_clips[clip].alive) ? de_clips[clip].frames : 0;
}

DE_API int de_audio_clip_rate(int clip)
{
    return (clip >= 0 && clip < DE_AUDIO_MAX_CLIPS && de_clips[clip].alive) ? de_clips[clip].rate : 0;
}

// --- voice'lar ---------------------------------------------------------------

// Bos voice; yoksa en eski calan calinir (stealing).
static int de_voice_alloc(void)
{
    int oldest = -1;
    unsigned oldestSeq = 0;
    for (int i = 0; i < DE_AUDIO_MAX_VOICES; i++)
    {
        de_voice_t *v = &de_voices[i];
        if (atomic_load_explicit(&v->state, memory_order_acquire) == DE_VOICE_FREE)
            return i;
        if (oldest < 0 || (int)(oldestSeq - v->seq) > 0)
        {
            oldest = i;
            oldestSeq = v->seq;
        }
    }
    return oldest;
}

// Donus: voice handle (>=0) ya da -1 (askida / gecersiz klip / ring dolu).
DE_API int de_audio_play(int clip, float volume, int loop)
{
    de_audio_init_once();
    if (de_suspended || clip < 0 || clip >= DE_AUDIO_MAX_CLIPS || !de_clips[clip].alive)
        return -1;
    int idx = de_voice_alloc();
    if (idx < 0)
        return -1;
    de_voice_t *v = &de_voices[idx];
    v->gen = (v->gen + 1) & 0x7FFFFF;
    v->seq = ++de_voice_seq;
    // RESERVED: ayni frame'de ikinci Play bu slotu secmesin. Callback PLAY'de PLAYING yapar.
    atomic_store_explicit(&v->state, DE_VOICE_RESERVED, memory_order_release);
    de_cmd_t c = { .type = DE_CMD_PLAY, .voice = idx, .clip = clip, .loop = loop, .gain = volume < 0 ? 0 : volume, .gen = v->gen };
    if (!de_push_cmd(&c))
    {
        de_voice_release(v);
        return -1;
    }
    de_device_open();
    return de_make_handle(idx, v->gen);
}

DE_API void de_audio_stop(int voice)
{
    if (voice < 0)
        return;
    int idx = de_handle_idx(voice);
    if (idx >= DE_AUDIO_MAX_VOICES || de_voices[idx].gen != de_handle_gen(voice))
        return;
    de_cmd_t c = { .type = DE_CMD_STOP, .voice = idx, .gen = de_handle_gen(voice) };
    de_push_cmd(&c);
}

DE_API void de_audio_set_volume(int voice, float volume)
{
    if (voice < 0)
        return;
    int idx = de_handle_idx(voice);
    if (idx >= DE_AUDIO_MAX_VOICES || de_voices[idx].gen != de_handle_gen(voice))
        return;
    if (de_voices[idx].stopping)
        return;
    atomic_store_explicit(&de_voices[idx].gain_bits, de_f_to_bits(volume < 0 ? 0 : volume), memory_order_relaxed);
}

DE_API int de_audio_is_playing(int voice)
{
    if (voice < 0)
        return 0;
    int idx = de_handle_idx(voice);
    if (idx >= DE_AUDIO_MAX_VOICES || de_voices[idx].gen != de_handle_gen(voice))
        return 0;
    return atomic_load_explicit(&de_voices[idx].state, memory_order_acquire) != DE_VOICE_FREE;
}

DE_API void de_audio_stop_all(void)
{
    de_audio_init_once();
    de_cmd_t c = { .type = DE_CMD_STOP_ALL };
    de_push_cmd(&c);
}

// ===========================================================================
// EDITOR DECODE: mp3 / ogg / wav -> PCM16 (hedef hiz + mono/stereo)
// ===========================================================================
#if !defined(DE_NO_DECODERS)

#define DR_WAV_IMPLEMENTATION
#define DR_WAV_NO_STDIO
#include "dr_wav.h"
#define DR_MP3_IMPLEMENTATION
#define DR_MP3_NO_STDIO
#include "dr_mp3.h"
#define STB_VORBIS_NO_PUSHDATA_API
#define STB_VORBIS_NO_STDIO
#include "stb_vorbis.c"

// Donus 1 = basarili; *outPcm malloc'lu int16 interleaved (de_audio_decode_free ile).
// targetRate <= 0: kaynak hizi korunur. mono != 0: tek kanala indirgenir.
// Resample: lineer (SFX icin yeterli; yukari ornekleme 44.1k->48k tipik yol).
DE_API int de_audio_decode(const void *data, int len, int targetRate, int mono,
                           void **outPcm, int *outFrames, int *outChannels, int *outRate)
{
    *outPcm = NULL;
    *outFrames = *outChannels = *outRate = 0;
    if (!data || len < 4)
        return 0;
    const unsigned char *p = (const unsigned char *)data;

    float *f32 = NULL;
    unsigned int ch = 0, rate = 0;
    uint64_t frames = 0;

    if (memcmp(p, "RIFF", 4) == 0)
    {
        f32 = drwav_open_memory_and_read_pcm_frames_f32(data, (size_t)len, &ch, &rate, &frames, NULL);
    }
    else if (memcmp(p, "OggS", 4) == 0)
    {
        int c = 0, r = 0;
        short *s16 = NULL;
        int n = stb_vorbis_decode_memory(p, len, &c, &r, &s16);
        if (n > 0 && s16)
        {
            frames = (uint64_t)n;
            ch = (unsigned)c;
            rate = (unsigned)r;
            f32 = (float *)malloc((size_t)frames * ch * sizeof(float));
            if (f32)
                for (uint64_t i = 0; i < frames * ch; i++)
                    f32[i] = s16[i] * (1.0f / 32768.0f);
        }
        free(s16);
    }
    else
    {
        drmp3_config cfg;
        f32 = drmp3_open_memory_and_read_pcm_frames_f32(data, (size_t)len, &cfg, &frames, NULL);
        if (f32)
        {
            ch = cfg.channels;
            rate = cfg.sampleRate;
        }
    }
    if (!f32 || frames == 0 || ch == 0 || rate == 0)
    {
        free(f32);
        return 0;
    }

    int outCh = mono ? 1 : (ch >= 2 ? 2 : 1);
    int outR = targetRate > 0 ? targetRate : (int)rate;
    uint64_t outFr = (uint64_t)((double)frames * outR / rate + 0.5);
    if (outFr == 0)
        outFr = 1;
    int16_t *out = (int16_t *)malloc((size_t)outFr * outCh * sizeof(int16_t));
    if (!out)
    {
        free(f32);
        return 0;
    }
    const double step = (double)rate / outR;
    for (uint64_t i = 0; i < outFr; i++)
    {
        double sp = i * step;
        uint64_t i0 = (uint64_t)sp;
        if (i0 >= frames) i0 = frames - 1;
        uint64_t i1 = i0 + 1 < frames ? i0 + 1 : i0;
        float t = (float)(sp - i0);
        const float *a = f32 + i0 * ch, *b = f32 + i1 * ch;
        if (outCh == 1)
        {
            float s = 0;
            for (unsigned c = 0; c < ch; c++)
                s += a[c] + (b[c] - a[c]) * t;
            s /= ch;
            int v = (int)(s * 32767.0f);
            out[i] = (int16_t)(v > 32767 ? 32767 : (v < -32768 ? -32768 : v));
        }
        else
        {
            for (int c = 0; c < 2; c++)
            {
                unsigned sc = c < (int)ch ? (unsigned)c : 0;
                float s = a[sc] + (b[sc] - a[sc]) * t;
                int v = (int)(s * 32767.0f);
                out[i * 2 + c] = (int16_t)(v > 32767 ? 32767 : (v < -32768 ? -32768 : v));
            }
        }
    }
    free(f32);
    *outPcm = out;
    *outFrames = (int)outFr;
    *outChannels = outCh;
    *outRate = outR;
    return 1;
}

DE_API void de_audio_decode_free(void *pcm) { free(pcm); }

#else
DE_API int de_audio_decode(const void *data, int len, int targetRate, int mono,
                           void **outPcm, int *outFrames, int *outChannels, int *outRate)
{
    (void)data; (void)len; (void)targetRate; (void)mono;
    *outPcm = NULL; *outFrames = *outChannels = *outRate = 0;
    return 0;
}
DE_API void de_audio_decode_free(void *pcm) { (void)pcm; }
#endif

// ===========================================================================
// MUZIK / RADYO — platform native player
// ===========================================================================
// Sozlesme (tum platformlar):
//   de_music_open(pathOrUrl, offset, length) -> handle | -1
//     "http://" / "https://" ile baslarsa ag akisi; degilse yerel dosya ve
//     [offset, offset+length) araligi (length 0 = tum dosya) — pak girisleri icin.
//   de_music_state(h): 0 aciliyor, 1 hazir/duraklatilmis, 2 caliyor, 3 bitti, -1 hata
//   play/pause/stop/volume/loop/position/seek; close -> kaynaklar birakilir.

#define DE_MUSIC_MAX 8

#if defined(_WIN32)
// Windows: Media Foundation IMFMediaEngine (audio-only, DXGI yok). Yerel dosya /
// pak araligi icin kendi IStream'imiz (aralikli dosya okuyucu) -> IMFByteStream;
// URL icin dogrudan SetSource. Olaylar MF thread'inde gelir, durum atomic.
// (Basliklar dosya basinda.)

// --- aralikli IStream -------------------------------------------------------
typedef struct
{
    IStreamVtbl *lpVtbl;
    LONG ref;
    HANDLE file;
    LONGLONG base, len, pos;
    CRITICAL_SECTION lock;
} de_range_stream_t;

static HRESULT STDMETHODCALLTYPE rs_QueryInterface(IStream *This, REFIID riid, void **ppv)
{
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_ISequentialStream) || IsEqualIID(riid, &IID_IStream))
    {
        *ppv = This;
        IStream_AddRef(This);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}
static ULONG STDMETHODCALLTYPE rs_AddRef(IStream *This)
{
    return (ULONG)InterlockedIncrement(&((de_range_stream_t *)This)->ref);
}
static ULONG STDMETHODCALLTYPE rs_Release(IStream *This)
{
    de_range_stream_t *s = (de_range_stream_t *)This;
    LONG r = InterlockedDecrement(&s->ref);
    if (r == 0)
    {
        if (s->file != INVALID_HANDLE_VALUE)
            CloseHandle(s->file);
        DeleteCriticalSection(&s->lock);
        free(s);
    }
    return (ULONG)r;
}
static HRESULT STDMETHODCALLTYPE rs_Read(IStream *This, void *pv, ULONG cb, ULONG *pcbRead)
{
    de_range_stream_t *s = (de_range_stream_t *)This;
    EnterCriticalSection(&s->lock);
    LONGLONG remain = s->len - s->pos;
    if (remain < 0) remain = 0;
    ULONG want = (ULONG)(cb < (ULONGLONG)remain ? cb : (ULONG)remain);
    DWORD got = 0;
    if (want > 0)
    {
        LARGE_INTEGER li;
        li.QuadPart = s->base + s->pos;
        if (SetFilePointerEx(s->file, li, NULL, FILE_BEGIN))
            ReadFile(s->file, pv, want, &got, NULL);
        s->pos += got;
    }
    LeaveCriticalSection(&s->lock);
    if (pcbRead) *pcbRead = got;
    return got == cb ? S_OK : S_FALSE;
}
static HRESULT STDMETHODCALLTYPE rs_Write(IStream *This, const void *pv, ULONG cb, ULONG *pcbWritten)
{
    (void)This; (void)pv; (void)cb; if (pcbWritten) *pcbWritten = 0;
    return STG_E_ACCESSDENIED;
}
static HRESULT STDMETHODCALLTYPE rs_Seek(IStream *This, LARGE_INTEGER dlibMove, DWORD dwOrigin, ULARGE_INTEGER *plibNewPosition)
{
    de_range_stream_t *s = (de_range_stream_t *)This;
    EnterCriticalSection(&s->lock);
    LONGLONG np = dwOrigin == STREAM_SEEK_SET ? dlibMove.QuadPart
                : dwOrigin == STREAM_SEEK_CUR ? s->pos + dlibMove.QuadPart
                                              : s->len + dlibMove.QuadPart;
    if (np < 0) np = 0;
    if (np > s->len) np = s->len;
    s->pos = np;
    LeaveCriticalSection(&s->lock);
    if (plibNewPosition) plibNewPosition->QuadPart = (ULONGLONG)np;
    return S_OK;
}
static HRESULT STDMETHODCALLTYPE rs_SetSize(IStream *This, ULARGE_INTEGER n) { (void)This; (void)n; return E_NOTIMPL; }
static HRESULT STDMETHODCALLTYPE rs_CopyTo(IStream *This, IStream *d, ULARGE_INTEGER cb, ULARGE_INTEGER *r, ULARGE_INTEGER *w)
{ (void)This; (void)d; (void)cb; (void)r; (void)w; return E_NOTIMPL; }
static HRESULT STDMETHODCALLTYPE rs_Commit(IStream *This, DWORD f) { (void)This; (void)f; return S_OK; }
static HRESULT STDMETHODCALLTYPE rs_Revert(IStream *This) { (void)This; return S_OK; }
static HRESULT STDMETHODCALLTYPE rs_LockRegion(IStream *This, ULARGE_INTEGER o, ULARGE_INTEGER c, DWORD t)
{ (void)This; (void)o; (void)c; (void)t; return E_NOTIMPL; }
static HRESULT STDMETHODCALLTYPE rs_UnlockRegion(IStream *This, ULARGE_INTEGER o, ULARGE_INTEGER c, DWORD t)
{ (void)This; (void)o; (void)c; (void)t; return E_NOTIMPL; }
static HRESULT STDMETHODCALLTYPE rs_Stat(IStream *This, STATSTG *st, DWORD flag)
{
    (void)flag;
    de_range_stream_t *s = (de_range_stream_t *)This;
    memset(st, 0, sizeof(*st));
    st->type = STGTY_STREAM;
    st->cbSize.QuadPart = (ULONGLONG)s->len;
    st->grfMode = STGM_READ;
    return S_OK;
}
static HRESULT STDMETHODCALLTYPE rs_Clone(IStream *This, IStream **pp) { (void)This; *pp = NULL; return E_NOTIMPL; }

static IStreamVtbl de_range_stream_vtbl = {
    rs_QueryInterface, rs_AddRef, rs_Release, rs_Read, rs_Write, rs_Seek, rs_SetSize,
    rs_CopyTo, rs_Commit, rs_Revert, rs_LockRegion, rs_UnlockRegion, rs_Stat, rs_Clone,
};

static IStream *de_range_stream_open(const char *path, LONGLONG offset, LONGLONG length)
{
    int wn = MultiByteToWideChar(CP_UTF8, 0, path, -1, NULL, 0);
    if (wn <= 0)
        return NULL;
    WCHAR *wpath = (WCHAR *)malloc((size_t)wn * sizeof(WCHAR));
    MultiByteToWideChar(CP_UTF8, 0, path, -1, wpath, wn);
    HANDLE h = CreateFileW(wpath, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    free(wpath);
    if (h == INVALID_HANDLE_VALUE)
        return NULL;
    LARGE_INTEGER sz;
    if (!GetFileSizeEx(h, &sz)) { CloseHandle(h); return NULL; }
    if (length <= 0 || offset + length > sz.QuadPart)
        length = sz.QuadPart - offset;
    if (offset < 0 || length <= 0) { CloseHandle(h); return NULL; }
    de_range_stream_t *s = (de_range_stream_t *)calloc(1, sizeof(*s));
    s->lpVtbl = &de_range_stream_vtbl;
    s->ref = 1;
    s->file = h;
    s->base = offset;
    s->len = length;
    InitializeCriticalSection(&s->lock);
    return (IStream *)s;
}

// --- IMFMediaEngineNotify --------------------------------------------------
typedef struct de_music_s de_music_t;
typedef struct
{
    IMFMediaEngineNotifyVtbl *lpVtbl;
    LONG ref;
    de_music_t *owner;
} de_notify_t;

struct de_music_s
{
    IMFMediaEngine *engine;
    de_notify_t *notify;
    IStream *stream;
    IMFByteStream *bytestream;
    _Atomic int state; // 0 opening, 1 ready/paused, 2 playing, 3 ended, -1 error
    int alive;
    int loop;
    float volume;
};

static de_music_t de_music[DE_MUSIC_MAX];
static int de_mf_started;

static HRESULT STDMETHODCALLTYPE nt_QueryInterface(IMFMediaEngineNotify *This, REFIID riid, void **ppv)
{
    if (IsEqualIID(riid, &IID_IUnknown) || IsEqualIID(riid, &IID_IMFMediaEngineNotify))
    {
        *ppv = This;
        IMFMediaEngineNotify_AddRef(This);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}
static ULONG STDMETHODCALLTYPE nt_AddRef(IMFMediaEngineNotify *This) { return (ULONG)InterlockedIncrement(&((de_notify_t *)This)->ref); }
static ULONG STDMETHODCALLTYPE nt_Release(IMFMediaEngineNotify *This)
{
    de_notify_t *n = (de_notify_t *)This;
    LONG r = InterlockedDecrement(&n->ref);
    if (r == 0)
        free(n);
    return (ULONG)r;
}
static HRESULT STDMETHODCALLTYPE nt_EventNotify(IMFMediaEngineNotify *This, DWORD ev, DWORD_PTR p1, DWORD p2)
{
    (void)p1; (void)p2;
    de_music_t *m = ((de_notify_t *)This)->owner;
    if (!m)
        return S_OK;
    switch (ev)
    {
    case MF_MEDIA_ENGINE_EVENT_CANPLAY:
    case MF_MEDIA_ENGINE_EVENT_LOADEDMETADATA:
        if (atomic_load(&m->state) == 0)
            atomic_store(&m->state, 1);
        break;
    case MF_MEDIA_ENGINE_EVENT_PLAYING:
        atomic_store(&m->state, 2);
        break;
    case MF_MEDIA_ENGINE_EVENT_PAUSE:
        if (atomic_load(&m->state) == 2)
            atomic_store(&m->state, 1);
        break;
    case MF_MEDIA_ENGINE_EVENT_ENDED:
        atomic_store(&m->state, 3);
        break;
    case MF_MEDIA_ENGINE_EVENT_ERROR:
        atomic_store(&m->state, -1);
        break;
    }
    return S_OK;
}
static IMFMediaEngineNotifyVtbl de_notify_vtbl = { nt_QueryInterface, nt_AddRef, nt_Release, nt_EventNotify };

static int de_mf_ensure(void)
{
    if (de_mf_started)
        return 1;
    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE)
        return 0;
    if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_FULL)))
        return 0;
    de_mf_started = 1;
    return 1;
}

static BSTR de_bstr_utf8(const char *s)
{
    int wn = MultiByteToWideChar(CP_UTF8, 0, s, -1, NULL, 0);
    if (wn <= 0)
        return NULL;
    WCHAR *w = (WCHAR *)malloc((size_t)wn * sizeof(WCHAR));
    MultiByteToWideChar(CP_UTF8, 0, s, -1, w, wn);
    BSTR b = SysAllocString(w);
    free(w);
    return b;
}

static void de_music_free(de_music_t *m)
{
    if (m->engine)
    {
        IMFMediaEngine_Shutdown(m->engine);
        IMFMediaEngine_Release(m->engine);
        m->engine = NULL;
    }
    if (m->notify)
    {
        m->notify->owner = NULL;
        IMFMediaEngineNotify_Release((IMFMediaEngineNotify *)m->notify);
        m->notify = NULL;
    }
    if (m->bytestream)
    {
        IMFByteStream_Release(m->bytestream);
        m->bytestream = NULL;
    }
    if (m->stream)
    {
        IStream_Release(m->stream);
        m->stream = NULL;
    }
    m->alive = 0;
}

DE_API int de_music_open(const char *pathOrUrl, long long offset, long long length)
{
    if (!pathOrUrl || !de_mf_ensure())
        return -1;
    int slot = -1;
    for (int i = 0; i < DE_MUSIC_MAX; i++)
        if (!de_music[i].alive) { slot = i; break; }
    if (slot < 0)
        return -1;
    de_music_t *m = &de_music[slot];
    memset(m, 0, sizeof(*m));
    m->alive = 1;
    m->volume = 1.0f;
    atomic_store(&m->state, 0);

    IMFMediaEngineClassFactory *factory = NULL;
    IMFAttributes *attr = NULL;
    HRESULT hr = CoCreateInstance(&CLSID_MFMediaEngineClassFactory, NULL, CLSCTX_INPROC_SERVER,
                                  &IID_IMFMediaEngineClassFactory, (void **)&factory);
    if (FAILED(hr))
        goto fail;
    m->notify = (de_notify_t *)calloc(1, sizeof(de_notify_t));
    m->notify->lpVtbl = &de_notify_vtbl;
    m->notify->ref = 1;
    m->notify->owner = m;
    if (FAILED(MFCreateAttributes(&attr, 2)))
        goto fail;
    IMFAttributes_SetUnknown(attr, &MF_MEDIA_ENGINE_CALLBACK, (IUnknown *)m->notify);
    hr = IMFMediaEngineClassFactory_CreateInstance(factory, MF_MEDIA_ENGINE_AUDIOONLY, attr, &m->engine);
    if (FAILED(hr))
        goto fail;

    int isUrl = _strnicmp(pathOrUrl, "http://", 7) == 0 || _strnicmp(pathOrUrl, "https://", 8) == 0;
    BSTR url = de_bstr_utf8(pathOrUrl);
    if (isUrl)
        hr = IMFMediaEngine_SetSource(m->engine, url);
    else
    {
        m->stream = de_range_stream_open(pathOrUrl, offset, length);
        if (!m->stream || FAILED(MFCreateMFByteStreamOnStream(m->stream, &m->bytestream)))
        {
            SysFreeString(url);
            goto fail;
        }
        // URL yalniz ipucu (uzanti -> cozucu secimi); baytlar aralikli stream'den gelir.
        IMFMediaEngineEx *ex = NULL;
        hr = IMFMediaEngine_QueryInterface(m->engine, &IID_IMFMediaEngineEx, (void **)&ex);
        if (SUCCEEDED(hr))
        {
            hr = IMFMediaEngineEx_SetSourceFromByteStream(ex, m->bytestream, url);
            IMFMediaEngineEx_Release(ex);
        }
    }
    SysFreeString(url);
    if (FAILED(hr))
        goto fail;
    IMFMediaEngine_SetVolume(m->engine, 1.0);
    IMFAttributes_Release(attr);
    IMFMediaEngineClassFactory_Release(factory);
    return slot;

fail:
    if (attr) IMFAttributes_Release(attr);
    if (factory) IMFMediaEngineClassFactory_Release(factory);
    de_music_free(m);
    return -1;
}

static de_music_t *de_music_get(int h)
{
    return (h >= 0 && h < DE_MUSIC_MAX && de_music[h].alive) ? &de_music[h] : NULL;
}

DE_API void de_music_close(int h)
{
    de_music_t *m = de_music_get(h);
    if (m)
        de_music_free(m);
}
DE_API void de_music_play(int h)
{
    de_music_t *m = de_music_get(h);
    if (m && m->engine)
    {
        if (atomic_load(&m->state) == 3)
            IMFMediaEngine_SetCurrentTime(m->engine, 0.0);
        IMFMediaEngine_Play(m->engine);
    }
}
DE_API void de_music_pause(int h)
{
    de_music_t *m = de_music_get(h);
    if (m && m->engine)
        IMFMediaEngine_Pause(m->engine);
}
DE_API void de_music_set_volume(int h, float v)
{
    de_music_t *m = de_music_get(h);
    if (!m) return;
    m->volume = v < 0 ? 0 : (v > 1 ? 1 : v);
    if (m->engine)
        IMFMediaEngine_SetVolume(m->engine, m->volume);
}
DE_API void de_music_set_loop(int h, int loop)
{
    de_music_t *m = de_music_get(h);
    if (!m) return;
    m->loop = loop;
    if (m->engine)
        IMFMediaEngine_SetLoop(m->engine, loop ? TRUE : FALSE);
}
DE_API int de_music_state(int h)
{
    de_music_t *m = de_music_get(h);
    return m ? atomic_load(&m->state) : -1;
}
DE_API double de_music_position(int h)
{
    de_music_t *m = de_music_get(h);
    return (m && m->engine) ? IMFMediaEngine_GetCurrentTime(m->engine) : 0.0;
}
DE_API double de_music_duration(int h)
{
    de_music_t *m = de_music_get(h);
    if (!m || !m->engine) return 0.0;
    double d = IMFMediaEngine_GetDuration(m->engine);
    return d != d || d > 1e12 ? -1.0 : d; // NaN/sonsuz (canli akis) -> -1
}
DE_API void de_music_seek(int h, double sec)
{
    de_music_t *m = de_music_get(h);
    if (m && m->engine)
        IMFMediaEngine_SetCurrentTime(m->engine, sec);
}

DE_API void de_music_shutdown(void)
{
    for (int i = 0; i < DE_MUSIC_MAX; i++)
        if (de_music[i].alive)
            de_music_free(&de_music[i]);
    if (de_mf_started)
    {
        MFShutdown();
        de_mf_started = 0;
    }
}

#else // !_WIN32 — macOS/iOS (AVPlayer), Android (MediaPlayer), Web (HTMLAudio): sonraki dilim.

DE_API int de_music_open(const char *pathOrUrl, long long offset, long long length)
{ (void)pathOrUrl; (void)offset; (void)length; return -1; }
DE_API void de_music_close(int h) { (void)h; }
DE_API void de_music_play(int h) { (void)h; }
DE_API void de_music_pause(int h) { (void)h; }
DE_API void de_music_set_volume(int h, float v) { (void)h; (void)v; }
DE_API void de_music_set_loop(int h, int loop) { (void)h; (void)loop; }
DE_API int de_music_state(int h) { (void)h; return -1; }
DE_API double de_music_position(int h) { (void)h; return 0.0; }
DE_API double de_music_duration(int h) { (void)h; return 0.0; }
DE_API void de_music_seek(int h, double sec) { (void)h; (void)sec; }
DE_API void de_music_shutdown(void) {}

#endif
