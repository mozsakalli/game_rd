# Digitoy sunucu sözleşmesi + board-game client felsefesi

Tüm board oyunları (Gin, Okey, ...) tek sunucuya, tek protokolle bağlanır. Bu belge **kablo sözleşmesi** (sunucu ne
gönderir, ne bekler) ve **client felsefesi**nin (veri nerede, ekran nasıl güncellenir, şüphede ne yapılır) tek sahibidir.
Kaynak: `Digitoy.Common.dll` (sunucu build çıktısı — wire modelleri `M.*` + `Codec`; olduğu gibi referans alınır, kopyalanmaz),
`core.upm` (device key), prod sunucu davranışı. Kod bu belgeye uyar; belge koddan türemez.

Bağlantı dosyaları: `Projects/Gin/Assets/Scripts/Net/` (Wire, Session, World, Auth, DeviceKey, PhaseSet).

---

## 1. Felsefe — 5 kural (kırılganlığı sıfırlayan)

1. **Tek gerçek: sunucu. Yerel kopyası: `World`.** Ekran yalnız `World`'den okur. Delta / tam-görünüm ayrımını yalnız World bilir.
2. **Tek çıkış: `Sync(instant)`.** Her değişiklikte (delta, refresh, prediction, reconnect, hot reload) aynı fonksiyon koşar ve
   ekranı veriye eşitler. Event'e göre motion YOK; motion = ekran↔veri farkının (zone diff) sonucu. `instant=true` → her şey snap.
3. **Tek kurtarma: `Resync()`.** Şüphe neyse (InvalidMove, istek zaman aşımı, reconnect, bilinmeyen paket, validate hatası,
   presenter exception, hot reload) → animasyonları öldür, prediction'ı at, `Refresh` iste, cevap gelince `Sync(instant:true)`.
4. **Prediction = World'e yazılan geçici kopya.** Input → `Predict(action)` kopyayı değiştirir → `Sync(false)` (kart hemen uçar) →
   `Act` gönderilir. Sunucu paketi gelince prediction atılır, gerçek state `Sync(false)`; eşleşiyorsa diff boş → hiçbir şey oynamaz.
5. **Fazlar veriden türer (`PhaseSet` set-diff).** Faz/buton/sıra bayrakları her Sync'te sıfırdan hesaplanır, önceki kümeyle farkı
   "giren/çıkan" olarak presenter'a verilir. Hafızada tutulan tek türev durum bu küme.

Sunucu istemcinin ne bildiğini bilmez → her oturum yüklemesinde her şey (PlayerState + masa tam görünümü) ÇEKİLİR; "aynam taze mi"
tahmini yok. SessionReady olmadan gelen push'lar düşer (ayna yok, delta uygulanamaz; tam görünüm Refresh ile gelir).

---

## 2. Adresler

- Ayar/directory: `GET https://api.digitoy.co/api/config/{configGameId}/settings[?version=&uid=&t=]`
  → `{ version, settings{ios_force_version, android_force_version, maintenance, policy_version, privacy_url, terms_url, support_url},
  gateways:["node1.digitoy.co","node0.digitoy.co"], seeds:[], serverTime }`. Gin için `configGameId = 5`.
- WebSocket: `ws://{gateway}/api/websocket/connect` (tarayıcıda `wss`). Gateway listesi sırayla denenir; gateway atlama yalnız soket
  hiç açılmadıysa (auth reddi ≠ ulaşılamayan gateway). Toplam bağlanma bütçesi 10 s.
- Gin masa/auth `GameId = 7`.

## 3. Çerçeveleme (Codec)

Tek WS, binary mesajlar. `new Codec(isClient:true)`.
- İstek: `BeginEncoding(); Encode(requestId:long); Encode((long)endpoint); EncodeObject(body); GetBuffer()` → `Send`.
- Cevap/push: `BeginDecoding(buf,len,0); requestId = TryDecodeNumber(); body = DecodeObject()`.
  - `requestId > 0` → bekleyen isteğin cevabı.
  - `requestId == -1` → sunucu push'u (oyun olayı).
  - `requestId == 0` → protokol ping cevabı, yok say.
- Text frame / boş mesaj yok sayılır.

## 4. Oturum

