# LoginScene — niyet gunlugu (append-only)

## 2026-10-10 — Motion preset proof (SAHNE KAYDEDILMEDI; kullanici kararina birakildi)
Prompt: "Isimli motion preset'leriyle giris animasyonu: buton grubu asagidan gelsin, butonlar sirayla pop-in, policy metni en son."
Yapi:
- Common/Scripts/Motion.cs: MotionPreset {PopIn, SlideUp, SlideDown, SlideLeft, SlideRight, DropIn, Shake, Pulse};
  alanlar preset/duration/delay/distance/amount/playOnStart. Anchor yerlesimli kutuda AnchoredPos, aksi halde transform.
- BUTTONS(4): SlideUp distance 220, 0.5s. FACEBOOK(3)/GUEST(5)/APPLE(7): PopIn delay 0.3/0.4/0.5. POLICY(10): SlideUp 80, delay 0.6.
Invariantlar:
- Layout'un dizdigi cocukta (BUTTONS altindakiler) YALNIZ olcek preset'leri (PopIn/Pulse/Shake) — pozisyon preset'i layout'la kavga eder.
- Stagger sirasi soldan saga 0.1s adim.
Dogrulama: iphone-14 landscape, frame 10/20/32/72 screenshot: f10 grup yolda+butonlar gizli, f20 BAGLAN tam + MISAFIR orta pop,
f32 hepsi yerinde + policy kayiyor, f72 nihai = statik sahneyle birebir (Restore).

## 2026-10-10 — BAGLAN butonu Facebook mavisi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "BAGLAN button rengini orjinal facebook mavisi yap"
Yapi: FACEBOOK(3) LayoutBox.fill Linear Vertical: #1877f2ff (Facebook marka mavisi) -> #1462c7ff (alt kenar koyu ton).
Onceki: #486bffff -> #3d57c9ff.
Invariant: Facebook butonu ust rengi = resmi marka tokeni #1877F2; gradient yalniz hafif koyulasma, ton kaymasi yok.
Dogrulama: iphone-14 landscape f80 screenshot — BAGLAN belirgin Facebook mavisi, metin okunakli, komsu butonlar degismedi.

## 2026-10-10 — Alt butonlar %12 buyutuldu (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "alttaki button.lari biraz buyut"
Yapi: BUTTONS(4) transform.scale 1 1 1 -> 1.12 1.12 1 (pivot ust-orta, buyume asagi/yanlara). Cocuk padding/textSize degismedi.
Not: prop.set MCP sargisi su an string/sayi degeri reddediyor ("must be object"); Vec4 padding'i {x,y,z,w} nesnesi olarak
yazmak motor tarafinda parse edilmedi (padding sifirlandi) -> undo edildi. Bu yuzden grup olcegi tercih edildi.
Invariant: Buton boyutu BUTTONS grubunun olcegiyle yonetilir; cocuklara tek tek olcek verilmez (layout spacing bozulur).
Dogrulama: f80 screenshot — uc buton ve VEYA orantili buyudu, araliklar korundu, policy metniyle cakisma yok.

