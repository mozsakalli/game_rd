// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Bindings;

namespace UnityEngine.LowLevelPhysics
{
    ///<summary>All basic geometric shapes implement this interface.</summary>
    ///<seealso cref="BoxGeometry" />
    ///<seealso cref="SphereGeometry" />
    ///<seealso cref="CapsuleGeometry" />
    ///<seealso cref="ConvexMeshGeometry" />
    ///<seealso cref="TriangleMeshGeometry" />
    ///<seealso cref="TerrainGeometry" />
    public interface IGeometry
    {
        ///<summary>Return the geometry type of the shape that implemented this interface.</summary>
        ///<seealso cref="GeometryType" />
        GeometryType GeometryType { get; }
    }

    ///<summary>Contains the basic geometric shape of a box.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BoxGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private Vector3 m_HalfExtents;

        ///<summary>The half extents of the box shape.</summary>
        ///<remarks>The half extent is half the total length of the box on each axis.</remarks>
        public Vector3 HalfExtents { get { return m_HalfExtents; } set { m_HalfExtents = value; } }

        ///<summary>Create a box shape with the provided parameters.</summary>
        ///<param name="halfExtents">The distance from the center of the box to the edge on each axis.</param>
        public BoxGeometry(Vector3 halfExtents)
        {
            m_UnusedReserved = -1;
            m_HalfExtents = halfExtents;
        }

