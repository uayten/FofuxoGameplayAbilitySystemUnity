using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Instant attribute change delivered as effect data: damage, healing,
    /// and resource costs without a query volume. The change folds into the
    /// target set's base value and fires its change event, so reactions and
    /// UI observe it exactly like any other attribute change.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ModifyAttributeEffect",
        menuName = "Fofuxo/Abilities/Effects/Modify Attribute")]
    public sealed class ModifyAttributeEffectDefinition : AbilityEffectDefinition
    {
        [SerializeField] private bool applyToTarget = true;
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField] private AttributeOperation operation = AttributeOperation.Add;
        [Tooltip("Positive adds, negative subtracts for the Add operation.")]
        [SerializeField] private float magnitude;
        [Header("Attribute Scaling")]
        [SerializeField] private GameplayAttribute scaleAttribute;
        [SerializeField, Min(0f)] private float scaleFactor;

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

            float amount = magnitude + TargetQueries.ResolveBonusDamage(
                context.Owner, scaleAttribute, scaleFactor);
            set.ApplyInstantModifier(new AttributeModifier(
                attribute, operation, amount, context.Owner));
        }

        private void OnValidate()
        {
            scaleFactor = Mathf.Max(0f, scaleFactor);
        }
    }
}
