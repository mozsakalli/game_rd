using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Inspector ceviri katmani (docs/engine-notes.md LOCALIZATION F2): component KOK duzeyindeki her string
// alanin saginda "loc" toggle'i. Bagliyken metin kutusu TABLONUN calisilan-dil hucresini gosterir/yazar
// (doc prop'u onbellek olarak birlikte guncellenir; bu dilde bos = sol kirmizi serit), altinda key satiri
// (Rename) ve yazarken canli oneri listesi (mevcut satira bagla; tekrar acilmasin). Diger dillerin
// hucreleri BURADA DEGIL (kafa karistiriyordu): Window/Localization.
// OLCUM (LocExtraRows) ve CIZIM ayni durumdan hesaplanir: bag var mi, oneri sayisi.
public sealed partial class InspectorPanel
{
    const float LocToggleW = 30f;
    const int LocSuggestMax = 5;

    static readonly Color LocOnColor = new(70, 130, 180, 255);
    static readonly Color LocOffColor = new(58, 62, 72, 255);
    static readonly Color LocMissingColor = new(226, 96, 96, 255);
    static readonly Color LocSuggestColor = new(40, 60, 50, 255);

    string _locSuggestPath; // oneri listesi acik olan alan (yazarken acilir; secim/tiklamayla kapanir)
    readonly List<(string Key, string Text)> _locSuggest = new();
    string _locKeyPath, _locKeyText; // key satiri duzenleme tamponu (Rename'e kadar doc'a yazilmaz)

    static bool IsLocCandidate(SerializedType.FieldSchema f) => f.Kind == SerializedType.Kind.String;

    // Alanin altina gelen ek satir sayisi (olcum + cizim).
    int LocExtraRows(EditorScene es, SceneDoc.CompDoc cd, SerializedType.FieldSchema f, string path)
    {
        string key = EditorScene.LocKeyOf(cd, f.Name);
        if (string.IsNullOrEmpty(key))
            return 0;
        string scope = es.Doc.LocTable ?? LocTableStore.HostScope;
        int rows = 1; // key satiri (diger dillerin hucreleri Localization penceresinde)
        string lang = LocTableStore.EffectiveLanguage(scope);
        string shown = LocTableStore.GetValue(scope, key, lang) ?? "";
        int argc = PlaceholderCount(shown);
        if (argc > 0)
            rows += argc + 1; // arg kutulari + onizleme
        if (_locSuggestPath == path)
        {
            LocTableStore.Suggest(LocTableStore.Get(scope), lang, shown, key, _locSuggest, LocSuggestMax);
            rows += _locSuggest.Count;
        }
        return rows;
    }

