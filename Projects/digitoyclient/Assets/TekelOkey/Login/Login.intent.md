# Login.scene — intent (APPEND-ONLY)

## 2026-11-08 — Açılış MovieClip'i
**Prompt:** Güzel bir açılış: logo büyükten küçülerek gelsin; logo tam küçüldüğünde stone'lar fırlasın; butonlar minik
dikey hareket + alpha ile, aralarında minik delay ile yerlerine gelsin; en sonunda policy alpha ile belirsin.

**Yapı:** ROOT'taki tek `MovieClip` (Fps 30, FrameCount 78, Loop=false, PlayOnStart=true). Zaman çizelgesi:
- 0–24  LOGO Transform.Scale 3→0.96 (OutQuart) →1 (OutSine).
- 20–44 STONE1..4 (2'şer kare kaymalı): Transform.Scale 0→1 OutBack + LayoutBox.anchoredPos logo merkezi (0 -151.6) → authored konum, OutCubic.
- 34/40/46 (+15) FB/GUEST/APPLE: `margin` top 40→0 (Mask=2, OutCubic) + renk A kanalları 0→authored alpha (Mask=8, OutSine):
  fill.color, fill.color2, borderFill.color, textFill.color, (FB/GUEST) innerShadowColor, (FB) textShadowColor.
- 60–78 POLICY textFill.color alpha 0→255 OutSine.
- BUTTONS konteyneri: height 0→120 sabit, alignChildren Start, overflowY Visible (margin animasyonu konteyneri büyütmesin, kardeşler birbirini itmesin).

**INVARIANTLAR:**
- LayoutBox'ta toplu alpha yok; fade = renk alanlarının A kanalı (Mask=8). Yeni renk alanı eklenirse clip'e track eklenmeli. (GÜNCELLENDİ: aşağıya bak — RenderGroup.Alpha)
- Horizontal layout çocuklarında Transform.Position / anchoredPos layout tarafından ezilir → dikey ofset için `margin` kullanılır.
- Butonların margin animasyonu için BUTTONS sabit yükseklik + Start + overflowY Visible şart; auto height'a dönülürse butonlar birlikte zıplar.
- Frame 0 authored: butonlar ve policy frame 0'da görünmez (alpha 0, margin 40). Clip 78'de biter, loop yok.
- Taşların bitiş konumu authored anchoredPos ile aynı tutulmalı; taş taşınırsa ilgili track'in son key'i güncellenmeli.

**Doğrulama:** `sim_run(restart, frames=N, describe)` tek çağrıda (play çağrılar arasında gerçek zamanda ilerler, çapraz çağrı ölçümü güvenilmez).
Frame 46: FB y=381 (≈son), GUEST 388, APPLE 400 (+40 mantıksal) → kademeli; frame 80: üç buton y=380, authored ile aynı; policy görünür.
captureEvery=8 kare dizisi görsel olarak doğrulandı. Sahne KAYDEDİLMEDİ (undo'lu).

## 2026-11-08 — Revizyon: örtüşmeli, daha keskin
**Prompt:** Olaylar ardışık olduğu için kesik duruyor; iç içe geçsinler, daha keskin easing.
**Değişen zaman çizelgesi (FrameCount 60):**
- 0–14 LOGO Scale 3→0.97 **OutExpo**; 14–18 →1.03, 18–24 →1 (OutQuad puls — taşlar "darbeyle" fırlar).
- 12–29 STONE1..4 (1'er kare kaymalı, 14 kare): Scale OutBack, konum **OutExpo**.
- 20/24/28 (+12) FB/GUEST/APPLE: margin 50→0 **OutExpo**, alpha **OutQuad**.
- 30–50 POLICY alpha OutSine.
Her blok bir öncekinin ~%60'ında başlar; delay 4 kare. Alan/track yapısı ve invariantlar değişmedi.
**Doğrulama:** kullanıcı editörde izleyip onayladı ("kontrol ettim ok"). Not: `stop=true` sonrası ilk `sim_run` başlangıçta ~9 kare gerçek zaman yer;
güvenilir ölçüm için play açıkken `restart=true` (yanıtta `restarted:true`) kullan. Sahne KAYDEDİLMEDİ (undo'lu).

## 2026-11-08 — Revizyon: alpha → RenderGroup
**Prompt:** Alpha kullanan objelere RenderGroup ekle, tek Alpha track'i; renk property'si başına alpha track'lerini kaldır.
**Yapı:** FB, GUEST, APPLE, POLICY'ye `RenderGroup` (comp index 1) eklendi. Clip'te renk-A track'leri (16 adet) silindi; yerine
4 adet `goId:RenderGroup:0` / `Alpha` (Mask=1) track'i: 0 Hold → aynı başlangıç karesi → 1 (aynı ease/zaman). Track sayısı 27→16.
**INVARIANT (günceller):** fade = `RenderGroup.Alpha`; LayoutBox renk alanlarına dokunulmaz. Butona yeni renk alanı eklemek clip değişikliği gerektirmez.
**Doğrulama:** frame 30 / 60 screenshot: kademeli giriş ve son kare authored. Sahne KAYDEDİLMEDİ (undo'lu).
