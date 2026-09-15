using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// GENERIC surukle-birak: yuk tipten habersizdir (asset = guid+yol, sahne nesnesi
// = docId), KABUL karari hedef alanin SEMASINDAN turetilir (FieldSchema.Kind +
// FieldType). Yeni asset/alan tipleri geldiginde ne panel ne payload degisir —
// yalniz Accepts/DocValue eslesmesi genisler (o da sema uzerinden calisir).
static class DragDrop
{
    public enum Payload { None, Asset, SceneObject }

    public static Payload Kind { get; private set; }
    public static string AssetGuid { get; private set; }
    public static string AssetPath { get; private set; }
    public static int GoId { get; private set; }

    public static bool Active => Kind != Payload.None;

    public static void BeginAsset(string guid, string path)
    {
        Kind = Payload.Asset;
        AssetGuid = guid;
        AssetPath = path;
        GoId = 0;
    }

    public static void BeginSceneObject(int docId)
    {
        Kind = Payload.SceneObject;
        GoId = docId;
        AssetGuid = null;
        AssetPath = null;
    }

    public static void Clear()
    {
        Kind = Payload.None;
        AssetGuid = null;
        AssetPath = null;
        GoId = 0;
    }

    // --- Drop hedef kaydi: imlec ustundeki kabul eden alan her frame kendini
    // kaydeder; birakma FRAME SONUNDA merkezi uygulanir (EndFrame). Boylece panel
    // cizim sirasi / event tuketimi drop'u etkilemez. ---

    static Action _dropApply;
    static int _dropFrame = -1;

    public static void RegisterTarget(Action apply)
    {
        _dropApply = apply;
        _dropFrame = Time.frameCount;
    }

    // EditorApp frame sonunda cagirir: sol tus birakildiysa kayitli hedefe uygula.
    public static void EndFrame(bool mouseReleased)
    {
        if (!Active)
        {
            _dropApply = null;
            return;
        }
        if (!mouseReleased)
            return;
        if (_dropFrame == Time.frameCount)
            _dropApply?.Invoke();
        _dropApply = null;
        Clear();
    }

    // Tasinan yuk bu alana birakilabilir mi? Karar tamamen SEMADAN:
    // - Asset yuku: alanin Kind'i asset-referansi ise (Texture; ileride Mesh,
    //   AudioClip... SerializedType hangi Kind'lari asset sayiyorsa) VE asset'in
    //   yuklenebilir tipi alan tipiyle eslesiyorsa.
    // - Sahne nesnesi yuku: GoRef her zaman; CompRef hedef GO'da alan tipinden
    //   component varsa.
    public static bool Accepts(in SerializedType.FieldSchema f)
        => Accepts(f.Kind, f.Info.FieldType);

    public static bool Accepts(SerializedType.Kind fieldKind, Type fieldType)
    {
        switch (Kind)
        {
            case Payload.Asset:
                return fieldKind == SerializedType.Kind.Asset
                    && App.Assets != null
                    && App.Assets.IsAssignable(AssetPath, fieldType);
            case Payload.SceneObject:
                if (fieldKind == SerializedType.Kind.GoRef)
                    return true;
                if (fieldKind != SerializedType.Kind.CompRef)
                    return false;
                var live = App.EditScene?.Live(GoId);
                return live != null && live.GetComponentOfType(fieldType) != null;
            default:
                return false;
        }
    }

    // Yukun DOC temsilini uretir (SceneDoc ref formatlariyla birebir; cozumleme
    // her yerde ayni ResolveRef/ReadField yolundan gecer).
    public static string DocValue(in SerializedType.FieldSchema f)
        => DocValue(f.Kind, f.Info.FieldType);

    public static string DocValue(SerializedType.Kind fieldKind, Type fieldType)
    {
        switch (Kind)
        {
            case Payload.Asset:
                return AssetGuid;
            case Payload.SceneObject when fieldKind == SerializedType.Kind.GoRef:
                return GoId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case Payload.SceneObject: // CompRef: "goId:TipAdi:0" — ilk eslesen component
                {
                    var live = App.EditScene?.Live(GoId);
                    var c = live?.GetComponentOfType(fieldType);
                    return c == null ? "" : GoId + ":" + c.GetType().Name + ":0";
                }
            default:
                return "";
        }
    }

    // Imleci izleyen hayalet etiket (DrawUi repaint'inde cagrilir).
    public static void DrawGhost(Vec2 mouse)
    {
        if (!Active)
            return;
        string label = Kind == Payload.Asset
            ? AssetPath
            : App.EditScene?.FindGo(GoId)?.Name ?? "GameObject";
        GuiRenderer.DrawTextIn(new Rect(mouse.x + 14, mouse.y - 8, 220, 16),
            label, Gui.FontSize - 3f, new Color(255, 220, 120, 230), false, 6);
    }
}
