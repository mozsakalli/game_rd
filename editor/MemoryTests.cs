#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: mem.* bellek izleyici (RpcMemory) — ifade cozumu (kisa/tam ad, ic tip, cakisma), static/instance uye, index/anahtar,
// derinlik kesmesi, dongu korumasi, getter istisnasi, non-public isaretlemesi, motor nesnesi ozeti.
public static class MemoryTests
{
    static int _pass, _fail;

    enum Suit { Hearts, Spades }
    struct Pt { public int x, y; public override string ToString() => x + " " + y; }
    sealed class Card { public int Id; public Suit Suit; public Card Self; public string Label => "c" + Id; public int Boom => throw new InvalidOperationException("no"); }
    sealed class Seat { public List<Card> Hand = new(); public Dictionary<int, string> Names = new(); public Dictionary<Suit, int> BySuit = new(); public Pt Pos; GameObject _go; public void SetGo(GameObject g) => _go = g; public GameObject Go => _go; }
    sealed class Svc
    {
#pragma warning disable CS0649, CS0414 // test verisi: reflection ile okunur
        public static Svc I = new();
        static readonly int _secretStatic = 7;
        [Sensitive] static string _staticToken = "tok";
        public static string Version => "v1";
        public Seat MySeat = new();
        public Seat Empty;
        int _pending = 3;
        public Func<int> Cb = () => 1;
        [Sensitive] public string clientToken = "secret";
        [Sensitive] string keyPassword = "pw";
#pragma warning restore CS0649, CS0414
        public static class Dup { public static int A = 1; }
    }
    static class Other { public static class Dup { public static int A = 2; } }

