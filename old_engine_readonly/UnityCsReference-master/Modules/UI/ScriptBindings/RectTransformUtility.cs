// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    public static partial class RectTransformUtility
    {
        private static readonly Vector3[] s_Corners = new Vector3[4];

        ///<summary>Does the RectTransform contain the screen point?</summary>
        ///<param name="rect">The RectTransform to test with.</param>
        ///<param name="screenPoint">The screen point to test.</param>
        ///<returns>True if the point is inside the rectangle. False if Canvas is set to <see cref="RenderMode.ScreenSpaceOverlay" />.</returns>
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint)
        {
            return RectangleContainsScreenPoint(rect, screenPoint, null);
        }

        ///<summary>Does the RectTransform contain the screen point as seen from the given camera?</summary>
        ///<param name="rect">The RectTransform to test with.</param>
        ///<param name="screenPoint">The screen point to test.</param>
        ///<param name="cam">The camera from which the test is performed from. (Optional)</param>
        ///<returns>True if the point is inside the rectangle.</returns>
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam)
        {
            return RectangleContainsScreenPoint(rect, screenPoint, cam, Vector4.zero);
        }

        ///<summary>Does the RectTransform, with the given offset, contain the screen point as seen from the given camera?</summary>
        ///<param name="rect">The RectTransform to test with.</param>
        ///<param name="screenPoint">The screen point to test.</param>
        ///<param name="cam">The camera from which the test is performed from. (Optional)</param>
        ///<param name="offset">The offset to apply to the RectTransform.</param>
        ///<returns>True if the point is inside the rectangle.</returns>
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam, Vector4 offset)
        {
            return PointInRectangle(screenPoint, rect, cam, offset);
        }

        ///<summary>Transform a screen space point to a position in world space that is on the plane of the given RectTransform.</summary>
        ///<remarks>The cam parameter should be the camera associated with the screen point. For a RectTransform in a Canvas set to Screen Space - Overlay mode, the cam parameter should be null.
        ///
        ///When ScreenPointToWorldPointInRectangle is used from within an event handler that provides a PointerEventData object, the correct camera can be obtained by using <c>PointerEventData.enterEventCamera</c> (for hover functionality) or <c>PointerEventData.pressEventCamera</c> (for click functionality). This will automatically use the correct camera (or null) for the given event.</remarks>
        ///<param name="rect">The RectTransform to find a point inside.</param>
        ///<param name="screenPoint">Screen space position.</param>
        ///<param name="cam">The camera associated with the screen space position.</param>
        ///<param name="worldPoint">Point in world space.</param>
        ///<returns>Returns true if the plane of the RectTransform is hit, regardless of whether the point is inside the rectangle.</returns>
        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint)
        {
            worldPoint = Vector2.zero;
            Ray ray = ScreenPointToRay(cam, screenPoint);
            var plane = new Plane(rect.rotation * Vector3.back, rect.position);

            float dist = 0;

            float dot = Vector3.Dot(Vector3.Normalize(rect.position - ray.origin), plane.normal);

            if (dot != 0 && !plane.Raycast(ray, out dist))
                return false;

            worldPoint = ray.GetPoint(dist);
            return true;
        }

        ///<summary>Transform a screen space point to a position in the local space of a RectTransform that is on the plane of its rectangle.</summary>
        ///<remarks>The cam parameter should be the camera associated with the screen point. For a RectTransform in a Canvas set to Screen Space - Overlay mode, the cam parameter should be null.
        ///
        ///When ScreenPointToLocalPointInRectangle is used from within an event handler that provides a PointerEventData object, the correct camera can be obtained by using <c>PointerEventData.enterEventCamera</c> (for hover functionality) or <c>PointerEventData.pressEventCamera</c> (for click functionality). This will automatically use the correct camera (or null) for the given event.</remarks>
        ///<param name="rect">The RectTransform to find a point inside.</param>
        ///<param name="screenPoint">Screen space position.</param>
        ///<param name="cam">The camera associated with the screen space position.</param>
        ///<param name="localPoint">Point in local space of the rect transform.</param>
        ///<returns>Returns true if the plane of the RectTransform is hit, regardless of whether the point is inside the rectangle.</returns>
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            Vector3 worldPoint;
            if (ScreenPointToWorldPointInRectangle(rect, screenPoint, cam, out worldPoint))
            {
                localPoint = rect.InverseTransformPoint(worldPoint);
                return true;
            }
            return false;
        }

        ///<summary>Transforms a screen space position into a ray.</summary>
        ///<param name="cam">The camera from which the ray originates. (Optional)</param>
        ///<param name="screenPos">The screen point.</param>
        ///<returns>The ray going from camera through the screen point if the camera is given, or from the screen point going forward if no camera is given.</returns>
        public static Ray ScreenPointToRay(Camera cam, Vector2 screenPos)
        {
            if (cam != null)
                return cam.ScreenPointToRay(screenPos);

            Vector3 pos = screenPos;
            pos.z -= 100f;
            return new Ray(pos, Vector3.forward);
        }

        ///<summary>Transforms a position in world space into a screen space point.</summary>
        ///<param name="cam">The camera associated with the screen space position. (Optional)</param>
        ///<param name="worldPoint">Point in world space.</param>
        ///<returns>Returns the screen point.</returns>
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint)
        {
            if (cam == null)
                return new Vector2(worldPoint.x, worldPoint.y);

            return cam.WorldToScreenPoint(worldPoint);
        }

        ///<summary>Creates a Bounds object that encapsulates all the child RectTransform objects found in the <c>child</c> parameter, and converts the resulting bounds into local space relative to the <c>root</c> transform.</summary>
        ///<param name="root">The Transform to use when converting from world to local space.</param>
        ///<param name="child">The parent Transform object whose RectTransform children will be encapsulated.</param>
        ///<returns>A Bounds object representing the encapsulated bounds in local space relative to the root Transform.</returns>
        public static Bounds CalculateRelativeRectTransformBounds(Transform root, Transform child)
        {
            RectTransform[] rects = child.GetComponentsInChildren<RectTransform>(false);

            if (rects.Length > 0)
            {
                Vector3 vMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 vMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                Matrix4x4 toLocal = root.worldToLocalMatrix;

                for (int i = 0, imax = rects.Length; i < imax; i++)
                {
                    rects[i].GetWorldCorners(s_Corners);
                    for (int j = 0; j < 4; j++)
                    {
                        Vector3 v = toLocal.MultiplyPoint3x4(s_Corners[j]);
                        vMin = Vector3.Min(v, vMin);
                        vMax = Vector3.Max(v, vMax);
                    }
                }

                Bounds b = new Bounds(vMin, Vector3.zero);
                b.Encapsulate(vMax);
                return b;
            }
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        ///<summary>Creates a Bounds object that encapsulates all the child RectTransform objects found in the <c>child</c> parameter, and converts the resulting bounds into local space relative to the <c>root</c> transform.</summary>
        ///<param name="trans">The Transform to both search for RectTransform children and convert into world to local space.</param>
        ///<returns>A Bounds object representing the encapsulated bounds in local space relative to the root Transform.</returns>
        public static Bounds CalculateRelativeRectTransformBounds(Transform trans)
        {
            return CalculateRelativeRectTransformBounds(trans, trans);
        }

        ///<summary>Flips the alignment of the RectTransform along the horizontal or vertical axis, and optionally its children as well.</summary>
        ///<remarks>This flips the alignment of the RectTransform. Any actual content such as images or text will not be flipped but may aligned differently.
        ///An example usage is to instantiate a control designed in a left to right manner (like a horizontal slider where 0 is to the left) and flip it horizontally so the layout becomes suitable for use in the opposite direction (like a horizontal slider where 0 is to the right).
        ///
        ///When used with the recursive argument set to true, the children are always flipped with the keepPositioning option set to false so that they properly follow the flip of the parent.</remarks>
        ///<param name="rect">The RectTransform to flip.</param>
        ///<param name="keepPositioning">Flips around the pivot if true. Flips within the parent rect if false.</param>
        ///<param name="recursive">Flip the children as well?</param>
        ///<param name="axis">The axis to flip along. 0 is horizontal and 1 is vertical.</param>
        public static void FlipLayoutOnAxis(RectTransform rect, int axis, bool keepPositioning, bool recursive)
        {
            if (rect == null)
                return;

            if (recursive)
            {
                for (int i = 0; i < rect.childCount; i++)
                {
                    RectTransform childRect = rect.GetChild(i) as RectTransform;
                    if (childRect != null)
                        FlipLayoutOnAxis(childRect, axis, false, true);
                }
            }

            Vector2 pivot = rect.pivot;
            pivot[axis] = 1.0f - pivot[axis];
            rect.pivot = pivot;

            if (keepPositioning)
                return;

            Vector2 anchoredPosition = rect.anchoredPosition;
            anchoredPosition[axis] = -anchoredPosition[axis];
            rect.anchoredPosition = anchoredPosition;

            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;
            float temp = anchorMin[axis];
            anchorMin[axis] = 1 - anchorMax[axis];
            anchorMax[axis] = 1 - temp;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
        }

        ///<summary>Flips the horizontal and vertical axes of the RectTransform size and alignment, and optionally its children as well.</summary>
        ///<remarks>This swaps the horizontal and vertical axis in the size and alignment of the RectTransform. This can also be thought of as a diagonal flip. Any actual content such as images or text will not be flipped or rotated but may be resized and aligned differently.
        ///
        ///An example usage is to instantiate a control designed for alignment along one axis (like a horizontal slider) and flip the axes so the layout becomes suitable for use along the other axis (like a vertical slider).
        ///
        ///When used with the recursive argument set to true, the children are always flipped with the keepPositioning option set to false so that they properly follow the flip of the parent.</remarks>
        ///<param name="rect">The RectTransform to flip.</param>
        ///<param name="keepPositioning">Flips around the pivot if true. Flips within the parent rect if false.</param>
        ///<param name="recursive">Flip the children as well?</param>
        public static void FlipLayoutAxes(RectTransform rect, bool keepPositioning, bool recursive)
        {
            if (rect == null)
                return;

            if (recursive)
            {
                for (int i = 0; i < rect.childCount; i++)
                {
                    RectTransform childRect = rect.GetChild(i) as RectTransform;
                    if (childRect != null)
                        FlipLayoutAxes(childRect, false, true);
                }
            }

            rect.pivot = GetTransposed(rect.pivot);
            rect.sizeDelta = GetTransposed(rect.sizeDelta);

            if (keepPositioning)
                return;

            rect.anchoredPosition = GetTransposed(rect.anchoredPosition);
            rect.anchorMin = GetTransposed(rect.anchorMin);
            rect.anchorMax = GetTransposed(rect.anchorMax);
        }

        private static Vector2 GetTransposed(Vector2 input)
        {
            return new Vector2(input.y, input.x);
        }

        ///<summary>Returns the screen-space axis-aligned bounding box of the given RectTransform.</summary>
        ///<remarks>The four world-space corners are projected to screen space using <see cref="Camera.main" />. For Screen Space - Overlay canvases, the corners are already in screen-pixel coordinates and camera projection is skipped regardless of whether <see cref="Camera.main" /> is set, to avoid double-projecting them.
        ///
        ///**Note**: This method uses <see cref="Camera.main" /> for camera projection. For canvases rendered by a specific non-main camera, use <see cref="GetScreenRect" />(RectTransform,Camera) and pass the correct camera explicitly.</remarks>
        ///<param name="rectTransform">The RectTransform whose screen-space bounding box to compute.</param>
        ///<returns>A <see cref="Rect" /> describing the screen-space bounding box in pixels. Width and height are always non-negative.</returns>
        ///<seealso cref="GetScreenRect" />
        public static Rect GetScreenRect(this RectTransform rectTransform)
        {
            return GetScreenRect(rectTransform, Camera.main);
        }

        ///<summary>Returns the screen-space axis-aligned bounding box of the given RectTransform, using the specified camera for projection.</summary>
        ///<remarks>The four world-space corners are projected to screen space using the provided camera. For Screen Space - Overlay canvases, projection is skipped regardless of the camera argument to avoid double-projecting coordinates that are already in screen space.
        ///
        ///Use the no-argument overload <see cref="GetScreenRect" />(RectTransform) to project using <see cref="Camera.main" />.</remarks>
        ///<param name="rectTransform">The RectTransform whose screen-space bounding box to compute.</param>
        ///<param name="camera">The camera to use for world-to-screen projection. If null, or if the RectTransform belongs to a Screen Space - Overlay canvas, camera projection is skipped and the world corners are used directly as screen coordinates.</param>
        ///<returns>A <see cref="Rect" /> describing the screen-space bounding box in pixels. Width and height are always non-negative.</returns>
        ///<seealso cref="GetScreenRect" />
        public static Rect GetScreenRect(this RectTransform rectTransform, Camera camera)
        {
            // Screen Space - Overlay canvases: world corners are already in screen space.
            // Passing them through a camera would double-project and produce incorrect
            // results, so we skip projection entirely in that mode.
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            bool isOverlay = (camera == null || canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay);

            if (isOverlay)
                return rectTransform.GetWorldRect();

            System.Span<Vector3> corners = stackalloc Vector3[4];
            rectTransform.GetWorldCorners(corners);

            Vector2 s0 = camera.WorldToScreenPoint(corners[0]);
            Vector2 s1 = camera.WorldToScreenPoint(corners[1]);
            Vector2 s2 = camera.WorldToScreenPoint(corners[2]);
            Vector2 s3 = camera.WorldToScreenPoint(corners[3]);

            Vector2 screenMin = Vector2.Min(Vector2.Min(s0, s1), Vector2.Min(s2, s3));
            Vector2 screenMax = Vector2.Max(Vector2.Max(s0, s1), Vector2.Max(s2, s3));

            return new Rect(screenMin, screenMax - screenMin);
        }
    }
}