## 2026-10-10 — Policy metni: tam cumle + tiklanabilir baglantilar (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "Policy metnini 'Giris tuslarina basarak Hizmet Kosullarimizi ve Gizlilik Politikamizi kabul etmis olursunuz.' yap;
Hizmet Kosullarimizi / Gizlilik Politikamizi bold + alt cizgili, tiklaninca simdilik log yaz."
Yapi: LayoutBox inline zengin metin desteklemiyor -> POLICY(10) yatay konteyner oldu (text bos, layout Horizontal, spacing 9,
alignChildren Center). 5 cocuk: TXT1(15) "Giris tuslarina basarak" | TERMS(16) "Hizmet Kosullarimizi" | TXT2(17) "ve" |
PRIVACY(18) "Gizlilik Politikamizi" | TXT3(19) "kabul etmis olursunuz.". Hepsi textSize 38, textShadow siyah.
Duz parcalar Digitoy-Light; baglantilar Digitoy-Bold + border "0 0 0 2" beyaz Solid (alt cizgi) + BlocksRaycast true
+ PolicyLink (Login/Scripts/PolicyLink.cs; linkId terms|privacy; OnPointerClick -> Console.WriteLine + e.Use()).
Invariantlar:
- Policy cumlesi tek satir yatay layout; kelime parcalari sirali, araligi POLICY.spacing yonetir (cocuklara margin verilmez).
- Baglanti stili = Bold font + 2px alt border; duz metin Light. Baglanti tiklanabilirligi BlocksRaycast + PolicyLink ile.
- Log API'si motorda yok; Console stdout'a gider (log.tail'de gorunmez).
Dogrulama: iphone-14 landscape f30 screenshot (scale 1 ve 3): cumle tam, kirpilma yok, baglantilar kalin+altcizgili.
Tiklama: TERMS (375,378) gecici TextSize yan etkisiyle handler'in tetiklendigi gorsel dogrulandi, yan etki kaldirildi.

## 2026-10-10 — Sari okey tasi butonlarin arkasina (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "loginscene sari okey tasini buttonlarin altina tasi"
Yapi: Sari "13" tasi = GameObject (2) (GO 13, parent logo grubu GO 11). Hiyerarsi sirasi (BUTTONS -> POLICY -> logo grubu)
nedeniyle tas BAGLAN'in ustune ciziliyordu. Cozum: BUTTONS(4) LayoutBox.SortingOrder 0 -> 1 (Relative). Tas konumu degismedi.
Denenip geri alinanlar:
- scene.reparent(4 -> 1) ayni parent'a: kardes sirasi degismiyor (no-op).
- GO 13 SpriteRenderer.SortingOrder -1 (Absolute ve Relative): tas arkaya gecti AMA "10" tasi (GO 14) tamamen kayboldu
  -> motor tarafinda negatif sorting order / batch sorunu suphesi, motor ekibine bildirilmeli. Undo edildi.
Invariantlar:
- Alt buton grubu her zaman logo/tas grafiklerinin ustunde cizilir (BUTTONS.SortingOrder = 1).
- Sprite'lara negatif SortingOrder verme (kardes sprite kaybolma hatasi).
Dogrulama: iphone-14 landscape f80 screenshot — BAGLAN "13" tasinin ustunde, "10" tasi ve logo saglam.

## 2026-10-10 — Policy metni yeniden kuruldu: tam cumle + tiklanabilir baglantilar (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "alttaki policy yazisi 'Giris tuslarina basarak [bold&underline&click|Hizmet Kosullarimizi] ve
[bold&underline&click|Gizlilik Politikamizi] kabul etmis olursunuz.' seklinde olsun"
Not: Onceki ayni isli kayit diske yazilmamisti; sahne diskteki haliyle (yarim cumle) acildi, yapi sifirdan kuruldu.
Yapi: POLICY(10) text bos, layout Horizontal, spacing 9, alignChildren Center. Cocuklar sirayla:
TXT1(16) "Giris tuslarina basarak" | TERMS(15) "Hizmet Kosullarimizi" | TXT2(18) "ve" | PRIVACY(17) "Gizlilik Politikamizi" |
TXT3(19) "kabul etmis olursunuz.". Hepsi textSize 38, textShadow #000000ff. Duz parcalar Digitoy-Light (d486343b...),
baglantilar Digitoy-Bold (dd3069bf...) + border "0 0 0 2" beyaz Solid + BlocksRaycast true + PolicyLink(linkId terms|privacy).
Tuzak: scene.addGo paralel cagrilinca id/kardes sirasi karisti (TERMS basa dustu) -> scene.reparent(id,10,insertBefore=0)
ARDISIK cagrilarla sira duzeltildi. Sira onemli isler icin addGo'yu seri cagir.
Invariantlar: (onceki kayitla ayni) tek satir yatay layout, aralik POLICY.spacing; baglanti = Bold + 2px alt border;
tiklanabilirlik BlocksRaycast + PolicyLink; Console log stdout'a gider (log.tail'de gorunmez).
Dogrulama: iphone-14 landscape f30 screenshot — cumle tam ve sirali, baglantilar kalin+altcizgili, butonlarla cakisma yok.
input.tap TERMS (375,378) ve PRIVACY (470,378) ok, hata yok.

