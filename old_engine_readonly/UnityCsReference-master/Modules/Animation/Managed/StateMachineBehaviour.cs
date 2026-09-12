// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>The SharedBetweenAnimatorsAttribute specifies that this StateMachineBehaviour is instantiated only once and shared by all Animator instances. This attribute reduces the memory footprint for each controller instance.</summary>
    ///<remarks>You choose which StateMachineBehaviour uses this attribute. If your StateMachineBehaviour changes a member variable, this affects all other Animator instances that use it.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///[SharedBetweenAnimators]
    ///public class AttackBehaviour : StateMachineBehaviour
    ///{
    ///    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        Debug.Log("OnStateEnter");
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="StateMachineBehaviour" />
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    [RequiredByNativeCode]
    public sealed partial class SharedBetweenAnimatorsAttribute : Attribute
    {
    }

    ///<summary>StateMachineBehaviour is a component that you add to a state machine state. It is the base class that a script must derive from.</summary>
    ///<remarks>A state machine can have up to three different active states at the same time: the current state, the next state, and the interrupted state.
    ///
    ///            A state machine always has a current state. When a state machine transitions to a new state, it adds a next state. When the transition is completed, the current state terminates and the next state becomes the current state.
    ///
    ///            
    ///            If an ongoing transition is interrupted by a transition to a new state, then the next state becomes the interrupted state and the state targeted by the new transition becomes the next state. The current state remains the same until all interrupted transitions are completed. When the last transition is completed and there are no interruptions, the current and interrupted states terminate and the next state becomes the current state.
    ///
    ///            StateMachineBehaviour has predefined state-related methods that you can implement:<see cref="StateMachineBehaviour.OnStateEnter">OnStateEnter</see>, <see cref="StateMachineBehaviour.OnStateExit">OnStateExit</see>, <see cref="StateMachineBehaviour.OnStateIK">OnStateIK</see>, <see cref="StateMachineBehaviour.OnStateMove">OnStateMove</see>, <see cref="StateMachineBehaviour.OnStateUpdate">OnStateUpdate</see>.
    ///
    ///            
    ///            These methods are invoked for the active states mentioned above in the following order: current state, then interrupted state, then next state.
    ///
    ///            Refer to the description of each method for more information.
    ///
    ///            StateMachineBehaviour also has predefined methods related to transitions in and out of state machines:
    ///
    ///            <see cref="StateMachineBehaviour.OnStateMachineEnter">OnStateMachineEnter</see> and <see cref="StateMachineBehaviour.OnStateMachineExit">OnStateMachineExit</see>.
    ///
    ///            These methods are invoked whenever a state machine transition is taken.
    ///
    ///            If an <see cref="T:UnityEditor.Animations.AnimatorController" /> contains sychronized layers, a method might be invoked multiple times for the same state. When this happens, the method is invoked once for each synchronized layer with the state, in ascending order.
    ///
    ///            By default the Animator instantiates a new instance of each behaviour defined in the controller. To share behaviour instances, use the <see cref="SharedBetweenAnimatorsAttribute" /> class attribute to control how behaviours are instantiated.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///public class AttackBehaviour : StateMachineBehaviour
    ///{
    ///    public GameObject particle;
    ///    public float radius;
    ///    public float power;
    ///
    ///    protected GameObject clone;
    ///
    ///    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        clone = Instantiate(particle, animator.rootPosition, Quaternion.identity) as GameObject;
    ///        Rigidbody rb = clone.GetComponent<Rigidbody>();
    ///        rb.AddExplosionForce(power, animator.rootPosition, radius, 3.0f);
    ///    }
    ///
    ///    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        Destroy(clone);
    ///    }
    ///
    ///    override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        Debug.Log("On Attack Update ");
    ///    }
    ///
    ///    override public void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        Debug.Log("On Attack Move ");
    ///    }
    ///
    ///    override public void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    ///    {
    ///        Debug.Log("On Attack IK ");
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [RequiredByNativeCode]
    public abstract class StateMachineBehaviour : ScriptableObject
    {
        // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
        ///<summary>Invoked on the first update frame when a state machine evaluates this state. Implement this message to react to a new state starting.</summary>
        ///<remarks>OnStateEnter is invoked when a transition to a state is initiated. It will be invoked after <see cref="StateMachineBehaviour.OnStateUpdate">OnStateUpdate</see> of the current state.
        ///                
        ///**Note**: OnStateEnter may be invoked multiple times on the same state if that state is synchronized on multiple layers.</remarks>
        ///<param name="animator">The Animator evaluating this state machine.</param>
        ///<param name="stateInfo">Information about the entered state.</param>
        ///<param name="layerIndex">The current layer being evaluated.</param>
        virtual public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        // OnStateUpdate is called on each Update frame between OnStateEnter and OnStateExit callbacks
        ///<summary>Invoked on each update frame except for the first and last frame. Implement this message to execute custom logic on a state by state basis.</summary>
        ///<remarks>OnStateUpdate is invoked every frame for the currently evaluated state, as well the next state and the interrupted state if applicable.
        ///
        ///                **Note**: OnStateUpdate may be invoked multiple times on the same state if that state is synchronized on multiple layers.</remarks>
        ///<param name="animator">The Animator evaluating this state machine.</param>
        ///<param name="stateInfo">Information about the state being evaluated.</param>
        ///<param name="layerIndex">The current layer being evaluated.</param>
        virtual public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        // OnStateExit is called when a transition ends and the state machine finishes evaluating this state
        ///<summary>Invoked on the last update frame when a state machine evaluates this state. Implement this message to react to a state ending.</summary>
        ///<remarks>OnStateExit is invoked when a transition to a state has completed or has been interrupted. It will be invoked before <see cref="StateMachineBehaviour.OnStateUpdate">OnStateUpdate</see> of the next state.
        ///                
        ///**Note**: OnStateExit may be invoked multiple times on the same state if that state is synchronized on multiple layers.</remarks>
        ///<param name="animator">The Animator evaluating this state machine.</param>
        ///<param name="stateInfo">Information about the exited state.</param>
        ///<param name="layerIndex">The current layer being evaluated.</param>
        virtual public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        // OnStateMove is called right after Animator.OnAnimatorMove(). Code that processes and affects root motion should be implemented here
        ///<summary>Invoked during the Animator Root Motion pass. Implement this message to modify the result of the animation root motion on a state by state basis.</summary>
        ///<remarks>OnStateMove is invoked every frame for the currently evaluated state, as well the next state and the interrupted state if applicable.
        ///
        ///                 It will be invoked after <see cref="M:UnityEngine.MonoBehaviour.OnAnimatorMove" />.
        ///
        ///                **Note**: OnAnimatorMove may be invoked multiple times on the same state if that state is synchronized on multiple layers.</remarks>
        ///<param name="animator">The Animator evaluating this state machine.</param>
        ///<param name="stateInfo">Information about the state being evaluated.</param>
        ///<param name="layerIndex">The current layer being evaluated.</param>
        virtual public void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        // OnStateIK is called right after Animator.OnAnimatorIK(). Code that sets up animation IK (inverse kinematics) should be implemented here.
        ///<summary>Invoked during the Animator IK pass. Implement this message to modify the result of the animation after the evaluation of the state machine on a state by state basis.</summary>
        ///<remarks>OnStateIK is invoked every frame for the currently evaluated state, as well the next state and the interrupted state if applicable.
        ///                 It will be invoked after <see cref="M:UnityEngine.MonoBehaviour.OnAnimatorIK" />.
        ///                
        ///**Note**: OnStateIK may be invoked multiple times on the same state if that state is synchronized on multiple layers.</remarks>
        ///<param name="animator">The Animator evaluating this state machine.</param>
        ///<param name="stateInfo">Information about the state being evaluated.</param>
        ///<param name="layerIndex">The current layer being evaluated.</param>
        virtual public void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
        }

        // OnStateMachineEnter is called when entering a statemachine via its Entry Node
        ///<summary>Invoked on the first update frame when taking a transition into a state machine. Implement this message to influence the entry transition into the sub-state machine.</summary>
        ///<remarks>**Notes**:
        ///
        ///                - This message will only be invoked when a State Machine entry transition is taken. It will not be invoked if a direct transition to a state machine sub-state is taken.
        ///
        ///                - Since this message is invoked during the evaluation of the state machine and can influence the transitions taken, implementing this message in your state machine prevents  multithreaded state machine evaluation and may be a performance concern.</remarks>
        ///<param name="animator">The Animator evaluating the state machine.</param>
        ///<param name="stateMachinePathHash">The hash of the full path to the state machine.</param>
        virtual public void OnStateMachineEnter(Animator animator, int stateMachinePathHash)
        {
        }

        // OnStateMachineExit is called when exiting a statemachine via its Exit Node
        ///<summary>Invoked on the last update frame when taking a transition out of a StateMachine. Implement this message to influence the exit transition out of the sub-state machine</summary>
        ///<remarks>**Notes**:
        ///
        ///                - This message will only be invoked when a State Machine exit transition is taken. It will not be invoked if a direct transition to a state machine sub-state is taken.
        ///
        ///                - Since this message is invoked during the evaluation of the state machine and can influence the transitions taken, implementing this message in your state machine prevents  multithreaded state machine evaluation and may be a performance concern.</remarks>
        ///<param name="animator">The Animator evaluating the state machine.</param>
        ///<param name="stateMachinePathHash">The hash of the full path to the state machine.</param>
        virtual public void OnStateMachineExit(Animator animator, int stateMachinePathHash)
        {
        }

        // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
        virtual public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateUpdate is called on each Update frame between OnStateEnter and OnStateExit callbacks
        virtual public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateExit is called when a transition ends and the state machine finishes evaluating this state
        virtual public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateMove is called right after Animator.OnAnimatorMove(). Code that processes and affects root motion should be implemented here
        virtual public void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateIK is called right after Animator.OnAnimatorIK(). Code that sets up animation IK (inverse kinematics) should be implemented here.
        virtual public void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateMachineEnter is called when entering a statemachine via its Entry Node
        virtual public void OnStateMachineEnter(Animator animator, int stateMachinePathHash, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }

        // OnStateMachineExit is called when exiting a statemachine via its Exit Node
        virtual public void OnStateMachineExit(Animator animator, int stateMachinePathHash, UnityEngine.Animations.AnimatorControllerPlayable controller)
        {
        }
    }
}
