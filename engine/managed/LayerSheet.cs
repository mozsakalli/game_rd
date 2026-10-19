using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Katmanli goruntu (PSD/PSB, ileride Aseprite) asset'i: SAF VERI. Her katman bir Sprite'tir ("x.psb#<id>");
// pikseller importer'in kendi sayfasinda (Standalone "sprites" tablosu) ya da bir AtlasGroup sayfasinda yasar —
// AssetDatabase sprite tablosuyla yerinde baglar (Font ile ayni model). Bu sinif dokuman duzenini tasir:
// her katmanin dokumandaki konumu/boyutu (ileride "katmanlari sahneye yerlestir"). Format bilgisi yok.
[Serializable, Asset]
public sealed class LayerSheet : ISpriteSource
{
    [Serializable]
    public struct Layer
    {
        public string Id;     // kalici kimlik (PSD lyid; yoksa ad tabanli)
        public string Name;   // katman adi (grup yolu dahil olabilir)
        public int X, Y;      // dokuman uzayinda sol-ust (y asagi)
        public int W, H;      // katman piksel kutusu
        public bool Visible;
        public Sprite Sprite; // "x.psb#Id" — tablo gelince baglanir
    }

    public int Width, Height; // dokuman boyutu
    public Layer[] Layers = Array.Empty<Layer>();
}