## 2026-10-10 — Sari 13 tasi butonlarin arkasina (yeniden; SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "sari 13 buttonlarin ustune cikmasin"
Yapi: BUTTONS(4) LayoutBox.SortingOrder 0 -> 1 (Relative). Onceki kayittaki kanitli cozum; tas konumu/hiyerarsi degismedi.
Invariant: BUTTONS.SortingOrder = 1 (butonlar daima logo/tas grafiklerinin ustunde). Sprite'lara negatif SortingOrder verme.
Dogrulama: iphone-14 landscape f30 screenshot — 13 tasi BAGLAN'in arkasinda, 10 tasi ve logo saglam, policy etkilenmedi.

## 2026-10-10 — Generic Button davranisi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "generic bir button behaviour yaz button.lara ekle"
Yapi: Common/Scripts/Button.cs — alanlar buttonId, pressScale 0.94, pressDuration 0.08 (OutCubic), releaseDuration 0.25 (OutBack),
interactable. OnPointerDown kucul, OnPointerUp geri gel, OnDragStart iptal (tiklama sayilmaz), OnPointerClick -> Clicked(this,e)
+ static AnyClicked + e.Use(). Kendi zamanini surer (Motion deseni; tween pool/alloc yok); taban olcek Awake'te yakalanir.
Sahne: FACEBOOK(3) buttonId facebook, GUEST(5) guest, APPLE(7) apple; ucunde LayoutBox.BlocksRaycast true (pointer olaylari icin sart).
Invariantlar:
- Tiklanabilir her buton = LayoutBox(BlocksRaycast) + Button; dinleyici Clicked'e abone olur, buttonId ile ayirir. Buton gorsel tipine bagimli degil.
- Basili his yalniz olcekle (renk/pozisyon degil); layout cocugunda pozisyon yazma.
- Button ve Motion ayni GO'da olabilir; ikisi de localScale yazar, ayni anda (giris animasyonu sirasinda basma) kisa catisma kabul.
Dogrulama: iphone-14 landscape; input.pointer down (370,330) GUEST -> f10 dump scale 0.94 ve ekranda kucuk; up -> f30 scale 1 1 1.

## 2026-10-10 — VEYA metni buyutuldu (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "buttonlar arasindaki veya yazisini biraz buyut"
Yapi: OR(6) LayoutBox.textSize 22 -> 30. Yatay layout komsulari ~5 birim disa itti; baska degisiklik yok.
Invariant: VEYA ayirici metni buton metninin (60) yarisi civari (30); butonlarla yarismaz, okunur.
Dogrulama: iphone-14 landscape f30 screenshot — VEYA belirgin buyudu, hizalama/aralik korundu.

