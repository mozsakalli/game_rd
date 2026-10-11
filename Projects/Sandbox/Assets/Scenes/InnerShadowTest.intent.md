# InnerShadowTest.scene — intent (APPEND-ONLY)

## 2026-10-11 — ic golge gorsel testi
- PROMPT: LayoutBox'a performans dusurmeden ic golge ekle; dogrulama sahnesi.
- YAPI: Root (fitScreen, Horizontal, Center/Center, fill #EBEBF0) > 4 kutu 160x160, beyaz dolgu, innerShadow #0000006E:
  "b<r" radius 24 / blur 8; "r=0" radius 0 / blur 12; "b>r" radius 6 / blur 16; "border" radius 16 / blur 16 + border 3
  #3C3C46 + dis golge #00000050 blur 10.
- INVARIANTLAR: dis golge ic golgeyle birlikte gorunur; ic golge border'in ICINDE; r=0 kosesi yuvarlak soner, kutu disina
  sizma yok; b>r kosesinde ucta koyu sizinti yok.
- DOGRULAMA: tools/innershadow_test.ps1 (sahneyi scene.open ile kurar, sim.run screenshot) + 4x fiziksel kose kirpimi.
  Sahne doc'u kaydedilmedi (undo'lu birakildi); dosya scene.new ile bos olusturuldu.
