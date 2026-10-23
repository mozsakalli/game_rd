# Table3D.scene — intent (APPEND-ONLY)

## 2026-10-22 — 3D masa + serbest kamera R&D doğrulama sahnesi
PROMPT: Gin Rummy prod'daki 3D masa (ginrummyextra.unity TableModel.fbx) + kamera açısı geçişi (tepeden → oyun) motorda,
low-end'de çalışacak biçimde.

YAPI:
- `Camera3D` (layer 1, depth -1, cullingMask 2, SolidColor #2a1a3a) + `FreePerspectiveProjection` fov 23 / near 0.3 / far 100.
  Konum/rotasyon Unity değerlerinin y-negatifi: gameplay (0,-28,-8) rot (45,0,0); enter (-11.43,-26.6,33.5) rot (90,0,0).
  `MovieClip` (demo): enter → gameplay 48 kare @60fps InOutCubic (Position + Rotation + FreePerspectiveProjection.fov).
  Oyunda bu geçiş TWEEN olacak (duruma tepkisel); clip yalnız motor doğrulaması.
- `TableRoot` (0,21.62,33.68) › `TableModel` scale 20, `MeshRenderer{Mesh=Table/TableModel.fbx (DMSH), Sprite=Table/TableTexture.jpg,
  CullMode Back, Depth test+write, Opaque, Sampler Trilinear}`. FBX importer scale 0.01 (cm→m, Unity paritesi) × GO 20 = Unity ile aynı.
- `CameraUI` (depth 0, cullingMask 1, DepthOnly) + PixelPerfect; `UI` fitScreen LayoutBox › `Title` (metin: projede font yok, görünmez).

INVARIANTLAR:
- 3D içerik layer 1'de, UI layer 0'da; UI kamerası 3D'nin ÜSTÜNE DepthOnly biner. Layout/picking yalnız UI kamerasında.
- Dünya y-AŞAĞI; Unity→motor eşlemesi: pos (x,-y,z), Euler (rx, ry, rz) AYNEN (rotasyon konvansiyonları aynaya göre eşleşiyor,
  görsel olarak doğrulandı). Importer y'yi ve üçgen sarımını çevirir; flipV=false (yükleme flip'i GL UV ile uyumlu).
- MeshRenderer ışıksız (doku pişmiş); normal yok.

DOĞRULAMA: 1664×768'de gameplay kadrajı prod ekranıyla aynı oranda; enter (tepeden) + 48 karelik geçiş captureEvery ile izlendi;
render.describe MeshRenderer rect'i 8-köşe AABB izdüşümüyle (42 43 1533 725); Trilinear ile uzak kenarlık aliasing'i düştü.

## 2026-10-22 (2) — MeshRenderer.Texture → Sprite
Texture alanı kaldırıldı; Sprite (atlas üyesi olabilir, UV bölgeye haritalanır). SortMode varsayılanı Transparent (BG sprite altında kalma sorunu). Sahne prop'u Sprite olarak güncellendi (undo'lu, kaydedilmedi).
