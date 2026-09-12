// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace UnityEngine
{
    public partial class ArticulationBody : Behaviour
    {
        ///<exclude />
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Please use ArticulationBody.linearVelocity instead. (UnityUpgradable) -> linearVelocity")]
        public Vector3 velocity { get => linearVelocity; set => linearVelocity = value; }

        ///<exclude />
        [Obsolete("computeParentAnchor has been renamed to matchAnchors (UnityUpgradable) -> matchAnchors")]
        public bool computeParentAnchor { get => matchAnchors; set => matchAnchors = value; }

        ///<summary>Assigns articulation body joint accelerations for the entire hierarchy of bodies.</summary>
        ///<remarks>This sets joint accelerations in the reduced coordinate space for the entire articulation hierarchy starting from root using the supplied list of floats.
        ///                    Every joint acceleration DOF is represented by one float value, however depending on the type of the articulation joint there might be zero, one or 3 DOFs per joint.
        ///                    The exact location of the data to be set in the supplied list for the specific articulation body can be found by calling <see cref="ArticulationBody.GetDofStartIndices" /> and indexing returned dofStartIndices list by the particular body index via <see cref="ArticulationBody.index" />.
        ///                    Number of degrees of freedom(DOF) for the articulation body can be found using <see cref="ArticulationBody.dofCount" />.
        ///
        ///                    Units of measurement - m/s^2 (meters per second squared) for linear and rad/s^2 (radians per second squared) for angular motion.</remarks>
        ///<param name="accelerations">Supplied list of floats used to set the joint accelerations.</param>
        ///<seealso cref="index" />
        ///<seealso cref="GetDofStartIndices" />
        ///<seealso cref="dofCount" />
        ///<seealso cref="GetJointAccelerations" />
        [Obsolete("Setting joint accelerations is not supported in forward kinematics. To have inverse dynamics take acceleration into account, use GetJointForcesForAcceleration instead",true)]
        extern public void SetJointAccelerations(List<float> accelerations);
    }
}
