using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Damage or heal over time delivered as effect data. Applies a periodic
    /// attribute modifier to the target (or owner) set: every period the
    /// magnitude folds into the base value like an instant change, firing the
    /// set's change event per tick. Stacking matches attribute, operation,
    /// and source (the ability owner), like duration modifiers.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PeriodicAttributeEffect",
        menuName = "Fofuxo/Abilities/Effects/Periodic Attribute")]
    public sealed class PeriodicAttributeEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private bool applyToTarget = true;
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField] private AttributeOperation operation = AttributeOperation.Add;
        [Tooltip("Applied every period. Negative damages, positive heals for the Add operation.")]
        [SerializeField] private float magnitudePerTick = -1f;
        [SerializeField, Min(0f)] private float durationSeconds = 3f;
        [SerializeField, Min(0f)] private float periodSeconds = 1f;
        [SerializeField] private EffectStacking stacking = EffectStacking.Refresh;

        public override void Apply(AbilityEffectContext context)
        {
            GameObject which = applyToTarget ? context.Target : context.Owner;
            if (which == null || attribute.IsEmpty)
            {
                return;
            }

            AttributeSet set = which.GetComponent<AttributeSet>();
            if (set == null)
            {
                return;
            }

            set.ApplyPeriodicModifier(
                new AttributeModifier(attribute, operation, magnitudePerTick, context.Owner),
                durationSeconds,
                periodSeconds,
                stacking);
        }

        private void OnValidate()
        {
            durationSeconds = Mathf.Max(0f, durationSeconds);
            periodSeconds = Mathf.Max(0f, periodSeconds);
        }
    }
}
