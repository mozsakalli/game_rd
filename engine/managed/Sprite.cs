using System;

namespace DigitoyEngine;

// TEK bolge primitifi: motor genelinde "cizilebilir doku bolgesi" hep budur
// (tekil png, atlas uyesi, ileride glyph). Renderer'lar Texture degil Sprite
// referanslar — atlas uyeligi bir DETAY olur: builder ayni instance'in icini
// yerinde gunceller (Page/X/Y), canli referanslar kirilmaz, frame'de lookup yok.
public sealed class Sprite : IAsset
{
    public string Name { get; internal set; }

    // Piksellerin yasadigi doku: tekil png'nin kendisi ya da atlas sayfasi.
    public Texture Page { get; private set; }

    // Sayfadaki kirpilmis bolge (piksel). W==0 = "tum sayfa" modu: bolge/boyut
    // Page'den okunur (async yuklemede pikseller inmeden boyut bilinmez).
    public int X { get; private set; }
    public int Y { get; private set; }
    public int W { get; private set; }
    public int H { get; private set; }
    public int OffX { get; private set; }   // trim ofseti (soldaki/ustteki bosluk)
    public int OffY { get; private set; }
    public int OrigW { get; private set; }  // mantiksal (kirpilmamis) boyut
    public int OrigH { get; private set; }
    public bool Rotated { get; private set; } // rezerve: v1 packer dondurmez

    public bool IsRegion => W > 0;

    // Mantiksal boyut; tum-sayfa modunda Page boyutu (yukleme bitmeden 0 olabilir).
    public int LogicalWidth => IsRegion ? OrigW : Page?.Width ?? 0;
    public int LogicalHeight => IsRegion ? OrigH : Page?.Height ?? 0;

    // Koddan uretim (test/tooling): tum dokuyu kaplayan sprite.
    public static Sprite FromTexture(Texture t, string name = null)
        => new() { Name = name ?? t?.Name, Page = t };

    // Atlas build/loader: ayni instance yerinde yeniden baglanir (motor-ici +
    // editor InternalsVisibleTo; oyun kodu sprite mutasyonuna kapali).
    internal void BindRegion(Texture page, int x, int y, int w, int h,
        int offX, int offY, int origW, int origH)
    {
        Page = page;
        X = x; Y = y; W = w; H = h;
        OffX = offX; OffY = offY;
        OrigW = origW; OrigH = origH;
        Rotated = false;
    }

    // Atlas uyeligi dusunce kaynak dokuya geri doner (tum-sayfa modu).
    internal void BindFull(Texture page)
    {
        Page = page;
        X = Y = W = H = 0;
        OffX = OffY = 0;
        OrigW = OrigH = 0;
        Rotated = false;
    }
}
