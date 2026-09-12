# Login.scene — niyet gunlugu (append-only: her AI mudahalesi tarihli giris ekler, eskisi degistirilmez)

## 2026-09-12 — ilk kurulum (RPC uzerinden, AI)
Prompt: "yeni sahne: LoginScene layoutbox ile fullscreen mavi gradient background,
ust ortali logo, altinda yanyana Facebook Login, Guest Login ve Apple Login;
apple login platform apple ise gorunur (simdilik dummy) ve her durumda ekrandaki
tus sayisindan bagimsiz yatayda ortalanacaklar; ekranin en altina copyright
(text render yok, bir kare yeterli)."

Yapi:
- BG (id 1): LayoutBox fitScreen, dikey gradient #0e1e4e -> #3f7fd9, layout None.
- Logo (id 2): 220x90 beyaz rounded kutu (placeholder), anchor (0.5,0) ust-orta, tepeden 48px.
- ButtonRow (id 3): layout Horizontal, spacing 14, anchor+pivot (0.5,0.5) — genislik
  AUTHORED DEGIL, iceriktan olculur (autosize).
- FacebookLogin (id 4) #1877f2 / GuestLogin (id 5) #6b7280 / AppleLogin (id 6) siyah+beyaz
  ince border; hepsi 150x46 radius 10.
- Copyright (id 7): 280x14 yari saydam beyaz serit, anchor (0.5,1) alt-orta, alttan 20px.
- AppleLogin'de ApplePlatformOnly component'i (Scripts/ApplePlatformOnly.cs):
  Awake'te OperatingSystem.IsMacOS/IsIOS degilse SetActive(false). DUMMY — gercek
  platform tespiti/build define'i ileride.

Invariantlar (sahne duzenlenirken KORUNMALI):
- Buton satiri buton sayisindan bagimsiz yatay ortali: ButtonRow'a sabit width VERME,
  autosize + anchor/pivot 0.5 mekanizmasi bozulmamali. Yeni buton = sadece yeni cocuk.
- AppleLogin yalniz Apple platformda gorunur; editorde de gizlenir (Awake edit
  projeksiyonunda da kosar — bilincli WYSIWYG karari, 2026-09-12).
- Metinler placeholder kutu: motorun UIText'i gelince buton/copyright metne cevrilecek.

Dogrulama (sim.run, 2026-09-12): Windows'ta AppleLogin active:false; kalan 2 buton
lokal x = -82/+82 (2x150+14=314 satir, tam ortali); Logo tepeden 48, Copyright alttan 20.