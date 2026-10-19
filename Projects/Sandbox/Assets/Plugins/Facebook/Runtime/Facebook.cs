using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;

// ORNEK PLUGIN (docs/plugins.md): Facebook Login. Runtime = tipli API + ayar; native taraf Android/ (Gradle library), iOS/, WASM/.
// Protokol (bu plugin'in kendi secimi; core bilmez): JSON, "id" ile korelasyon.
//   -> {"id":1,"method":"login","permissions":["public_profile","email"]}
//   <- {"id":1,"ok":true,"token":"...","userId":"..."} | {"id":1,"ok":false,"error":"cancel"}
//   <- {"event":"deepLink","url":"..."}  (istenmemis; soguk baslangicta inbox'ta bekler, OnMessage'a dusur)
// Runtime/ icinde #if ve DllImport YOK: editor/Windows/wasm'da ayni kod derlenir; host yoksa Available=false.
public sealed class Facebook : Plugin<Facebook.Config>
{
    [Serializable]
    public sealed class Config
    {
        [Required] public string appId = "";
        [Required, Sensitive] public string clientToken = "";
        public string displayName = "";
        public bool autoLogAppEvents = true;
    }

    public enum LoginStatus { Success, Cancel, Fail, Unavailable, Timeout }
    public struct LoginResult { public LoginStatus Status; public string Token, UserId, Error; }

    readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    int _nextId;
    public string PendingDeepLink { get; private set; }

    public async Task<LoginResult> Login(float timeout = 60f, params string[] permissions)
    {
        if (!Available) return new LoginResult { Status = LoginStatus.Unavailable, Error = "no host plugin" };
        var perms = permissions != null && permissions.Length > 0 ? permissions : new[] { "public_profile" };
        var r = await Request("login", "\"permissions\":" + JsonArray(perms), timeout);
        if (r == null) return new LoginResult { Status = LoginStatus.Timeout };
        if (r.GetBool("ok"))
            return new LoginResult { Status = LoginStatus.Success, Token = r.GetString("token"), UserId = r.GetString("userId") };
        string err = r.GetString("error", "fail");
        return new LoginResult { Status = err == "cancel" ? LoginStatus.Cancel : LoginStatus.Fail, Error = err };
    }

    public void Logout() => Send("{\"method\":\"logout\"}");

    // Tek-yanitli istek: id -> TCS; timeout/iptal -> null (exception yok; SDK hatasi sonuc degeridir).
    async Task<JsonNode> Request(string method, string extraJson, float timeout)
    {
        int id = ++_nextId;
        var tcs = new TaskCompletionSource<JsonNode>();
        _pending[id] = tcs;
        using var cts = Wait.Timeout(timeout);
        using var reg = cts.Token.Register(() => { _pending.Remove(id); tcs.TrySetResult(null); });
        if (!Send("{\"id\":" + id + ",\"method\":\"" + method + "\"" + (extraJson != null ? "," + extraJson : "") + "}"))
        {
            _pending.Remove(id);
            return null;
        }
        return await tcs.Task;
    }

    protected override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        var idNode = j["id"];
        if (idNode != null)
        {
            if (_pending.Remove(idNode.AsInt(), out var tcs)) tcs.TrySetResult(j);
            return;
        }
        if (j.GetString("event") == "deepLink") PendingDeepLink = j.GetString("url");
    }

    static string JsonArray(string[] items)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[");
        for (int i = 0; i < items.Length; i++) { if (i > 0) sb.Append(","); sb.Append("\"").Append(items[i]).Append("\""); }
        return sb.Append("]").ToString();
    }
}
