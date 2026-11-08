using System;
using System.Security.Cryptography;
using System.Text;
using DigitoyEngine;

namespace Net;

// Cihaz kimligi (docs/server-protocol.md "Device key"; core.upm PersistentDeviceKey ile birebir turetim):
//   key = HMAC-SHA256(rawId, _k)[0..16] -> Guid "D" BUYUK HARF.
// rawId kaynagi platforma gore (Android widevine/ANDROID_ID, iOS keychain vendorId); motorun cihaz-id API'si henuz
// yok -> core.upm'in kendi son caresi olan `guid` yontemi: bir kez uretilen GUID PlayerPrefs'te saklanir.
// Saklanan anahtar (`_pk_`, "key:method"): ilk basarili login'de yazilir; rawId kaynagi degisse de oyuncu ayni kalir.
// core.upm bunu AES ile sarmalar; motor corelib'inde AES yok -> PlayerPrefs'te duz saklanir (platform deposu zaten
// uygulamaya ozel). Sunucuya ayni alan adlariyla gider: deviceKey/dkMethod + sdeviceKey/sdkMethod.
public static class DeviceKey
{
    static readonly byte[] _k = { 0x64, 0x39, 0x74, 0x6F, 0x79, 0x5F, 0x70, 0x64, 0x6B, 0x5F, 0x73, 0x31, 0x67, 0x6E };
    const string RawKey = "dk_raw";
    const string StoredKey = "_pk_";
    public const string Method = "guid";

    public static string Key
    {
        get
        {
            string raw = PlayerPrefs.GetString(RawKey, "");
            if (string.IsNullOrEmpty(raw))
            {
                raw = Guid.NewGuid().ToString("D");
                PlayerPrefs.SetString(RawKey, raw);
                PlayerPrefs.Save();
            }
            return Derive(raw);
        }
    }

    static string Derive(string rawId)
    {
        using var h = new HMACSHA256(_k);
        var mac = h.ComputeHash(Encoding.UTF8.GetBytes(rawId));
        var g = new byte[16];
        Array.Copy(mac, g, 16);
        return new Guid(g).ToString("D").ToUpperInvariant();
    }

    // Onceki basarili login'de saklanan anahtar; yoksa ikisi de null.
    public static void Stored(out string key, out string method)
    {
        key = null; method = null;
        string s = PlayerPrefs.GetString(StoredKey, "");
        int i = s.IndexOf(':');
        if (i <= 0) return;
        key = s.Substring(0, i);
        method = s.Substring(i + 1);
    }

    public static void StoreCurrent()
    {
        PlayerPrefs.SetString(StoredKey, Key + ":" + Method);
        PlayerPrefs.Save();
    }
}
