// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    // Describes how physics materials of colliding objects are combined.
    ///<exclude />
    [Obsolete("PhysicMaterialCombine has been renamed to PhysicsMaterialCombine. Please use PhysicsMaterialCombine instead. (UnityUpgradable) -> PhysicsMaterialCombine", true)]
    public enum PhysicMaterialCombine
    {
        ///<exclude />
        Average = 0,
        ///<exclude />
        Minimum = 2,
        ///<exclude />
        Multiply = 1,
        ///<exclude />
        Maximum = 3
    }

    ///<exclude />
    [Obsolete("PhysicMaterial has been renamed to PhysicsMaterial. Please use PhysicsMaterial instead. (UnityUpgradable) -> PhysicsMaterial", true)]
    [NativeClass(null)]
    public class PhysicMaterial : UnityEngine.Object
    {
        ///<exclude />
        public PhysicMaterial() { }
        ///<exclude />
        public PhysicMaterial(string name) { }

        ///<exclude />
        public float bounciness { get; set; }
        ///<exclude />
        public float dynamicFriction { get; set; }
        ///<exclude />
        public float staticFriction { get; set; }
        ///<exclude />
        public PhysicMaterialCombine frictionCombine { get; set; }
        ///<exclude />
        public PhysicMaterialCombine bounceCombine { get; set; }

        [Obsolete("Use PhysicMaterial.bounciness instead (UnityUpgradable) -> bounciness")]
        public float bouncyness { get; set; }
    }
}
