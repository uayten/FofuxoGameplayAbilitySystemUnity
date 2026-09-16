using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Whether a movement task keeps the aim it started with or re-reads it.
    /// Every movement task states one explicitly: a lunge that homes onto a
    /// dodging enemy and one that commits to where it was aimed are different
    /// moves, and neither should be an accident of implementation.
    /// </summary>
    public enum AbilityMovementDirectionPolicy
    {
        /// <summary>Resolved once when the task starts, then never again.</summary>
        Snapshot,

        /// <summary>Re-resolved every tick from the live world.</summary>
        Track
    }

    /// <summary>
    /// What a movement task does on the ticks it does not own the body. Only
    /// one task moves the owner per tick unless it says otherwise here.
    /// </summary>
    public enum AbilityMovementConflictPolicy
    {
        /// <summary>
        /// Waits its turn without spending any of its budget, so it still
        /// travels its full distance once it wins the body. This is what
        /// concurrent displacement has always done, and it stays the default.
        /// </summary>
        Yield,

        /// <summary>Adds its delta on top of the winner, moving in the same tick.</summary>
        Blend,

        /// <summary>Gives up the moment it loses the body, ending as failed.</summary>
        Abort
    }

    /// <summary>
    /// Constant-speed travel over a duration, and the arbitration data that
    /// decides which of several concurrent travels owns the body.
    ///
    /// Speed is remaining distance over remaining duration, recomputed each
    /// tick, so a hitch redistributes the travel instead of teleporting through
    /// it, and the total is exactly the configured distance however the frames
    /// were cut. That arithmetic is the whole reason movement is a task: it used
    /// to live in the activation and could only ever run once per actor.
    ///
    /// A task computes a delta; it never touches the body. The system applies
    /// the delta through the owner <see cref="IAbilityMotor"/>, which is what
    /// keeps every engine dependency on the far side of one interface.
    /// </summary>
    public abstract class AbilityMoveTask : AbilityTask
    {
        /// <summary>The priority ability-timeline displacement runs at.</summary>
        public const int DisplacementPriority = 0;

        /// <summary>
        /// The priority the target-assist approach runs at. Equal to plain
        /// displacement: the two cannot be authored on one step, so the tie is
        /// theoretical, and keeping them level preserves the oldest-wins order.
        /// </summary>
        public const int ApproachPriority = 0;

        /// <summary>
        /// The priority a hit reaction runs at. Above the timeline, because
        /// being knocked back is something done to the owner and outranks
        /// whatever the owner had planned.
        /// </summary>
        public const int KnockbackPriority = 100;

        private float remainingDistance;
        private float remainingDuration;
        private Vector3 direction;
        private bool hasDirection;
        private bool hasFrameWindow;
        private int startFrame = 1;
        private int endFrame = int.MaxValue;

        protected AbilityMoveTask(
            float distance,
            float durationSeconds,
            AbilityMovementDirectionPolicy directionPolicy)
        {
            remainingDistance = Mathf.Max(0f, distance);
            remainingDuration = Mathf.Max(0f, durationSeconds);
            DirectionPolicy = directionPolicy;
        }

        public AbilityMovementDirectionPolicy DirectionPolicy { get; }

        /// <summary>Higher wins the body. Ties break by activation age, oldest first.</summary>
        public int Priority { get; set; } = DisplacementPriority;

        public AbilityMovementConflictPolicy ConflictPolicy { get; set; } =
            AbilityMovementConflictPolicy.Yield;

        public AbilityMovementCollision Collision { get; set; } =
            AbilityMovementCollision.Unswept;

        public float RemainingDistance => remainingDistance;
        public float RemainingDuration => remainingDuration;

        /// <summary>Everything the task has produced so far, summed.</summary>
        public Vector3 TravelledDelta { get; private set; }

        /// <summary>
        /// The travel direction in use. Meaningless before the first resolve.
        /// </summary>
        public Vector3 Direction => direction;

        internal sealed override bool IsMovement => true;

        /// <summary>
        /// First activation frame the travel may run on. Meaningless unless
        /// <see cref="HasFrameWindow"/>.
        /// </summary>
        public int StartFrame => startFrame;

        /// <summary>Last activation frame the travel may run on.</summary>
        public int EndFrame => endFrame;

        /// <summary>
        /// True when the travel is bound to a stretch of the step timeline. A
        /// task started outside one — a knockback, a caller driving the task by
        /// hand — is never gated.
        /// </summary>
        public bool HasFrameWindow => hasFrameWindow;

        /// <summary>
        /// False on the ticks the task is alive but must not move: an authored
        /// frame window that has not opened yet, or has closed. A closed window
        /// spends no budget, so the travel is delayed, never lost.
        /// </summary>
        public bool IsWindowOpen
        {
            get
            {
                if (!hasFrameWindow || Instance == null)
                {
                    return true;
                }

                int frame = Instance.CurrentFrame;
                return frame >= startFrame && frame <= endFrame;
            }
        }

        /// <summary>
        /// Binds the travel to a stretch of the owning step timeline, in
        /// 1-based frames.
        /// </summary>
        public void SetFrameWindow(int firstFrame, int lastFrame)
        {
            startFrame = Mathf.Max(1, firstFrame);
            endFrame = Mathf.Max(startFrame, lastFrame);
            hasFrameWindow = true;
        }

        /// <summary>
        /// Where the travel points. Called once at start under
        /// <see cref="AbilityMovementDirectionPolicy.Snapshot"/> and every tick
        /// under <see cref="AbilityMovementDirectionPolicy.Track"/>.
        /// </summary>
        protected abstract bool TryResolveDirection(out Vector3 resolvedDirection);

        /// <summary>
        /// Lets a tracking task re-measure how far is still left — a lunge onto
        /// a target that moved has a different gap than when it started. The
        /// default keeps the budget the task was built with.
        /// </summary>
        protected virtual bool TryUpdateRemainingDistance(out float distance)
        {
            distance = remainingDistance;
            return false;
        }

        protected override void OnStart()
        {
            if (remainingDistance <= Mathf.Epsilon || remainingDuration <= Mathf.Epsilon)
            {
                Succeed();
                return;
            }

            if (DirectionPolicy == AbilityMovementDirectionPolicy.Snapshot &&
                !ResolveDirectionNow())
            {
                Fail();
            }
        }

        /// <summary>
        /// Produces one tick of travel. The system calls this only for the task
        /// that won the body, so a losing task under the default policy spends
        /// nothing and arrives intact at the tick it wins.
        /// </summary>
        /// <returns>False when there is nothing to move this tick.</returns>
        internal bool TickMovement(float deltaTime, out Vector3 delta)
        {
            delta = Vector3.zero;
            if (!IsRunning)
            {
                return false;
            }

            if (!IsWindowOpen)
            {
                return false;
            }

            if (DirectionPolicy == AbilityMovementDirectionPolicy.Track &&
                !ResolveDirectionNow())
            {
                Fail();
                return false;
            }

            if (TryUpdateRemainingDistance(out float updated))
            {
                remainingDistance = Mathf.Max(0f, updated);
            }

            float tick = Mathf.Max(0f, deltaTime);
            if (remainingDistance <= Mathf.Epsilon)
            {
                Succeed();
                return false;
            }

            if (!hasDirection || remainingDuration <= Mathf.Epsilon)
            {
                Succeed();
                return false;
            }

            float speed = remainingDistance / Mathf.Max(Mathf.Epsilon, remainingDuration);
            float stepDistance = Mathf.Min(remainingDistance, speed * tick);
            remainingDuration = Mathf.Max(0f, remainingDuration - tick);
            if (stepDistance <= Mathf.Epsilon)
            {
                // A zero delta time is not the end of the travel, it is a tick
                // that moved no clock. Ending here would drop the remainder.
                if (remainingDuration <= Mathf.Epsilon)
                {
                    Succeed();
                }

                return false;
            }

            remainingDistance -= stepDistance;
            delta = direction * stepDistance;
            TravelledDelta += delta;
            if (remainingDistance <= Mathf.Epsilon || remainingDuration <= Mathf.Epsilon)
            {
                Succeed();
            }

            return true;
        }

        /// <summary>
        /// Replaces the travel budget before the task starts. For a subclass
        /// that has to measure the world to know how far it is going, which it
        /// cannot do from a constructor argument.
        /// </summary>
        protected void SetMeasuredDistance(float distance)
        {
            remainingDistance = Mathf.Max(0f, distance);
        }

        /// <summary>
        /// Called by the system when the task lost the body under
        /// <see cref="AbilityMovementConflictPolicy.Abort"/>.
        /// </summary>
        internal void AbortForConflict()
        {
            Fail();
        }

        private bool ResolveDirectionNow()
        {
            if (!TryResolveDirection(out Vector3 resolved) ||
                resolved.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            direction = resolved.normalized;
            hasDirection = true;
            return true;
        }
    }
}
