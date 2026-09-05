using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Savable attribute defaults shared by every actor using the same build:
    /// initial values and regeneration, mirroring an Unreal AttributeSet CDO.
    /// Assign on <see cref="AttributeSet"/> to stop authoring numbers per instance.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AttributeSetDefinition",
        menuName = "Fofuxo/Abilities/Attribute Set")]
    public sealed class AttributeSetDefinition : ScriptableObject
    {
        [SerializeField] private AttributeSet.InitialValue[] initialValues = { };
        [SerializeField] private AttributeSet.Regeneration[] regeneration = { };

        public AttributeSet.InitialValue[] InitialValues => initialValues;
        public AttributeSet.Regeneration[] Regeneration => regeneration;
    }
}
