// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.Playables
{
    [Serializable]
    [RequiredByNativeCode]
    public abstract class PlayableBehaviour : IPlayableBehaviour, ICloneable
    {
        public PlayableBehaviour() {}

        public virtual void OnGraphStart(Playable playable) {}
        public virtual void OnGraphStop(Playable playable)  {}

        public virtual void OnPlayableCreate(Playable playable) {}
        public virtual void OnPlayableDestroy(Playable playable) {}

        [Obsolete("OnBehaviourDelay is obsolete; use a custom ScriptPlayable to implement this feature", true)]
        public virtual void OnBehaviourDelay(Playable playable, FrameData info) {}
        public virtual void OnBehaviourPlay(Playable playable, FrameData info) {}
        public virtual void OnBehaviourPause(Playable playable, FrameData info) {}

        [Obsolete("PrepareData is obsolete. This method was invoked as part of the Playable delay mechanism, which has now been fully deprecated. This method is no longer invoked by Unity and will be removed in a future version. You can emulate this functionality by implementing your own delay mechanism as part of a PlayableBehaviour.", false)]
        public virtual void PrepareData(Playable playable, FrameData info) {}
        public virtual void PrepareFrame(Playable playable, FrameData info) {}
        public virtual void ProcessFrame(Playable playable, FrameData info, object playerData) {}

        public virtual object Clone()
        {
            return MemberwiseClone();
        }
    }
}
