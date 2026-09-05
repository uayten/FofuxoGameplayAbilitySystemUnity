using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Stable identifier for a numeric attribute such as
    /// <c>Combat.Health</c>. Authoring data only; runtime values live in
    /// <see cref="AttributeSet"/> components.
    /// </summary>
    [Serializable]
    public struct GameplayAttribute : IEquatable<GameplayAttribute>
    {
        [SerializeField] private string id;

        public GameplayAttribute(string id)
        {
            this.id = id?.Trim() ?? string.Empty;
        }

        public string Id => id ?? string.Empty;
        public bool IsEmpty => string.IsNullOrWhiteSpace(Id);

        public bool Equals(GameplayAttribute other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GameplayAttribute other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        public override string ToString()
        {
            return Id;
        }

        public static bool operator ==(GameplayAttribute left, GameplayAttribute right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameplayAttribute left, GameplayAttribute right)
        {
            return !left.Equals(right);
        }
    }
}
