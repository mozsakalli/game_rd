// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.VFX;

namespace UnityEditor.VFX
{
    [UsedByNativeCode]
    [NativeHeader("Modules/VFXEditor/Public/VisualEffectSubgraph.h")]
    [NativeHeader("VFXScriptingClasses.h")]
    [NativeClass("VisualEffectSubgraph", PersistentTypeId = 0x3B4A7520)]
    internal abstract class VisualEffectSubgraph : VisualEffectObject
    {
    }

    [UsedByNativeCode]
    [NativeHeader("Modules/VFXEditor/Public/VisualEffectSubgraph.h")]
    [NativeHeader("VFXScriptingClasses.h")]
    [NativeClass("VisualEffectSubgraphOperator", PersistentTypeId = 0x3B4A752B)]
    internal class VisualEffectSubgraphOperator : VisualEffectSubgraph
    {
        public const string Extension = ".vfxoperator";

        public VisualEffectSubgraphOperator()
        {
            CreateVisualEffectSubgraph(this);
        }

        private static extern void CreateVisualEffectSubgraph([Writable] VisualEffectSubgraphOperator subGraph);
    }

    [UsedByNativeCode]
    [NativeHeader("Modules/VFXEditor/Public/VisualEffectSubgraph.h")]
    [NativeHeader("VFXScriptingClasses.h")]
    [NativeClass("VisualEffectSubgraphBlock", PersistentTypeId = 0x3B4A752C)]
    internal class VisualEffectSubgraphBlock : VisualEffectSubgraph
    {
        public const string Extension = ".vfxblock";
        public VisualEffectSubgraphBlock()
        {
            CreateVisualEffectSubgraph(this);
        }

        private static extern void CreateVisualEffectSubgraph([Writable] VisualEffectSubgraphBlock subGraph);
    }
}
