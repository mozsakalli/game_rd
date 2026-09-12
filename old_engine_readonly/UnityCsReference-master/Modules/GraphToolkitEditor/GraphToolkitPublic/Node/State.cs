// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor.Implementation;
using UnityEngine;

namespace Unity.GraphToolkit.Editor
{
    /// <summary>
    /// The base class for all user-accessible states in a <see cref="StateMachine"/>.
    /// </summary>
    /// <remarks>
    /// Inherit from this class to define custom state types that appear in a state machine graph. A state represents
    /// a node in a <see cref="StateMachine"/>; transitions connect states to one another. Unlike <see cref="Node"/>,
    /// a state does not expose configurable input and output ports: its incoming and outgoing transition connections
    /// are managed by the state machine itself.
    ///
    /// See also:
    ///
    ///- <see cref="StateMachine"/> for the graph type that contains states
    ///- <see cref="Node"/> for the equivalent base class used in regular graphs
    ///- <see cref="IState"/> for the interface this class implements
    ///
    /// </remarks>
    [Serializable]
    public abstract partial class State : IState
    {
        /// <summary>
        /// The <see cref="StateMachine"/> that contains this state.
        /// </summary>
        public StateMachine StateMachine => ((IState)GetImplementation()).StateMachine;

        /// <summary>
        /// Whether the state is connected to at least one transition.
        /// </summary>
        public bool IsConnected => ((IState)GetImplementation()).IsConnected;

        /// <summary>
        /// The globally unique identifier for this state.
        /// </summary>
        public Hash128 ID => GetImplementation().Guid;

        /// <summary>
        /// The text displayed when hovering over the state's header.
        /// </summary>
        public string Tooltip
        {
            get => GetImplementation().Tooltip;
            set => GetImplementation().Tooltip = value;
        }

        /// <summary>
        /// The main text displayed in the state's header.
        /// </summary>
        /// <remarks>
        /// Use this property to specify the state title displayed in the state machine view.
        /// To modify the state title displayed in the graph item library, use <see cref="NodeAttribute.Title"/>.
        /// </remarks>
        /// <seealso cref="NodeAttribute.Title"/>
        public string Title
        {
            get => GetImplementation().Title;
            set => ((IState)GetImplementation()).Title = value;
        }

        /// <summary>
        /// The secondary text displayed in the state's header.
        /// </summary>
        public string Subtitle
        {
            get => GetImplementation().Subtitle;
            set => GetImplementation().Subtitle = value;
        }

        /// <summary>
        /// The highlight color of the state. The highlight is located on the upper border of the state.
        /// </summary>
        public Color DefaultColor
        {
            get => GetImplementation().DefaultColor;
            set => GetImplementation().DefaultColor = value;
        }

        /// <summary>
        /// Called when the state is created or when the state machine is enabled.
        /// </summary>
        /// <remarks>
        /// Use this method to perform initialization logic.
        /// </remarks>
        public virtual void OnEnable() { }

        /// <summary>
        /// Called when the state is removed or when the state machine is disabled.
        /// </summary>
        /// <remarks>
        /// Use this method to perform any cleanup logic.
        /// </remarks>
        public virtual void OnDisable() { }

        /// <summary>
        /// The position of the state in the state machine.
        /// </summary>
        public Vector2 Position
        {
            get => GetImplementation().Position;
            set => GetImplementation().SetNodeModelPosition(value);
        }

        /// <summary>
        /// Removes the state from its state machine.
        /// </summary>
        public void RemoveFromStateMachine() => ((IState)GetImplementation()).RemoveFromStateMachine();

        /// <summary>
        /// Retrieves the transitions that go to this state.
        /// </summary>
        /// <returns>An <c>IEnumerable</c> of the incoming <see cref="ITransition"/>s.</returns>
        /// <remarks>
        /// A self transition is anchored on both the incoming and outgoing side of its state, so it appears in
        /// the results of both <see cref="GetIncomingTransitions"/> and <see cref="GetOutgoingTransitions"/>.
        /// </remarks>
        public IEnumerable<ITransition> GetIncomingTransitions() => ((IState)GetImplementation()).GetIncomingTransitions();

        /// <summary>
        /// Retrieves the transitions that originate from this state.
        /// </summary>
        /// <returns>An <c>IEnumerable</c> of the outgoing <see cref="ITransition"/>s.</returns>
        /// <remarks>
        /// A self transition is anchored on both the incoming and outgoing side of its state, so it appears in
        /// the results of both <see cref="GetIncomingTransitions"/> and <see cref="GetOutgoingTransitions"/>.
        /// </remarks>
        public IEnumerable<ITransition> GetOutgoingTransitions() => ((IState)GetImplementation()).GetOutgoingTransitions();
    }
}
