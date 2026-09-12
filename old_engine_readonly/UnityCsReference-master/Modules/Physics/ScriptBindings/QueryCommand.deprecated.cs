// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    public partial struct RaycastCommand
    {
        ///<summary>Create a RaycastCommand.</summary>
        ///<remarks>The query is run in the default physics scene.</remarks>
        ///<param name="from">The starting point of the ray in world coordinates.</param>
        ///<param name="direction">The direction of the ray.</param>
        ///<param name="distance">The maximum distance the ray should check for collisions.</param>
        ///<param name="layerMask">A <see cref="LayerMask" /> that is used to selectively filter which colliders are considered when casting a ray.</param>
        ///<param name="maxHits">The maximum number of Colliders the ray can hit.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public RaycastCommand(Vector3 from, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers, int maxHits = 1)
        {
            this.from = from;
            this.direction = direction;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = QueryParameters.Default;
            this.distance = distance;
            this.layerMask = layerMask;
        }
        ///<summary>Create a RaycastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the raycast query in.</param>
        ///<param name="from">The starting point of the ray in world coordinates.</param>
        ///<param name="direction">The direction of the ray.</param>
        ///<param name="distance">The maximum distance the ray should check for collisions.</param>
        ///<param name="layerMask">A <see cref="LayerMask" /> that is used to selectively filter which colliders are considered when casting a ray.</param>
        ///<param name="maxHits">The maximum number of Colliders the ray can hit.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public RaycastCommand(PhysicsScene physicsScene, Vector3 from, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers, int maxHits = 1)
        {
            this.from = from;
            this.direction = direction;
            this.physicsScene = physicsScene;
            this.queryParameters = QueryParameters.Default;
            this.distance = distance;
            this.layerMask = layerMask;
        }
        [Obsolete("maxHits property was moved to be a part of RaycastCommand.ScheduleBatch.", false)]
        public int maxHits { get { return 1; } set {} }
        ///<summary>A <see cref="LayerMask" /> that is used to selectively filter which colliders are considered when casting a ray.</summary>
        [Obsolete("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask { get { return queryParameters.layerMask; } set { queryParameters.layerMask = value; }}
    }

    public partial struct SpherecastCommand
    {
        ///<summary>Creates a SpherecastCommand.</summary>
        ///<remarks>The command is run in the default physics scene.</remarks>
        ///<param name="origin">The starting point of the sphere cast.</param>
        ///<param name="radius">The radius of the casting sphere.</param>
        ///<param name="direction">The direction of the sphere cast.</param>
        ///<param name="distance">The maximum distance the cast should check for collisions.</param>
        ///<param name="layerMask">The <see cref="LayerMask" /> that selectively ignores Colliders when casting a sphere.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public SpherecastCommand(Vector3 origin, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.origin = origin;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }
        ///<summary>Creates a SpherecastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="origin">The starting point of the sphere cast.</param>
        ///<param name="radius">The radius of the casting sphere.</param>
        ///<param name="direction">The direction of the sphere cast.</param>
        ///<param name="distance">The maximum distance the cast should check for collisions.</param>
        ///<param name="layerMask">The <see cref="LayerMask" /> that selectively ignores Colliders when casting a sphere.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public SpherecastCommand(PhysicsScene physicsScene, Vector3 origin,  float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.origin = origin;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }

        ///<summary>The <see cref="LayerMask" /> that selectively ignores Colliders when casting a sphere.</summary>
        [Obsolete("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask { get { return queryParameters.layerMask; } set { queryParameters.layerMask = value; }}
    }

    public partial struct CapsulecastCommand
    {
        ///<summary>Creates a CapsulecastCommand.</summary>
        ///<remarks>This command is run in the default physics scene.</remarks>
        ///<param name="p1">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="p2">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="direction">The direction of the capsule cast.</param>
        ///<param name="distance">The maximum length of the sweep.</param>
        ///<param name="layerMask">The <see cref="LayerMask" /> that selectively ignores Colliders when casting a capsule.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public CapsulecastCommand(Vector3 p1, Vector3 p2, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.point1 = p1;
            this.point2 = p2;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }
        ///<summary>Creates a CapsulecastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="p1">The center of the sphere at the <c>start</c> of the capsule.</param>
        ///<param name="p2">The center of the sphere at the <c>end</c> of the capsule.</param>
        ///<param name="radius">The radius of the capsule.</param>
        ///<param name="direction">The direction of the capsule cast.</param>
        ///<param name="distance">The maximum length of the sweep.</param>
        ///<param name="layerMask">The <see cref="LayerMask" /> that selectively ignores Colliders when casting a capsule.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public CapsulecastCommand(PhysicsScene physicsScene, Vector3 p1, Vector3 p2, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.point1 = p1;
            this.point2 = p2;
            this.direction = direction;
            this.radius = radius;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }

        ///<summary>A <see cref="LayerMask" /> that selectively ignores Colliders when casting a capsule.</summary>
        [Obsolete("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask { get { return queryParameters.layerMask; } set { queryParameters.layerMask = value; }}
    }

    public partial struct BoxcastCommand
    {
        ///<summary>Creates a BoxcastCommand.</summary>
        ///<remarks>This command is run in the default physics scene.</remarks>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">The half size of the box in each dimension.</param>
        ///<param name="orientation">The rotation of the box.</param>
        ///<param name="direction">The direction in which to sweep the box.</param>
        ///<param name="distance">The maximum length of the cast.</param>
        ///<param name="layerMask">A LayerMask that is used to selectively filter which colliders are considered when casting a box.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public BoxcastCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.direction = direction;
            this.distance = distance;
            this.physicsScene = Physics.defaultPhysicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }
        ///<summary>Creates a BoxcastCommand.</summary>
        ///<param name="physicsScene">The physics scene to run the command in.</param>
        ///<param name="center">The center of the box.</param>
        ///<param name="halfExtents">The half size of the box in each dimension.</param>
        ///<param name="orientation">The rotation of the box.</param>
        ///<param name="direction">The direction in which to sweep the box.</param>
        ///<param name="distance">The maximum length of the cast.</param>
        ///<param name="layerMask">A LayerMask that is used to selectively filter which colliders are considered when casting a box.</param>
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public BoxcastCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, float distance = float.MaxValue, int layerMask = Physics.DefaultRaycastLayers)
        {
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.direction = direction;
            this.distance = distance;
            this.physicsScene = physicsScene;
            this.queryParameters = QueryParameters.Default;
            this.layerMask = layerMask;
        }

        ///<summary>A LayerMask that is used to selectively filter which colliders are considered when casting a box.</summary>
        [Obsolete("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask { get { return queryParameters.layerMask; } set { queryParameters.layerMask = value; }}
    }
}