## 2026-10-10 — VEYA + Apple girisi yalniz iOS (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "veya ve apple ile giris platform sadece ios ise gozukmeli"
Yapi: Common/Scripts/PlatformFilter.cs — [Flags] Platforms {Windows, MacOS, Linux, Android, IOS, Web, Mobile, Desktop, All};
alanlar platforms (izinli kume), simulate (None degilse gercek platform yerine gecer; editorde test icin). Motorun platform
API'si yok -> System.OperatingSystem.IsIOS/IsAndroid/IsBrowser/IsWindows... Awake'te izinli degilse gameObject.SetActive(false).
Sahne: OR(6) ve APPLE(7) PlatformFilter platforms=IOS, simulate=None.
Invariantlar:
- Platforma ozel gorunurluk YALNIZ PlatformFilter ile (kodda if/else veya ayri sahne degil). Yeni platform = enum'a bayrak.
- simulate teslimde None kalir (release'te de etkili).
- Layout cocugu kapaninca yerlesimden cikar: Windows/Android'de BAGLAN + MISAFIR ikili ortalanir; iOS'ta dortlu dizilim.
Dogrulama: iphone-14 landscape f30: editor (Windows) -> OR/APPLE active:false, iki buton ortali; simulate=IOS -> dortlu
gorunur, konumlar oncekiyle ayni; simulate None'a geri alindi.

## 2026-10-10 — Logo etrafina stone9 + stone3 eklendi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "logo altinda 2 tas var diger 2 tasi da stone3 ve stone9 gorsel butunluk olusturacak sekilde ekle"
Yapi: Logo grubu (GO 11) altina, BG(8)'den ONCE (logo arkasinda kalsin) iki SpriteRenderer:
STONE9(20) img-stone9 400x288 pos (300,-250) sag ust | STONE3(21) img-stone3 373x322 pos (310,235) sag alt.
Mevcutlar: stone10 (GO 13) sol ust (-279,-236), stone13 (GO 14) sol alt (-312,229). Dortlu capraz dizilim; sprite'lardaki
hareket izleri (blur) merkezden disa dogru, konumlarla uyumlu. Not: stone13 (GO 14) y 298 -> 229 olmus (editorde tasinmis, bu is disinda).
Invariantlar:
- Taslar logo grubunun cocugu ve hiyerarside BG/logo'dan once (logo arkasinda); BUTTONS.SortingOrder=1 ile butonlar hep ustte.
- Dizilim merkez simetrik: sol-ust/sag-ust ~ ±300,-240; sol-alt/sag-alt ~ ±310,+230. Sprite dogal piksel boyutunda (olcek 1).
Dogrulama: iphone-14 landscape f30 screenshot — dort tas koselerde dengeli, logo ve butonlarla cakisma yok.

## 2026-10-10 — stone9/stone3 izleri logodan disa (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "taslar logodan disari cikiyormus gibi durmali, son eklenen 2 tas oyle olmadi"
Yapi: Sprite'lardaki hareket izi (blur) merkeze bakacak, tas dis ucta olacak sekilde dondurme:
STONE9(20) pos (290,-235) rot z 45 | STONE3(21) pos (335,225) rot z -90. Olcek 1, sprite degismedi.
Not: transform.set rot z isareti: pozitif = ekranda saat yonunun TERSI (y asagi sistemde).
Invariant: Her tasin izi logo merkezine dogru, tas disarida ("firlama" hissi). Yeni tas eklerken sprite'in dogal iz yonune gore rot ver.
Dogrulama: iphone-14 landscape f10 screenshot — 4 tasin izi de merkeze bakiyor, 3 tasi MISAFIR butonuyla cakismiyor.

## 2026-10-10 — Logo etrafindaki 4 tas kaldirildi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "logodaki taslari kaldir sahne kalabalik oldu" (kullanici: dordu de)
Yapi: GO 13 (stone10), 14 (stone13), 20 (STONE9), 21 (STONE3) silindi. Logo grubu (11) altinda yalniz BG(8) + logo(12) kaldi.
Sprite dosyalari Assets/Login/Sprites'ta duruyor (ileride geri istenirse onceki kayitlardaki konum/rot degerleri gecerli).
Invariant: Logo alani sade — logo + isik BG; dekoratif tas yok. BUTTONS.SortingOrder=1 artik gerekli degil ama zararsiz, birakildi.
Dogrulama: iphone-14 landscape f10 screenshot — yalniz logo, butonlar, policy.

## 2026-10-10 — Isimsiz GO'lar adlandirildi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "adi gameobject olan go.lara dogru isimler ver"
Yapi: GO 11 "GameObject" -> LOGO_GROUP (logo + BG isigi grubu, y -90); GO 12 "GameObject (1)" -> LOGO (ana logo sprite'i 583x450).
Invariant: Sahnede varsayilan "GameObject" adi birakilmaz; adlar BUYUK_HARF rolu tanimlar (mevcut CAMERA/ROOT/BUTTONS/POLICY ile uyumlu).

## 2026-10-10 — Soft giris animasyonu (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "giris animasyonu tasarla soft profesyonel ve etkili olsun"
Kod: Motion.cs'e sicramasiz preset'ler: SoftIn (olcek 1-amount -> 1, OutQuint), SoftUp (asagidan distance, OutQuint),
FadeIn (SpriteRenderer.Color alfa 0 -> 1, OutSine; LayoutBox'ta etkisiz). Restore alfayi da geri yazar.
Zaman cizelgesi (~1.5s, uc katman):
- 0.00s LOGO_GROUP(11) SoftIn amount 0.10, 0.8s | BG(8) FadeIn 0.9s | LOGO(12) FadeIn 0.6s delay 0.15 (isik once, logo ustune suzulur)
- 0.45s BUTTONS(4) SoftUp distance 70, 0.7s; cocuklar SoftIn amount 0.15, 0.5s: FACEBOOK 0.55 / GUEST 0.65 / OR 0.75 / APPLE 0.85 (0.1s stagger)
- 0.90s POLICY(10) SoftUp distance 30, 0.6s (en son, sakin)
Invariantlar:
- "Soft" sozlugu = OutQuint/OutSine, overshoot yok; PopIn/Bounce bu sahnede kullanilmaz.
- Katman sirasi sabit: logo -> butonlar (soldan saga stagger) -> policy. Toplam < 1.6s; kullanici bekletilmez.
- Layout cocuklarinda (BUTTONS altindakiler) yalniz SoftIn (olcek); pozisyon preset'i yok.
- Nihai kare statik sahneyle birebir (Motion Restore).
Tuzak: Ardisik sim.run cagrilarinda ilk kareye gercek duvar saati dt'si biner (MaxDeltaTime) -> zaman cizelgesi dogrulamasi
icin her kareyi AYRI sim.run(restart=true, frames=N) ile al.
Dogrulama: iphone-14 landscape; f8 logo 0.96 olcek + alfa dusuk, butonlar asagida; f36 logo oturmus, butonlar yolda
(y 401), FACEBOOK olcekleniyor, GUEST bekliyor, policy asagida; f100 = statik sahne.

## 2026-10-10 — DISKTEKI MEVCUT DURUM (senkron kaydi; scene.dump dirty=false)
Prompt: "loginscene intent icerigini mevcut duruma gore guncelle"
Amac: Yukaridaki kayitlarin cogu "SAHNE KAYDEDILMEDI" notuyla birakildi; bir kismi sonradan kaydedilmis, bir kismi
kaybolmus. Bu kayit diskteki gercek hali sabitler. Sahneye dokunmadan once BU bolumu esas al.
Hiyerarsi (id, kardes sirasi = cizim sirasi):
- CAMERA(2) pos 0 0 -3000; CameraComponent Perspective fov 30 + CameraScaler ref 1440x960.
- ROOT(1) LayoutBox fitScreen 1440x960, arka plan sprite a3bbb7fe..., 1px beyaz border.
  - BUTTONS(4) LayoutBox Horizontal spacing 30, anchor/pivot ust-orta, anchoredPos 0 -100, alignChildren Center,
    SortingOrder 1, scale 1 1 1. Cocuklar sirayla:
    - FACEBOOK(3) "BAGLAN" Digitoy-Bold 60, fill Linear #486bffff -> #3d57c9ff, radius 20, padding 50 15 50 15,
      textShadow #0000005e, BlocksRaycast + Button(buttonId facebook).
    - GUEST(5) "MISAFIR GIRISI" seffaf fill + 2px beyaz border, radius 20, BlocksRaycast + Button(guest).
    - OR(6) "VEYA" textSize 30. (PlatformFilter YOK)
    - APPLE(7) "APPLE ILE GIRIS YAP" beyaz fill, siyah textFill, BlocksRaycast + Button(apple). (PlatformFilter YOK)
  - POLICY(10) LayoutBox text bos, Horizontal spacing 9, alignChildren Center, anchor ust-orta, anchoredPos 0 -14.9,
    textSize 38, textShadow siyah. Cocuklar sirayla: TXT1(16) "Giris tuslarina basarak" Light | TERMS(15) Bold + alt border
    + BlocksRaycast + PolicyLink(terms) | TXT2(18) "ve" Light | PRIVACY(17) Bold + alt border + BlocksRaycast +
    PolicyLink(privacy) | TXT3(19) "kabul etmis olursunuz." Light.
  - LOGO_GROUP(11) pos 0 -90 (component yok): BG(8) SpriteRenderer 402x314 scale 2 2 1 (isik) -> LOGO(12) SpriteRenderer 583x450.
Kodlar (Assets): Common/Scripts/{Button, CameraScaler, LoadScene, PlatformFilter}.cs, Login/Scripts/PolicyLink.cs.
Diske YANSIMIS isler: policy tam cumle + baglantilar; BUTTONS.SortingOrder 1; Button component'leri; VEYA 30;
dekoratif taslar kaldirildi; LOGO_GROUP/LOGO adlari.
Diske YANSIMAMIS (yalniz gunlukte kalan) isler:
- Motion.cs ve tum giris animasyonlari (motion preset proof + soft giris) — dosya YOK, hicbir GO'da Motion yok.
- BAGLAN Facebook mavisi (#1877f2) — hala #486bffff.
- BUTTONS %12 olcek — hala 1 1 1.
- OR/APPLE PlatformFilter(IOS) — script var, sahnede component yok; tum platformlarda 4'lu dizilim gorunur.
Not: POLICY.anchoredPos -14.9 ve BUTTONS anchoredPos -100 gunluk disi (editorde elle) ayarlanmis; gecerli degerler bunlar.
Invariantlar (gecerli olanlar): BUTTONS.SortingOrder=1; tiklanabilir = LayoutBox(BlocksRaycast)+Button/PolicyLink;
policy tek satir yatay layout, aralik POLICY.spacing; baglanti = Bold + 2px alt border; logo alani sade (tas yok);
varsayilan "GameObject" adi birakilmaz; sprite'lara negatif SortingOrder verme.
Dogrulama: iphone-14 landscape (844x390 @3x) f30 screenshot — logo ortada, 4 buton (BAGLAN mavi, MISAFIR, VEYA, APPLE)
alt satirda, policy cumlesi en altta kalin+altcizgili baglantilarla; animasyon yok (statik acilis).

## 2026-10-10 — Soft giris animasyonu MovieClip ile (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "rpc_describe ile ogren sonra LoginScene acilis animasyonu movieclip ile"
Yapi: ROOT(1) comp[1] MovieClip Fps 60, FrameCount 96 (1.6s), PlayOnStart, Loop false. Kod YOK (Motion.cs kullanilmadi; koreografi doc'ta).
Track'ler (hedef:yol -> key'ler; Frame@Value, segment ease = baslangic key'inin Ease'i):
- T0 LOGO_GROUP(11) Transform:Scale 0@0.9 OutQuint -> 48@1
- T1 BG(8) SpriteRenderer:Color 0@255 255 255 0 OutSine -> 54@255 255 255 255 (isik once suzulur)
- T2 LOGO(12) SpriteRenderer:Color 0@a0 Hold, 9@a0 OutSine -> 45@a255
- T3 BUTTONS(4) LayoutBox:anchoredPos 0@0 -170 Hold, 27@0 -170 OutQuint -> 69@0 -100
- T4..T7 FACEBOOK(3)/GUEST(5)/OR(6)/APPLE(7) Transform:Scale 0@0.85 Hold, bekle 33/39/45/51 OutQuint -> +30fr @1 (0.1s stagger)
- T8 POLICY(10) LayoutBox:anchoredPos 0@0 -44.9 Hold, 54@... OutQuint -> 90@0 -14.9
Ogrenilenler:
- prop.set Tracks[i] NESTED nesne kabul eder ({Target,Path,Keys:[{Frame,Mode,Ease,Value}]}) — tek cagrida track; index-append SIRALI cagrilmali.
- Color key Value = "r g b a" 0..255 (anim.props hint'i dogru). 0..1 yazarsan ~siyah/seffaf olur.
- Mask default (0) = tum kanallar; 15 yazmak gerekmez.
- Isik huzmesi ROOT arka plan sprite'inda; BG(8) logo arkasindaki yumusak parilti — alfa animasyonu ince bir etki, dramatik degil.
- Karsilastirma icin her kareyi AYRI sim.run(restart=true) ile al; ekran goruntusu dump'tan bir kare geride olabilir (erken karelerde yaniltir).
Invariantlar:
- Giris koreografisi YALNIZ bu MovieClip'te (Motion/Tween yok); yeni ogeye giris = yeni track.
- "Soft" sozlugu: OutQuint/OutSine, overshoot yok. Katman sirasi: logo -> butonlar (soldan saga 0.1s) -> policy; toplam 1.6s.
- Layout cocuklarinda (BUTTONS altindakiler) yalniz Scale track'i; pozisyon BUTTONS.anchoredPos ile.
- Son key degerleri = doc'un statik degerleri (nihai kare statik sahneyle birebir). Statik deger degisirse son key de guncellenir.
Dogrulama: iphone-14 landscape; f30 logo oturmus (0.999), butonlar yolda (y 331) + 0.85, policy asagida; f55 butonlar yerinde,
FACEBOOK 1 / GUEST 0.997 / APPLE 0.93 stagger, policy kayiyor (439); f100 = statik: BUTTONS 380, POLICY 465.1, tum olcekler 1.

## 2026-10-10 — Faz 1 "carpma" (impact) girisi; fade kaldirildi (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "animasyonu guzellestir. 1.faz logo ve bg fade olmasin logo buyukten kucuge bg kucukten buyuge gelsin ve sert bir carpma hissi versin"
Yapi (ayni MovieClip, ROOT(1) comp[1]; Color track'leri yerinde Scale track'leriyle degistirildi):
- T2 LOGO(12) Transform:Scale 0@2.0 InCubic -> 18@0.94 (CARPMA) OutQuad -> 26@1  (kameradan hizlanarak gelir, ezilir, toparlanir)
- T1 BG(8) = altin elmas cerceve (logo-bg.png) Transform:Scale 0@0.3 InSine -> 18@0.6 OutExpo -> 42@2 (metnin arkasindan patlayarak cikar)
- T0 LOGO_GROUP(11) Transform:Scale 0@1 -> 18@1 OutQuad -> 22@1.05 InOutSine -> 34@1 (darbe nabzi)
- T3 BUTTONS anchoredPos baslangic 0 +160 (EKRAN DISI, alt kenarin altinda) hold 27 -> 69@0 -100; T8 POLICY 0 +200 hold 54 -> 90@0 -14.9.
  Neden: faz 1'de dev logo butonlarla cakisiyordu; carpmaya kadar alt UI gorunmez.
- T4..T7 buton stagger degismedi.
Ogrenilen: BG(8) isik huzmesi DEGIL, elmas cerceve; isik huzmesi ROOT arka plan sprite'inda. Fade/alpha bu sahnede artik kullanilmiyor.
Invariantlar:
- Carpma ani = frame 18 (0.3s); logo InCubic ile gelir (yavaslama YOK, sertlik buradan), ezilme 0.94, toparlanma <= 8 kare.
- Elmas cerceve carpmadan once logo metninin arkasinda (<=0.6), carpmayla OutExpo 2.0'a; carpmadan sonra baslayan buton/policy akisi korunur.
- Alt UI carpmadan once ekran disinda bekler (anchoredPos +y); yeni cihaz oraninda sizarsa ofseti buyut.
- Son key'ler = statik doc degerleri (LOGO 1, BG 2 2 1, GROUP 1).
Dogrulama: iphone-14 landscape; f12 logo 1.69 buyuk + alt UI yok; f18 logo 0.94, cerceve 0.6 gizli; f24 cerceve 1.75 patlamis, grup 1.047;
f100 = statik (BUTTONS 380, POLICY 465.1, BG 2 2 1).

## 2026-10-10 — Butonlar: kaydirma yok, carpma bitince OutBack ile belir (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "buttonlar asagidan yukariya gelmesin carpma efekti bitene kadar gorunmesin sonra scale up dinamik bir easing ile belirsin"
Yapi (ayni MovieClip; BUTTONS anchoredPos track'i SILINDI -> prop.set Tracks[3]=null; sonraki indexler kaydi):
- T3..T6 FACEBOOK(3)/GUEST(5)/OR(6)/APPLE(7) Transform:Scale 0@0 Hold, bekle 34/40/46/52 OutBack -> +20fr @1 (0.1s stagger)
- T7 POLICY anchoredPos 0@0 +200 hold -> 60 OutQuint -> 90@0 -14.9 (butonlardan sonra)
- BUTTONS(4) anchoredPos artik anime edilmiyor (doc degeri 0 -100 sabit).
Invariantlar:
- Butonlar carpma nabzi bitene (f34) kadar olcek 0 (gorunmez); pozisyon animasyonu YOK (layout cocugu).
- Belirme = OutBack 20 kare, soldan saga 6 kare stagger; "dinamik" sozlugu = OutBack (tek overshoot), Bounce/Elastic yok.
- Olcek 0 layout'u bozmaz (layout olcumsuz boyut kullanir) -> butonlar yerinde belirir.
Dogrulama: iphone-14 landscape; f30 buton yok, logo oturmus; f46 BAGLAN 1.10 (OutBack tepesi) + MISAFIR 0.91; f100 = statik.

## 2026-10-10 — Policy: kaydirma yok, yalniz fade-in (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "privacy de asagidan kaymasin sadece fadein olsun"
Yapi (ayni MovieClip): POLICY anchoredPos track'i (T7) yerine 12 renk track'i, hepsi 0@a0 Hold -> 60@a0 OutSine -> 84@a255:
- T7..T11 textFill.color (255 255 255 a) TXT1(16)/TERMS(15)/TXT2(18)/PRIVACY(17)/TXT3(19)
- T12..T16 textShadowColor (0 0 0 a) ayni bes parca
- T17..T18 borderFill.color (255 255 255 a) TERMS(15)/PRIVACY(17) (alt cizgi)
POLICY(10) anchoredPos artik anime edilmiyor (doc 0 -14.9 sabit).
Ogrenilen: LayoutBox'ta tek "opacity" yok; fade = textFill.color + textShadowColor + borderFill.color alfalari birlikte.
textFill doc'ta yoksa varsayilan beyaz (#ffffffff) — son key buna esit yazildi.
Invariantlar:
- Policy girisi yalniz alfa (f60-84, OutSine); pozisyon/olcek animasyonu yok.
- Yeni policy parcasi eklenirse 2 (baglantiysa 3) renk track'i de eklenir; aksi halde o parca animasyonsuz gorunur.
- Son key alfalari 255 = statik gorunum (metin beyaz, golge siyah, alt cizgi beyaz).
Dogrulama: iphone-14 landscape; f50 policy tamamen gorunmez (golge/altcizgi dahil); f66 soluk; f72 ~%85; f100 = statik.

## 2026-10-11 — Policy fade-in RenderGroup ile (SAHNE KAYDEDILMEDI; undo'lu)
Prompt: "logincscene acilis movieclipteki privacy fadein.i rendergroup ile yap"
Yapi (ayni MovieClip, ROOT(1) comp[1]): 12 renk track'i (T7..T18 textFill.color/textShadowColor/borderFill.color) SILINDI
(sondan basa Tracks[i]=null). POLICY(10) comp[1] RenderGroup eklendi (Alpha default 1, IgnoreParentAlpha false).
Yeni T7: 10:RenderGroup:0 Alpha 0@0 Hold -> 60@0 OutSine -> 84@1. Toplam 8 track.
Ogrenilen: RenderGroup.Alpha (Float 0..1) alt agacin tamamini (metin + golge + alt cizgi) tek kanaldan soldurur;
LayoutBox'in parcali renk alfalarina gerek kalmaz. anim.props(RenderGroup): Alpha, IgnoreParentAlpha, Enabled.
Invariantlar:
- Policy girisi = POLICY.RenderGroup.Alpha track'i (f60-84 OutSine); cocuklarin renk alanlari anime EDILMEZ.
- Yeni policy parcasi eklenince ek track GEREKMEZ (RenderGroup kapsar) — onceki "2-3 renk track'i" invarianti gecersiz.
- Son key Alpha 1 = statik gorunum; POLICY.RenderGroup.Alpha doc degeri 1 kalir.
Dogrulama: iphone-14 landscape; f50 policy gorunmez; f72 soluk (golge/altcizgi birlikte); f100 = statik, tam gorunur.
