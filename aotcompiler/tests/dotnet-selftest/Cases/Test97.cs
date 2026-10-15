using System;
namespace Demo97
{
    // GC RefMap (docs/engine-notes.md): ref tasiyan struct'in GC'ye gorunen TUM yollari tek testte —
    // class alani, ic ice struct, struct dizisi, STATIK struct, kutulu struct, Nullable<struct>, generic struct<ref> (canon).
    // (a) tam dongu x R suresi stderr'e ([gcbench], diff'e girmez) -> isaretleme performans olcumu;
    // (b) TRI-COLOR BARRIER SINAVI (TriColor): dongu MARK'tayken BEYAZ (henuz taranmamis) nesnenin tek referansi SIYAH hedefe
    //     tasinir (statik slot, statik struct, MARK sirasinda dogan -> siyah class alani / struct alani / struct dizisi elemani,
    //     canon List<T>, Dictionary<int,struct>). Bariyer yoksa nesne supurulur; ardindan ayni boy tahsis slotu geri kullanir ->
    //     okunan deger .NET'ten sapar. (Eski Demo51 deseni "genc -> yasli" artimli GC'de bariyer gerektirmez: yeni nesne siyah dogar.)
    // (c) butunluk toplami -> eksik tarama = bozuk deger/cokme.
    class Obj { public int v; public Obj(int v) { this.v = v; } }
    struct Pair { public Obj a; public int k; public Obj b; }
    struct Nest { public Pair p; public int z; public Pair q; }
    struct KV<T> { public T key; public int n; }
    class Node { public Obj o; public Pair pr; public Node next; public Nest[] arr; }
    class Link { public Link next; public Obj v1; public Obj v2; public Obj v3; public Obj v4; public Obj v5; public Obj v6; public Obj v7; public Obj v8; public Obj v9; }
    class Holder { public Obj r; public Pair pr; public Nest[] arr; }
    class App97
    {
        static Pair sPair;
        static Nest sNest;
        static Nest[] sArr;
        static KV<string>[] sKv;
        static object sBox;
        static Pair? sNul;
        static Node head;
        static Link chain;
        static Obj sHold;
        static Pair sHoldPair;
        static Nest sHoldNest;
        static Holder blackH;
        static System.Collections.Generic.List<Obj> blackList;
        static System.Collections.Generic.Dictionary<int, Pair> blackMap;

        static int TriColor()
        {
            // beyaz bolge: 1000 halkalik zincir; kurbanlar SON halkada (256'lik dilim ulasamaz)
            chain = null;
            for (int i = 0; i < 1000; i++) { var l = new Link(); l.next = chain; chain = l; }
            Link tail = chain;
            while (tail.next != null) tail = tail.next;
            tail.v1 = new Obj(1001); tail.v2 = new Obj(1002); tail.v3 = new Obj(1003); tail.v4 = new Obj(1004);
            tail.v5 = new Obj(1005); tail.v6 = new Obj(1006); tail.v7 = new Obj(1007); tail.v8 = new Obj(1008); tail.v9 = new Obj(1009);
            sHold = null; sHoldPair = default(Pair); sHoldNest = default(Nest); blackH = null; blackList = null; blackMap = null;
            GC.Collect();            // temiz baslangic (idle)
            GC.Collect(0);           // dongu basla: kokler grilenir
            GC.Collect(0);           // 256 nesne taranir; zincirin sonu hala beyaz
            // kurbanlarin tek referansini siyah hedeflere tasi
            sHold = tail.v1; tail.v1 = null;                                   // statik ref slot
            sHoldPair.a = tail.v2; tail.v2 = null;                             // statik struct alani (ldsflda + stfld)
            blackH = new Holder();                                             // MARK'ta dogdu -> siyah
            blackH.r = tail.v3; tail.v3 = null;                                // siyah class alani
            blackH.arr = new Nest[2];
            blackH.arr[1].q.b = tail.v4; tail.v4 = null;                       // siyah struct dizisi elemani ici (ldelema + stfld)
            Pair tp; tp.a = tail.v5; tp.k = 5; tp.b = null; blackH.pr = tp; tail.v5 = null;   // struct kopyasi -> siyah class alani
            Nest nn = default(Nest); nn.p.b = tail.v6; blackH.arr[0] = nn; tail.v6 = null;   // struct kopyasi -> siyah dizi elemani
            Nest n2 = default(Nest); n2.q.a = tail.v7; sHoldNest = n2; tail.v7 = null;       // struct kopyasi -> statik struct
            blackList = new System.Collections.Generic.List<Obj>(); blackList.Add(tail.v8); tail.v8 = null;   // canon List<__Canon> ici
            blackMap = new System.Collections.Generic.Dictionary<int, Pair>();
            Pair mp; mp.a = tail.v9; mp.k = 9; mp.b = null; blackMap[7] = mp; tail.v9 = null; // Dictionary entry struct dizisi
            GC.Collect();            // donguyu bitir (bariyersiz kurbanlar supurulur)
            for (int i = 0; i < 20000; i++) new Obj(-9); // ayni boy slotlar geri kullanilsin -> olu okuma .NET'ten sapar
            return sHold.v + sHoldPair.a.v + blackH.r.v + blackH.arr[1].q.b.v + blackH.pr.a.v + blackH.arr[0].p.b.v
                 + sHoldNest.q.a.v + blackList[0].v + blackMap[7].a.v;
        }

