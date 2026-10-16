# RichTextCheck.scene — intent (APPEND-ONLY)

## 2026-10-17 — rich text gorsel kontrol sahnesi (Copilot, kullanici istegi)
Prompt: LayoutBox rich text (TMPro alt kumesi) motor isi bittikten sonra gorsel dogrulama kullanicida; "kontrol
edilecek sahneyi kur ve ne gormem lazim anlat".

Yapi: ROOT (fitScreen, koyu dolgu, Vertical, padding 16/6) altinda 9 satir LayoutBox:
  R1 duz metin (referans) · R2 b/i/u + ic ice · R3 color (#hex, ad, alpha) · R4 size (44, +10, -8, 50%)
  R5 font=Bold span + size+font · R6 gecersiz tag'ler literal + noparse + kapanmamis <b>
  R7 link + wrap (RichLinkProbe: tiklama Console'a '[richlink] click: id') · R8 ellipsis tek satir
  R9 kontur + golge + b/i/u/color

INVARIANTLAR:
  - Fontlar Assets/Fonts.asset'ten AD ile (Primary varsayilan, Bold); component'te font asset referansi YOK.
  - Metinler loc'a baglanmadi (test sahnesi; tag'ler metnin icinde).
  - RichLinkProbe (Assets/Test/Scripts) yalniz bu sahne icin; LayoutBox.LinkAt(e) kullanim ornegi.

Dogrulama (RPC): render.describe 9 satir ekranda (offscreen 0); input.tap R7 'Terms of Service' -> 'terms',
'Privacy Policy' -> 'privacy', duz metin -> 'outside links'. Editor testleri [richtext] 42 PASS.
