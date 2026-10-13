using System;
using UnityEngine;

namespace Digitoy.Box
{
    /// <summary>
    /// Procedural gloss (core UIKit <c>fx_button_light</c> stand-in): a soft radial highlight centred
    /// horizontally, hanging from the box's top edge. Same sheet (UIPieces shadow ramp), 16 vertices,
    /// no extra texture/draw. Sprite override arrives with the atlas sprite phase.
    /// </summary>
    [Serializable]
    public struct UIBoxLight
    {
        public bool enabled;
        public Color color;
        [Range(0.1f, 1f)] public float width;   // fraction of box width
        [Min(1f)] public float height;          // px
        public float offsetY;                   // px from the top edge down to the light's top
        public float offsetX;                   // px horizontal shift from centre

        public static UIBoxLight Default => new UIBoxLight
        {
            enabled = false,
            color = new Color(1f, 1f, 1f, 0.8f),
            width = 0.9f,
            height = 24f,
            offsetY = 2f,
            offsetX = 0f,
        };
    }
}
