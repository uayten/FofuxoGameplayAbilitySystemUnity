using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// The kind of ability a target plays when it is hit. It is started by a
    /// gameplay event — <c>Event.HitReaction</c> by default, sent by the damage
    /// effect once the damage has landed — and it owns everything the reaction
    /// is: the animation on its timeline, the control lock through its granted
    /// tags and movement lock, its own cancellation, and the knockback, which it
    /// runs as a movement task in its own scope so that ending the reaction
    /// ends the push with it.
    ///
    /// The decision to react is the target's. The event goes through the same
    /// activation rules as any ability, so an owner holding a blocked tag —
    /// <c>State.Invulnerable</c> from a roll, <c>State.Dead</c> — plays nothing,
    /// and the attacker never learns why; it only counted a hit.
    ///
    /// What the hit was is read off <see cref="AbilityInstance.TriggeringSpec"/>,
    /// the very application that landed: its source, its level, its contact
    /// point, and the knockback the effect authored. Nothing is copied into a
    /// struct on the way and nothing is handed to a consumer callback.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GA_HitReaction",
        menuName = "Fofuxo/Abilities/Hit Reaction Ability")]
    public class HitReactionAbilityDefinition : TimelineAbilityDefinition
    {
        [Header("Knockback")]
        [Tooltip("Scales the knockback the hit authored. One takes it as authored; zero takes the hit without moving — a heavy actor, or a reaction that stands its ground.")]
        [SerializeField, Min(0f)] private float knockbackScale = 1f;
        [Tooltip("Snapshot commits to the vector the hit carried. Track keeps pushing away from the attacker as it moves.")]
        [SerializeField] private AbilityMovementDirectionPolicy knockbackDirectionPolicy =
            AbilityMovementDirectionPolicy.Snapshot;

        public float KnockbackScale => Mathf.Max(0f, knockbackScale);
        public AbilityMovementDirectionPolicy KnockbackDirectionPolicy => knockbackDirectionPolicy;

        /// <summary>
        /// The knockback this activation is running, or null when it runs none.
        /// Per activation, never per definition: two actors hit by one swing
        /// each own their push.
        /// </summary>
        public static ApplyKnockbackTask FindKnockback(AbilityInstance instance)
        {
            if (instance == null)
            {
                return null;
            }

            System.Collections.Generic.IReadOnlyList<AbilityTask> tasks = instance.Tasks.Tasks;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i] is ApplyKnockbackTask knockback)
                {
                    return knockback;
                }
            }

            return null;
        }

        protected internal override void OnActivated(AbilityInstance instance)
        {
            base.OnActivated(instance);
            BeginKnockback(instance);
        }

        /// <summary>
        /// The push the hit authored, scaled by this reaction, started in the
        /// activation's own scope. Nothing moves when the hit carried no
        /// knockback, when the scale is zero, or when the owner has no motor and
        /// no Rigidbody to move — the reaction still plays; it just stands.
        /// </summary>
        protected virtual void BeginKnockback(AbilityInstance instance)
        {
            GameplayEffectSpec hit = instance.TriggeringSpec;
            if (hit == null ||
                KnockbackScale <= 0f ||
                instance.System == null ||
                !instance.System.HasMotor)
            {
                return;
            }

            if (!hit.TryGetKnockback(out Vector3 velocity, out float duration))
            {
                return;
            }

            instance.RunTask(new ApplyKnockbackTask(
                velocity * KnockbackScale,
                duration,
                hit.Source,
                knockbackDirectionPolicy));
        }

        /// <summary>
        /// A reaction with no trigger is reachable only from code. Legal, and
        /// worth a word: the damage effects send an event, and an asset without
        /// a matching trigger will never answer it.
        /// </summary>
        public override bool TryGetAuthoringWarning(out string warning)
        {
            if (base.TryGetAuthoringWarning(out warning))
            {
                return true;
            }

            if (ActivationTriggers.Count == 0)
            {
                warning =
                    "No Activation Trigger, so no damage effect can start this " +
                    "reaction. Add one for the event the effects send — " +
                    $"{CommonGameplayTags.HitReactionEvent} by default, " +
                    $"{CommonGameplayTags.KnockdownEvent} for a hit that floors.";
                return true;
            }

            return false;
        }
    }
}
