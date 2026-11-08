using System.Collections.Generic;
using DigitoyEngine;
using M;

namespace Net;

// Auth-on-open istegi (docs/server-protocol.md "Oturum"). Token varsa hizli yol; cihaz anahtari her zaman gider
// (token yoksa/duserse sunucu oyuncuyu ondan bulur; eski anahtar sdeviceKey ile gelir).
public static class Auth
{
    public static AuthRequest Request(int gameId, string token)
    {
        DeviceKey.Stored(out string storedKey, out string storedMethod);
        var p = new Dictionary<string, string>
        {
            [AuthRequestParams.DeviceKey] = DeviceKey.Key,
            [AuthRequestParams.DeviceKeyMethod] = DeviceKey.Method,
            [AuthRequestParams.OS] = Os(),
            [AuthRequestParams.AppVersion] = string.IsNullOrEmpty(GameHost.Version) ? "0.0.0" : GameHost.Version,
            [AuthRequestParams.Platform] = PlatformName(),
        };
        if (!string.IsNullOrEmpty(storedKey)) { p[AuthRequestParams.StoredDeviceKey] = storedKey; p[AuthRequestParams.StoredDeviceKeyMethod] = storedMethod; }
        return new AuthRequest { GameId = gameId, Token = string.IsNullOrEmpty(token) ? null : token, Params = p };
    }

    // Motorun platform API'si gelene dek derleme sabitleri (release hedefi eklendikce dallanir).
    static string PlatformName()
    {
#if DE_AOT
        return DevicePlatform.Unknown.ToString();
#else
        return GameHost.IsDev ? DevicePlatform.Editor.ToString() : DevicePlatform.Windows.ToString();
#endif
    }

    static string Os()
    {
#if DE_AOT
        return "native";
#else
        return System.Environment.OSVersion.ToString();
#endif
    }
}
