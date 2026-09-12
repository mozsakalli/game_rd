using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Atlas cozumleme tablosu: kaynak yol -> atlas bolgesi. SpriteRenderer Encode'da
// sorgular: png bir atlasa UYEYSE her modda (edit/play/release) atlastan cizilir —
// davranis paritesi: drawcall sayisi/bleeding editorde de aynidir, "cihazda surpriz" olmaz.
// Kayitlari editor (AtlasBuilder) veya release loader yapar; motor uretim bilmez.
public static class SpriteTable
{
    public sealed class Entry
    {
        public Texture Atlas;
        public int FrameX, FrameY;   // atlastaki konum (piksel)
        public int DrawW, DrawH;     // kirpilmis (cizilen) boyut
        public int OrigW, OrigH;     // orijinal gorsel boyutu
        public int OffX, OffY;       // kirpma ofseti (soldaki/ustteki bosluk)
        public bool Rotated;         // rezerve: v1 packer dondurmez
    }

    static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string path, Entry e) => _entries[path] = e;
    public static void Unregister(string path) => _entries.Remove(path);
    public static void Clear() => _entries.Clear();
    public static Entry Resolve(string path)
        => path != null && _entries.TryGetValue(path, out var e) ? e : null;
}

// Atlas grubu TANIMI (asset): hangi klasorler girer + pack ayarlari. Uretim
// editor/build isidir; runtime yalniz SpriteTable kayitlarini gorur.
[Serializable]
[CreateAssetMenu(MenuName = "Atlas Group", FileName = "AtlasGroup")]
public class AtlasGroup
{
    public List<string> folders = new();  // Assets'e goreli klasor onekleri ("Textures/UI")
    public int padding = 2;
    public int maxSize = 2048;
}
