#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: de_async kuyrugu + NativeAsync (docs/network.md Faz 0a). Cok thread'den complete ->
// tek drain'de FIFO ve kayipsiz; Await/Listen/TryTake sozlesmeleri; sahipsiz tamponun serbest birakilmasi.
public static class AsyncQueueTests
{
    static int _pass, _fail;

    public static void Run()
    {
        _pass = _fail = 0;
        NativeAsync.Pump(); // onceki (acilis) tamamlanmalari temizle: test kuyrugu bos baslasin

        // 1) Await: tamamlanma Pump'ta cozulur, oncesinde degil.
        int j1 = NativeAsync.NewJob();
        var t1 = NativeAsync.Await(j1);
        NativeAsync.Complete(j1, 1, 7, 9);
        Check(!t1.IsCompleted, "Await Pump'tan once tamamlanmaz (ana thread sozlesmesi)");
        NativeAsync.Pump();
        Check(t1.IsCompleted && t1.Result.Ok && t1.Result.A == 7 && t1.Result.B == 9 && t1.Result.Job == j1, "Await Pump'ta cozulur, a/b tasinir");

        // 2) Hata kodu: Ok=false, Task fault degil (sonuc tasir).
        int j2 = NativeAsync.NewJob();
        var t2 = NativeAsync.Await(j2);
        NativeAsync.Complete(j2, -1);
        NativeAsync.Pump();
        Check(t2.IsCompleted && !t2.Result.Ok && !t2.IsFaulted, "hata kodu Ok=false, Task fault olmaz");

        // 3) Gecersiz job hemen basarisiz.
        var t3 = NativeAsync.Await(-1);
        Check(t3.IsCompleted && !t3.Result.Ok, "job<=0 aninda basarisiz");

        // 4) Listen: olaylar (EventBit) sirayla, son tamamlanmada kayit duser.
        int j4 = NativeAsync.NewJob();
        var seen = new List<int>();
        NativeAsync.Listen(j4, c => seen.Add(c.IsEvent ? c.A : -c.A));
        NativeAsync.Complete(j4, NativeAsync.EventBit | 1, 1);
        NativeAsync.Complete(j4, NativeAsync.EventBit | 1, 2);
        NativeAsync.Complete(j4, 1, 3);
        NativeAsync.Pump();
        NativeAsync.Complete(j4, NativeAsync.EventBit | 1, 4); // kayit dustu: sahipsiz, sessizce birakilir
        NativeAsync.Pump();
        Check(seen.Count == 3 && seen[0] == 1 && seen[1] == 2 && seen[2] == -3, "Listen olaylari FIFO, son tamamlanma kaydi dusurur");
        Check(NativeAsync.NativePending() == 0, "sahipsiz olay kuyrukta kalmaz");

        // 5) TryTake: yalniz istenen job cekilir, digerleri kuyrukta kalir.
        int j5a = NativeAsync.NewJob(), j5b = NativeAsync.NewJob();
        NativeAsync.Complete(j5a, 1, 10);
        NativeAsync.Complete(j5b, 1, 20);
        Check(NativeAsync.TryTake(j5b, out var cb) && cb.A == 20 && NativeAsync.NativePending() == 1, "TryTake yalniz o job'u ceker");
        Check(!NativeAsync.TryTake(j5b, out _), "TryTake ikinci kez bos");
        var t5 = NativeAsync.Await(j5a);
        NativeAsync.Pump();
        Check(t5.IsCompleted && t5.Result.A == 10, "kalan tamamlanma Pump'ta Await'e gider");

        // 6) Cok thread: 8 thread x 500 complete -> hepsi gelir, her thread kendi icinde sirali.
        const int threads = 8, per = 500;
        var jobs = new int[threads];
        for (int i = 0; i < threads; i++) jobs[i] = NativeAsync.NewJob();
        var got = new List<int>[threads];
        for (int i = 0; i < threads; i++) { got[i] = new List<int>(); int ji = jobs[i]; NativeAsync.Listen(ji, c => got[Array.IndexOf(jobs, c.Job)].Add(c.A)); }
        var ts = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            int ti = i;
            ts[i] = new Thread(() => { for (int k = 0; k < per; k++) NativeAsync.Complete(jobs[ti], NativeAsync.EventBit | 1, k); });
            ts[i].Start();
        }
        foreach (var t in ts) t.Join();
        NativeAsync.Pump();
        bool ok = true;
        for (int i = 0; i < threads && ok; i++)
        {
            if (got[i].Count != per) { ok = false; break; }
            for (int k = 0; k < per; k++) if (got[i][k] != k) { ok = false; break; }
        }
        Check(ok, $"{threads}x{per} cok thread complete: kayipsiz ve thread ici sirali");
        for (int i = 0; i < threads; i++) NativeAsync.Unlisten(jobs[i]);

        // 7) Frame.Next bir Pump sonra tamamlanir.
        var f = Frame.Next();
        Check(!f.IsCompleted, "Frame.Next hemen tamamlanmaz");
        NativeAsync.Pump();
        Check(f.IsCompleted, "Frame.Next Pump'ta tamamlanir");

        Cancellation();

