using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Ceviri (localization) undo op'lari. Doc tarafi docId adresli (PropOp gibi), tablo tarafi
// LocTableStore uzerinden (canli instance + dirty). Hepsi tek bogaz: inspector/pencere bunlari push eder.

// "<alan>@loc" prop'u: null = yok (varsayilan), "" = bilerek kapali, key = bagli. Yeni satir acildiysa
// undo satiri da siler (CreatedKey), redo geri acar.
public sealed class LocKeyOp : UndoOp
{
    public int GoId, CompIndex;
    public string Field;
    public string OldKey, NewKey;
    public string OldText, NewText; // bagsiz durumdaki doc metni (bagliyken prop yok)
    public string Scope, CreatedKey;
    public List<string> CreatedValues;

    public LocKeyOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        if (undo)
        {
            es.ApplyLocKeyOp(GoId, CompIndex, Field, OldKey, OldText);
            if (CreatedKey != null)
                LocTableStore.RemoveRow(Scope, CreatedKey);
        }
        else
        {
            if (CreatedKey != null)
                LocTableStore.AddRow(Scope, CreatedKey, CreatedValues);
            es.ApplyLocKeyOp(GoId, CompIndex, Field, NewKey, NewText);
        }
    }
}

// Bagli alanin metni = yalniz tablonun calisilan-dil hucresi (doc'ta metin prop'u yok); yazarken birlesir.
public sealed class LocEditOp : UndoOp
{
    public int GoId, CompIndex;
    public string Prop;
    public string Scope, Key, Lang;
    public string OldValue, NewValue;

    public override void Apply(EditorScene es, bool undo)
        => LocTableStore.SetValue(Scope, Key, Lang, undo ? OldValue : NewValue);

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not LocEditOp o || o.GoId != GoId || o.CompIndex != CompIndex || o.Prop != Prop
            || o.Scope != Scope || o.Key != Key || o.Lang != Lang)
            return false;
        NewValue = o.NewValue;
        return true;
    }
}

// Tablo hucresi (inspector diger-dil satirlari, pencere): yazarken birlesir.
public sealed class LocCellOp : UndoOp
{
    public string Scope, Key, Lang;
    public string OldValue, NewValue;

    public override void Apply(EditorScene es, bool undo)
        => LocTableStore.SetValue(Scope, Key, Lang, undo ? OldValue : NewValue);

    public override bool TryCoalesce(UndoOp next)
    {
        if (next is not LocCellOp o || o.Scope != Scope || o.Key != Key || o.Lang != Lang)
            return false;
        NewValue = o.NewValue;
        return true;
    }
}

// Key rename: tablo satiri + ACIK doc'taki tum "@loc" prop'lari (baska sahneler: LocTableStore.RenameInFiles).
public sealed class LocRenameOp : UndoOp
{
    public string Scope, OldKey, NewKey;

    public LocRenameOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        string from = undo ? NewKey : OldKey, to = undo ? OldKey : NewKey;
        LocTableStore.RenameRow(Scope, from, to);
        es.RenameLocKey(from, to);
    }
}

// Satir ekle/sil (pencere).
public sealed class LocRowOp : UndoOp
{
    public string Scope, Key;
    public List<string> Values;
    public bool Removed; // true: op satiri SILDI (undo geri ekler)

    public LocRowOp() => Sealed = true;

    public override void Apply(EditorScene es, bool undo)
    {
        bool add = Removed == undo;
        if (add)
            LocTableStore.AddRow(Scope, Key, Values);
        else
            LocTableStore.RemoveRow(Scope, Key);
    }
}
