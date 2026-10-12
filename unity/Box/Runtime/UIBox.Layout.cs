using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Digitoy.Box
{
    public enum LayoutMode { None = 0, Horizontal = 1, Vertical = 2 }
    public enum LayoutJustify { Start = 0, Center = 1, End = 2, SpaceBetween = 3, SpaceAround = 4, SpaceEvenly = 5 }
    // Grow = icerik kutuyu buyutur; Visible = authored boyut, tasan cizilir; Hidden = authored boyut + RectMask2D;
    // Wrap = ana eksende sigmayan cocuk sonraki satira (capraz eksen Grow ise satir toplamina buyur).
    public enum OverflowMode { Grow = 0, Visible = 1, Hidden = 2, Wrap = 3 }

    // Engine LayoutBox'un YERLESIM yarisi (CSS-box: anchor yerlesimi + flex dizme), RectTransform uzerinden:
    //  * Kaynak = bu component'in alanlari (width/height authored, grow, layout...). RectTransform'un
    //    sizeDelta'si HEP surulur (nokta anchor: preferred, stretch: authored delta); grup diziyorsa
    //    anchor (0,1) + anchoredPosition da surulur (DrivenRectTransformTracker). Pivot/anchor/pozisyon
    //    dizilmeyen kutuda kullanicinindir (Unity'nin kendi alanlari).
    //  * Kutu agaci = DOGRUDAN ebeveyn/cocuk UIBox'lar (arada kutusuz GO = zincir kopar).
    //  * Zamanlama = LAZY PULL: setter'lar kirletir, RectWidth/RectHeight okuyan aninda cozer;
    //    Canvas.preWillRenderCanvases sweep'i okunmamis kirlileri Canvas rebuild'den ONCE cozer.
    //  * Olcum: preferred = max(authored, icerik); icerik = cocuk kutular + kendi metni (senkron).
    //  * Hidden eksen = ayni GO'ya RectMask2D (geometrik kirpma yerine; batch-dostu).
    public sealed partial class UIBox : ILayoutElement
    {
        [Header("Layout — self (authored size; RectTransform.sizeDelta is driven)")]
        [SerializeField] float width, height;          // nokta anchor: boyut; stretch: delta (authored)
        [SerializeField] float grow;                   // >0: parent grubun kalan alanindan agirlikli pay
        [SerializeField] bool ignoreLayout;            // grup beni dizmesin (anchor'la yerlesirim)
        [Header("Layout — children")]
        [SerializeField] LayoutMode layout;
        [SerializeField] float spacing;
        [SerializeField] LayoutAlign alignChildren = LayoutAlign.Start;
        [SerializeField] LayoutJustify justifyChildren = LayoutJustify.Start;
        [SerializeField] bool reverse;
        [SerializeField] OverflowMode overflowX;
        [SerializeField] OverflowMode overflowY;

        public float Width { get => width; set { if (width != value) { width = value; MarkDirty(); } } }
        public float Height { get => height; set { if (height != value) { height = value; MarkDirty(); } } }
        public float Grow { get => grow; set { if (grow != value) { grow = value; MarkDirty(); } } }
        public bool IgnoreLayout { get => ignoreLayout; set { if (ignoreLayout != value) { ignoreLayout = value; MarkDirty(); } } }
        public LayoutMode Layout { get => layout; set { if (layout != value) { layout = value; MarkDirty(); } } }
        public float Spacing { get => spacing; set { if (spacing != value) { spacing = value; MarkDirty(); } } }
        public LayoutAlign AlignChildren { get => alignChildren; set { if (alignChildren != value) { alignChildren = value; MarkDirty(); } } }
        public LayoutJustify JustifyChildren { get => justifyChildren; set { if (justifyChildren != value) { justifyChildren = value; MarkDirty(); } } }
        public bool Reverse { get => reverse; set { if (reverse != value) { reverse = value; MarkDirty(); } } }
        public OverflowMode OverflowX { get => overflowX; set { if (overflowX != value) { overflowX = value; MarkDirty(); } } }
        public OverflowMode OverflowY { get => overflowY; set { if (overflowY != value) { overflowY = value; MarkDirty(); } } }
        public OverflowMode Overflow { get => overflowX; set { OverflowX = value; OverflowY = value; } }

        float PadLeft => padding.x;
        float PadTop => padding.y;
        float PadRight => padding.z;
        float PadBottom => padding.w;

        // --- cozulmus durum ---
        bool _dirty = true;
        bool _queued;
        UIBox _parentBox;        // son cozumde beni dizen ebeveyn
        bool _placedByGroup;     // grup dizdi (anchor/pozisyon surulu)
        float _prefW, _prefH;
        bool _prefWValid, _prefHValid;
        DrivenRectTransformTracker _tracker;   // struct: readonly OLMAMALI (mutasyon kopyaya giderdi)

        /// <summary>Cozulmus genislik; okumak COZER (lazy pull).</summary>
        public float RectWidth { get { ResolveIfDirty(); return _rw; } }
        /// <summary>Cozulmus yukseklik; okumak COZER (lazy pull).</summary>
        public float RectHeight { get { ResolveIfDirty(); return _rh; } }

        // --- kirletme ---

        /// <summary>Kirlet + zinciri yukari kirlet (autosize) + sweep kuyruguna gir. INVARIANT: dirty kutu ⇒ tum atalari dirty.</summary>
        public void MarkDirty()
        {
            var b = this;
            while (b != null && !b._dirty)
            {
                b._dirty = true;
                b._prefWValid = b._prefHValid = false;
                b = b.ParentBox();
            }
            // Zaten dirty olan kutunun olcum cache'i de dusmeli (ayni frame icinde ikinci degisiklik).
            _prefWValid = _prefHValid = false;
            QueueSweep();
        }

        static readonly List<UIBox> s_queue = new List<UIBox>();
        static bool s_sweepHooked;

        void QueueSweep()
        {
            if (_queued)
                return;
            _queued = true;
            s_queue.Add(this);
            if (!s_sweepHooked)
            {
                s_sweepHooked = true;
                Canvas.preWillRenderCanvases += Sweep;
            }
        }

        // Pull'un kacirdigi kirlileri Canvas rebuild'den once cozer.
        static void Sweep()
        {
            int n = s_queue.Count;
            if (n == 0)
                return;
            for (int i = 0; i < n; i++)
            {
                var b = s_queue[i];
                if (b == null)
                    continue;
                b._queued = false;
                if (b.isActiveAndEnabled)
                    b.ResolveIfDirty();
            }
            s_queue.RemoveRange(0, n);
        }

        // Dogrudan ebeveyn GO'daki aktif kutu (katilik: atlama yok).
        UIBox ParentBox()
        {
            var p = transform.parent;
            if (p == null)
                return null;
            var b = p.GetComponent<UIBox>();
            return b != null && b.isActiveAndEnabled ? b : null;
        }

        // Layout'a katilan cocuk kutu: aktif GO + enabled kutu.
        static UIBox BoxOf(Transform t)
        {
            if (!t.gameObject.activeInHierarchy)
                return null;
            var b = t.GetComponent<UIBox>();
            return b != null && b.enabled ? b : null;
        }

        // --- Unity kancalari (Graphic'inkilerle birlesik) ---

        void OnTransformChildrenChanged() => MarkDirty();

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            UIBoxMaterials.EnsureCanvasChannels(canvas);
            MarkDirty();
        }

        static int s_writing;   // kendi sizeDelta yazimlarimizin tetikledigi callback'leri yut

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (s_writing == 0)
                MarkDirty();
        }

        // --- cozum ---

        public void ResolveIfDirty()
        {
            if (!_dirty)
            {
                UIBox dirtyTop = null;
                for (var p = _parentBox; p != null && p.isActiveAndEnabled; p = p._parentBox)
                    if (p._dirty)
                        dirtyTop = p;
                dirtyTop?.ResolveTop();
                return;
            }
            var top = this;
            for (var p = ParentBox(); p != null; p = p.ParentBox())
                if (p._dirty)
                    top = p;
            top.ResolveTop();
        }

        // Zincir tepesi: kendi rect'ini kur, alt agaci diz.
        void ResolveTop()
        {
            _dirty = false;
            var parent = ParentBox();
            _parentBox = parent;
            s_writing++;
            try
            {
                if (parent != null)
                    PlaceInParent(parent);     // guvenlik dali
                else
                {
                    // Bagimsiz kutu: boyut preferred (nokta anchor) / authored delta (stretch); pozisyona DOKUNULMAZ.
                    _tracker.Clear();
                    _placedByGroup = false;
                    var rt = rectTransform;
                    bool sx = rt.anchorMin.x != rt.anchorMax.x, sy = rt.anchorMin.y != rt.anchorMax.y;
                    var sd = new Vector2(sx ? width : PreferredW(), sy ? height : PreferredH());
                    _tracker.Add(this, rt, DrivenTransformProperties.SizeDelta);
                    if (rt.sizeDelta != sd)
                        rt.sizeDelta = sd;
                    var r = rt.rect;
                    _rw = r.width;
                    _rh = r.height;
                }
                ArrangeChildren();
            }
            finally { s_writing--; }
            UpdateMask();
        }

        // --- olcum (measure): asagidan yukari ---

        float PreferredW()
        {
            if (_prefWValid)
                return _prefW;
            _prefW = ComputePreferredW();
            _prefWValid = true;
            return _prefW;
        }

        float PreferredH()
        {
            if (_prefHValid)
                return _prefH;
            _prefH = ComputePreferredH();
            _prefHValid = true;
            return _prefH;
        }

        float ComputePreferredW()
        {
            if (overflowX != OverflowMode.Grow)
                return width;
            float aw = Mathf.Max(width, TextPreferredWidth(width));
            if (layout == LayoutMode.None)
                return aw;
            float pad = PadLeft + PadRight;
            if (layout == LayoutMode.Vertical && overflowY == OverflowMode.Wrap)
                return Mathf.Max(aw, WrapContentCross(false) + pad);
            var t = transform;
            if (layout == LayoutMode.Horizontal)
            {
                float sum = 0;
                int n = 0;
                for (int i = 0; i < t.childCount; i++)
                {
                    var b = BoxOf(t.GetChild(i));
                    if (b == null || b.ignoreLayout)
                        continue;
                    n++;
                    if (b.grow <= 0)
                        sum += b.PreferredW();
                }
                return Mathf.Max(aw, sum + pad + spacing * Mathf.Max(0, n - 1));
            }
            float mx = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var b = BoxOf(t.GetChild(i));
                if (b == null || b.ignoreLayout)
                    continue;
                mx = Mathf.Max(mx, b.PreferredW());
            }
            return Mathf.Max(aw, mx + pad);
        }

        float ComputePreferredH()
        {
            if (overflowY != OverflowMode.Grow)
                return height;
            float ah = Mathf.Max(height, TextPreferredHeight(width));
            if (layout == LayoutMode.None)
                return ah;
            float pad = PadTop + PadBottom;
            if (layout == LayoutMode.Horizontal && overflowX == OverflowMode.Wrap)
                return Mathf.Max(ah, WrapContentCross(true) + pad);
            var t = transform;
            if (layout == LayoutMode.Vertical)
            {
                float sum = 0;
                int n = 0;
                for (int i = 0; i < t.childCount; i++)
                {
                    var b = BoxOf(t.GetChild(i));
                    if (b == null || b.ignoreLayout)
                        continue;
                    n++;
                    if (b.grow <= 0)
                        sum += b.PreferredH();
                }
                return Mathf.Max(ah, sum + pad + spacing * Mathf.Max(0, n - 1));
            }
            float mx = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var b = BoxOf(t.GetChild(i));
                if (b == null || b.ignoreLayout)
                    continue;
                mx = Mathf.Max(mx, b.PreferredH());
            }
            return Mathf.Max(ah, mx + pad);
        }

        const float WrapEps = 0.01f;

        float WrapContentCross(bool horiz)
        {
            float availMain = horiz ? width - PadLeft - PadRight : height - PadTop - PadBottom;
            if (availMain <= 0)
                availMain = float.MaxValue;
            float sum = 0, used = 0, lineCross = 0;
            int n = 0, lines = 0;
            var t = transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var b = BoxOf(t.GetChild(i));
                if (b == null || b.ignoreLayout)
                    continue;
                float main = horiz ? b.PreferredW() : b.PreferredH();
                float add = (n > 0 ? spacing : 0) + main;
                if (n > 0 && used + add > availMain + WrapEps)
                {
                    sum += (lines++ > 0 ? spacing : 0) + lineCross;
                    used = 0;
                    n = 0;
                    lineCross = 0;
                    add = main;
                }
                used += add;
                n++;
                lineCross = Mathf.Max(lineCross, horiz ? b.PreferredH() : b.PreferredW());
            }
            if (n > 0)
                sum += (lines > 0 ? spacing : 0) + lineCross;
            return sum;
        }

        // --- yerlestirme (arrange): yukaridan asagi ---

        // Anchor yerlesimi: cocugun KENDI anchor/pivot/anchoredPosition'i gecerli; yalniz sizeDelta surulur
        // (nokta anchor: preferred, stretch: authored delta). Boyut parent rect'inden hesaplanir.
        void PlaceInParent(UIBox parent)
        {
            var rt = rectTransform;
            _tracker.Clear();
            _placedByGroup = false;
            bool sx = rt.anchorMin.x != rt.anchorMax.x, sy = rt.anchorMin.y != rt.anchorMax.y;
            float w = sx ? (rt.anchorMax.x - rt.anchorMin.x) * parent._rw + width : PreferredW();
            float h = sy ? (rt.anchorMax.y - rt.anchorMin.y) * parent._rh + height : PreferredH();
            var sd = new Vector2(sx ? width : w, sy ? height : h);
            _tracker.Add(this, rt, DrivenTransformProperties.SizeDelta);
            if (rt.sizeDelta != sd)
                rt.sizeDelta = sd;
            _rw = w;
            _rh = h;
        }

        // Grup dizimi: anchor (0,1) = parent rect sol-ust; (x,y) = cocugun sol-ustu, parent sol-ustune gore y-down.
        void SetPlaced(float x, float y, float w, float h)
        {
            var rt = rectTransform;
            _tracker.Clear();
            _placedByGroup = true;
            _tracker.Add(this, rt, DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
            var tl = new Vector2(0, 1);
            if (rt.anchorMin != tl) rt.anchorMin = tl;
            if (rt.anchorMax != tl) rt.anchorMax = tl;
            var sd = new Vector2(w, h);
            if (rt.sizeDelta != sd) rt.sizeDelta = sd;
            var pv = rt.pivot;
            var ap = new Vector2(x + pv.x * w, -(y + pv.y * h));
            if (rt.anchoredPosition != ap) rt.anchoredPosition = ap;
            _rw = w;
            _rh = h;
        }

        // Cozumde cocuga yazilan rect sonrasi: alt agaca yalniz GEREKINCE inilir.
        void FinishChild(UIBox b, bool wasDirty, float oldW, float oldH)
        {
            b._parentBox = this;
            b._dirty = false;
            if (wasDirty || b._rw != oldW || b._rh != oldH)
                b.ArrangeChildren();
            b.UpdateMask();
        }

        void ArrangeChildren()
        {
            var t = transform;
            if (layout == LayoutMode.None)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    var b = BoxOf(t.GetChild(i));
                    if (b == null)
                        continue;
                    bool wasDirty = b._dirty;
                    float ow = b._rw, oh = b._rh;
                    b.PlaceInParent(this);
                    FinishChild(b, wasDirty, ow, oh);
                }
                return;
            }

            bool horiz = layout == LayoutMode.Horizontal;
            if ((horiz ? overflowX : overflowY) == OverflowMode.Wrap)
            {
                ArrangeWrapped(horiz);
                return;
            }

            float fixedSum = 0, growSum = 0;
            int count = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var b = BoxOf(t.GetChild(i));
                if (b == null || b.ignoreLayout)
                    continue;
                count++;
                if (b.grow > 0)
                    growSum += b.grow;
                else
                    fixedSum += horiz ? b.PreferredW() : b.PreferredH();
            }

            float availMain = (horiz ? _rw - PadLeft - PadRight : _rh - PadTop - PadBottom)
                - spacing * Mathf.Max(0, count - 1);
            float leftover = Mathf.Max(0, availMain - fixedSum);
            float crossAvail = horiz ? _rh - PadTop - PadBottom : _rw - PadLeft - PadRight;
            float cursor = horiz ? PadLeft : PadTop;
            float gap = spacing;
            if (growSum <= 0)
            {
                JustifyOffsets(leftover, count, out float lead, out float extraGap);
                cursor += lead;
                gap += extraGap;
            }

            int n = t.childCount;
            for (int k = 0; k < n; k++)
            {
                var b = BoxOf(t.GetChild(reverse ? n - 1 - k : k));
                if (b == null)
                    continue;
                bool wasDirty = b._dirty;
                float ow = b._rw, oh = b._rh;
                if (b.ignoreLayout)
                {
                    b.PlaceInParent(this);
                    FinishChild(b, wasDirty, ow, oh);
                    continue;
                }

                float main = b.grow > 0 ? leftover * (b.grow / growSum)
                    : (horiz ? b.PreferredW() : b.PreferredH());
                float cross = horiz ? b.PreferredH() : b.PreferredW();
                float crossOff = alignChildren == LayoutAlign.Center ? (crossAvail - cross) * 0.5f
                    : alignChildren == LayoutAlign.End ? crossAvail - cross : 0;

                if (horiz)
                    b.SetPlaced(cursor, PadTop + crossOff, main, cross);
                else
                    b.SetPlaced(PadLeft + crossOff, cursor, cross, main);
                cursor += main + gap;
                FinishChild(b, wasDirty, ow, oh);
            }
        }

        void JustifyOffsets(float leftover, int count, out float lead, out float extraGap)
        {
            lead = 0; extraGap = 0;
            if (leftover <= 0 || count <= 0)
                return;
            switch (justifyChildren)
            {
                case LayoutJustify.Center: lead = leftover * 0.5f; break;
                case LayoutJustify.End: lead = leftover; break;
                case LayoutJustify.SpaceBetween:
                    if (count > 1) extraGap = leftover / (count - 1);
                    else lead = leftover * 0.5f;
                    break;
                case LayoutJustify.SpaceAround:
                    extraGap = leftover / count;
                    lead = extraGap * 0.5f;
                    break;
                case LayoutJustify.SpaceEvenly:
                    extraGap = leftover / (count + 1);
                    lead = extraGap;
                    break;
            }
        }

        // Wrap satir dizimi (greedy): satir basina en az 1 uye; grow yok sayilir; reverse ters siradan paketler.
        void ArrangeWrapped(bool horiz)
        {
            var t = transform;
            int n = t.childCount;
            float availMain = horiz ? _rw - PadLeft - PadRight : _rh - PadTop - PadBottom;
            float mainStart = horiz ? PadLeft : PadTop;
            float crossCursor = horiz ? PadTop : PadLeft;

            int Idx(int k) => reverse ? n - 1 - k : k;
            int node = 0;
            while (node < n)
            {
                float used = 0, lineCross = 0;
                int cnt = 0;
                int scan = node;
                for (; scan < n; scan++)
                {
                    var b = BoxOf(t.GetChild(Idx(scan)));
                    if (b == null || b.ignoreLayout)
                        continue;
                    float main = horiz ? b.PreferredW() : b.PreferredH();
                    float add = (cnt > 0 ? spacing : 0) + main;
                    if (cnt > 0 && used + add > availMain + WrapEps)
                        break;
                    used += add;
                    cnt++;
                    lineCross = Mathf.Max(lineCross, horiz ? b.PreferredH() : b.PreferredW());
                }
                JustifyOffsets(Mathf.Max(0, availMain - used), cnt, out float lead, out float extraGap);
                float cursor = mainStart + lead;
                float gap = spacing + extraGap;
                for (int k = node; k < scan; k++)
                {
                    var b = BoxOf(t.GetChild(Idx(k)));
                    if (b == null)
                        continue;
                    bool wasDirty = b._dirty;
                    float ow = b._rw, oh = b._rh;
                    if (b.ignoreLayout)
                    {
                        b.PlaceInParent(this);
                        FinishChild(b, wasDirty, ow, oh);
                        continue;
                    }
                    float main = horiz ? b.PreferredW() : b.PreferredH();
                    float cross = horiz ? b.PreferredH() : b.PreferredW();
                    float crossOff = alignChildren == LayoutAlign.Center ? (lineCross - cross) * 0.5f
                        : alignChildren == LayoutAlign.End ? lineCross - cross : 0;
                    if (horiz)
                        b.SetPlaced(cursor, crossCursor + crossOff, main, cross);
                    else
                        b.SetPlaced(crossCursor + crossOff, cursor, cross, main);
                    cursor += main + gap;
                    FinishChild(b, wasDirty, ow, oh);
                }
                if (scan == node)
                    scan++;   // yalniz ignoreLayout/kutusuz cocuklar: ilerle
                node = scan;
                crossCursor += lineCross + spacing;
            }
        }

        // --- Hidden: RectMask2D ---

        void UpdateMask()
        {
            bool hidden = overflowX == OverflowMode.Hidden || overflowY == OverflowMode.Hidden;
            var mask = GetComponent<RectMask2D>();
            if (hidden)
            {
                if (mask == null)
                    mask = gameObject.AddComponent<RectMask2D>();
                if (!mask.enabled)
                    mask.enabled = true;
            }
            else if (mask != null && mask.enabled)
                mask.enabled = false;
        }

        // --- ILayoutElement: uGUI LayoutGroup icine konulan kutu preferred boyutunu bildirir (disa dogru). ---
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => -1;
        public float preferredWidth => PreferredW();
        public float flexibleWidth => -1;
        public float minHeight => -1;
        public float preferredHeight => PreferredH();
        public float flexibleHeight => -1;
        public int layoutPriority => 1;
    }
}