        Console.WriteLine($"[async] {_pass} PASS, {_fail} FAIL");
    }

    // CancellationToken ile iptal: Await/Listen/Frame.Next/Wait + Component.destroyCancellationToken.
    static void Cancellation()
    {
        // Await iptali: onCancel kosar, Task iptal; sonra gelen native tamamlanma sahipsiz -> birakilir.
        var cts = new CancellationTokenSource();
        int job = NativeAsync.NewJob();
        int cancels = 0;
        var t = NativeAsync.Await(job, cts.Token, () => cancels++);
        cts.Cancel();
        Check(t.IsCanceled && cancels == 1 && !t.IsFaulted, "Await iptal: onCancel + IsCanceled");
        NativeAsync.Complete(job, 1, 5);
        NativeAsync.Pump();
        Check(NativeAsync.NativePending() == 0 && NativeAsync.PendingCount == 0, "iptal sonrasi gec tamamlanma sahipsiz, sizmaz");
        bool threw = false;
        try { t.GetAwaiter().GetResult(); } catch (OperationCanceledException) { threw = true; }
        Check(threw, "iptal edilen Await beklenince OperationCanceledException");

        // Zaten iptal edilmis token: hemen iptal, kuyruga kayit yok.
        int cancels2 = 0;
        var t2 = NativeAsync.Await(NativeAsync.NewJob(), cts.Token, () => cancels2++);
        Check(t2.IsCanceled && cancels2 == 1 && NativeAsync.PendingCount == 0, "iptal edilmis token ile Await aninda iptal");

        // Normal tamamlanma kaydi dusurur: sonra Cancel gelse de onCancel kosmaz.
        var cts3 = new CancellationTokenSource();
        int job3 = NativeAsync.NewJob(); int cancels3 = 0;
        var t3 = NativeAsync.Await(job3, cts3.Token, () => cancels3++);
        NativeAsync.Complete(job3, 1); NativeAsync.Pump();
        cts3.Cancel();
        Check(t3.IsCompletedSuccessfully && cancels3 == 0, "tamamlanan Await'in iptal kaydi dusmus");

        // Listen iptali: olaylar artik gelmez, onCancel kosar.
        var cts4 = new CancellationTokenSource();
        int job4 = NativeAsync.NewJob(); int events = 0, cancels4 = 0;
        NativeAsync.Listen(job4, c => events++, cts4.Token, () => cancels4++);
        NativeAsync.Complete(job4, NativeAsync.EventBit | 1); NativeAsync.Pump();
        cts4.Cancel();
        NativeAsync.Complete(job4, NativeAsync.EventBit | 1); NativeAsync.Pump();
        Check(events == 1 && cancels4 == 1 && NativeAsync.NativePending() == 0, "Listen iptal: olay kesilir, onCancel bir kez");

        // Frame.Next(ct) iptal; Pump'ta sessizce dusurulur.
        var cts5 = new CancellationTokenSource();
        var fn = Frame.Next(cts5.Token);
        cts5.Cancel();
        Check(fn.IsCanceled, "Frame.Next(ct) iptal");
        NativeAsync.Pump();

        // Wait.Seconds: dt birikimi; iptal.
        var ws = Wait.Seconds(0.25f);
        NativeAsync.Pump(0.1f); NativeAsync.Pump(0.1f);
        Check(!ws.IsCompleted, "Wait.Seconds 0.2/0.25 bitmedi");
        NativeAsync.Pump(0.1f);
        Check(ws.IsCompletedSuccessfully, "Wait.Seconds 0.3/0.25 bitti");
        var cts6 = new CancellationTokenSource();
        var ws2 = Wait.Seconds(1f, cts6.Token);
        NativeAsync.Pump(0.5f); cts6.Cancel(); NativeAsync.Pump(1f);
        Check(ws2.IsCanceled, "Wait.Seconds iptal");
        var to = Wait.Timeout(0.2f);
        NativeAsync.Pump(0.1f);
        Check(!to.IsCancellationRequested, "Wait.Timeout erken degil");
        NativeAsync.Pump(0.2f);
        Check(to.IsCancellationRequested, "Wait.Timeout suresi dolunca iptal");

        // destroyCancellationToken: component yok edilince bekleyen is duser.
        var prev = Scene.Active;
        var s = Scene.Create("async-cancel-test");
        s.Catalog = prev?.Catalog;
        Scene.SetActive(s);
        var go = new GameObject("victim");
        var comp = go.AddComponent<SpriteRenderer>();
        var token = comp.destroyCancellationToken;
        int job7 = NativeAsync.NewJob(); int cancels7 = 0;
        var t7 = NativeAsync.Await(job7, token, () => cancels7++);
        var waitT = Wait.Seconds(10f, token);
        s.Update(0f);
        Check(!token.IsCancellationRequested && !t7.IsCompleted, "destroy token canliyken aktif degil");
        GameObject.Destroy(go);
        s.Update(0f); // destroy frame sonunda uygulanir
        Check(token.IsCancellationRequested && t7.IsCanceled && cancels7 == 1 && waitT.IsCanceled, "GO destroy -> destroyCancellationToken iptal, bekleyen Await/Wait duser");
        Check(comp.destroyCancellationToken.IsCancellationRequested, "yok edilmis component'in token'i iptal durumunda");
        NativeAsync.Complete(job7, 1); NativeAsync.Pump(1f);
        Check(NativeAsync.NativePending() == 0 && NativeAsync.PendingCount == 0, "destroy sonrasi gec tamamlanma temizlenir");
        Scene.SetActive(prev);
        Scene.Unload(s);
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[async] FAIL: {name}"); }
    }
}
#endif
