// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.GraphToolkit.Editor.Implementation;

namespace Unity.GraphToolkit.Editor
{
    public abstract partial class State
    {
        [NonSerialized]
        internal StateModel m_Implementation;

        StateModel IState.StateModel => GetImplementation();

        internal StateModel GetImplementation()
        {
            if (m_Implementation == null)
            {
                CreateImplementation();
            }

            return m_Implementation;
        }

        internal void CreateImplementation()
        {
            new UserStateModelImp().InitCustomState(this);
        }

        internal void SetImplementation(StateModel implementation)
        {
            m_Implementation = implementation;
        }
    }
}
