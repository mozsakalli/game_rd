using System.Collections.Generic;

namespace Net;

// Veriden turetilen faz etiketleri (eski digiplay StateManager<T> fikri, docs/server-protocol.md "Felsefe" 4).
// Ekran her Sync'te etiket kumesini sifirdan hesaplar (Compute); PhaseSet onceki kumeyle farki verir:
// Entered = yeni gelen, Exited = dusen. Gecis = fark; "olay geldi" diye degil "veri boyle" diye. instant Sync'te
// cagiran farki okumaz (Reset ile mevcut kumeyi oldugu gibi kabul eder).
public sealed class PhaseSet<T>
{
    readonly HashSet<T> _current = new();
    readonly HashSet<T> _next = new();
    public readonly List<T> Entered = new();
    public readonly List<T> Exited = new();

    public bool Has(T tag) => _current.Contains(tag);

    // Yeni kumeyi `fill` doldurur; fark Entered/Exited'a yazilir.
    public void Update(System.Action<HashSet<T>> fill)
    {
        _next.Clear();
        fill(_next);
        Entered.Clear(); Exited.Clear();
        foreach (var t in _next) if (!_current.Contains(t)) Entered.Add(t);
        foreach (var t in _current) if (!_next.Contains(t)) Exited.Add(t);
        _current.Clear();
        foreach (var t in _next) _current.Add(t);
    }

    // Farksiz kabul (instant Sync): mevcut kume = yeni kume, Entered/Exited bos.
    public void Reset(System.Action<HashSet<T>> fill)
    {
        _current.Clear();
        fill(_current);
        Entered.Clear(); Exited.Clear();
    }
}