**Auth-on-open:** soket açılır açılmaz `Endpoint.AuthLogin (0)` ← `AuthRequest{GameId, Token, FbToken, AppleToken, Params}`
→ `AuthResult{Ok, Token, PlayerId, Error, Ban, Current/Target (AccountConflict), FacebookId, AppleId}`.
- `Error == AlreadyLoggedIn (2010)` tolere edilir (önceki oturum sunucuda düşüyordur).
- Başarıda `Token` saklanır (gameId'ye göre); bir sonraki login'de `AuthRequest.Token` fast-path'tir.
- Reddedilirse sunucu özel CloseReason ile kapatır; 3 s içinde kapatmazsa client kapatır.

**Params (anahtar adları `M.AuthRequestParams`, analitik anahtarları client sahipli):**
| anahtar | değer |
|---|---|
| `deviceKey` | PersistentDeviceKey (bkz. §5) |
| `dkMethod` | `wv` / `aid` / `guid` / platform yöntemi |
| `sdeviceKey`, `sdkMethod` | son başarılı login'in anahtarı+yöntemi (AES'li saklanan `_pk_`) |
| `migrationKey0` | eski client DeviceKey (varsa; sunucu yalnız deviceKey bulunamazsa bakar) |
| `os`, `appVersion`, `platform` | karar anahtarları |
| `model` | analitik (client sahipli, `ClientAuthParams.Model`) |
Sınırlar: ≤32 anahtar, anahtar ≤32 kr, değer ≤256 kr.

**Oturum yükleme (login ve her reconnect'te aynı):** `ConfigDefinitions (101)` ‖ `PlayerGetState (200)` paralel → ikisi de
zorunlu (hata = login başarısız). `PlayerState.CurrentTableId > 0` ise `TableAction(304, Refresh)` → tam görünüm. Sonra SessionReady.

**Heartbeat:** 25 s gönderim boşluğunda `Ping (800)`; sunucu 60 s uygulama çerçevesi görmezse düşürür. İstek zaman aşımı 30 s.

**Kapanış nedeni çözümü:** yerel niyet > WS close code (`M.CloseReason` 4000-4010) > mesajdaki int > 1006 = Aborted.
Reconnect YAPILMAYAN (killed) nedenler: Logout, Unauthorized, AnotherLogin, Kicked, IdleTimeout. Diğerleri: hemen dene,
0/1/2 s backoff, 3 deneme; sonra kullanıcıya bırak.

## 5. Device key (core.upm ile birebir)

- **PersistentDeviceKey** = `HMAC-SHA256(rawId, K) → ilk 16 byte → Guid "D" BÜYÜK HARF`. `K` 14 byte sabit (core.upm
  `PersistentDeviceKey._k`; değiştirilemez — mevcut anahtarlar bozulur).
  rawId: Android `"widevine_"+DRM id` (`wv`) → `"android_"+ANDROID_ID` (`aid`, `9774d56d682e549c` hariç) → rastgele GUID (`guid`);
  iOS Keychain vendorId; Desktop/Windows makine kimliği (Unity `deviceUniqueIdentifier` karşılığı: `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`).
- **Stored key** (`_pk_`): `key + ":" + method`, AES-128-CBC PKCS7, Key=IV= eski client'ın 15 byte anahtarı (core.upm
  `DeviceKeyHelper._oldAesKey`) 16'ya sıfır dolgulu; Base64. Başarılı login'den sonra yazılır, login'de `sdeviceKey/sdkMethod`.
- **Migration key**: eski client DeviceKey (Android MD5 bileşimi, iOS Keychain, desktop deviceUniqueIdentifier). Windows'ta yok.

## 6. World — sunucunun yerel kopyası

