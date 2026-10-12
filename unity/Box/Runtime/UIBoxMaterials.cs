using System.Collections.Generic;
using UnityEngine;

namespace Digitoy.Box
{
    public enum TextAlign { Left, Center, Right }
    public enum LayoutAlign { Start = 0, Center = 1, End = 2 }

    /// <summary>
    /// Shared material per sheet texture ("Digitoy/UIBox SDF") + the font-less fallback pieces texture.
    /// Every box/text on the same sheet shares one material → one draw.
    /// </summary>
    public static class UIBoxMaterials
    {
        public const string ShaderName = "Digitoy/UIBox SDF";

        static Shader _shader;
        static readonly Dictionary<Texture, Material> s_materials = new Dictionary<Texture, Material>();
        static Texture2D _fallback;

        public static Shader Shader
        {
            get
            {
                if (_shader == null)
                    _shader = Shader.Find(ShaderName);
                return _shader;
            }
        }

        public static Material For(Texture sheet)
        {
            if (sheet == null)
                sheet = FallbackTexture;
            if (s_materials.TryGetValue(sheet, out var m) && m != null)
                return m;
            var sh = Shader;
            if (sh == null)
            {
                Debug.LogError($"[UIBox] shader '{ShaderName}' not found (package shader missing or stripped)");
                return null;
            }
            m = new Material(sh) { name = "UIBox " + sheet.name, hideFlags = HideFlags.HideAndDontSave, mainTexture = sheet };
            s_materials[sheet] = m;
            return m;
        }

        /// <summary>Font-less boxes sample pieces from this lazily baked RGBA sheet (UiPieces.Fallback).</summary>
        public static Texture2D FallbackTexture
        {
            get
            {
                if (_fallback == null)
                {
                    const int w = 512, h = 128;
                    var px = new Color32[w * h];
                    UIPieces.BakeRegion(px, w, 0, 0);
                    var flipped = new Color32[px.Length];
                    for (int y = 0; y < h; y++)
                        System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
                    _fallback = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
                    {
                        name = "UIBox Pieces",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    _fallback.SetPixels32(flipped);
                    _fallback.Apply(false, true);
                }
                return _fallback;
            }
        }

        /// <summary>USER channel lives in TexCoord1; the canvas must forward it.</summary>
        public static void EnsureCanvasChannels(Canvas canvas)
        {
            if (canvas == null)
                return;
            const AdditionalCanvasShaderChannels need = AdditionalCanvasShaderChannels.TexCoord1;
            if ((canvas.additionalShaderChannels & need) != need)
                canvas.additionalShaderChannels |= need;
        }
    }
}