        ///<summary>Returns the geometry type of this shape, which is BoxGeometry.</summary>
        public GeometryType GeometryType => GeometryType.Box;
    }

    ///<summary>Contains the basic geometric shape of a sphere.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SphereGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private float m_Radius;

        ///<summary>The radius of the sphere shape.</summary>
        public float Radius { get { return m_Radius; } set { m_Radius = value; } }

        ///<summary>Create a sphere shape with the provided parameter.</summary>
        ///<param name="radius">The radius of the sphere.</param>
        public SphereGeometry(float radius)
        {
            m_UnusedReserved = -1;
            m_Radius = radius;
        }

        ///<summary>Returns the geometry type of this shape, which is SphereGeometry.</summary>
        public GeometryType GeometryType => GeometryType.Sphere;
    }

    ///<summary>Contains the basic geometric shape of a capsule.</summary>
    ///<remarks>When Unity retrieves the geometry from the <see cref="CapsuleCollider" />, the <see cref="CapsuleCollider.direction" /> is not included. For this reason, you should assume the direction is always along the X axis.</remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct CapsuleGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private float m_Radius;
        private float m_HalfLength;

        ///<summary>The radius of the half-sphere at either cap of the capsule.</summary>
        public float Radius { get { return m_Radius; } set { m_Radius = value; } }
        ///<summary>The distance from the center of the shape to the center of either half-sphere at the caps.</summary>
        public float HalfLength { get { return m_HalfLength; } set { m_HalfLength = value; } }

        ///<summary>Create a capsule shape with the provided parameters.</summary>
        ///<remarks>The capsule shape is made from a cylinder shape with 2 half-spheres at each end. Therefore, the total height of the capsule is `2 * halfLength + 2 * radius`.</remarks>
        ///<param name="radius">The radius of the capsule's end caps.</param>
        ///<param name="halfLength">The distance from the center of the capsule to the center of the end point sphere.</param>
        public CapsuleGeometry(float radius, float halfLength)
        {
            m_UnusedReserved = -1;
            m_Radius = radius;
            m_HalfLength = halfLength;
        }

        ///<summary>Returns the geometry type of this shape, which is CapsuleGeometry.</summary>
        public GeometryType GeometryType => GeometryType.Capsule;
    }

    // From PxConvexMeshGeometry.h
    ///<summary>Contains the basic geometric shape of a convex mesh.</summary>
    ///<remarks>The only way to retrieve this shape is to use the <see cref="Collider.GeometryHolder" /> property to get it from a convex <see cref="MeshCollider" /> component.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ConvexMeshGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private Vector3 m_Scale;
        private Quaternion m_Rotation;
        private IntPtr m_ConvexMesh;
        private byte m_MeshFlags;
        private fixed byte m_MeshFlagsPadding[3];

        ///<summary>The scale of this geometry.</summary>
        public Vector3 Scale { get { return m_Scale; } set { m_Scale = value; } }
        ///<summary>The rotation of the scale axis of this geometry.</summary>
        public Quaternion ScaleAxisRotation { get { return m_Rotation; } set { m_Rotation = value; } }

        ///<summary>Returns the geometry type of this shape, which is ConvexMeshGeometry.</summary>
        public GeometryType GeometryType => GeometryType.ConvexMesh;
    }

    // From PxTriangleMeshGeometry.h
    ///<summary>Contains the basic geometric shape of a non-convex mesh (sometimes known as a triangle mesh).</summary>
    ///<remarks>The only way to retrieve this shape is to use the <see cref="Collider.GeometryHolder" /> property to get it from a non-convex <see cref="MeshCollider" /> component.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TriangleMeshGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private Vector3 m_Scale;
        private Quaternion m_Rotation;
        private byte m_MeshFlags;
        private fixed byte m_MeshFlagsPadding[3];
        private IntPtr m_TriangleMesh;

        ///<summary>The scale of this geometry.</summary>
        public Vector3 Scale { get { return m_Scale; } set { m_Scale = value; } }
        ///<summary>The rotation of the scale axis of this geometry.</summary>
        public Quaternion ScaleAxisRotation { get { return m_Rotation; } set { m_Rotation = value; } }

        ///<summary>Returns the geometry type of this shape, which is TriangleMeshGeometry.</summary>
        public GeometryType GeometryType => GeometryType.TriangleMesh;
    }

    ///<summary>Contains the geometric shape of a Terrain collider.</summary>
    ///<remarks>The only way to retrieve this shape is to use the <see cref="Collider.GeometryHolder" /> property to get it from a <see cref="T:UnityEngine.TerrainCollider" /> component.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TerrainGeometry : IGeometry
    {
        private int m_UnusedReserved;
        private IntPtr m_TerrainData;
        private float m_HeightScale;
        private float m_RowScale;
        private float m_ColumnScale;
        private byte m_TerrainFlags;
        private fixed byte m_TerrainFlagsPadding[3];

        ///<summary>Returns the geometry type of this shape, which is TerrainGeometry.</summary>
        public GeometryType GeometryType => GeometryType.Terrain;
    }

    ///<summary>The set of basic geometry shape types that can exist.</summary>
    ///<seealso cref="BoxGeometry" />
    ///<seealso cref="SphereGeometry" />
    ///<seealso cref="CapsuleGeometry" />
    ///<seealso cref="ConvexMeshGeometry" />
    ///<seealso cref="TriangleMeshGeometry" />
    ///<seealso cref="TerrainGeometry" />
    public enum GeometryType : int
    {
        ///<summary>A sphere shape.</summary>
        Sphere = 0,
        ///<summary>A capsule shape.</summary>
        Capsule = 2,
        ///<summary>A cube shape.</summary>
        Box = 3,
        ///<summary>A convex mesh shape.</summary>
        ConvexMesh = 4,
        ///<summary>A triangle or non-convex mesh shape.</summary>
        TriangleMesh = 5,
        ///<summary>The geometric shape of a Terrain collider.</summary>
        Terrain = 6,
        ///<summary>An invalid shape type.</summary>
        Invalid = -1
    }

    ///<summary>Holds the basic information of a geometric shape and its type.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GeometryHolder
    {
        // !!!Keep in sync with PhysicsCollisionGeometry.h!!!
        //
        // physx::PxGeometryHolder blob data, the blob data members are provided in such as way so that the geometry type of the holder is the only non-opaque piece of data inside
        // the memory layout matches PxConvexMeshGeometry, ensuring we can fit all smaller types inside the holder blob
        //
        // PxTriangleMeshLayout (64bit):
        // [00...03] -- PxGeometryType
        // [04...31] -- PxMeshScale
        // [32...39] -- PxConvexMesh ptr
        // [40...43] -- PxConvexMeshGeometryFlag + 3 byte padding
        // [44...47] -- 4 byte padding
        internal fixed int m_Data[12];

        ///<summary>Return the specified geometric shape stored inside this Geometry Holder object.</summary>
        ///<remarks>This function throws an InvalidOperationException if you try to request a geometric shape that is not stored in the GeometryHolder.
        ///
        ///In the case of <see cref="CapsuleGeometry" />, when Unity retrieves the shape from a <see cref="CapsuleCollider" /> component, the <see cref="CapsuleCollider.direction" /> is not included. For this reason, you should assume the direction is always along the X axis.</remarks>
        ///<returns>Returns the basic geometric shape which is stored in the GeometryHolder.</returns>
        ///<seealso cref="BoxGeometry" />
        ///<seealso cref="SphereGeometry" />
        ///<seealso cref="CapsuleGeometry" />
        ///<seealso cref="ConvexMeshGeometry" />
        ///<seealso cref="TriangleMeshGeometry" />
        ///<seealso cref="TerrainGeometry" />
        public T As<T>() where T : struct, IGeometry
        {
            T geometry = default;

            if (geometry.GeometryType != Type)
                throw new InvalidOperationException($"Unable to get geometry of type {geometry.GeometryType} from a geometry holder that stores {Type}.");

            UnsafeUtility.CopyPtrToStructure(UnsafeUtility.AddressOf(ref this), out geometry);

            return geometry;
        }

        ///<summary>Create a GeometryHolder object with a specified geometric shape.</summary>
        ///<param name="geometry">The geometry to store in this GeometryHolder.</param>
        ///<returns>Returns the GeometryHolder object with the geometric shape stored inside.</returns>
        ///<seealso cref="BoxGeometry" />
        ///<seealso cref="SphereGeometry" />
        ///<seealso cref="CapsuleGeometry" />
        ///<seealso cref="ConvexMeshGeometry" />
        ///<seealso cref="TriangleMeshGeometry" />
        ///<seealso cref="TerrainGeometry" />
        public static GeometryHolder Create<T>(T geometry) where T : struct, IGeometry
        {
            GeometryHolder holder = default;
            UnsafeUtility.CopyStructureToPtr(ref geometry, UnsafeUtility.AddressOf(ref holder));
            //we need to ensure we properly patch in the geometry type as we can't ensure that the correct one is being provided to the struct due to the invalid value being -1 rather than 0
            holder.m_Data[0] = (int)geometry.GeometryType;

            return holder;
        }

        ///<summary>Returns the type of the geometry shape that was saved previously.</summary>
        ///<seealso cref="GeometryType" />
        public GeometryType Type => (GeometryType)m_Data[0];
    }

    [NativeHeader("Modules/Physics/PhysicsCollisionGeometry.h")]
    internal static class PhysXGeometryHolderExtension
    {
        [FreeFunction("Physics::PhysXGeometryExtension::GetGeometryHolderFromCollider")]
        public static extern GeometryHolder GetGeometryHolder(this Collider col);
    }
}
