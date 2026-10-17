using DigitoyEngine;

// Editor Play'de Facebook host taklidi (docs/plugins.md): login istegine bir sonraki frame basari doner, soguk baslangic
// deep link'ini kuyruga koyar. Oyun akisi editorde test edilir; gercek SDK yok.
public sealed class FacebookMock : PluginMock<Facebook>
{
    public bool CancelNext;

    public FacebookMock()
    {
        Send("{\"event\":\"deepLink\",\"url\":\"sandbox://mock\"}"); // instance henuz yok: Plugin.Get'te teslim edilir
    }

    public override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        if (j.GetString("method") == "login")
        {
            int id = j.GetInt("id");
            if (CancelNext) { CancelNext = false; Send("{\"id\":" + id + ",\"ok\":false,\"error\":\"cancel\"}"); }
            else Send("{\"id\":" + id + ",\"ok\":true,\"token\":\"mock-token\",\"userId\":\"42\"}");
        }
    }
}
