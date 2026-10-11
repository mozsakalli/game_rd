using System;
using DigitoyEngine;

// Hangi platformlarda gorunecegi (bayrak). Motorun platform API'si yok; .NET OperatingSystem uzerinden belirlenir.
[Flags]
public enum Platforms
{
    None = 0,
    Windows = 1 << 0,
    MacOS = 1 << 1,
    Linux = 1 << 2,
    Android = 1 << 3,
    IOS = 1 << 4,
    Web = 1 << 5,
    Mobile = Android | IOS,
    Desktop = Windows | MacOS | Linux,
    All = ~0,
}

// GameObject'i yalniz 'platforms' kumesindeki platformlarda aktif birakir; digerlerinde Awake'te kapatir.
// 'simulate' editorde test icin: None degilse gercek platform yerine o kabul edilir (release'te de etkilidir —
// teslimden once None birakin). Layout cocugunda kapanan GO yerlesimden cikar (komsular kayar).
public class PlatformFilter : Component
{
    public Platforms platforms = Platforms.All;
    public Platforms simulate = Platforms.None;

    public static Platforms Current
    {
        get
        {
            if (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst()) return Platforms.IOS;
            if (OperatingSystem.IsAndroid()) return Platforms.Android;
            if (OperatingSystem.IsBrowser()) return Platforms.Web;
            if (OperatingSystem.IsWindows()) return Platforms.Windows;
            if (OperatingSystem.IsMacOS()) return Platforms.MacOS;
            if (OperatingSystem.IsLinux()) return Platforms.Linux;
            return Platforms.None;
        }
    }

    public bool IsAllowed()
    {
        var cur = simulate != Platforms.None ? simulate : Current;
        return (platforms & cur) != 0;
    }

    protected override void Awake()
    {
        if (!IsAllowed())
            gameObject.SetActive(false);
    }
}
