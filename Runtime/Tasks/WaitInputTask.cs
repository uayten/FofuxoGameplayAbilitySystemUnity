namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>Which edge of a bound input a task is waiting for.</summary>
    public enum AbilityInputEdge
    {
        Pressed,
        Released
    }

    /// <summary>
    /// Waits for one edge of a bound input.
    ///
    /// It reuses the input ownership the router already established rather than
    /// reading devices of its own: <see cref="AbilityInputRouter"/> reports both
    /// edges through <c>NotifyInputPressed</c> and <c>NotifyInputReleased</c>,
    /// which a game driving its own devices calls directly, and those forward to
    /// <see cref="AbilitySystem.NotifyAbilityInput"/>. The core assembly
    /// therefore never references the Input System, and a task sees the same
    /// edges the activation policies see.
    /// </summary>
    public sealed class WaitInputTask : AbilityTask
    {
        private readonly AbilityInputEdge edge;
        private readonly AbilityDefinition boundAbility;

        /// <param name="edge">Which edge ends the wait.</param>
        /// <param name="boundAbility">
        /// The binding to listen to, or null for any bound input. Null is the
        /// useful default for a step waiting on the button it was started with,
        /// since the game may route that press through more than one binding.
        /// </param>
        public WaitInputTask(AbilityInputEdge edge, AbilityDefinition boundAbility = null)
        {
            this.edge = edge;
            this.boundAbility = boundAbility;
        }

        /// <summary>
        /// A task that finishes on the next input press, optionally only for
        /// one ability's binding.
        /// </summary>
        public static WaitInputTask Press(AbilityDefinition boundAbility = null)
        {
            return new WaitInputTask(AbilityInputEdge.Pressed, boundAbility);
        }

        /// <summary>A task that finishes on the next input release.</summary>
        public static WaitInputTask Release(AbilityDefinition boundAbility = null)
        {
            return new WaitInputTask(AbilityInputEdge.Released, boundAbility);
        }

        public AbilityInputEdge Edge => edge;
        public AbilityDefinition BoundAbility => boundAbility;

        internal void NotifyInput(AbilityInputEdge raisedEdge, AbilityDefinition source)
        {
            if (!IsRunning ||
                raisedEdge != edge ||
                (boundAbility != null && source != boundAbility))
            {
                return;
            }

            Succeed();
        }
    }
}
