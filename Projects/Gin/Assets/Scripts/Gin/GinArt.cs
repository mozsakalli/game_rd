using DigitoyEngine;

namespace Gin;

// Gin sanat anahtarlari: PSB katman KIMLIKLERI (kalici id; isim degil). Yeni atlas = bu tablo guncellenir,
// ekran kodu anahtar bilmez. Sprite yukleme Scene.Assets uzerinden (cache'li, ayni instance).
public static class GinArt
{
    public const string Cards = "Art/Sprites/CardsAtlas.psb#";
    public const string Seat = "Art/Sprites/SeatAtlas.psb#";
    public const string Table = "Art/Sprites/TableSceneAtlas2.psb#";

    public const string Wall = "Art/Sprites/TableDefaultWall.jpg";
    public const string TableMesh = "Art/Models/TableModel.fbx";
    public const string TableTexture = "Art/Models/TableTexture.jpg";

    // Kartlar (194x280). Katman adi = sunucu kart no (0-51); id'ler PSB'den okundu.
    public const string CardBack = Cards + "143";
    public const string CardShadow = Cards + "1209";
    public const string CardHighlight = Cards + "1619";

    public static string Card(int card)
    {
        int id = card switch
        {
            0 => 2,
            >= 1 and <= 9 => 53 + card,
            10 => 65, 11 => 66, 12 => 67,
            >= 13 and <= 52 => 86 + card,
            _ => 143, // bilinmeyen -> sirt
        };
        return Cards + id;
    }

    // Koltuk
    public const string AvatarBackground = Seat + "868"; // 200x200
    public const string AvatarFrame = Seat + "867";      // 202x202
    public const string AvatarShadow = Seat + "945";     // 212x226
    public const string AvatarSearching = Seat + "889";  // Seat_BlurSearching
    public const string SeatGlow = Seat + "912";         // 372x372, sira isigi

    // Masa HUD
    public const string IconMenu = Table + "739";
    public const string CoinGlyph = Table + "831";
    public const string JackpotPlate = Table + "1215";   // 359x152
    public const string CornerButton = Table + "1042";   // 227x190 (sol alt kart seti)
    public const string ButtonCircle = Table + "1123";
    public const string EmojisGlyph = Table + "943";
    public const string SortGlyph = Table + "942";
    public const string TableLogo = Table + "751";       // 245x156

    public static Sprite Load(Scene scene, string key) => scene.Assets.LoadSprite(key);
}
