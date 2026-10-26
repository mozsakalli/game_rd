# Main.scene — intent (APPEND-ONLY)

## 2026-10-25 — Puppet/Cue sandbox, dilim 1 (Card kaniti)
PROMPT: "Sanatsal metot" modelini (Puppet = iradesiz gorsel varlik, Cue = disaridan cagrilan parametreli hareket,
Director = oyun kodu) mevcut motoru bozmadan sandbox'ta kanitla. Ilk konu: tek kart, `Deal(to)` ve `Flip(up)`.

YAPI:
- `Card` (Puppet; prefab `Card.prefab`) → `Back` (LayoutBox mavi, her zaman gorunur) → `Front` (LayoutBox krem, "A",
  baslangicta inactive; Back'in USTUNDE cizilir, acilinca on yuz gorunur).
- Cue `Deal(to: Vec3)` 30fps/16 kare: Card.Position 0→$to (OutCubic), Card.Scale 0.6→1 (OutBack). Key[1] of Position
  parametreye BAGLI (Bindings[0]); doc'taki key degeri "0 0 0 0" kalir, deger argumandan gelir.
- Cue `Flip(up: Bool)` 30fps/13 kare: Card.Scale.x 1→0→1 (InQuad/OutQuad), Front.Active Hold false@0, Hold $up@6.
  "!up" yok: Back hep acik, Front=$up yeterli.

INVARIANTLAR:
- Cue karar vermez, gosterir; secim/zamanlama Director'dadir (bu sahnede Director yok, editor onizlemesi var).
- Bir cue tek kliptir; hedef cogullugu (4 koltuk, N slot) PARAMETRE ile cozulur, klip kopyalanmaz (Digiplay stone.xml dersi).
- Puppet/MovieClip ayni ClipTrack/ClipKey verisini ve ayni ClipSampler'i kullanir (editor scrub == oyun oynatmasi).
- Onizleme argumani = parametrenin Default'u; cue degisince sahne doc pozuna doner (PreviewSession.Restart).

DOGRULAMA (RPC): scrub Deal frame 10 → Card pos (224.6,-112.3) scale 1.04; frame 15 → (240,-120) = Default `to`.
Flip frame 7 → scale.x 0.31, Front active. Cue gecisi → pos 0, Front inactive. Acilis testi `[puppet] 25 PASS`.
SONRAKI DILIMLER (bilerek disarida): $rol gizmosu, KeyedList bagi (el), durum cue'lari (Turn/Full), ucus katmani,
Director, arayuz codegen.

## 2026-10-25 — Dilim 2: parametrik AKSIYONLAR (Reparent/Event), Target parametresi, Current key
PROMPT: Parent degisimi cue'nun parametresi olmali ve istenen karede tetiklenebilmeli ("bu bir action degil mi?").
YAPI: sahneye Deck(-300,0) Hand(300,0) Discard(0,200) eklendi; Card Deck'in cocugu (yerel 0). Card instance'inda
Deal OVERRIDE edildi (prefab'daki hali hala Vec3 — bilinçli; sahne override'i): Deal(to: Target) Default=Hand;
Actions: fr0 Reparent(node=kök, target=$to, KeepWorld), fr12 Event("landed"); Position key0 = **Current**
(Play aninda reparent SONRASI yakalanir), key1 = (0,0,0) yani yeni ebeveynin yerel orijini.
INVARIANTLAR: aksiyon = frame girisinde BIR kez tetiklenen yan etki; Reparent scrub'da uygulanir, Event susar; kesmede
kalan Reparent'lar uygulanir, Event atlanir. Geri sarma = reload (Reparent geri alinmaz). Ayni cue Deck→Hand→Discard
icin yeterli: hedef cogullugu parametrede, klip tek.
DOGRULAMA: scrub fr~14 → canli Hand/Card pos -1.4 (doc hala Deck altinda: iz, override degil); geri sarma fr~7 →
reload + yeniden ornekleme, Hand/Card -91 (temiz -600→0 ucusu, surunme yok). Acilis [puppet] 42 PASS.

## 2026-10-25 — Dilim 3: DIRECTOR (duz C#), masa sahnesi
PROMPT: Modelin kod ucunu test et; motora yalnizca kodun soyleyemedigi girsin.
YAPI: Table (TableDirector) → Deck, Discard (LayoutBox yer tutucu), Seat0..3/Hand (LayoutBox Horizontal) → Slot1..5
(LayoutBox 76x104, gorunmez). Camera (CameraComponent + Orthographic 360). Kartlar runtime spawn (Card.prefab, Deck altinda).
Prefab'daki Deal artik Deal(to: Target) + fr0 Reparent + Position Current→0 + Scale 0.6; Flip Scale maskesi X.
KOD: Assets/Scripts/FakeServer.cs (zaman damgali olaylar; dagitim 20 kart tek anda t=0.5, Discard t=1.2 — sunum
bitmeden), TableDirector.cs (olay → cue; el/yer listeleri; stagger zamanlayicisi; Deal bitince Flip zinciri; Rearrange).
INVARIANTLAR: Director puppet'a referansla ulasir (spawn sozlugu), isim/yol yok. Durum anında, sunum kendi hizinda.
Hedef slot ATES aninda hesaplanir (bayat hedef = Slot3 boslugu hatasi, duzeltildi).
DOGRULAMA: sim.run 6.5 s → Seat0 Slot1..4 dolu boslüksuz, Seat1/2 5'er, yerde acik kart, yerden alinan kapali.

## 2026-10-25 — Dilim 3b: FlipWhenLanded zinciri kaldirildi → koreografi cue'da
PROMPT: "ilk bozulan yer FlipWhenLanded; tween motorlarinin klasik tuzagi" → once secenek 1.
YAPI: Card.prefab'a Discard(to: Target) (ucus + acilis: Scale.x 0.6→0→0.6 kare 6..14, Front.Active Hold true @10) ve
Take(to: Target) (ayni ucus + kapanis) cue'lari eklendi; her ikisi fr0 Reparent $to, Position Current→0.
Director: Discarded → Play("Discard", Discard), Picked → Play("Take", slot); CueCompleted abonesi YOK.
INVARIANT: "bitince sunu yap" sahnede degil — ya klipte ya await'te. Olay abonesiyle zincir yasak.
DOGRULAMA: 3 s'de Card4 Take ile donerek ucuyor; 6.5 s yerde Card9/Card2 Front aktif, Seat1/Slot5/Card4 Front inaktif.
