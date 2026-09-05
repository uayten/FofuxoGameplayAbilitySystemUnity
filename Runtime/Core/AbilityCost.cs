using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Attribute resource consumed when an ability activates, such as Stamina
    /// for a sprint attack. Checked before activation, deducted on success.
    /// </summary>
    [Serializable]
    public struct AbilityCost
    {
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField, Min(0f)] private float amount;

        public AbilityCost(GameplayAttribute attribute, float amount)
        {
            this.attribute = attribute;
            this.amount = Mathf.Max(0f, amount);
        }

        public GameplayAttribute Attribute => attribute;
        public float Amount => Mathf.Max(0f, amount);
    }
}