        // A4 (canon): generic METOT x struct<ref> argumani x IEnumerable — Items<ValueTuple<int,string>> canon govdesi
        // Items<ValueTuple<int,__Canon>>; tip handle'lari sozlukten gelmeli (eskiden generated.c derlenmezdi).
        static int Items<T>(System.Collections.Generic.IEnumerable<T> e) { int n = 0; foreach (var x in e) { object o = x; if (o is T) n += 1; if (o.GetType() == typeof(T)) n += 10; if (o.Equals(x)) n += 100; if (((T)o).Equals(x)) n += 1000; } return n; }
        static int CanonStructArg()
        {
            var l = new System.Collections.Generic.List<(int, string)>(); l.Add((1, "ab")); l.Add((22, "c"));
            var k = new System.Collections.Generic.List<KV<string>>(); k.Add(new KV<string> { key = "zz", n = 3 });
            var p = new System.Collections.Generic.List<Pair>(); Pair p0 = default(Pair); p0.a = new Obj(1); p.Add(p0); Pair p1 = default(Pair); p1.b = new Obj(2); p.Add(p1);
            var ints = new System.Collections.Generic.List<int> { 1, 2, 3 };
            // NOT: T[] IEnumerable<T> olarak gecirilemez (dizi descriptor'unda itable yok -> InvalidCast): bilinen sinir, ayri is.
            return Items(l) + Items(k) + Items(p) + Items(ints);
        }

        public static int Run()
        {
            const int N = 100000;
            head = null;
            for (int i = 0; i < N; i++)
            {
                var n = new Node();
                n.o = new Obj(i); n.pr.a = new Obj(i * 2); n.pr.b = new Obj(i * 3); n.next = head;
                if ((i & 63) == 0)
                {
                    n.arr = new Nest[4];
                    for (int j = 0; j < 4; j++) { n.arr[j].p.a = new Obj(j); n.arr[j].q.b = new Obj(j + 1); }
                }
                head = n;
            }
            sPair.a = new Obj(1); sPair.b = new Obj(2);
            sNest.p.a = new Obj(3); sNest.q.b = new Obj(4);
            sArr = new Nest[8];
            for (int i = 0; i < 8; i++) sArr[i].p.b = new Obj(10 + i);
            sKv = new KV<string>[8];
            for (int i = 0; i < 8; i++) { sKv[i].key = "k" + i; sKv[i].n = i; }
            Pair bp; bp.a = new Obj(5); bp.b = new Obj(6); bp.k = 7; sBox = bp;
            Pair np; np.a = new Obj(8); np.b = null; np.k = 0; sNul = np;

            int t0 = Environment.TickCount;
            for (int r = 0; r < 100; r++) GC.Collect();
            int ms = Environment.TickCount - t0;
            Console.Error.WriteLine("[gcbench] 100x full GC: " + ms + " ms (nodes=" + N + ")");

            int acc = 0;
            for (int f = 0; f < 60; f++)
            {
                sPair.a = new Obj(f); sNest.q.a = new Obj(f * 2); sArr[f % 8].p.a = new Obj(f * 3);
                Pair tp; tp.a = new Obj(f * 5); tp.b = new Obj(f * 7); tp.k = f; sBox = tp;
                sNul = tp;
                sKv[f % 8].key = "v" + f;
                Node n = head;
                for (int s = 0; s < f % 17; s++) n = n.next;
                n.pr.a = new Obj(f * 11);
                if (n.arr != null) n.arr[f % 4].q.a = new Obj(f * 13);
                GC.Collect(0);
                Pair bx = (Pair)sBox;
                acc += sPair.a.v + sNest.q.a.v + sArr[f % 8].p.a.v + bx.a.v + bx.b.v + sNul.Value.a.v + sKv[f % 8].key.Length + n.pr.a.v;
                if (n.arr != null) acc += n.arr[f % 4].q.a.v;
            }
            GC.Collect();
            long sum = 0;
            for (var n = head; n != null; n = n.next)
            {
                sum += n.o.v + n.pr.a.v + n.pr.b.v;
                if (n.arr != null) for (int j = 0; j < 4; j++) sum += n.arr[j].p.a.v + n.arr[j].q.b.v;
            }
            for (int i = 0; i < 8; i++) sum += sArr[i].p.b.v + sKv[i].n;
            return acc + (int)(sum % 1000003) + sPair.b.v + sNest.p.a.v + sNest.q.b.v + TriColor() + CanonStructArg() * 100;
        }
    }
}
