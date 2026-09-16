using System;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Why an activation attempt was refused, together with the message a human
    /// reads. The code is what game code branches on — an AI that must know the
    /// difference between "still on cooldown" and "out of range" cannot get it
    /// from a bare <c>bool</c>, and must never get it by matching on the text of
    /// <see cref="Message"/>, which is free to change.
    ///
    /// A cancel tag is the counterpart for the end of an activation; this
    /// covers its beginning.
    /// </summary>
    public readonly struct AbilityActivationResult : IEquatable<AbilityActivationResult>
    {
        /// <summary>The activation passed every check.</summary>
        public static readonly AbilityActivationResult Accepted = default;

        private readonly string message;

        private AbilityActivationResult(AbilityActivationRejection rejection, string message)
        {
            Rejection = rejection;
            this.message = message;
        }

        public AbilityActivationRejection Rejection { get; }

        /// <summary>
        /// Human-readable detail, empty when accepted. Never parsed: the code in
        /// <see cref="Rejection"/> is the contract.
        /// </summary>
        public string Message => message ?? string.Empty;

        public bool IsAccepted => Rejection == AbilityActivationRejection.None;

        /// <summary>A refusal, carrying the typed reason and a message for a human.</summary>
        public static AbilityActivationResult Rejected(
            AbilityActivationRejection rejection, string message)
        {
            return rejection == AbilityActivationRejection.None
                ? Accepted
                : new AbilityActivationResult(rejection, message);
        }

        public bool Equals(AbilityActivationResult other)
        {
            return Rejection == other.Rejection &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AbilityActivationResult other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Rejection * 397) ^ Message.GetHashCode();
        }

        public override string ToString()
        {
            return IsAccepted ? nameof(AbilityActivationRejection.None) : $"{Rejection}: {Message}";
        }
    }
}
