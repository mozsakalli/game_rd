// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.U2D
{
    ///<summary>Additional data about the shape's control point. This is useful during tessellation of the shape.</summary>
    [StructLayoutAttribute(LayoutKind.Sequential)]
    [MovedFrom("UnityEngine.Experimental.U2D")]
    public struct SpriteShapeMetaData
    {
        ///<summary>The height of the tessellated edge.</summary>
        public float height;
        ///<summary>The threshold of the angle that decides if it should be tessellated as a curve or a corner.</summary>
        public float bevelCutoff;
        ///<summary>The radius of the curve to be tessellated.</summary>
        public float bevelSize;
        ///<summary>The Sprite to be used for a particular edge.</summary>
        public uint spriteIndex;
        ///<summary>True will indicate that this point should be tessellated as a corner or a continuous line otherwise.</summary>
        public bool corner;
    }

    ///<summary>Data that describes the important points of the shape.</summary>
    [StructLayoutAttribute(LayoutKind.Sequential)]
    [MovedFrom("UnityEngine.Experimental.U2D")]
    public struct ShapeControlPoint
    {
        ///<summary>The position of this point in the object's local space.</summary>
        public Vector3 position;
        ///<summary>The position of the left tangent in local space.</summary>
        public Vector3 leftTangent;
        ///<summary>The position of the right tangent point in the local space.</summary>
        public Vector3 rightTangent;
        ///<summary>The various modes of the tangent handles. They could be continuous or broken.</summary>
        public int mode;
    }

    ///<summary>Describes the information about the edge and how to tessellate it.</summary>
    [StructLayoutAttribute(LayoutKind.Sequential)]
    [MovedFrom("UnityEngine.Experimental.U2D")]
    public struct AngleRangeInfo
    {
        ///<summary>The minimum angle to be considered within this range.</summary>
        public float start;
        ///<summary>The maximum angle to be considered within this range.</summary>
        public float end;
        ///<summary>The render order of the edges that belong in this range.</summary>
        public uint order;
        ///<summary>The list of Sprites that are associated with this range.</summary>
        public int[] sprites;
    }

    ///<summary>A static class that helps tessellate a SpriteShape mesh.</summary>
    [NativeHeader("Modules/SpriteShape/Public/SpriteShapeUtility.h")]
    [MovedFrom("UnityEngine.Experimental.U2D")]
    public class SpriteShapeUtility
    {
        ///<summary>Generate a mesh based on input parameters.</summary>
        ///<param name="mesh">The output mesh.</param>
        ///<param name="shapeParams">Input parameters for the SpriteShape tessellator.</param>
        ///<param name="points">A list of control points that describes the shape.</param>
        ///<param name="metaData">Additional data about the shape's control point. This is useful during tessellation of the shape.</param>
        ///<param name="sprites">The list of Sprites that could be used for the edges.</param>
        ///<param name="corners">The list of Sprites that could be used for the corners.</param>
        ///<param name="angleRange">A parameter that determins how to tessellate each of the edge.</param>
        [FreeFunction("SpriteShapeUtility::Generate", ThrowsException = true)]
        extern public static int[] Generate(Mesh mesh, SpriteShapeParameters shapeParams, ShapeControlPoint[] points, SpriteShapeMetaData[] metaData, AngleRangeInfo[] angleRange, Sprite[] sprites, Sprite[] corners);
        ///<summary>Generate a mesh based on input parameters.</summary>
        ///<param name="renderer">SpriteShapeRenderer to which the generated geometry is fed to.</param>
        ///<param name="shapeParams">Input parameters for the SpriteShape tessellator.</param>
        ///<param name="points">A list of control points that describes the shape.</param>
        ///<param name="metaData">Additional data about the shape's control point. This is useful during tessellation of the shape.</param>
        ///<param name="sprites">The list of Sprites that could be used for the edges.</param>
        ///<param name="corners">The list of Sprites that could be used for the corners.</param>
        ///<param name="angleRange">A parameter that determins how to tessellate each of the edge.</param>
        [FreeFunction("SpriteShapeUtility::GenerateSpriteShape", ThrowsException = true)]
        extern public static void GenerateSpriteShape(SpriteShapeRenderer renderer, SpriteShapeParameters shapeParams, ShapeControlPoint[] points, SpriteShapeMetaData[] metaData, AngleRangeInfo[] angleRange, Sprite[] sprites, Sprite[] corners);
    }
}
