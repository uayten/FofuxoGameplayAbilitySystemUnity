using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Timed attribute modifier delivered as effect data: buffs, debuffs, and
    /// stuns. The modifier detaches automatically on expiry or through
    /// <see cref="AttributeSet.RemoveModifiers"/> (cleanse/dispel).
    /// </summary>
    [CreateAssetMenu(
        fileName = "DurationAttributeEffect",
        menuName = "Fofuxo/Abilities/Effects/Duration Attribute")]
    public sealed class DurationAttributeEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private bool applyToTarget = true;
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField] private AttributeOperation operation = AttributeOperation.Add;
        [SerializeField] private float magnitude;
        [SerializeField, Min(0f)] private float durationSeconds = 1f;
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

            set.ApplyDurationModifier(
                new AttributeModifier(attribute, operation, magnitude, context.Owner),
                durationSeconds,
                stacking);
        }

        private void OnValidate()
        {
            durationSeconds = Mathf.Max(0f, durationSeconds);
        }
    }
}
