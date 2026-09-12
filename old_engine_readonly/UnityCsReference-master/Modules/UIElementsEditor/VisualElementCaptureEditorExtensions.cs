// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.UIElements
{
    /// <summary>
    /// Use these extension methods to save the rendered visual content of a <see cref="VisualElement"/> to disk.
    /// </summary>
    public static class VisualElementCaptureEditorExtensions
    {
        /// <summary>
        /// Renders the element's panel, captures the visual content of <paramref name="element"/>,
        /// and saves it to disk as a PNG file.
        /// </summary>
        /// <remarks>
        /// This method repaints the whole panel before it captures content. This method calls
        /// <see cref="VisualElementCaptureExtensions.CaptureToRenderTexture(VisualElement)"/>, 
        /// then encodes the result as a PNG, and releases the intermediate texture.
        /// </remarks>
        /// <param name="element">The element to capture. It must belong to a panel.</param>
        /// <param name="path">The destination file path. This function creates the directory if it does not exist.</param>
        /// <exception cref="ArgumentNullException">Throws if <paramref name="element"/> is null.</exception>
        /// <exception cref="ArgumentException">Throws if <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// Throws if the element does not belong to a panel, or the panel cannot be captured because it draws
        /// directly into cameras.
        /// </exception>
        public static void CaptureToPNG(this VisualElement element, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path must be provided.", nameof(path));

            // CaptureToRenderTexture validates the element and panel, and throws for camera-drawn panels.
            var capture = element.CaptureToRenderTexture();
            try
            {
                WritePNG(capture, path);
            }
            finally
            {
                capture.Release();
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        static void WritePNG(RenderTexture renderTexture, string path)
        {
            var oldActive = RenderTexture.active;
            var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            byte[] pngBytes;
            try
            {
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
                texture.Apply(false, false);
                pngBytes = texture.EncodeToPNG();
            }
            finally
            {
                // Always restore the active render texture and release the scratch texture.
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(texture);
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, pngBytes);
        }
    }
}
