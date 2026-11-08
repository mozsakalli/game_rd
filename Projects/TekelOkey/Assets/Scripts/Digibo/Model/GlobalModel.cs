using System;
using Digiplay;

// PORT: digiplay-net/Digibo/model/GlobalModel.cs — MINIMAL dilim (kullanici karari: modul geldikce alan/fonksiyon tasinir).
// Sunucu verisinin TEK GIRIS KAPISI burasi olacak; gorunumler yalniz anahtar YOLLARINA baglanir ("ME.money"), bkz.
// docs/tekelokey-model-keys.md. 2. fazda (yeni sunucu) ME'nin arkasi degisir, yollar/sahneler degismez.
namespace Digibo.Model
{
    public class GlobalModel
    {
        public static Dynamic ME = new Dynamic();

        // --- ME turevli saf okuyucular (orijinalle birebir) ---
        public static long levelPercent
        {
            get { return (int)Math.Ceiling((myXP - minXP) * 100 / (double)(maxXP - minXP)); }
        }

        public static long myMoney => ME.ToLong("money");
        public static int myXP => ME.ToInt("xp");
        public static int minXP => ME.ToInt("minxp");
        public static int maxXP => ME.ToInt("maxxp");
        public static int myTotemId => ME.ContainsKey("totem") ? ME.ToInt("totem") : -1;
        public static int MyMaxProductGroupId => ME.ContainsKey("playerMaxProductGroupId") ? ME.ToInt("playerMaxProductGroupId") : -1;
        public static string myNick => ME.ToString("name");
        public static string myReferral => ME.ToString("referral");
        public static long myReferralBonus => ME.ToLong("rfbns");
        public static int myLevelId => ME.ToInt("level");
        public static long myNextLevelBonus => ME.ToLong("nlb");
        public static Dynamic myPreferences => ME["pref"] as Dynamic;
        public static string myCountry => ME.ToString("ipcountry");
    }
}
