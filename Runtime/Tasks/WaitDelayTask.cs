using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Succeeds once the configured number of seconds has been ticked. Time is
    /// accumulated rather than compared against a wall clock, so the wait lasts
    /// the same however the frames were cut, and an EditMode test drives it by
    /// handing the system whatever delta it likes.
    /// </summary>
    public sealed class WaitDelayTask : AbilityTask
    {
        private readonly float duration;

        public WaitDelayTask(float durationSeconds)
        {
            duration = Mathf.Max(0f, durationSeconds);
        }

        public float Duration => duration;
        public float ElapsedTime { get; private set; }
        public float TimeRemaining => Mathf.Max(0f, duration - ElapsedTime);

        protected override void OnStart()
        {
            // A zero wait is over before it starts. Succeeding here keeps it out
            // of the scope entirely instead of costing a tick to notice.
            if (duration <= 0f)
            {
                Succeed();
            }
        }

        protected override void OnTick(float deltaTime)
        {
            ElapsedTime += deltaTime;
            if (ElapsedTime >= duration)
            {
                Succeed();
            }
        }
    }
}
