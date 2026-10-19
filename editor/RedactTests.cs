#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: [Sensitive] RPC cikis filtresi (docs/plugins.md "Sensitive") — agac maskeleme, metin ikamesi (YAML/JSON/properties),
// masum alanlara dokunmama, maske geri yazma reddi.
public static class RedactTests
{
    static int _pass, _fail;

    sealed class Secretive { [Sensitive] public string clientToken = ""; public string appId = ""; [Sensitive] public string keyPassword = ""; }

    public static void Run()
    {
        _pass = _fail = 0;
        try
        {
            RpcRedact.Rebuild(new[] { typeof(RedactTests).Assembly });
            Check(RpcRedact.IsSensitiveKey("clientToken") && RpcRedact.IsSensitiveKey("keystorePassword") && !RpcRedact.IsSensitiveKey("appId"), "kume: attribute'lu alanlar");

            // agac
            var m = DocNode.Map();
            m.Add("appId", DocNode.Scal("123"));
            m.Add("clientToken", DocNode.Scal("abc-secret"));
            var inner = DocNode.Map(); inner.Add("keyPassword", DocNode.Scal("pw")); inner.Add("alias", DocNode.Scal("k"));
            m.Add("signing", inner);
            var seq = DocNode.Seq(); var item = DocNode.Map(); item.Add("clientToken", DocNode.Scal("x")); seq.Items.Add(item); m.Add("list", seq);
            RpcRedact.Apply(m);
            Check(m.GetScalar("appId") == "123" && m.GetScalar("clientToken") == RpcRedact.Mask, "agac: hassas skaler maskelendi, masum kaldi");
            Check(inner.GetScalar("keyPassword") == RpcRedact.Mask && inner.GetScalar("alias") == "k", "agac: ic ice");
            Check(item.GetScalar("clientToken") == RpcRedact.Mask, "agac: liste elemani");

            // metin
            string yaml = "type: Config\nfields:\n  appId: 123\n  clientToken: abc-secret\n  keyPassword: \"pw\"\n";
            string r = RpcRedact.RedactText(yaml);
            Check(r.Contains("appId: 123") && r.Contains("clientToken: ***") && r.Contains("keyPassword: ***") && !r.Contains("abc-secret") && !r.Contains("pw"), "metin: YAML satirlari");
            string json = "{\"appId\":\"1\",\"clientToken\":\"abc\",\"x\":2}";
            string rj = RpcRedact.RedactText(json);
            Check(rj.Contains("\"clientToken\":***") && rj.Contains("\"x\":2") && !rj.Contains("abc"), "metin: JSON satir ici");
            string props = "signing.keyPassword=pw\nsigning.keyAlias=k\n";
            string rp = RpcRedact.RedactText(props);
            Check(rp.Contains("keyPassword=***") && rp.Contains("keyAlias=k"), "metin: properties");
            Check(RpcRedact.RedactText("myclientToken: zzz") == "myclientToken: zzz", "metin: kelime siniri (onek eslesmez)");
            var txt = DocNode.Map(); txt.Add("content", DocNode.Scal(yaml)); RpcRedact.Apply(txt);
            Check(!txt.GetScalar("content").Contains("abc-secret"), "agac icindeki metin skaleri de suzuldu");

            // girdi
            bool threw = false;
            try { RpcRedact.GuardInput("clientToken", "***"); } catch (RpcError) { threw = true; }
            Check(threw, "maske geri yazimi reddi");
            threw = false;
            try { RpcRedact.GuardInput("clientToken", "real"); RpcRedact.GuardInput("appId", "***"); } catch (RpcError) { threw = true; }
            Check(!threw, "gercek deger / masum alan serbest");
            threw = false;
            try { RpcRedact.GuardText("fields:\n  clientToken: ***\n"); } catch (RpcError) { threw = true; }
            Check(threw, "asset.write maskeli satir reddi");
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("[redact] EXCEPTION: " + e);
        }
        finally
        {
            RpcRedact.Rebuild(App.Code?.CodeAssemblies); // gercek kume
        }
        Console.WriteLine($"[redact] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[redact] FAIL: {name}"); }
    }
}
#endif