```
World.Me        PlayerState  (her gelişte KOMPLE değişir; login/reconnect'te çekilir)
World.Table     TableState   (delta merge / tam görünüm replace; TableId<=0 → null)
World.Defs      definitions  (oturum başında)
World.Predicted TableState   (optimistic kopya; sunucu paketi gelince null)
World.Version   int          (her yazımda ++)
World.Apply(object msg)      tip switch → yaz → Version++ → Changed(part)
```
Türevler (oyun bağımsız): `MyId`, `Nick`, `Amount(itemId)`, `MySeat` (Players'ta PlayerId==MyId, yoksa -1), `IsMyTurn`,
`InTable`, `ServerNow` (ServerTime + yerel geçen süre). Oyuna özel yorum (`GinRules`) World'ü DEĞİŞTİRMEZ, üstünde saf fonksiyondur.

**Apply tipleri:** `TableState`, `GameActionResult{Error, TableState, CallerEvent, Ok}` (→ TableState), `PlayerState`,
`EventExecutionResult`, `FriendPreviewsResult`, `PlayerPreview`, `OnlineStatsResponse`. Tanınmayan tip yok sayılır.

**TableState merge (delta):**
- `TableId <= 0` → masadan düştüm (Action==PlayerKicked ise atıldım) → Table=null.
- Tam görünüm = ilk state, reconcile cevabı (Join/Refresh/PlayNow/Create), TableId değişimi, ya da deltada KENDİ `Hand`'im gelmiş
  → komple replace, `LastMove PlayerLeft/PlayerKicked` koltuklar silinir. Tam görünüm → `Sync(instant:true)`.
- Delta: null alan = değişmedi; dolu skaler = yaz; `Phase/SubPhase None` = değişmedi; liste (`Deck`, `Discards[k]`,
  `PluginState`) = komple değiştir; koltuk: `LastMove PlayerLeft/Kicked` → sil, `PlayerJoined` ya da farklı PlayerId → komple
  değiştir, yoksa `SeatPreview.MergeFrom`. Benim koltuğum silindiyse → Table=null.
- `SeatPreview.EventResult` doluysa Me'ye uygulanır ve sıfırlanır (masa ödülü).

**TableState alanları:** `TableId, GameId, TurnNo, TurnOwner, Phase(TablePhase), SubPhase(TableSubPhase), Players{pos→SeatPreview},
Spectators, Deck, DeckCount, DeckLastNo, Discards{key→List<int>}, Flags, Bet, Jackpot, Action(GameAction), ServerTime, TimeoutAt,
GameEndsAt, PluginState{string→long}`.
`SeatPreview: PlayerId, FacebookId, Nick, Stats, Balance, Hand, LastMove, Score, Position, EventResult, Message`.
`TablePhase: None, Waiting, Starting, Dealing, Playing, Finishing, RoundOver, GameOver`. `TableSubPhase: None, Idle, ReadyCheck, Pass`.

## 7. Masa istekleri

Hepsi `GameActionRequest{GameId, PlayerId?, TableId?, Bet?, Queries, Action, Params(List<long>), Source, Item, MinBet?, MaxBet?}`
→ `GameActionResult`.
| iş | endpoint | Action | not |
|---|---|---|---|
| hızlı oyna | `TablePlayNow 305` | PlayNow | reconcile (tam görünüm) |
| masa kur | `TableCreate 301` | CreateTable | reconcile; Queries = `GinCreateBuilder` |
| katıl / koltuk / seyir | `TableJoin 302` | JoinTable | Params=[seatIndex] (-1 seyirci); reconcile |
| ayrıl | `TableLeave 303` | LeaveTable | reconcile; TableState yoksa Table=null |
| tazele | `TableAction 304` | Refresh | reconcile — **Resync bunu kullanır** |
| hazır | `TableAction 304` | Ready | |
| hamle | `TableAction 304` | oyun aksiyonu | optimistik + fire-and-forget; `Error==InvalidMove(4003)` → Resync |
Hata kodları `M.ErrorCode` (NotYourTurn 4004, AlreadyInTable 4001, TableFull 4008 ...).

## 8. Gin (GameId 7) kablo kuralları

- Tek ıskarta yığını: `Discards[-1]`. Kart kimliği kablo boyunca benzersiz: 0-51, 52/53 joker.
- Sunucu hamle deltalarında ELİ GÖNDERMEZ (el yalnız Join/Refresh/Start/Finish/ResetSeat tam görünümünde). Kendi hamlem prediction'la
  elimdedir; rakibin eli sayıyla (kapalı sanal kartlar) gösterilir.
- Aksiyonlar: `Pass 700, Ready 701, PickFromDeck 702 [card], PickFromDiscard 703 [card], Discard 704 [card], RequestFinish 705
  [11 kart sıralı], OpponentFinished 706 (sunucu), SendFinish 707 [sıralı el], NewGame 708, NewRound 709, DeckEmpty 710`.
- İzinler (veriden): `CanPick = Playing && sıram && el==10`; `CanPickFromDiscard = CanPick && top>=0`;
  `CanPickFromDeck = CanPick && SubPhase!=Pass && DeckLastNo>=0`; `CanDiscard = Playing && sıram && el==11`;
  `CanPass = SubPhase==Pass && sıram && LastMove!=Pass`. Knock/gin: 11 kartı sıralı gönder, atılacak kartı sunucu seçer.
- Validate: Playing'de oturuyorsam el 10 ya da 11 ve tekrarsız; ihlal → Resync. Sunucu timeout'ta yerime oynamaz, masadan atar.
- `Flags` bit alanı: `M.Gin` (Straight 256, Joker 512, Rush 1024, Private 2048, Joinable 4096, Score/LobbyId alanları).

## 9. Hot reload (editör)

Oyun assembly'si ALC reload'da ölür; kimlikli soket engine tarafında `Net.Keep(key, ws)` ile yaşatılır, yeni kod `Net.Take(key)`
ile geri alır ve `Resync()` koşar (oturum = Me + Table yeniden çekilir). Release'te no-op. Reload sırasında gelen push'lar düşer
(zaten SessionReady değil); Refresh tam görünümü getirir.
