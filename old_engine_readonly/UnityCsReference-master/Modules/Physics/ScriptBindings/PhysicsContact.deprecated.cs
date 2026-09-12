// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    public readonly partial struct ContactPairHeader
    {
        ///<summary>Instance ID of the first <see cref="Rigidbody" /> or the <see cref="ArticulationBody" /> in the pair.</summary>
        [Obsolete("Please use ContactPairHeader.bodyInstanceID instead. (UnityUpgradable) -> bodyInstanceID", false)]
        public int BodyInstanceID => bodyInstanceID;
        
        ///<summary>Instance ID of the second <see cref="Rigidbody" /> or the <see cref="ArticulationBody" /> in the pair.</summary>
        [Obsolete("Please use ContactPairHeader.otherBodyInstanceID instead. (UnityUpgradable) -> otherBodyInstanceID", false)]
        public int OtherBodyInstanceID => otherBodyInstanceID;

        ///<summary>The first <see cref="Rigidbody" /> or <see cref="ArticulationBody" /> in the pair.</summary>
        ///<remarks>Use with the <c>as</c> keyword to determine the actual type of the component.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class Example
        ///{
        ///    public Rigidbody ExtractRigidbodyFromContactPairHeader(ContactPairHeader header)
        ///    {
        ///        return header.body as Rigidbody;
        ///    }
        ///
        ///    public ArticulationBody ExtractArticulationBodyFromContactPairHeader(ContactPairHeader header)
        ///    {
        ///        return header.body as ArticulationBody;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [Obsolete("Please use ContactPairHeader.body instead. (UnityUpgradable) -> body", false)]
        public Component Body => body;

        ///<summary>The second <see cref="Rigidbody" /> or <see cref="ArticulationBody" /> in the pair.</summary>
        ///<remarks>Use with the <c>as</c> keyword to determine the actual type of the component.</remarks>
        [Obsolete("Please use ContactPairHeader.otherBody instead. (UnityUpgradable) -> otherBody", false)]
        public Component OtherBody => otherBody;
        
        ///<summary>Number of <see cref="ContactPair" />s that this header contains.</summary>
        [Obsolete("Please use ContactPairHeader.pairCount instead. (UnityUpgradable) -> pairCount", false)]
        public int PairCount => pairCount;
    }

    public unsafe readonly partial struct ContactPair
    {
        ///<summary>Instance ID of the first Collider in the <see cref="ContactPair" />.</summary>
        [Obsolete("Please use ContactPair.colliderInstanceID instead. (UnityUpgradable) -> colliderInstanceID", false)]
        public int ColliderInstanceID => colliderInstanceID;

        ///<summary>Instance ID of the second Collider in the <see cref="ContactPair" />.</summary>
        [Obsolete("Please use ContactPair.otherColliderInstanceID instead. (UnityUpgradable) -> otherColliderInstanceID", false)]
        public int OtherColliderInstanceID => otherColliderInstanceID;

        ///<summary>The first <see cref="Collider" /> component of the <see cref="ContactPair" />.</summary>
        ///<remarks>This property is only accessible from the main thread.</remarks>
        [Obsolete("Please use ContactPair.collider instead. (UnityUpgradable) -> collider", false)]
        public Collider Collider => collider;

        ///<summary>The second <see cref="Collider" /> component of the <see cref="ContactPair" />.</summary>
        ///<remarks>This property is only accessible from the main thread.</remarks>
        [Obsolete("Please use ContactPair.otherCollider instead. (UnityUpgradable) -> otherCollider", false)]
        public Collider OtherCollider => otherCollider;

        ///<summary>The number of <see cref="ContactPairPoint" />s that this pair contains.</summary>
        [Obsolete("Please use ContactPair.contactCount instead. (UnityUpgradable) -> contactCount", false)]
        public int ContactCount => contactCount;

        ///<summary>Total impulse sum of the pair.</summary>
        ///<remarks>Equivalent to <see cref="Collision.impulse" />.</remarks>
        [Obsolete("Please use ContactPair.impulseSum instead. (UnityUpgradable) -> impulseSum", false)]
        public Vector3 ImpulseSum => impulseSum;

        ///<summary>Whether or not this pair is equivalent to a pair reported in <see cref="M:UnityEngine.MonoBehaviour.OnCollisionEnter(UnityEngine.Collision)" /> events.</summary>
        [Obsolete("Please use ContactPair.isCollisionEnter instead. (UnityUpgradable) -> isCollisionEnter", false)]
        public bool IsCollisionEnter => isCollisionEnter;

        ///<summary>Whether or not this pair is equivalent to a pair reported in <see cref="M:UnityEngine.MonoBehaviour.OnCollisionExit(UnityEngine.Collision)" /> events.</summary>
        [Obsolete("Please use ContactPair.isCollisionExit instead. (UnityUpgradable) -> isCollisionExit", false)]
        public bool IsCollisionExit => isCollisionExit;

        ///<summary>Whether or not this pair is equivalent to a pair reported in <see cref="M:UnityEngine.MonoBehaviour.OnCollisionStay(UnityEngine.Collision)" /> events.</summary>
        [Obsolete("Please use ContactPair.isCollisionStay instead. (UnityUpgradable) -> isCollisionStay", false)]
        public bool IsCollisionStay => isCollisionStay;
    }

    public readonly partial struct ContactPairPoint
    {
        ///<summary>The position of the contact point between the Colliders, in world space.</summary>
        [Obsolete("Please use ContactPairPoint.position instead. (UnityUpgradable) -> position", false)]
        public Vector3 Position => position;

        ///<summary>The distance between the edges of Colliders at the contact point.</summary>
        [Obsolete("Please use ContactPairPoint.separation instead. (UnityUpgradable) -> separation", false)]
        public float Separation => separation;

        ///<summary>Normal of the contact point.</summary>
        ///<remarks>The normal direction points from the second Collider to the first Collider.</remarks>
        [Obsolete("Please use ContactPairPoint.normal instead. (UnityUpgradable) -> normal", false)]
        public Vector3 Normal => normal;

        ///<summary>The impulse applied to this contact pair to resolve the collision.</summary>
        ///<remarks>To work out the force applied you can divide the impulse by the last frame's simulation time step.</remarks>
        [Obsolete("Please use ContactPairPoint.impulse instead. (UnityUpgradable) -> impulse", false)]
        public Vector3 Impulse => impulse;
    }
}