    // "{0} coins {1:number}" -> 2 (en buyuk indeks + 1); "{{" kacisi sayilmaz.
    static int PlaceholderCount(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int max = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '{') continue;
            if (i + 1 < s.Length && s[i + 1] == '{') { i++; continue; }
            int j = i + 1, v = 0; bool any = false;
            while (j < s.Length && char.IsDigit(s[j])) { v = v * 10 + (s[j] - '0'); j++; any = true; }
            if (any && j < s.Length && (s[j] == '}' || s[j] == ':') && v > max) max = v;
        }
        return max + 1;
    }

    readonly Dictionary<string, string> _locArgs = new(); // path#i -> onizleme arg metni (editor durumu)

    void DrawLocField(EditorScene es, SceneDoc.GoDoc g, SceneDoc.CompDoc cd,
        SerializedType.FieldSchema f, DocNode node, ref float y, float w)
    {
        int ci = g.Components.IndexOf(cd);
        string path = "c:" + g.Id + ":" + ci + ":" + f.Name;
        string key = EditorScene.LocKeyOf(cd, f.Name);   // null | "" | key
        bool bound = !string.IsNullOrEmpty(key);
        string scope = es.Doc.LocTable ?? LocTableStore.HostScope;
        string lang = LocTableStore.EffectiveLanguage(scope);
        string docText = node?.Scalar ?? "";
        string cell = bound ? LocTableStore.GetValue(scope, key, lang) : null;
        string shown = bound ? (cell ?? "") : docText;

        var labelRect = new Rect(12, y, LabelW, 18);
        float valueX = 12 + LabelW;
        var valueRect = new Rect(valueX, y, Math.Max(20, w - valueX - LocToggleW - 4), 18);
        var toggleRect = new Rect(w - LocToggleW, y, LocToggleW - 2, 18);
        DrawFieldLabel(labelRect, f.Name);
        if (bound && string.IsNullOrEmpty(cell) && Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(valueRect.x - 4, y + 1, 3, 16), LocMissingColor, 1); // bu dilde eksik

        // Metin kutusu (doc string tamponu ile ayni mekanizma; gorunen deger tablo hucresi olabilir).
        var tmp = DocNode.Scal(shown);
        bool changed = false;
        DrawDocString(valueRect, shown, tmp, path, ref changed);
        if (changed)
        {
            string next = tmp.Scalar ?? "";
            if (bound)
            {
                es.History.Push(new LocEditOp
                {
                    GoId = g.Id, CompIndex = ci, Prop = f.Name,
                    Scope = scope, Key = key, Lang = lang, OldValue = cell ?? "", NewValue = next,
                });
                LocTableStore.SetValue(scope, key, lang, next); // doc'a yazilmaz: metin tabloda; Touch -> RefreshAll canliyi yazar
                _locSuggestPath = path;
            }
            else
                es.SetProp(g, cd, f, docText, next); // varsayilan-cevrilen alansa SetProp bagi bekletir (TickLoc)
        }

        // Toggle: bagli = mavi serit, degil = soluk.
        bool clicked = Gui.Button(toggleRect, "loc");
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(toggleRect.x + 2, toggleRect.yMax - 3, toggleRect.width - 4, 2),
                bound ? LocOnColor : LocOffColor, 1);
        if (clicked)
        {
            if (bound)
                es.SetLocKey(g, cd, f.Name, ""); // bilerek kapali (nick vb.)
            else if (es.Doc.LocTable != null)
                es.BindLocNew(g, cd, f, docText);
            _locSuggestPath = null;
            _locKeyPath = null;
            y += RowH;
            return; // bag durumu degisti: alt satirlar sonraki frame
        }
        y += RowH;
        if (!bound)
            return;

        float subX = valueX;
        float subW = Math.Max(20, w - subX - 4);

        // Key satiri: [key alani] [Rename] â€” tampon ayri (yazarken doc'a gitmez, Rename'le uygulanir).
        DrawFieldLabel(new Rect(24, y, LabelW - 12, 18), "key");
        string keyCur = _locKeyPath == path ? _locKeyText : key;
        var keyNode = DocNode.Scal(keyCur);
        bool keyChanged = false;
        DrawDocString(new Rect(subX, y, subW - 68, 18), keyCur, keyNode, path + "@key", ref keyChanged);
        if (keyChanged)
        {
            _locKeyPath = path;
            _locKeyText = keyNode.Scalar ?? "";
        }
        string keyBuf = _locKeyPath == path ? _locKeyText : key;
        if (Gui.Button(new Rect(w - 66, y, 62, 18), "Rename") && keyBuf != key && keyBuf.Length > 0)
        {
            if (LocTableStore.Get(scope)?.Find(keyBuf) >= 0)
                EditorLog.Warning($"[loc] key zaten var: {keyBuf}");
            else
            {
                es.History.Push(new LocRenameOp { Scope = scope, OldKey = key, NewKey = keyBuf });
                LocTableStore.RenameRow(scope, key, keyBuf);
                es.RenameLocKey(key, keyBuf);
                int other = LocTableStore.RenameInOtherScenes(scope, key, keyBuf, es.Path);
                if (other > 0)
                    EditorLog.Info($"[loc] rename {key} -> {keyBuf}: {other} baska dosyada guncellendi");
                _locKeyPath = null;
                ResetDocTextBuffers();
                return;
            }
        }
        y += RowH;

        // Parametre onizleme (Unity inspector paritesi): {n} basina arg kutusu + bicimlenmis sonuc.
        int argc = PlaceholderCount(shown);
        if (argc > 0)
        {
            var args = new object[argc];
            for (int i = 0; i < argc; i++)
            {
                string ak = path + "#" + i;
                string cur = _locArgs.TryGetValue(ak, out var av) ? av : "";
                DrawFieldLabel(new Rect(24, y, LabelW - 12, 18), "{" + i + "}");
                var an = DocNode.Scal(cur);
                bool ac = false;
                DrawDocString(new Rect(subX, y, subW, 18), cur, an, ak, ref ac);
                if (ac) { cur = an.Scalar ?? ""; _locArgs[ak] = cur; }
                args[i] = long.TryParse(cur, out long li) ? li : double.TryParse(cur, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dv) ? dv : cur;
                y += RowH;
            }
            DrawFieldLabel(new Rect(24, y, LabelW - 12, 18), "preview");
            if (Event.Current.Type == EventType.Repaint)
            {
                string preview;
                try { preview = Localize.FormatString(shown, args, lang, LocTableStore.Get(scope)); }
                catch (Exception e) { preview = "error: " + e.Message; }
                GuiRenderer.DrawTextIn(new Rect(subX + 2, y, subW - 4, 18), preview, SmallFont, RefTextColor, false, 2);
            }
            y += RowH;
        }

        // Oneriler: mevcut satira bagla (yeni acilan satir kullanilmiyorsa silinir).
        if (_locSuggestPath == path)
        {
            LocTableStore.Suggest(LocTableStore.Get(scope), lang, shown, key, _locSuggest, LocSuggestMax);
            for (int i = 0; i < _locSuggest.Count; i++)
            {
                var (sk, st) = _locSuggest[i];
                var r = new Rect(subX, y, subW, 18);
                if (Event.Current.Type == EventType.Repaint)
                    GuiRenderer.DrawRect(r, LocSuggestColor, 2);
                if (Gui.Button(r, sk + "  â€”  " + st))
                {
                    es.RebindLocExisting(g, cd, f, key, sk);
                    _locSuggestPath = null;
                    ResetDocTextBuffers();
                    return;
                }
                y += RowH;
            }
        }
    }
}
