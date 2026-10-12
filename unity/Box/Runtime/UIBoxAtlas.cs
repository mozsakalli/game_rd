using System;
using UnityEngine;

namespace Digitoy.Box
{
    /// <summary>
    /// Project-level UI atlas: fonts (MTSDF) and, later, images baked into shared RGBA32 pages, every page
    /// carrying the <see cref="UIPieces"/> band. One font always lives on ONE page, so a box and its text
    /// sample a single texture → one draw. Produced by the editor-only <c>.uiboxatlas</c> importer
    /// (<see cref="UIFont"/> + page textures are sub-assets); runtime only reads.
    /// </summary>
    public sealed class UIBoxAtlas : ScriptableObject
    {
        [Serializable]
        public struct PageInfo
        {
            public Texture2D texture;
            public int width, height;
            /// <summary>UIPieces band origin on this page (px, row 0 top).</summary>
            public int piecesX, piecesY;
        }

        public PageInfo[] pages = Array.Empty<PageInfo>();
        public UIFont[] fonts = Array.Empty<UIFont>();

        public UIFont FindFont(string fontName)
        {
            foreach (var f in fonts)
                if (f != null && f.name == fontName)
                    return f;
            return null;
        }
    }
}
