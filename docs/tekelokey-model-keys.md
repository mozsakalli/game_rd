# TekelOkey — model anahtar sözlüğü (`GlobalModel.ME` ve diğer Dynamic kökleri)

Amaç: görünümler yalnız **yol adlarına** bağlanır; bu tablo 2. fazda (yeni sunucu, `M.*`) çeviri şartnamesidir.
Port ilerledikçe modül bazında eklenir. Tip = Java sunucudan gelen JSON tipi; "Kim yazar" = tek giriş kapısı.

## `GlobalModel.ME` (oyuncunun kendisi; context = `MEModule`)

| Anahtar | Tip | Anlam | Kim yazar | Kullanan (xml → yeni) |
|---|---|---|---|---|
| name | string | takma ad | login/player paketi | lobby, table, chat.row, gift (`bind:Text="ME.name"`) |
| money | long | bakiye (long tutulur; float YASAK) | player paketi / güncellemeler | lobby, create.table |
| faceId | int | avatar yüz id | player paketi | lobby, table (`bind:FaceId="ME.faceId"`) |
| xp, minxp, maxxp | int | seviye ilerleme (`levelPercent` türetir) | player paketi | — |
| level | int | seviye id | player paketi | — |
| nlb | long | sonraki seviye bonusu | player paketi | — |
| totem | int? | totem id (yoksa -1) | player paketi | — |
| playerMaxProductGroupId | int? | ürün grubu (yoksa -1) | player paketi | — |
| referral, rfbns | string, long | referans kodu / bonusu | player paketi | — |
| pref | Dynamic | tercihler | player paketi | — |
| ipcountry | string | ülke | login | — |

Notlar
- Bind yolu context'e görelidir: `MEModule` GO'sunun altında `"name"`, dışarıdan global olarak `"$ME.name"` (henüz SetGlobal yok).
- Eksik anahtar = yazılmaz, son değer kalır (motor kuralı); eski `Dynamic` null-güvenli indexer ile aynı his.
