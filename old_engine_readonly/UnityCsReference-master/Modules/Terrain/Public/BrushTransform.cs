// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.TerrainTools
{
    // represents a linear 2D transform between brush UV space and some other target XY space
    //      xy = u * brushU + v * brushV + brushOrigin
    //      uv = x * targetX + y * targetY + targetOrigin
    ///<summary>Represents a linear 2D transformation between brush UV space and a target XY space (typically this is a Terrain-local object space.)</summary>
    ///<remarks>The BrushTransform represents a rectangular brush, with scale, rotation, and skew.
    ///The brush is assumed to lie in the [0,1] range in brush UV space.
    ///
    ///The transform and its inverse are represented as follows:
    ///
    ///<c>xy = u * BrushTransform.brushU + v * BrushTransform.brushV + BrushTransform.brushOrigin</c><c>uv = x * BrushTransform.targetX + y * BrushTransform.targetY + BrushTransform.targetOrigin</c></remarks>
    [MovedFrom("UnityEngine.Experimental.TerrainAPI")]
    public struct BrushTransform
    {
        ///<summary>(RO) Brush UV origin, in XY space.</summary>
        public Vector2 brushOrigin { get; }     // brush UV origin, in XY space
        ///<summary>(RO) Brush U vector, in XY space.</summary>
        public Vector2 brushU { get; }          // brush U vector, in XY space
        ///<summary>(RO) Brush V vector, in XY space.</summary>
        public Vector2 brushV { get; }          // brush V vector, in XY space

        ///<summary>(RO) Target XY origin, in Brush UV space.</summary>
        public Vector2 targetOrigin { get; }    // XY origin, in brush UV space
        ///<summary>(RO) Target X vector, in Brush UV space.</summary>
        public Vector2 targetX { get; }         // X vector, in brush UV space
        ///<summary>(RO) Target Y vector, in Brush UV space.</summary>
        public Vector2 targetY { get; }         // Y vector, in brush UV space

        ///<summary>Creates a BrushTransform.</summary>
        ///<param name="brushOrigin">Origin of the brush, in target XY space.</param>
        ///<param name="brushU">Brush U vector, in target XY space.</param>
        ///<param name="brushV">Brush V vector, in target XY space.</param>
        public BrushTransform(Vector2 brushOrigin, Vector2 brushU, Vector2 brushV)
        {
            // invert the rotation matrix [BrushU, BrushV]
            // this gives us [X, Y] vectors in brush UV space
            // note we run the true inverse, to support non-orthogonal brush axes
            float det = brushU.x * brushV.y - brushU.y * brushV.x;
            float invDet = Mathf.Approximately(det, 0.0f) ? 1.0f : 1.0f / det;      // for non-invert-able matrices, we do 'something'
            Vector2 targetX = new Vector2(brushV.y, -brushU.y) * invDet;
            Vector2 targetY = new Vector2(-brushV.x, brushU.x) * invDet;

            // calculate XY origin in brush UV space
            Vector2 targetOrigin = -brushOrigin.x * targetX - brushOrigin.y * targetY;

            this.brushOrigin = brushOrigin;
            this.brushU = brushU;
            this.brushV = brushV;
            this.targetOrigin = targetOrigin;
            this.targetX = targetX;
            this.targetY = targetY;
        }

        ///<summary>Get the axis-aligned bounding rectangle of the brush, in target XY space.</summary>
        ///<returns>Bounding rectangle in target XY space.</returns>
        public Rect GetBrushXYBounds()           // get the XY bounding rectangle around the Brush [0,1] UV space
        {
            // compute all four corners of the brush [0,1] UV space
            Vector2 pU = brushOrigin + brushU;
            Vector2 pV = brushOrigin + brushV;
            Vector2 pUV = brushOrigin + brushU + brushV;

            // compute min and max XY coordinates
            float minX = Mathf.Min(Mathf.Min(brushOrigin.x, pU.x), Mathf.Min(pV.x, pUV.x));
            float maxX = Mathf.Max(Mathf.Max(brushOrigin.x, pU.x), Mathf.Max(pV.x, pUV.x));
            float minY = Mathf.Min(Mathf.Min(brushOrigin.y, pU.y), Mathf.Min(pV.y, pUV.y));
            float maxY = Mathf.Max(Mathf.Max(brushOrigin.y, pU.y), Mathf.Max(pV.y, pUV.y));

            // return the XY bounding rectangle
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        ///<summary>Creates an axis-aligned BrushTransform from a rectangle.</summary>
        ///<param name="brushRect">Brush rectangle, in target XY coordinates.</param>
        ///<returns>BrushTransform describing the brush.</returns>
        public static BrushTransform FromRect(Rect brushRect)
        {
            Vector2 brushOrigin = brushRect.min;
            Vector2 brushU = new Vector2(brushRect.width, 0.0f);
            Vector2 brushV = new Vector2(0.0f, brushRect.height);
            return new BrushTransform(brushOrigin, brushU, brushV);
        }

        ///<summary>Applies the transform to convert a target XY coordinate to Brush UV space.</summary>
        ///<param name="targetXY">Point in target XY space.</param>
        ///<returns>Point transformed to Brush UV space.</returns>
        public Vector2 ToBrushUV(Vector2 targetXY)
        {
            return targetXY.x * targetX + targetXY.y * targetY + targetOrigin;
        }

        ///<summary>Applies the transform to convert a Brush UV coordinate to the target XY space.</summary>
        ///<param name="brushUV">Brush UV coordinate to transform.</param>
        ///<returns>Target XY coordinate.</returns>
        public Vector2 FromBrushUV(Vector2 brushUV)
        {
            return brushUV.x * brushU + brushUV.y * brushV + brushOrigin;
        }
    }
}
