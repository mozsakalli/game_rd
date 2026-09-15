using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Doc-mutasyon journal'i. TUM authored degisiklikler EditorScene.Set* bogazindan
// gecer ve kaydi o bogaz basar — Unity'nin "Undo.RecordObject unutuldu" sinifi
// bug burada yapisal olarak imkansiz. Girisler docId + prop adiyla adreslenir:
// save/reload/kod-reload journal'i gecersiz kilmaz. Preview canliya yazar,
// journal'a hicbir zaman giremez (undo = yalniz authored gecmis).
public abstract class UndoOp
{
    public bool Sealed; // jest bitti mi (coalescing kilidi)

    public abstract void Apply(EditorScene es, bool undo);

    // Surekli jest (drag): ayni hedefe ardisik girisler tek undo adiminda birlesir
    // (ilk Old korunur, son New guncellenir).
    public virtual bool TryCoalesce(UndoOp next) => false;
}

public sealed class UndoStack
{
    readonly List<UndoOp> _undo = new();
    readonly List<UndoOp> _redo = new();

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    // Her doc mutasyonunda artar (coalesce dahil) — "aninda hot reload" tetigi.
    public int Version { get; private set; }

    public void Push(UndoOp op)
    {
        Version++;
        _redo.Clear();
        if (_undo.Count > 0 && !_undo[^1].Sealed && _undo[^1].TryCoalesce(op))
            return;
        _undo.Add(op);
    }

    // Jest bitti (hot control birakildi): usttekini muhurle.
    public void SealTop()
    {
        if (_undo.Count > 0)
            _undo[^1].Sealed = true;
    }

    public void Undo(EditorScene es)
    {
        if (_undo.Count == 0)
            return;
        Version++;
        var op = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        op.Apply(es, undo: true);
        _redo.Add(op);
    }

    public void Redo(EditorScene es)
    {
        if (_redo.Count == 0)
            return;
        Version++;
        var op = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        op.Apply(es, undo: false);
        _undo.Add(op);
    }
}

// Component alani (DocNode = kanonik doc degeri; scalar/liste/nesne ayni undo yolu).
public sealed class PropOp : UndoOp
{
    public int GoId;
    public int CompIndex;
    public string Prop;
    public DocNode OldValue;
    public DocNode NewValue;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyPropOp(GoId, CompIndex, Prop, undo ? OldValue : NewValue);

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not PropOp p || p.GoId != GoId || p.CompIndex != CompIndex || p.Prop != Prop)
            return false;
        NewValue = p.NewValue?.Clone();
        return true;
    }
}

public sealed class TransformOp : UndoOp
{
    public int GoId;
    public Vec3 OldPos, OldRot, OldScale;
    public Vec3 NewPos, NewRot, NewScale;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyTransformOp(GoId,
            undo ? OldPos : NewPos, undo ? OldRot : NewRot, undo ? OldScale : NewScale);

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not TransformOp t || t.GoId != GoId)
            return false;
        NewPos = t.NewPos;
        NewRot = t.NewRot;
        NewScale = t.NewScale;
        return true;
    }
}

public sealed class ActiveOp : UndoOp
{
    public int GoId;
    public bool Old, New;

    public ActiveOp() => Sealed = true; // tek atimlik: jest yok

    public override void Apply(EditorScene es, bool undo) => es.ApplyActiveOp(GoId, undo ? Old : New);
}

public sealed class LayerOp : UndoOp
{
    public int GoId;
    public int Old, New;

    public LayerOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo) => es.ApplyLayerOp(GoId, undo ? Old : New);
}

public sealed class EnabledOp : UndoOp
{
    public int GoId;
    public int CompIndex;
    public bool Old, New;

    public EnabledOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyEnabledOp(GoId, CompIndex, undo ? Old : New);
}

// --- Yapisal op'lar: doc mutasyonu + FULL RELOAD (kismi canli-senkron bug sinifi yok) ---

public sealed class AddGoOp : UndoOp
{
    public SceneDoc.GoDoc Doc;
    public int Index;

    public AddGoOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        if (undo)
            es.Doc.Objects.Remove(Doc);
        else
            es.Doc.Objects.Insert(Index, Doc);
        es.RefreshLive();
    }
}

public sealed class RemoveGoOp : UndoOp
{
    // Alt agac dahil; indeksler kaldirma ANINDAKI konumlar (artan sirali).
    public readonly List<(SceneDoc.GoDoc doc, int index)> Items = new();

