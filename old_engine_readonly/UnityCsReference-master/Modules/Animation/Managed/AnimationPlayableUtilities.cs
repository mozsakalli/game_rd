// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Animations;

namespace UnityEngine.Playables
{
    ///<summary>Implements high-level utility methods to simplify use of the <see cref="Playable" /> API with Animations.</summary>
    public static class AnimationPlayableUtilities
    {
        ///<summary>Plays the <see cref="Playable" /> on  the given Animator.
        ///
        ///                    **Note:** This method is deprecated as it overrides the Time Update Mode of the Playable Graph. For an equivalent function, refer to the example below.</summary>
        ///<param name="animator">Target Animator.</param>
        ///<param name="playable">The Playable that will be played.</param>
        ///<param name="graph">The Graph that owns the Playable.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Animations;
        ///using UnityEngine.Playables;
        ///
        ///public class Example
        ///{
        ///    void Play(Animator animator, Playable playable, PlayableGraph graph)
        ///    {
        ///        AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
        ///        playableOutput.SetSourcePlayable(playable, 0);
        ///        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        ///        graph.Play();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [Obsolete("This function is no longer used as it overrides the Time Update Mode of the Playable Graph. Refer to the documentation for an example of an equivalent function.")]
        static public void Play(Animator animator, Playable playable, PlayableGraph graph)
        {
            AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            playableOutput.SetSourcePlayable(playable, 0);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
        }

        ///<summary>Creates a <see cref="PlayableGraph" /> to be played on the given Animator. An <see cref="AnimationClipPlayable" /> is also created for the given AnimationClip.</summary>
        ///<param name="animator">Target Animator.</param>
        ///<param name="clip">The AnimationClip to create an <see cref="AnimationClipPlayable" /> for.</param>
        ///<param name="graph">The created <see cref="PlayableGraph" />.</param>
        ///<returns>A handle to the newly-created <see cref="AnimationClipPlayable" />.</returns>
        static public AnimationClipPlayable PlayClip(Animator animator, AnimationClip clip, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            var clipPlayable = AnimationClipPlayable.Create(graph, clip);
            playableOutput.SetSourcePlayable(clipPlayable);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();

            return clipPlayable;
        }

        ///<summary>Creates a <see cref="PlayableGraph" /> to be played on the given Animator. An <see cref="AnimationMixerPlayable" /> is also created.</summary>
        ///<param name="animator">Target Animator.</param>
        ///<param name="inputCount">The input count for the <see cref="AnimationMixerPlayable" />.</param>
        ///<param name="graph">The created <see cref="PlayableGraph" />.</param>
        ///<returns>A handle to the newly-created <see cref="AnimationMixerPlayable" />.</returns>
        static public AnimationMixerPlayable PlayMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var mixer = AnimationMixerPlayable.Create(graph, inputCount);
            playableOutput.SetSourcePlayable(mixer);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();

            return mixer;
        }

        ///<summary>Creates a <see cref="PlayableGraph" /> to be played on the given Animator. An <see cref="AnimationLayerMixerPlayable" /> is also created.</summary>
        ///<param name="animator">Target Animator.</param>
        ///<param name="inputCount">The input count for the <see cref="AnimationLayerMixerPlayable" />. Defines the number of layers.</param>
        ///<param name="graph">The created <see cref="PlayableGraph" />.</param>
        ///<returns>A handle to the newly-created <see cref="AnimationLayerMixerPlayable" />.</returns>
        static public AnimationLayerMixerPlayable PlayLayerMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var mixer = AnimationLayerMixerPlayable.Create(graph, inputCount);
            playableOutput.SetSourcePlayable(mixer);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();

            return mixer;
        }

        ///<summary>Creates a <see cref="PlayableGraph" /> to be played on the given Animator. An <see cref="AnimatorControllerPlayable" /> is also created for the given RuntimeAnimatorController.</summary>
        ///<param name="animator">Target Animator.</param>
        ///<param name="controller">The RuntimeAnimatorController to create an <see cref="AnimatorControllerPlayable" /> for.</param>
        ///<param name="graph">The created <see cref="PlayableGraph" />.</param>
        ///<returns>A handle to the newly-created <see cref="AnimatorControllerPlayable" />.</returns>
        static public AnimatorControllerPlayable PlayAnimatorController(Animator animator, RuntimeAnimatorController controller, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "AnimatorControllerPlayable", animator);
            var controllerPlayable = AnimatorControllerPlayable.Create(graph, controller);
            playableOutput.SetSourcePlayable(controllerPlayable);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();

            return controllerPlayable;
        }
    }
}