    public static void Run()
    {
        _pass = _fail = 0;
        RpcMemory.AssembliesOverride = _ => new[] { typeof(MemoryTests).Assembly };
        try
        {
            var svc = Svc.I;
            svc.MySeat.Hand.Add(new Card { Id = 5, Suit = Suit.Spades });
            svc.MySeat.Hand.Add(new Card { Id = 9 });
            svc.MySeat.Hand[0].Self = svc.MySeat.Hand[0];
            svc.MySeat.Names[-1] = "pile";
            svc.MySeat.BySuit[Suit.Hearts] = 4;
            svc.MySeat.Pos = new Pt { x = 1, y = 2 };
            svc.MySeat.SetGo(new GameObject("Table"));

            // kesif
            var types = RpcMemory.TypesForTest("Svc", "editor");
            Check(types.Get("types").Items.Exists(t => t.GetScalar("name") == "MemoryTests.Svc" && t.GetScalar("statics") == "4"), "mem.types: ic tip adi + statics sayisi");

            // statics
            var st = RpcMemory.StaticsForTest("Svc");
            var members = st.Get("members");
            Check(members.Get("I")?.GetScalar("value") == "{MemoryTests.Svc}", "mem.statics: public alan ozeti");
            Check(members.Get("~_secretStatic")?.GetScalar("value") == "7", "mem.statics: non-public '~'");
            Check(members.Get("Version")?.GetScalar("value") == "v1", "mem.statics: static property");
            Check(RpcRedact.Apply(RpcMemory.StaticsForTest("Svc")).Get("members").Get("~_staticToken")?.Scalar == RpcRedact.Mask, "mem.statics: [Sensitive] static alan maskeli");
            Check(Throws(() => RpcMemory.DumpForTest("Svc._staticToken"), "Sensitive"), "yol: hassas static alandan gecis reddi");

            // gizlilik: [Sensitive] alan maskelenir (RpcHost.Ok yolu), uzerinden gecis reddedilir
            var masked = RpcRedact.Apply(RpcMemory.DumpForTest("Svc.I", depth: 1)).Get("value");
            Check(masked.GetScalar("clientToken") == RpcRedact.Mask, "dump: [Sensitive] public alan maskeli");
            Check(masked.GetScalar("~keyPassword") == RpcRedact.Mask, "dump: [Sensitive] non-public '~' alan maskeli");
            Check(Throws(() => RpcMemory.DumpForTest("Svc.I.clientToken"), "Sensitive"), "yol: hassas alandan gecis reddi");
            Check(Throws(() => RpcMemory.DumpForTest("Svc.I.keyPassword"), "Sensitive"), "yol: hassas non-public alandan gecis reddi");

            // dump: nesne, derinlik, non-public, delegate atlanir
            var d = RpcMemory.DumpForTest("Svc.I", depth: 1);
            var v = d.Get("value");
            Check(v.GetScalar("$type") == "MemoryTests.Svc", "dump: $type");
            Check(v.GetScalar("MySeat") == "{MemoryTests.Seat}", "dump: depth kesmesi → ozet");
            Check(v.GetScalar("~_pending") == "3", "dump: non-public instance alan");
            Check(v.GetScalar("Empty") == "null" && v.Get("Cb") == null, "dump: null + delegate atlandi");

            // yol: liste index, dictionary anahtari (int/enum), struct ToString, property, enum
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Hand[0].Id").Get("value").Scalar == "5", "yol: liste index + alan");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Hand[1].Label").Get("value").Scalar == "c9", "yol: property getter");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Hand[0].Suit").Get("value").Scalar == "Spades", "yol: enum adi");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Names[-1]").Get("value").Scalar == "pile", "yol: dictionary int anahtar (negatif)");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.BySuit[Hearts]").Get("value").Scalar == "4", "yol: dictionary enum anahtar");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Pos").Get("value").Scalar == "1 2", "struct ToString override kompakt");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat.Names").Get("value").GetScalar("-1") == "pile", "dictionary haritasi");
            var hand = RpcMemory.DumpForTest("Svc.I.MySeat.Hand", depth: 3, max: 1).Get("value");
            Check(hand.Items.Count == 2 && hand.Items[1].Scalar == "…+1", "koleksiyon max kesmesi");
            Check(hand.Items[0].GetScalar("Self") == "{MemoryTests.Card} (cycle)", "dongu korumasi");
            Check(hand.Items[0].GetScalar("Boom").StartsWith("!InvalidOperationException"), "getter istisnasi dump'i bozmaz");
            Check(RpcMemory.DumpForTest("Svc.I.MySeat", depth: 1).Get("value").GetScalar("~_go") == "{GameObject 'Table'}", "motor nesnesi tek satir");

            // tip cozumu: ic tip tam ad, cakisma, yok
            Check(RpcMemory.DumpForTest("MemoryTests.Svc.Dup.A").Get("value").Scalar == "1" && RpcMemory.DumpForTest("MemoryTests.Other.Dup.A").Get("value").Scalar == "2", "ic tip tam ad ile cozum");
            Check(Throws(() => RpcMemory.DumpForTest("Dup.A"), "birden cok"), "kisa ad cakismasi → adaylar");
            Check(Throws(() => RpcMemory.DumpForTest("NoSuchType.X"), "tip yok"), "bilinmeyen tip");
            Check(Throws(() => RpcMemory.DumpForTest("Svc.I.Nope"), "uye yok"), "bilinmeyen uye");
            Check(Throws(() => RpcMemory.DumpForTest("Svc.I.MySeat.Hand[9]"), "sinir disi"), "index sinir disi");
            Check(Throws(() => RpcMemory.DumpForTest("Svc.I.Empty.Hand"), "null"), "null uzerinden yurume");
            GameObject.Destroy(svc.MySeat.Go);
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("[memory] EXCEPTION: " + e);
        }
        finally
        {
            RpcMemory.AssembliesOverride = null;
        }
        Console.WriteLine($"[memory] {_pass} PASS, {_fail} FAIL");
    }

    static bool Throws(Action a, string contains)
    {
        try { a(); return false; }
        catch (RpcError e) { return e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase); }
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[memory] FAIL: {name}"); }
    }
}
#endif
