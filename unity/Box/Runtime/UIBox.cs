using System;
using UnityEngine;
using UnityEngine.UI;

namespace Digitoy.Box
{
    /// <summary>
    /// DigitoyEngine LayoutBox port, step 2: the DRAWING half. One uGUI Graphic emits box (SDF pieces:
    /// fill / border ring / shadow / inner shadow) and MTSDF text from the SAME font sheet with one
    /// material → one draw for every box + text on that sheet. Layout (anchors/flex) comes in step 3.
    /// Local box space is y-DOWN (0,0 = rect top-left, px) like the engine; <see cref="UIBox.EmitQuad"/>
    /// converts to Unity's y-up rect.
    /// </summary>
    [AddComponentMenu("Digitoy/UI Box")]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed partial class UIBox : MaskableGraphic
    {
        // --- style (StyleBox). Graphic.color is IGNORED (hidden in the inspector); use Tint for runtime fades/states. ---
        [Header("Style")]
        [SerializeField] UIFill fill = UIFill.Solid(Color.white);
        [SerializeField] Vector4 radius;                       // TL, TR, BR, BL (px)
        [SerializeField] Vector4 border;                       // L, T, R, B (px)
        [SerializeField] UIFill borderFill = UIFill.Solid(Color.white);
        [SerializeField] Vector4 padding;                      // L, T, R, B (px)

        // --- shadow (alpha > 0 = on) ---
        [Header("Shadow")]
        [SerializeField] Color shadowColor = new Color(0, 0, 0, 0);
        [SerializeField] Vector2 shadowOffset = new Vector2(0, 4);  // y-down px
        [SerializeField] float shadowBlur = 8f;
        [SerializeField] float shadowGrow;

        // --- inner shadow (alpha > 0 = on) ---
        [Header("Inner Shadow")]
        [SerializeField] Color innerShadowColor = new Color(0, 0, 0, 0);
        [SerializeField] float innerShadowBlur = 8f;

        // Runtime-only tint over every layer (fill/border/text/shadows); not serialized (engine/core UIBox.Tint).
        [NonSerialized] Color _tint = Color.white;
        public Color Tint
        {
            get => _tint;
            set { if (_tint != value) { _tint = value; SetVerticesDirty(); } }
        }

        public UIFill Fill { get => fill; set { fill = value; SetVerticesDirty(); } }
        public Vector4 Radius { get => radius; set { radius = value; SetVerticesDirty(); } }
        public float CornerRadius { get => radius.x; set { radius = new Vector4(value, value, value, value); SetVerticesDirty(); } }
        public Vector4 Border { get => border; set { border = value; SetVerticesDirty(); } }
        public float BorderWidth { get => border.x; set { border = new Vector4(value, value, value, value); SetVerticesDirty(); } }
        public UIFill BorderFill { get => borderFill; set { borderFill = value; SetVerticesDirty(); } }
        public Vector4 Padding { get => padding; set { padding = value; SetVerticesDirty(); } }
        public Color ShadowColor { get => shadowColor; set { shadowColor = value; SetVerticesDirty(); } }
        public Vector2 ShadowOffset { get => shadowOffset; set { shadowOffset = value; SetVerticesDirty(); } }
        public float ShadowBlur { get => shadowBlur; set { shadowBlur = value; SetVerticesDirty(); } }
        public float ShadowGrow { get => shadowGrow; set { shadowGrow = value; SetVerticesDirty(); } }
        public Color InnerShadowColor { get => innerShadowColor; set { innerShadowColor = value; SetVerticesDirty(); } }
        public float InnerShadowBlur { get => innerShadowBlur; set { innerShadowBlur = value; SetVerticesDirty(); } }

        bool HasFill => fill.Visible;
        bool HasBorder => borderFill.Visible && (border.x > 0 || border.y > 0 || border.z > 0 || border.w > 0);
        bool HasShadow => shadowColor.a > 0f;
        bool HasInnerShadow => innerShadowColor.a > 0f && innerShadowBlur > 0f;
        bool HasStyle => HasFill || HasBorder || HasShadow || HasInnerShadow;

        // --- material / texture: the font sheet (or the fallback pieces texture) ---

        /// <summary>Sheet every quad samples from: font sheet when a font is set, else the shared fallback.</summary>
        Texture Sheet => font != null && font.sheet != null ? font.sheet : UIBoxMaterials.FallbackTexture;

        public override Texture mainTexture => Sheet;

        public override Material defaultMaterial => UIBoxMaterials.For(Sheet) ?? defaultGraphicMaterial;

        protected override void OnEnable()
        {
            base.OnEnable();
            UIBoxMaterials.EnsureCanvasChannels(canvas);
            SetAllDirty();
            MarkDirty();
        }

        protected override void OnDisable()
        {
            _tracker.Clear();
            ParentBox()?.MarkDirty();
            base.OnDisable();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            UIBoxMaterials.EnsureCanvasChannels(canvas);
        }

        // --- mesh ---

        VertexHelper _vh;
        float _rw, _rh;
        float _ox, _oy;   // Unity local offset of box-space origin (rect top-left)

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!HasStyle && !HasText)
                return;
            var rect = GetPixelAdjustedRect();
            _rw = rect.width;
            _rh = rect.height;
            if (_rw <= 0 || _rh <= 0)
                return;
            _ox = rect.xMin;
            _oy = rect.yMax;
            _vh = vh;

