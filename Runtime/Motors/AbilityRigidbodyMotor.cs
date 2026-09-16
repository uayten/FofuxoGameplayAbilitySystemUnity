using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Moves a kinematic-style Rigidbody with <c>MovePosition</c>, the way root
    /// motion does: velocity is never touched, because the owner own motor owns
    /// velocity and the ability only adds travel.
    ///
    /// Deltas are accumulated and flushed once per physics step. Issuing several
    /// <c>MovePosition</c> calls between two steps keeps only the last, so a
    /// component that flushed on the spot would silently under-travel at frame
    /// rates above the physics rate. Accumulating is what makes the total
    /// distance the same whatever the frame rate.
    /// </summary>
    [AddComponentMenu("Fofuxo/Ability Rigidbody Motor")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AbilityRigidbodyMotor : AbilityMotorBehaviour
    {
        [Tooltip("Skin left between the body and a blocker on swept travel.")]
        [SerializeField, Min(0f)] private float sweepSkin = 0.01f;

        private Rigidbody body;
        private Vector3 pendingDelta;

        public Rigidbody Body
        {
            get
            {
                if (body == null)
                {
                    body = GetComponent<Rigidbody>();
                }

                return body;
            }
        }

        public override Vector3 Position => Body != null ? Body.position : transform.position;

        public override Vector3 Move(Vector3 delta, AbilityMovementCollision collision)
        {
            if (Body == null || delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            Vector3 applied = collision == AbilityMovementCollision.Swept
                ? ClampToFirstBlocker(delta)
                : delta;
            pendingDelta += applied;
            return applied;
        }

        private void FixedUpdate()
        {
            if (pendingDelta.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Body.MovePosition(Body.position + pendingDelta);
            pendingDelta = Vector3.zero;
        }

        /// <summary>
        /// Shortens the delta to stop in front of whatever the body would sweep
        /// into. Nothing in the way leaves it untouched, which is why swept and
        /// unswept travel agree in open space.
        /// </summary>
        private Vector3 ClampToFirstBlocker(Vector3 delta)
        {
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return Vector3.zero;
            }

            Vector3 direction = delta / distance;
            if (!Body.SweepTest(direction, out RaycastHit hit, distance, QueryTriggerInteraction.Ignore))
            {
                return delta;
            }

            return direction * Mathf.Max(0f, hit.distance - sweepSkin);
        }
    }
}
