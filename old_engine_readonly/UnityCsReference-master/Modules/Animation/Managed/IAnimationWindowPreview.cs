// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>Allows a class to modify how an <see cref="AnimationClip" /> is sampled in the Animation window by providing its own <see cref="T:UnityEngine.Playables.Playable" /> nodes to the Animation window <see cref="T:UnityEngine.Playables.PlayableGraph" />. The class must also inherit from <see cref="MonoBehaviour" />.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Animations;
    ///using UnityEngine.Playables;
    ///
    ///[RequireComponent(typeof(Animator))]
    ///public class ExampleScript : MonoBehaviour, IAnimationWindowPreview
    ///{
    ///    public Vector3 offset = Vector3.zero;
    ///
    ///    private AnimationScriptPlayable m_Playable;
    ///    private AnimationJob m_Job;
    ///    private Vector3 m_CurrentOffset;
    ///
    ///    struct AnimationJob : IAnimationJob
    ///    {
    ///        public TransformStreamHandle transform;
    ///        public Vector3 offset;
    ///
    ///        public void ProcessRootMotion(AnimationStream stream)
    ///        {
    ///            Vector3 position = transform.GetLocalPosition(stream);
    ///            position += offset;
    ///
    ///            transform.SetLocalPosition(stream, position);
    ///        }
    ///
    ///        public void ProcessAnimation(AnimationStream stream)
    ///        {
    ///        }
    ///    }
    ///
    ///    public void StartPreview()
    ///    {
    ///        m_CurrentOffset = offset;
    ///    }
    ///
    ///    public void StopPreview()
    ///    {
    ///    }
    ///
    ///    public void UpdatePreviewGraph(PlayableGraph graph)
    ///    {
    ///        if (m_CurrentOffset != offset)
    ///        {
    ///            m_Job.offset = offset;
    ///            m_Playable.SetJobData(m_Job);
    ///
    ///            m_CurrentOffset = offset;
    ///        }
    ///    }
    ///
    ///    public Playable BuildPreviewGraph(PlayableGraph graph, Playable input)
    ///    {
    ///        Animator animator = GetComponent<Animator>();
    ///
    ///        m_Job = new AnimationJob();
    ///        m_Job.transform = animator.BindStreamTransform(transform);
    ///        m_Job.offset = offset;
    ///
    ///        m_Playable = AnimationScriptPlayable.Create(graph, m_Job, 1);
    ///
    ///        graph.Connect(input, 0, m_Playable, 0);
    ///
    ///        return m_Playable;
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="AnimationScriptPlayable" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    public interface IAnimationWindowPreview
    {
        ///<summary>Notification callback when the Animation window starts previewing an <see cref="AnimationClip" />.</summary>
        ///<remarks>The Animation window calls this function when it starts previewing an <see cref="AnimationClip" />.</remarks>
        ///<seealso cref="M:UnityEditor.AnimationMode.StartAnimationMode" />
        void StartPreview();
        ///<summary>Notification callback when the Animation window stops previewing an <see cref="AnimationClip" />.</summary>
        ///<remarks>The Animation window calls this function when it stops previewing an <see cref="AnimationClip" />.</remarks>
        ///<seealso cref="M:UnityEditor.AnimationMode.StopAnimationMode" />
        void StopPreview();

        ///<summary>Notification callback when the Animation Window updates its <see cref="T:UnityEngine.Playables.PlayableGraph" /> before sampling an <see cref="AnimationClip" />.</summary>
        ///<remarks>The Animation window calls this function before sampling an <see cref="AnimationClip" />.
        ///**Note:** This does not support legacy Animation clips.</remarks>
        ///<param name="graph">The Animation window <see cref="T:UnityEngine.Playables.PlayableGraph" />.</param>
        void UpdatePreviewGraph(PlayableGraph graph);
        ///<summary>Appends custom <see cref="T:UnityEngine.Playables.Playable" /> nodes to the Animation window <see cref="T:UnityEngine.Playables.PlayableGraph" />.</summary>
        ///<remarks>The Animation window calls this function when it samples an <see cref="AnimationClip" /> for the first time.
        ///**Note:** This does not support legacy Animation clips.</remarks>
        ///<param name="graph">The Animation window <see cref="T:UnityEngine.Playables.PlayableGraph" />.</param>
        ///<param name="inputPlayable">Current root of the <see cref="T:UnityEngine.Playables.PlayableGraph" />.</param>
        ///<returns>Returns the new root of the <see cref="T:UnityEngine.Playables.PlayableGraph" />.</returns>
        Playable BuildPreviewGraph(PlayableGraph graph, Playable inputPlayable);
    }
}
