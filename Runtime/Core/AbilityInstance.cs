using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public sealed class AbilityInstance
    {
        private readonly bool[] executedEffects;
        private readonly bool[] executedCues;
        private readonly HashSet<(int TriggerIndex, Object Receiver)> registeredHits = new();

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
