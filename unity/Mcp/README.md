# Digitoy MCP for Unity

Unity Editor icinde satir-bazli JSON RPC sunucusu (TCP `localhost:5158`) + stdio MCP koprusu.
DigitoyEngine editorundeki `RpcHost`/`RpcCommands` yapisinin Unity portudur; motor koduna bagimliligi yoktur.

## Kurulum

1. Paketi projeye ekle — `Packages/manifest.json`:
   ```json
   "com.digitoygames.mcp": "file:../../game_rd/unity/Mcp"
   ```
   (ya da `Packages/` altina kopyala / git URL).
2. Unity'yi ac. Paket acilista otomatik:
   - RPC sunucusunu baslatir (port `UNITY_MCP_PORT`, default 5158),
   - kopruyu `Library/DigitoyMcp/bridge/unity-mcp-bridge.dll` olarak derler (.NET SDK 9 gerekir),
   - `<proje>/.vscode/mcp.json`'a `unity-editor` sunucusunu yazar (mevcut sunuculara dokunmaz).
   Elle tekrar: `Digitoy > MCP > Setup VS Code` / `Rebuild bridge`.
3. VS Code'u Unity proje kokunde ac; MCP sunucusu `unity-editor` hazirdir.
4. ONERILEN: `Edit > Project Settings > Editor > Enter Play Mode Options` → **Reload Domain kapali**.
   Aksi halde play'e giris baglantiyi dusurur; komut `{state:"starting"}` doner ve tekrar cagrilmasi gerekir.

## Kullanim

Kopru `tools/list`'i editordeki `rpc.describe`'dan canli uretir; `rpc.guide` initialize'da `instructions`
olarak gomulur. Komut ekleme: herhangi bir Editor assembly'sinde

```csharp
[Digitoy.Mcp.RpcCommand("game.reset", "Oyunu sifirlar")]
static Digitoy.Mcp.JNode Reset(int level = 1) { ...; return Digitoy.Mcp.JNode.Map().Add("ok", true); }
```

Elle test:
```powershell
@('{"id":1,"cmd":"rpc.guide","args":{}}') | ../game_rd/tools/rpc.ps1 5158
```

## Komut aileleri

| Aile | Komutlar |
|---|---|
| Kesif | `rpc.describe`, `rpc.guide`, `project.info`, `comp.types`, `comp.schema`, `scene.list` |
| Gozlem | `scene.tree`, `scene.dump`, `scene.diff`, `scene.dumpLive`, `prop.get`, `log.tail` |
| Yapi | `scene.addGo/removeGo/rename/reparent`, `go.setActive/setLayer`, `comp.add/remove/setEnabled`, `prop.set`, `transform.set` |
| Toplu | `scene.apply` (bildirimsel agac), `edit.batch` (atomik op listesi), `edit.undo/redo` |
| Oturum | `scene.save/saveAs/new/open`, `prefab.open/close/instantiate`, `select.set/asset` |
| Play | `play.start/stop/pause/state`, `sim.run` (sabit dt, duraklatilmis adimlama) |
| Gorus/El | `view.get/set/device/devices`, `render.screenshot`, `input.pointer/tap/drag` |
| Asset | `asset.write/read/list/info/reimport/create/delete`, `code.build` |

## Sinirlar

- GO id = `instanceID` (oturum ici; editor yeniden acilinca degisir).
- Edit modunda screenshot kamera render'idir; Screen Space Overlay UI yalnizca play'de (`sim.run screenshot=true`) gorunur.
- Game View sekmesi gorunur olmali (komutlar `Window/General/Game` ile one getirir).
- Klavye simulasyonu yok; davranis testi icin oyun kodu kendi `[RpcCommand]`'ini ekler.
