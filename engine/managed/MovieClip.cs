using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Flash MovieClip mirasi: keyframe animasyonu CORE ozelliktir. Kendi GameObject'inin
// transform/renk kanallarini surer. Sample(t) SAF degerlendirmedir (scrub bedava);
// Update yalnizca zamani ilerletir. Editor preview'i icin ozel kod YOK: [Previewable]
// + PreviewSession yeter (doc=snapshot, bitis=reload).

public enum AnimChannel
{
    PositionX,
    PositionY,
    Rotation,
    ScaleX,
    ScaleY,
    Alpha,
}

[Serializable]
public sealed class MovieKey
{
    public float Time;
    public float Value;
    public Ease Ease;
}

[Serializable]
public sealed class MovieTrack
{
    public AnimChannel Channel;
    public bool Active = true;
    public List<MovieKey> Keys = new();
}

[Previewable]
public sealed class MovieClip : Component
{
    public List<MovieTrack> Tracks = new();
    public bool Loop = true;
    public bool PlayOnStart = true;
    public float Speed = 1f;

    float _t;
    bool _playing;
    SpriteRenderer _sr; // Alpha kanali icin (varsa)

    public bool IsPlaying => _playing;
    public float Elapsed => _t;

    public float Duration
    {
        get
        {
            float d = 0f;
            foreach (var tr in Tracks)
                if (tr.Keys.Count > 0 && tr.Keys[^1].Time > d)
                    d = tr.Keys[^1].Time;
            return d;
        }
    }

    protected internal override void Awake() => _sr = GetComponent<SpriteRenderer>();

    protected internal override void Start()
    {
        if (PlayOnStart)
            Play();
    }

    public void Play(float time = 0f)
    {
        _t = time;
        _playing = true;
        Sample(_t);
    }

    public void Stop() => _playing = false;

    protected internal override void Update()
    {
        if (!_playing)
            return;
        _t += Time.deltaTime * Speed;
        float d = Duration;
        if (_t >= d)
        {
            if (Loop && d > 0f)
                _t %= d;
            else
            {
                _t = d;
                _playing = false;
            }
        }
        Sample(_t);
    }

    // SAF degerlendirme: t'deki durumu yazar, ic durum degistirmez (editor scrub
    // dogrudan cagirir). Keyframe'ler zaman sirali varsayilir (editor sirali tutar).
    public void SampleAt(float t) => Sample(t);

    void Sample(float t)
    {
        foreach (var tr in Tracks)
        {
            if (!tr.Active || tr.Keys.Count == 0)
                continue;
            Apply(tr.Channel, Evaluate(tr.Keys, t));
        }
    }

    static float Evaluate(List<MovieKey> keys, float t)
    {
        int n = keys.Count;
        if (t <= keys[0].Time)
            return keys[0].Value;
        if (t >= keys[n - 1].Time)
            return keys[n - 1].Value;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid].Time <= t) lo = mid;
            else hi = mid;
        }
        float span = keys[hi].Time - keys[lo].Time;
        float f = span > 1e-5f ? (t - keys[lo].Time) / span : 1f;
        f = Easing.Evaluate(keys[lo].Ease, f); // core LUT — her egri ayni maliyet
        return keys[lo].Value + (keys[hi].Value - keys[lo].Value) * f;
    }

    void Apply(AnimChannel ch, float v)
    {
        switch (ch)
        {
            case AnimChannel.PositionX:
                {
                    var p = transform.localPosition;
                    transform.localPosition = new Vec3(v, p.y, p.z);
                    break;
                }
            case AnimChannel.PositionY:
                {
                    var p = transform.localPosition;
                    transform.localPosition = new Vec3(p.x, v, p.z);
                    break;
                }
            case AnimChannel.Rotation:
                {
                    var r = transform.localEulerAngles;
                    transform.localEulerAngles = new Vec3(r.x, r.y, v);
                    break;
                }
            case AnimChannel.ScaleX:
                {
                    var s = transform.localScale;
                    transform.localScale = new Vec3(v, s.y, s.z);
                    break;
                }
            case AnimChannel.ScaleY:
                {
                    var s = transform.localScale;
                    transform.localScale = new Vec3(s.x, v, s.z);
                    break;
                }
            case AnimChannel.Alpha:
                {
                    if (_sr != null)
                    {
                        var c = _sr.Color;
                        c.a = (byte)Math.Clamp((int)(v * 255f), 0, 255);
                        _sr.Color = c;
                    }
                    break;
                }
        }
    }
}
