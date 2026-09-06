using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public sealed class AbilityInstance
    {
        private readonly bool[] executedEffects;
        private readonly bool[] executedCues;
        private readonly HashSet<(int TriggerIndex, Object Receiver)> registeredHits = new();
        private Vector3 displacementDirection;
        private float displacementRemainingDistance;
        private float displacementRemainingDuration;
        private int displacementStartFrame = 1;
        private int displacementEndFrame = int.MaxValue;

        public AbilityInstance(AbilityDefinition definition, AbilityContext context)
        {
            Definition = definition;
            Context = context;
            executedEffects = new bool[definition.EffectTriggers.Count];
            executedCues = new bool[definition.CueTriggers.Count];
            CurrentFrame = 0;
            CurrentPhase = definition.GetPhase(0);
        }

        public AbilityDefinition Definition { get; }
        public AbilityContext Context { get; }
        public float ElapsedTime { get; private set; }
        public int CurrentFrame { get; private set; }
        public AbilityPhase CurrentPhase { get; private set; }
        public int RegisteredHitCount => registeredHits.Count;

        public bool HasActiveDisplacement =>
            displacementRemainingDistance > Mathf.Epsilon &&
            displacementRemainingDuration > Mathf.Epsilon &&
            displacementDirection.sqrMagnitude > Mathf.Epsilon;
        public bool IsDisplacementWindowOpen =>
            CurrentFrame >= displacementStartFrame &&
            CurrentFrame <= displacementEndFrame;
        public Rigidbody DisplacementBody { get; private set; }

        internal bool Tick(
            AbilitySystem abilitySystem,
            float deltaTime,
            out AbilityPhase previousPhase,
            List<GameplayTag> firedCues)
        {
            previousPhase = CurrentPhase;
            ElapsedTime += Mathf.Max(0f, deltaTime);
            CurrentFrame = Mathf.Clamp(
                Mathf.FloorToInt(ElapsedTime * Definition.FrameRate) + 1,
                1,
                Definition.RecoveryEndFrame);
            CurrentPhase = Definition.GetPhase(CurrentFrame);

            for (int i = 0; i < Definition.EffectTriggers.Count; i++)
            {
                AbilityEffectTrigger trigger = Definition.EffectTriggers[i];
                if (executedEffects[i] || trigger.Frame > CurrentFrame)
                {
                    continue;
                }

                executedEffects[i] = true;
                trigger.Effect?.Apply(new AbilityEffectContext(abilitySystem, this, i));
            }

            for (int i = 0; i < Definition.CueTriggers.Count; i++)
            {
                GameplayCueTrigger cueTrigger = Definition.CueTriggers[i];
                if (executedCues[i] || cueTrigger.Frame > CurrentFrame)
                {
                    continue;
                }

                executedCues[i] = true;
                if (!cueTrigger.Cue.IsEmpty)
                {
                    firedCues?.Add(cueTrigger.Cue);
                }
            }

            return ElapsedTime >= Definition.Duration;
        }

        /// <summary>
        /// Snapshots the travel for this activation. Direction and body are
        /// resolved once by the system; the window consumes at constant speed
        /// (remaining distance over remaining duration), so hitches distribute
        /// instead of teleporting.
        /// </summary>
        internal void BeginDisplacement(
            Vector3 direction,
            Rigidbody body,
            float distance,
            float duration)
        {
            BeginDisplacement(
                direction,
                body,
                distance,
                duration,
                1,
                int.MaxValue);
        }

        internal void BeginDisplacement(
            Vector3 direction,
            Rigidbody body,
            float distance,
            float duration,
            int startFrame,
            int endFrame)
        {
            displacementDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (displacementDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                displacementDirection = Vector3.zero;
            }
            else
            {
                displacementDirection.Normalize();
            }

            DisplacementBody = body;
            displacementRemainingDistance = Mathf.Max(0f, distance);
            displacementRemainingDuration = Mathf.Max(0f, duration);
            displacementStartFrame = Mathf.Max(1, startFrame);
            displacementEndFrame = Mathf.Max(displacementStartFrame, endFrame);
        }

        /// <summary>
        /// Consumes one tick of travel. Returns false when there is nothing
        /// left to move this tick; the step never exceeds the remainder, so
        /// the total travelled distance equals the configured distance.
        /// </summary>
        internal bool TickDisplacement(float deltaTime, out Vector3 step)
        {
            step = Vector3.zero;
            if (!HasActiveDisplacement)
            {
                return false;
            }

            float tick = Mathf.Max(0f, deltaTime);
            float speed = displacementRemainingDistance /
                Mathf.Max(Mathf.Epsilon, displacementRemainingDuration);
            float stepDistance = Mathf.Min(displacementRemainingDistance, speed * tick);
            if (stepDistance <= Mathf.Epsilon)
            {
                return false;
            }

            displacementRemainingDistance -= stepDistance;
            displacementRemainingDuration = Mathf.Max(
                0f,
                displacementRemainingDuration - tick);
            step = displacementDirection * stepDistance;
            return true;
        }

        public bool TryRegisterHit(int triggerIndex, Object receiver)
        {
            if (receiver == null)
            {
                return false;
            }

            return registeredHits.Add((triggerIndex, receiver));
        }
    }
}
