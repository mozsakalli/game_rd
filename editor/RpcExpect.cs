using System;
using System.Collections.Generic;
using System.Globalization;
using DigitoyEngine;

namespace DigitoyEditor;

// render.expect: ekran beklentilerini describe listesine karsi dogrular (PASS/FAIL + sapma). Goruntuye
// bakmadan "buton burada mi, yazi bu mu, panelin icinde mi" sorusu kesin yanit alir; screenshot yalniz
// FAIL/ton icin. Girdi JSON dizisi: [{path, visible?, rect?, x?, y?, w?, h?, cx?, cy?, text?, click?,
// alpha?, order?, within?, above?, below?, leftOf?, rightOf?}]. path = tam yol ya da sondan eslesen ad.
static class RpcExpect
{
    static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "path", "visible", "rect", "x", "y", "w", "h", "cx", "cy", "text", "click", "alpha", "order",
        "within", "above", "below", "leftOf", "rightOf",
    };

    [RpcCommand("render.expect", "Ekran beklentilerini DOGRULAR (goruntusuz, kesin): expects=[{path, visible?, rect:'x y w h'?, x/y/w/h/cx/cy?, text? ('~' oneki = icerir), click?, alpha?, order?, within/above/below/leftOf/rightOf:'OtherPath'?}]. path tam yol ya da sondan eslesen ad (coklu eslesme → ilk, ambiguous uyarisi). visible:false → ekranda olmamali. Sayisal karsilastirma tol (mantiksal px, varsayilan 2). Donus: pass, checked, failures:[{path, check, expected, actual}]; verbose=true her kontrolu listeler. Oncelik: describe → expect → yalniz FAIL'de render.screenshot(annotate=true)")]
    static DocNode Expect(RpcContext ctx, DocNode expects, string which = "play", float tol = 2f, bool verbose = false)
    {
        Validate(expects);
        var scene = RpcDescribe.ResolveScene(ctx, which);
        return Run(scene, expects, tol, verbose);
    }

    // Sim kosmadan once yapisal kontrol: hatali anahtar/bicim sim bitince degil, hemen reddedilsin.
    internal static void Validate(DocNode expects)
    {
        if (expects == null || !expects.IsSeq)
            throw new RpcError("expects bir liste olmali: [{path, ...}]");
        for (int i = 0; i < expects.Items.Count; i++)
        {
            var e = expects.Items[i];
            if (!e.IsMap)
                throw new RpcError($"expects[{i}] bir nesne olmali");
            if (string.IsNullOrEmpty(e.GetScalar("path", null)))
                throw new RpcError($"expects[{i}] 'path' gerekli");
            foreach (var kv in e.Fields)
            {
                if (!Keys.Contains(kv.Key))
                    throw new RpcError($"expects[{i}] bilinmeyen anahtar '{kv.Key}' (izinli: {string.Join(", ", Keys)})");
                if (kv.Value.Scalar == null)
                    throw new RpcError($"expects[{i}].{kv.Key} skaler olmali");
            }
            if (e.Get("rect") is { } rc)
                ParseRect(rc.Scalar, i);
        }
    }

    internal static DocNode Run(Scene scene, DocNode expects, float tol = 2f, bool verbose = false)
    {
        var snap = RpcDescribe.Collect(scene, minSize: 0f);
        var failures = DocNode.Seq();
        var checks = verbose ? DocNode.Seq() : null;
        var ambiguous = DocNode.Seq();
        int checked_ = 0;

        for (int i = 0; i < expects.Items.Count; i++)
        {
            var e = expects.Items[i];
            string path = e.GetScalar("path");
            var it = Find(snap, path, out bool amb);
            if (amb)
                ambiguous.Items.Add(DocNode.Scal(path));

            void Check(string check, bool ok, string expected, string actual)
            {
                checked_++;
                if (!ok)
                {
                    var f = DocNode.Map();
                    f.Add("path", DocNode.Scal(path));
                    f.Add("check", DocNode.Scal(check));
                    f.Add("expected", DocNode.Scal(expected));
                    f.Add("actual", DocNode.Scal(actual));
                    failures.Items.Add(f);
                }
                checks?.Items.Add(DocNode.Scal($"{(ok ? "PASS" : "FAIL")} {path} {check}: beklenen {expected}, gercek {actual}"));
            }

            bool wantVisible = e.GetScalar("visible", "true") != "false";
            if (!wantVisible)
            {
                Check("visible", it == null, "false", it == null ? "false" : $"true rect {RectStr(it)}");
                continue;
            }
            if (it == null)
            {
                Check("visible", false, "true", "missing (gorunur degil: yok/inactive/alpha 0/ekran disi)");
                continue;
            }

            if (e.Get("rect") is { } rc)
            {
                var r = ParseRect(rc.Scalar, i);
                bool ok = Near(it.X, r[0], tol) && Near(it.Y, r[1], tol) && Near(it.W, r[2], tol) && Near(it.H, r[3], tol);
                Check("rect", ok, rc.Scalar, RectStr(it));
            }
            Num(e, "x", it.X); Num(e, "y", it.Y); Num(e, "w", it.W); Num(e, "h", it.H);
            Num(e, "cx", it.Cx); Num(e, "cy", it.Cy);
            void Num(DocNode node, string key, float actual)
            {
                if (node.Get(key) is not { } n) return;
                float want = ParseF(n.Scalar, key, i);
                Check(key, Near(actual, want, tol), F(want), F(actual));
            }

            if (e.Get("text") is { } tn)
            {
                string want = tn.Scalar, actual = it.Text ?? "";
                bool ok = want.Length > 0 && want[0] == '~'
                    ? actual.Contains(want.Substring(1), StringComparison.Ordinal)
                    : actual == want;
                Check("text", ok, want, actual);
            }
            if (e.Get("click") is { } cn)
                Check("click", (cn.Scalar == "true") == it.Click, cn.Scalar, it.Click ? "true" : "false");
            if (e.Get("alpha") is { } an)
            {
                float want = ParseF(an.Scalar, "alpha", i);
                Check("alpha", MathF.Abs(it.Alpha - want) <= 0.02f, F(want), F(it.Alpha));
            }
            if (e.Get("order") is { } on)
            {
                int want = (int)ParseF(on.Scalar, "order", i);
                Check("order", it.Order == want, want.ToString(CultureInfo.InvariantCulture), it.Order.ToString(CultureInfo.InvariantCulture));
            }

            Rel(e, "within", it, (a, b) => a.X >= b.X - tol && a.Y >= b.Y - tol && a.X + a.W <= b.X + b.W + tol && a.Y + a.H <= b.Y + b.H + tol);
            Rel(e, "above", it, (a, b) => a.Y + a.H <= b.Y + tol);
            Rel(e, "below", it, (a, b) => a.Y >= b.Y + b.H - tol);
            Rel(e, "leftOf", it, (a, b) => a.X + a.W <= b.X + tol);
            Rel(e, "rightOf", it, (a, b) => a.X >= b.X + b.W - tol);
            void Rel(DocNode node, string key, RpcDescribe.Item a, Func<RpcDescribe.Item, RpcDescribe.Item, bool> pred)
            {
                if (node.Get(key) is not { } n) return;
                var other = Find(snap, n.Scalar, out bool amb2);
                if (amb2)
                    ambiguous.Items.Add(DocNode.Scal(n.Scalar));
                if (other == null) { Check(key, false, n.Scalar, "other missing"); return; }
                Check(key, pred(a, other), $"{n.Scalar} rect {RectStr(other)}", RectStr(a));
            }
        }

        var res = DocNode.Map();
        res.Add("pass", DocNode.Scal(failures.Items.Count == 0 ? "true" : "false"));
        res.Add("checked", DocNode.Scal(checked_.ToString(CultureInfo.InvariantCulture)));
        res.Add("failures", failures);
        if (ambiguous.Items.Count > 0)
            res.Add("ambiguous", ambiguous); // birden cok eslesme: ilk alindi, yolu tam yaz
        if (checks != null)
            res.Add("checks", checks);
        return res;
    }

    // Tam yol ya da sondan eslesen ad ('Panel/Title' → '.../Panel/Title'); deterministik ilk eslesme.
    static RpcDescribe.Item Find(RpcDescribe.Snapshot snap, string path, out bool ambiguous)
    {
        ambiguous = false;
        RpcDescribe.Item first = null;
        foreach (var it in snap.Items)
        {
            if (it.Path != path && !it.Path.EndsWith("/" + path, StringComparison.Ordinal))
                continue;
            if (first == null) first = it;
            else { ambiguous = true; break; }
        }
        return first;
    }

    static bool Near(float a, float b, float tol) => MathF.Abs(a - b) <= tol;
    static string F(float v) => RpcCommands.F(MathF.Round(v, 2));
    static string RectStr(RpcDescribe.Item it)
        => string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(it.X)} {MathF.Round(it.Y)} {MathF.Round(it.W)} {MathF.Round(it.H)}");

    static float ParseF(string s, string key, int i)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v
            : throw new RpcError($"expects[{i}].{key} sayi olmali: '{s}'");

    static float[] ParseRect(string s, int i)
    {
        var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            throw new RpcError($"expects[{i}].rect 'x y w h' olmali: '{s}'");
        var r = new float[4];
        for (int k = 0; k < 4; k++)
            r[k] = ParseF(parts[k], "rect", i);
        return r;
    }
}
