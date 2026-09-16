using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Succeeds on the first gameplay event matching its tag.
    ///
    /// It listens on the same entry point the step advancement already uses:
    /// <see cref="AbilitySystem.TryHandleGameplayEvent"/> reports every event it
    /// receives to the running tasks, whether or not the event also advanced a
    /// step or activated a triggered ability. So an author who wants a step to
    /// wait on an event still names the tag on the ability
    /// (<c>Step Advance Event</c>); this task is for waiting inside one step
    /// without moving on when the event lands.
    /// </summary>
    public sealed class WaitGameplayEventTask : AbilityTask
    {
        private readonly GameplayTag eventTag;

        public WaitGameplayEventTask(GameplayTag eventTag)
        {
            this.eventTag = eventTag;
        }

        public GameplayTag EventTag => eventTag;

        /// <summary>
        /// The context the event carried, valid once the task has succeeded.
        /// </summary>
        public AbilityContext EventContext { get; private set; }

        protected override void OnStart()
        {
            // An empty tag would match the first event of any kind, which is
            // never what an author meant.
            if (eventTag.IsEmpty)
            {
                Fail();
            }
        }

        internal void NotifyGameplayEvent(GameplayTag raisedTag, AbilityContext context)
        {
            if (!IsRunning || raisedTag != eventTag)
            {
                return;
            }

            EventContext = context;
            Succeed();
        }
    }
}
