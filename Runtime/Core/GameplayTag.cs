using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        [SerializeField] private string value;

        public GameplayTag(string value)
        {
            this.value = value?.Trim() ?? string.Empty;
        }

        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        public bool Equals(GameplayTag other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GameplayTag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(GameplayTag left, GameplayTag right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameplayTag left, GameplayTag right)
        {
            return !left.Equals(right);
        }
    }

    public static class CommonGameplayTags
    {
        public static readonly GameplayTag Attacking = new("State.Attacking");
        public static readonly GameplayTag Blocking = new("State.Blocking");
        public static readonly GameplayTag Parrying = new("State.Parrying");
        public static readonly GameplayTag Rolling = new("State.Rolling");
        public static readonly GameplayTag Stunned = new("State.Stunned");
        public static readonly GameplayTag KnockedDown = new("State.KnockedDown");
        public static readonly GameplayTag Dead = new("State.Dead");
        public static readonly GameplayTag ActionLocked = new("State.ActionLocked");
        public static readonly GameplayTag Invulnerable = new("State.Invulnerable");
    }
}
