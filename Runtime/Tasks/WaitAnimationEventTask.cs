using System;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Waits for a Unity Animation Event forwarded by
    /// <see cref="AbilityAnimationEventBridge"/>.
    ///
    /// This is a presentation clock, not a gameplay one: the package rule that
    /// Animator state is never the sole authority for gameplay timing still
    /// holds, so a step that must land on a frame keeps using its authored
    /// timeline. Use this for work whose cue genuinely lives in the clip — a
    /// footstep, a release point an artist retimes — and give the step a timeout
    /// policy for the case where the clip is swapped and the event never fires.
    /// </summary>
    public sealed class WaitAnimationEventTask : AbilityTask
    {
        private readonly string eventName;

        public WaitAnimationEventTask(string eventName)
        {
            this.eventName = eventName ?? string.Empty;
        }

        public string EventName => eventName;

        protected override void OnStart()
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Fail();
            }
        }

        internal void NotifyAnimationEvent(string raisedEvent)
        {
            if (!IsRunning ||
                !string.Equals(raisedEvent, eventName, StringComparison.Ordinal))
            {
                return;
            }

            Succeed();
        }
    }
}