            PiecesOrigin(out _pbx, out _pby, out _texW, out _texH);

            float x0 = 0, y0 = 0, x1 = _rw, y1 = _rh;
            if (HasShadow)
                EmitShadow(x0, y0, x1, y1);

            float ix0 = x0, iy0 = y0, ix1 = x1, iy1 = y1;
            float irTL = radius.x, irTR = radius.y, irBR = radius.z, irBL = radius.w;
            if (HasBorder)
            {
                float bl = border.x, bt = border.y, br = border.z, bb = border.w;
                float sx = bl + br > _rw ? _rw / (bl + br) : 1f;
                float sy = bt + bb > _rh ? _rh / (bt + bb) : 1f;
                bl *= sx; br *= sx; bt *= sy; bb *= sy;
                EmitLayer(x0, y0, x1, y1, radius.x, radius.y, radius.z, radius.w, bl, bt, br, bb, isBorder: true);
                ix0 += bl; iy0 += bt; ix1 -= br; iy1 -= bb;
                irTL = Mathf.Max(radius.x - Mathf.Max(bl, bt), 0);
                irTR = Mathf.Max(radius.y - Mathf.Max(br, bt), 0);
                irBR = Mathf.Max(radius.z - Mathf.Max(br, bb), 0);
                irBL = Mathf.Max(radius.w - Mathf.Max(bl, bb), 0);
            }
            if (HasFill)
                EmitLayer(ix0, iy0, ix1, iy1, irTL, irTR, irBR, irBL, 0, 0, 0, 0, isBorder: false);
            if (HasInnerShadow)
            {
                float b = innerShadowBlur;
                EmitLayer(ix0, iy0, ix1, iy1, irTL, irTR, irBR, irBL, b, b, b, b, isBorder: false, innerBlur: b);
            }
            if (HasText)
                EmitText(x0, y0);
            _vh = null;
        }

        // Sheet pieces origin (px, row 0 top) and sheet size.
        float _pbx, _pby, _texW, _texH;

        void PiecesOrigin(out float px, out float py, out float w, out float h)
        {
            if (font != null && font.sheet != null)
            {
                px = font.piecesX; py = font.piecesY; w = font.sheetWidth; h = font.sheetHeight;
            }
            else
            {
                var t = UIBoxMaterials.FallbackTexture;
                px = 0; py = 0; w = t.width; h = t.height;
            }
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            SetAllDirty();
            if (isActiveAndEnabled)
                MarkDirty();
        }
#endif
    }
}