    public RemoveGoOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        if (undo)
        {
            foreach (var (doc, index) in Items) // artan sirayla geri ekle
                es.Doc.Objects.Insert(index, doc);
        }
        else
        {
            for (int i = Items.Count - 1; i >= 0; i--)
                es.Doc.Objects.Remove(Items[i].doc);
        }
        es.RefreshLive();
    }
}

public sealed class ReparentOp : UndoOp
{
    public int GoId;
    public int OldParent, NewParent;
    public Vec3 OldPos, OldRot, OldScale;   // world korumali: lokal TRS parent'la degisir
    public Vec3 NewPos, NewRot, NewScale;

    public ReparentOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        var g = es.FindGo(GoId);
        if (g == null)
            return;
        g.Parent = undo ? OldParent : NewParent;
        g.Pos = undo ? OldPos : NewPos;
        g.Rot = undo ? OldRot : NewRot;
        g.Scale = undo ? OldScale : NewScale;
        es.RefreshLive();
    }
}

// Parent + liste sirasi (kardes siralamasi) tek operasyonda: index'ler eleman
// LISTEDEN CIKARILMIS haldeki insert konumudur.
public sealed class MoveGoOp : UndoOp
{
    public int GoId;
    public int OldParent, NewParent;
    public int OldIndex, NewIndex;
    public Vec3 OldPos, OldRot, OldScale;   // world korumali reparent: lokal TRS degisir
    public Vec3 NewPos, NewRot, NewScale;

    public MoveGoOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyMoveOp(GoId, undo ? OldParent : NewParent, undo ? OldIndex : NewIndex,
            undo ? OldPos : NewPos, undo ? OldRot : NewRot, undo ? OldScale : NewScale);
}

// GO adi (yazarken karakter karakter birlesir).
public sealed class NameOp : UndoOp
{
    public int GoId;
    public string Old, New;

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not NameOp n || n.GoId != GoId)
            return false;
        New = n.New;
        return true;
    }

    public override void Apply(EditorScene es, bool undo) => es.ApplyNameOp(GoId, undo ? Old : New);
}

// Prefab instance eklendi: undo = kok + tum expand-node'lar gider; redo = yeniden expand.
public sealed class PrefabAddOp : UndoOp
{
    public int RootId;
    public string Guid;
    public int Parent;
    public string Name;

    public PrefabAddOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        if (undo)
            es.RemovePrefabInstance(RootId);
        else
            es.ReaddPrefabInstance(RootId, Guid, Parent, Name);
    }
}

// Prefab COCUGU silindi: koke 'removed' isareti + alt-agac. Undo isareti kaldirip geri koyar.
public sealed class RemovePrefabChildOp : UndoOp
{
    public int RootId;
    public int LocalId;
    public readonly List<(SceneDoc.GoDoc doc, int index)> Items = new();

    public RemovePrefabChildOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyRemovePrefabChild(RootId, LocalId, Items, undo);
}

// Alt agac yeni yaratilan prefab'a BAGLANDI (connect). Undo isaretleri soker (dosya kalir).
public sealed class ConnectPrefabOp : UndoOp
{
    public int RootId;
    public string Guid;
    public readonly List<(int goId, int localId)> Locals = new();

    public ConnectPrefabOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo) => es.ApplyConnectPrefab(this, undo);
}

public sealed class AddCompOp : UndoOp
{
    public int GoId;
    public SceneDoc.CompDoc Comp;
    public int Index;

    public AddCompOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        var g = es.FindGo(GoId);
        if (g == null)
            return;
        if (undo)
            g.Components.Remove(Comp);
        else
            g.Components.Insert(Index, Comp);
        es.RefreshLive();
    }
}

public sealed class RemoveCompOp : UndoOp
{
    public int GoId;
    public SceneDoc.CompDoc Comp;
    public int Index;

    public RemoveCompOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        var g = es.FindGo(GoId);
        if (g == null)
            return;
        if (undo)
            g.Components.Insert(Index, Comp);
        else
            g.Components.Remove(Comp);
        es.RefreshLive();
    }
}

// CommitComponent: component'in tum prop setinin degisimi (zengin editorler).
public sealed class CompPropsOp : UndoOp
{
    public int GoId;
    public int CompIndex;
    public List<KeyValuePair<string, DigitoyEngine.DocNode>> Old;
    public List<KeyValuePair<string, DigitoyEngine.DocNode>> New;

    public override void Apply(EditorScene es, bool undo)
        => es.ApplyCompProps(GoId, CompIndex, undo ? Old : New);

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not CompPropsOp p || p.GoId != GoId || p.CompIndex != CompIndex)
            return false;
        New = p.New; // drag boyunca ardisik commit'ler tek undo adimi
        return true;
    }
}
