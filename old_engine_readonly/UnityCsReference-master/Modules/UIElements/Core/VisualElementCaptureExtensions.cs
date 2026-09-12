// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Rendering;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Use these extension methods to capture the rendered visual content of a <see cref="VisualElement"/>
    /// into a <see cref="RenderTexture"/>.
    /// </summary>
    /// <remarks>
    /// You can capture elements that are part of an editor panel or a runtime panel, except
    /// if the element draws directly into a camera (for example, a world space runtime panel).
    /// These methods render the whole panel, so any elements drawn on top of the captured element are reflected in the result.
    ///
    /// The content of an <see cref="IMGUIContainer"/> is captured only when you call the capture method
    /// from within IMGUI event processing in the Editor, for example from an OnGUI callback.
    /// In any other context, the <see cref="IMGUIContainer"/>'s content is blank in the result.
    /// </remarks>
    public static class VisualElementCaptureExtensions
    {
        /// <summary>
        /// Renders the element's panel and captures the visual content of <paramref name="element"/>
        /// into a new <see cref="RenderTexture"/> sized to the element's pixel bounds.
        /// </summary>
        /// <remarks>
        /// This method repaints the whole panel before it captures content.
        /// </remarks>
        /// <param name="element">The element to capture. It must belong to a panel.</param>
        /// <returns>
        /// A new <see cref="RenderTexture"/> scaled according to the panel. The caller owns
        /// the returned texture and is responsible for releasing it with
        /// <see cref="RenderTexture.Release"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">Throws if <paramref name="element"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Throws if the element does not belong to a panel, or the element's panel cannot be captured because it draws
        /// directly into cameras.
        /// </exception>
        public static RenderTexture CaptureToRenderTexture(this VisualElement element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            var panel = element.elementPanel;
            if (panel == null)
                throw new InvalidOperationException("The element does not belong to a panel.");

            // Bring layout and visuals up to date before reading bounds and allocating the texture.
            panel.Repaint();

            GetElementPixelSize(element, panel, out int width, out int height);
            var destination = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = $"{element.name} Capture"
            };
            destination.Create();

            if (!CaptureInto(element, panel, destination))
            {
                DestroyTexture(destination);
                throw new InvalidOperationException(
                    "The element's panel cannot be captured because it draws directly into cameras.");
            }
            return destination;
        }

        /// <summary>
        /// Renders the element's panel and captures the visual content of <paramref name="element"/>
        /// into the supplied <paramref name="destination"/>.
        /// </summary>
        /// <remarks>
        /// This method repaints the whole panel before it captures content. Use
        /// <see cref="TryCaptureIntoRenderTexture(VisualElement, RenderTexture)"/> when you expect the panel is missing or draws into cameras.
        /// </remarks>
        /// <param name="element">The element to capture. It must belong to a panel.</param>
        /// <param name="destination">
        /// The texture to render into. The caller owns and is responsible for releasing it. The
        /// element is captured at the panel's scale and aligned to the top-left of the texture.
        /// Content that does not fit is cropped, and any uncovered region is left transparent.
        /// On platforms without <see cref="CopyTextureSupport.Basic"/>, the capture falls back to a
        /// blit that scales the element across the destination, so the result may be stretched and
        /// is not offset within a larger destination.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Throws if <paramref name="element"/> or <paramref name="destination"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Throws if the element does not belong to a panel, or the element's panel cannot be captured because it draws
        /// directly into cameras.
        /// </exception>
        public static void CaptureIntoRenderTexture(this VisualElement element, RenderTexture destination)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            var panel = element.elementPanel;
            if (panel == null)
                throw new InvalidOperationException("The element does not belong to a panel.");

            // Bring layout and visuals up to date before reading bounds and rendering.
            panel.Repaint();

            if (!CaptureInto(element, panel, destination))
                throw new InvalidOperationException(
                    "The element's panel cannot be captured because it draws directly into cameras.");
        }

        /// <summary>
        /// Renders the element's panel and captures the visual content of
        /// <paramref name="element"/> into the supplied <paramref name="destination"/>.
        /// Returns <c>false</c> when the element cannot be captured.
        /// </summary>
        /// <remarks>
        /// This method repaints the whole panel before it captures content.
        /// It returns <c>false</c> at runtime if capturing content is not possible,
        /// for example, if the element is not attached to a panel, or if the element's panel draws
        /// directly into cameras.
        /// </remarks>
        /// <param name="element">The element to capture.</param>
        /// <param name="destination">
        /// The texture to render into. The caller owns and is responsible for releasing it. The
        /// element is captured at the panel's scale and aligned to the top-left of the texture.
        /// Content that does not fit is cropped, and any uncovered region is left transparent.
        /// On platforms without <see cref="CopyTextureSupport.Basic"/>, the capture falls back to a
        /// blit that scales the element across the destination, so the result may be stretched and
        /// is not offset within a larger destination.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> if the capture succeeded. Returns <c>false</c> if <paramref name="element"/> is not
        /// attached to a panel, or if the element's panel cannot be captured because it draws directly into cameras.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Throws if <paramref name="element"/> or <paramref name="destination"/> is null.
        /// </exception>
        public static bool TryCaptureIntoRenderTexture(this VisualElement element, RenderTexture destination)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            // A missing panel is an expected, non-exceptional state for a Try method.
            var panel = element.elementPanel;
            if (panel == null)
                return false;

            // Bring layout and visuals up to date before reading bounds and rendering.
            panel.Repaint();

            return CaptureInto(element, panel, destination);
        }

        static void GetElementPixelSize(VisualElement element, BaseVisualElementPanel panel, out int width, out int height)
        {
            // worldBound is expressed in panel points; scale to pixels for the capture size.
            float pixelsPerPoint = panel.scaledPixelsPerPoint;
            var worldBound = element.worldBound;
            width = Mathf.Max(1, Mathf.RoundToInt(worldBound.width * pixelsPerPoint));
            height = Mathf.Max(1, Mathf.RoundToInt(worldBound.height * pixelsPerPoint));
        }

        // Assumes the panel layout/visuals are already up to date (callers Repaint first).
        static bool CaptureInto(VisualElement element, BaseVisualElementPanel panel, RenderTexture destination)
        {
            float pixelsPerPoint = panel.scaledPixelsPerPoint;
            var panelLayout = panel.visualTree.layout;
            int panelWidth = Mathf.Max(1, Mathf.CeilToInt(panelLayout.width * pixelsPerPoint));
            int panelHeight = Mathf.Max(1, Mathf.CeilToInt(panelLayout.height * pixelsPerPoint));

            // Clear so any region not covered by the element (when partially off-panel, or smaller
            // than the destination) stays transparent.
            var prevActive = RenderTexture.active;
            RenderTexture.active = destination;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prevActive;

            // 24-bit depth gives the temp texture the stencil buffer UI clipping/masking needs.
            // Match the destination's format so the GPU copy below is format-compatible.
            var panelCapture = RenderTexture.GetTemporary(panelWidth, panelHeight, 24, destination.format);
            try
            {
                if (!panel.TryRenderIntoTexture(panelCapture))
                    return false;

                GetElementPixelSize(element, panel, out int elementWidth, out int elementHeight);

                // The element's bottom-left corner in the panel texture. Textures use a bottom-left
                // origin while worldBound uses a top-left origin, so flip Y. Either coordinate can be
                // negative when the element extends past the top/left edge of the panel.
                var worldBound = element.worldBound;
                int elemSrcX = Mathf.RoundToInt(worldBound.xMin * pixelsPerPoint);
                int elemSrcY = panelHeight - Mathf.RoundToInt(worldBound.yMax * pixelsPerPoint);

                // Place the element's footprint at the top-left of the destination. In bottom-left
                // texture coordinates the top-left row starts at destination.height - elementHeight.
                int dstBaseY = destination.height - elementHeight;

                // Intersect the element's footprint with both the panel (source) and destination
                // bounds, expressed in the element's own pixel space (i from its left, j from its
                // bottom). This copies only the element's pixels — never sibling content beside it —
                // and crops whatever does not fit, leaving the rest of the destination transparent.
                int iLo = Mathf.Max(0, -elemSrcX);
                int iHi = Mathf.Min(elementWidth, Mathf.Min(panelWidth - elemSrcX, destination.width));
                int jLo = Mathf.Max(0, Mathf.Max(-elemSrcY, elementHeight - destination.height));
                int jHi = Mathf.Min(elementHeight, panelHeight - elemSrcY);

                int copyWidth = iHi - iLo;
                int copyHeight = jHi - jLo;
                if (copyWidth > 0 && copyHeight > 0)
                    CopyRegion(panelCapture, elemSrcX + iLo, elemSrcY + jLo, copyWidth, copyHeight,
                        destination, iLo, dstBaseY + jLo);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(panelCapture);
            }
            return true;
        }

        // Copies an integer sub-rect from 'source' into 'destination' at the given offset. Uses the
        // GPU copy path when supported (preserves the transparent border for partially off-panel
        // elements) and falls back to a Blit on platforms without copy support.
        static void CopyRegion(RenderTexture source, int srcX, int srcY, int width, int height,
            RenderTexture destination, int dstX, int dstY)
        {
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
            {
                Graphics.CopyTexture(source, 0, 0, srcX, srcY, width, height, destination, 0, 0, dstX, dstY);
                return;
            }

            // Portable fallback: Blit fills the whole destination, so it ignores the destination
            // offset and stretches the source region rather than placing it 1:1.
            var scale = new Vector2((float)width / source.width, (float)height / source.height);
            var offset = new Vector2((float)srcX / source.width, (float)srcY / source.height);
            var prevActive = RenderTexture.active;
            Graphics.Blit(source, destination, scale, offset);
            RenderTexture.active = prevActive;
        }

        static void DestroyTexture(RenderTexture renderTexture)
        {
            renderTexture.Release();
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(renderTexture);
                return;
            }
            Object.Destroy(renderTexture);
        }
    }
}
