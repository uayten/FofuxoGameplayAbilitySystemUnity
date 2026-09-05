using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    public enum AttributeOperation
    {
        Add,
        Multiply,
        Override
    }

    /// <summary>
    /// Instant or duration change to one attribute. Instant application folds
    /// into the base value; duration modifiers are kept listed until removed.
    /// Evaluation order is deterministic: base, additive, multiplicative
    /// (each as a <c>1 + magnitude</c> factor), then the last override wins.
    /// </summary>
    [Serializable]
    public struct AttributeModifier
    {
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField] private AttributeOperation operation;
        [SerializeField] private float magnitude;
        [SerializeField] private UnityEngine.Object source;

        public AttributeModifier(
            GameplayAttribute attribute,
            AttributeOperation operation,
            float magnitude,
            UnityEngine.Object source = null)
        {
            this.attribute = attribute;
            this.operation = operation;
            this.magnitude = magnitude;
            this.source = source;
        }

        public GameplayAttribute Attribute => attribute;
        public AttributeOperation Operation => operation;
        public float Magnitude => magnitude;
        public UnityEngine.Object Source => source;
    }
}
