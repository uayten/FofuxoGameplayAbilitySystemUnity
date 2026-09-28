using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Plays ability-owned clips after the Animator Controller output. The
    /// controller keeps evaluating locomotion and parameters underneath, while
    /// the ability clip is the authoritative animation during its activation.
    /// </summary>
    internal sealed class AbilityAnimationPlayer : IDisposable
    {
        private const int MixerInputCount = 2;
        private const int OutputSortingOrder = ushort.MaxValue;

        private readonly Animator animator;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable currentPlayable;
        private AnimationClipPlayable previousPlayable;
        private int currentPort = -1;
        private int previousPort = -1;

        private bool clipBlending;
        private float clipBlendElapsed;
        private float clipBlendDuration;

        private bool outputBlending;
        private float outputBlendStart;
        private float outputBlendTarget;
        private float outputBlendElapsed;
        private float outputBlendDuration;

        internal AbilityAnimationPlayer(Animator animator)
        {
            this.animator = animator;
        }

        internal AnimationClip CurrentClip { get; private set; }
        internal bool IsPlaying => currentPlayable.IsValid();
        internal float OutputWeight => graph.IsValid() ? output.GetWeight() : 0f;
        internal AnimationStreamSource StreamSource =>
            graph.IsValid()
                ? output.GetAnimationStreamSource()
                : AnimationStreamSource.DefaultValues;

        internal bool Play(AnimationClip clip, float blendDuration)
        {
            if (animator == null || clip == null)
            {
                Stop(blendDuration);
                return false;
            }

            EnsureGraph();
            DestroyPreviousPlayable();

            bool hasPrevious = currentPlayable.IsValid();
            if (hasPrevious)
            {
                previousPlayable = currentPlayable;
                previousPort = currentPort;
                currentPlayable = default;
                currentPort = -1;
            }

            int newPort = previousPort == 0 ? 1 : 0;
            currentPlayable = AnimationClipPlayable.Create(graph, clip);
            currentPlayable.SetTime(0d);
            currentPlayable.SetSpeed(1d);
            currentPlayable.SetApplyFootIK(false);
            currentPlayable.SetApplyPlayableIK(false);
            graph.Connect(currentPlayable, 0, mixer, newPort);
            currentPort = newPort;
            CurrentClip = clip;

            float duration = Mathf.Max(0f, blendDuration);
            if (hasPrevious && duration > Mathf.Epsilon)
            {
                mixer.SetInputWeight(previousPort, 1f);
                mixer.SetInputWeight(currentPort, 0f);
                clipBlendElapsed = 0f;
                clipBlendDuration = duration;
                clipBlending = true;
            }
            else
            {
                DestroyPreviousPlayable();
                mixer.SetInputWeight(currentPort, 1f);
                clipBlending = false;
            }

            BeginOutputBlend(1f, duration);
            return true;
        }

        internal void Stop(float blendDuration)
        {
            if (!graph.IsValid() || !currentPlayable.IsValid())
            {
                StopImmediate();
                return;
            }

            float duration = Mathf.Max(0f, blendDuration);
            if (duration <= Mathf.Epsilon)
            {
                StopImmediate();
                return;
            }

            BeginOutputBlend(0f, duration);
        }

        internal void Tick(float deltaTime)
        {
            if (!graph.IsValid())
            {
                return;
            }

            float tick = Mathf.Max(0f, deltaTime);
            TickClipBlend(tick);
            TickOutputBlend(tick);
        }

        internal void EvaluateForTests(float deltaTime)
        {
            if (graph.IsValid())
            {
                graph.Evaluate(Mathf.Max(0f, deltaTime));
            }
        }

        /// <summary>Releases the playable graph. Called when the actor tears down.</summary>
        public void Dispose()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            graph = default;
            mixer = default;
            output = default;
            currentPlayable = default;
            previousPlayable = default;
            currentPort = -1;
            previousPort = -1;
            CurrentClip = null;
            clipBlending = false;
            outputBlending = false;
        }

        private void EnsureGraph()
        {
            if (graph.IsValid())
            {
                return;
            }

            graph = PlayableGraph.Create("Fofuxo Ability Animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, MixerInputCount);

            output = AnimationPlayableOutput.Create(
                graph,
                "Fofuxo Ability Animation",
                animator);
            output.SetSourcePlayable(mixer);
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            output.SetSortingOrder(OutputSortingOrder);
            output.SetWeight(0f);
            graph.Play();
        }

        private void BeginOutputBlend(float targetWeight, float duration)
        {
            outputBlendStart = output.GetWeight();
            outputBlendTarget = Mathf.Clamp01(targetWeight);
            outputBlendElapsed = 0f;
            outputBlendDuration = Mathf.Max(0f, duration);
            outputBlending = outputBlendDuration > Mathf.Epsilon;

            if (!outputBlending)
            {
                output.SetWeight(outputBlendTarget);
                if (outputBlendTarget <= Mathf.Epsilon)
                {
                    ClearPlayables();
                }
            }
        }

        private void TickClipBlend(float deltaTime)
        {
            if (!clipBlending || !previousPlayable.IsValid())
            {
                return;
            }

            clipBlendElapsed += deltaTime;
            float progress = Mathf.Clamp01(
                clipBlendElapsed / clipBlendDuration);
            mixer.SetInputWeight(previousPort, 1f - progress);
            mixer.SetInputWeight(currentPort, progress);

            if (progress >= 1f)
            {
                DestroyPreviousPlayable();
                mixer.SetInputWeight(currentPort, 1f);
                clipBlending = false;
            }
        }

        private void TickOutputBlend(float deltaTime)
        {
            if (!outputBlending)
            {
                return;
            }

            outputBlendElapsed += deltaTime;
            float progress = Mathf.Clamp01(
                outputBlendElapsed / outputBlendDuration);
            output.SetWeight(Mathf.Lerp(
                outputBlendStart,
                outputBlendTarget,
                progress));

            if (progress < 1f)
            {
                return;
            }

            outputBlending = false;
            if (outputBlendTarget <= Mathf.Epsilon)
            {
                ClearPlayables();
            }
        }

        private void StopImmediate()
        {
            if (graph.IsValid())
            {
                output.SetWeight(0f);
            }

            ClearPlayables();
            outputBlending = false;
        }

        private void ClearPlayables()
        {
            DestroyPreviousPlayable();
            DestroyPlayable(ref currentPlayable, ref currentPort);
            CurrentClip = null;
            clipBlending = false;
        }

        private void DestroyPreviousPlayable()
        {
            DestroyPlayable(ref previousPlayable, ref previousPort);
        }

        private void DestroyPlayable(
            ref AnimationClipPlayable playable,
            ref int port)
        {
            if (graph.IsValid() && mixer.IsValid() && port >= 0)
            {
                mixer.DisconnectInput(port);
                mixer.SetInputWeight(port, 0f);
            }

            if (playable.IsValid())
            {
                playable.Destroy();
            }

            playable = default;
            port = -1;
        }
    }
}
