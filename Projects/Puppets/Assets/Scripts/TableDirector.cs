using System;
using System.Collections.Generic;
using DigitoyEngine;

// DIRECTOR (duz C#, sandbox dilim 3): sunucu olaylarini puppet cue cagrilarina cevirir. Oyun durumu (kimin eli,
// yerdeki kartlar) burada, sahnenin disinda. Puppet'lara isim/yol ile DEGIL referansla ulasir (spawn'da sozluk).
// KURAL: "bitince sunu yap" SAHNEDE degil — ya klipte (Discard = ucus+acilis tek cue) ya da await'te. Olay abonesiyle
// zincir (FlipWhenLanded) tween motorlarinin klasik tuzagi: kesmede asili kalir, durum gorunmez olur.
public sealed class TableDirector : Component
{
    public Prefab CardPrefab;
    public Transform Deck;
    public Transform Discard;
    public Transform[] Hands = Array.Empty<Transform>(); // Seat i → Hand (cocuklari slot)
    public float Stagger = 0.08f;       // dagitimda kart basina kaydirma (sunum karari, sunucu bilmez)
    public bool AutoPlay = true;

    readonly Dictionary<int, Puppet> _cards = new();
    readonly List<Puppet>[] _hands = { new(), new(), new(), new() };
    readonly List<Puppet> _discard = new();
    List<TableEvent> _script;
    int _next;
    float _clock;

    // Sunum zamanlayicisi: "su kadar sonra su cue" — stagger icin kacinilmaz oldu (bulgu #1).
    // Hedef ATES ANINDA hesaplanir (seat + eldeki sira): kuyruga girdigi andaki slot bayatlayabilir (bulgu #4).
    struct Later { public float Due; public Puppet P; public int Seat; }
    readonly List<Later> _later = new();

    protected override void Start()
    {
        _script = FakeServer.Script();
        Debug.Log($"[director] script {_script.Count} olay");
    }

    protected override void Update()
    {
        if (!AutoPlay || _script == null) return;
        _clock += Time.deltaTime;
        while (_next < _script.Count && _script[_next].Time <= _clock)
            Handle(_script[_next++]);
        for (int i = _later.Count - 1; i >= 0; i--)
        {
            if (_later[i].Due > _clock) continue;
            var l = _later[i];
            _later.RemoveAt(i);
            int idx = _hands[l.Seat].IndexOf(l.P);
            if (idx >= 0) l.P.Play("Deal", Slot(l.Seat, idx)); // bu arada elden ciktiysa Discarded zaten oynatti
        }
    }

    void Handle(in TableEvent e)
    {
        switch (e.Kind)
        {
            case TableEventKind.Dealt:
                {
                    var p = Spawn(e.Card);
                    _hands[e.Seat].Add(p);
                    // Sunucu 20 karti ayni anda gonderdi; sunum kart basina kaydirir.
                    _later.Add(new Later { Due = _clock + CountInHands() * Stagger, P = p, Seat = e.Seat });
                    break;
                }
            case TableEventKind.Discarded:
                {
                    var p = _cards[e.Card];
                    _hands[e.Seat].Remove(p);
                    _discard.Add(p);
                    p.Play("Discard", Discard);                    // ucus + acilis TEK cue (koreografi sanatta, zincir yok)
                    Rearrange(e.Seat);                             // kalanlar yeni slotlarina (bulgu #3)
                    break;
                }
            case TableEventKind.Picked:
                {
                    var p = _cards[e.Card];
                    _discard.Remove(p);
                    _hands[e.Seat].Add(p);
                    p.Play("Take", Slot(e.Seat, _hands[e.Seat].Count - 1)); // ucus + kapanis TEK cue
                    break;
                }
            case TableEventKind.DrewFromDeck:
                {
                    var p = Spawn(e.Card);
                    _hands[e.Seat].Add(p);
                    p.Play("Deal", Slot(e.Seat, _hands[e.Seat].Count - 1));
                    break;
                }
        }
    }

    Puppet Spawn(int id)
    {
        var go = CardPrefab.Instantiate(Deck);
        go.name = "Card" + id;
        go.transform.localPosition = default;
        go.transform.localScale = new Vec3(0.6f, 0.6f, 1f); // destede bekleyen kart el olcegi (bulgu #5: "dinlenme pozu" kimin?)
        var p = go.GetComponent<Puppet>();
        _cards[id] = p;
        return p;
    }

    Transform Slot(int seat, int index)
    {
        var hand = Hands[seat];
        return index < hand.childCount ? hand.GetChild(index) : hand; // slot bitti: ele yigil
    }

    int CountInHands()
    {
        int n = 0;
        foreach (var h in _hands) n += h.Count;
        return n - 1;
    }

    // Eldeki kartlar slot sirasina gore yeniden yerlesir (atilan kartin boslugu kapanir).
    void Rearrange(int seat)
    {
        var list = _hands[seat];
        for (int i = 0; i < list.Count; i++)
        {
            var slot = Slot(seat, i);
            if (list[i].transform.parent != slot)
                list[i].Play("Deal", slot);
        }
    }
}
