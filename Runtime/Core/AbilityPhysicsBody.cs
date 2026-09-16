using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Whether a physics push replaces the body's velocity or adds to what it
    /// already had.
    /// </summary>
    public enum PhysicsForceApplication
    {
        ReplaceVelocity,
        Accumulate
    }

    /// <summary>
    /// One generic handoff between ability-driven actors and Unity physics.
    /// Force effects provide a velocity change; this component owns the
    /// dynamic Rigidbody lifetime and completes the target's reaction ability
    /// after landing, settling, or reaching its safety timeout.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(AbilitySystem))]
    public sealed class AbilityPhysicsBody : MonoBehaviour
    {
        private const int GroundHitCapacity = 8;
        private static readonly RaycastHit[] GroundHits = new RaycastHit[GroundHitCapacity];

        [Header("Reaction Lifetime")]
        [SerializeField, Min(0f)] private float minimumControlDuration = 0.1f;
        [SerializeField, Min(0.1f)] private float maximumControlDuration = 8f;
        [SerializeField, Min(0f)] private float settleSpeed = 0.2f;
        [SerializeField, Min(1)] private int settleFixedFrames = 3;
        [SerializeField, Min(0f)] private float controlledLinearDamping = 2f;
        [SerializeField, Min(0f)] private float controlledAngularDamping = 2f;

        [Header("Grounding")]
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.15f;
        [SerializeField, Range(0f, 1f)] private float minimumGroundNormal = 0.5f;

        private Rigidbody body;
        private AbilitySystem abilitySystem;
        private Collider bodyCollider;
        private AbilityDefinition reactionAbility;
        private float elapsedControlTime;
        private int settledFrames;
        private bool originalIsKinematic;
        private bool originalUseGravity;
        private CollisionDetectionMode originalCollisionDetectionMode;
        private float originalLinearDamping;
        private float originalAngularDamping;

        public bool IsActive { get; private set; }
        public Rigidbody Body
        {
            get
            {
                EnsureReferences();
                return body;
            }
        }
        public AbilitySystem AbilitySystem
        {
            get
            {
                EnsureReferences();
                return abilitySystem;
            }
        }

        /// <summary>Fires before a kinematic body is handed to Unity physics.</summary>
        public event Action PhysicsControlStarted;
        /// <summary>Fires after the original Rigidbody state is restored.</summary>
        public event Action PhysicsControlEnded;

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnEnable()
        {
            EnsureReferences();
            if (abilitySystem == null)
            {
                return;
            }

            abilitySystem.AbilityCompleted += OnAbilityCompleted;
            abilitySystem.AbilityCancelled += OnAbilityCancelled;
        }

        private void OnDisable()
        {
            if (abilitySystem != null)
            {
                abilitySystem.AbilityCompleted -= OnAbilityCompleted;
                abilitySystem.AbilityCancelled -= OnAbilityCancelled;
            }

            EndPhysicsControl(completeReactionAbility: true);
        }

        private void OnDestroy()
        {
            EndPhysicsControl(completeReactionAbility: true);
        }

        private void FixedUpdate()
        {
            if (!IsActive)
            {
                return;
            }

            TickPhysicsControl(Time.fixedDeltaTime, IsGrounded());
        }

        public bool ApplyVelocityChange(
            Vector3 velocityChange,
            AbilityDefinition activeReactionAbility,
            PhysicsForceApplication application)
        {
            EnsureReferences();
            if (body == null || activeReactionAbility == null)
            {
                return false;
            }

            if (!IsActive)
            {
                BeginPhysicsControl(activeReactionAbility);
            }
            else if (reactionAbility != activeReactionAbility)
            {
                EndPhysicsControl(completeReactionAbility: false);
                BeginPhysicsControl(activeReactionAbility);
            }

            elapsedControlTime = 0f;
            settledFrames = 0;

            Vector3 impulse = velocityChange;
            if (application == PhysicsForceApplication.ReplaceVelocity)
            {
                impulse -= body.linearVelocity;
            }

            body.WakeUp();
            body.AddForce(impulse, ForceMode.VelocityChange);
            return true;
        }

        private void BeginPhysicsControl(AbilityDefinition activeReactionAbility)
        {
            reactionAbility = activeReactionAbility;
            elapsedControlTime = 0f;
            settledFrames = 0;
            originalIsKinematic = body.isKinematic;
            originalUseGravity = body.useGravity;
            originalCollisionDetectionMode = body.collisionDetectionMode;
            originalLinearDamping = body.linearDamping;
            originalAngularDamping = body.angularDamping;

            IsActive = true;
            PhysicsControlStarted?.Invoke();

            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = controlledLinearDamping;
            body.angularDamping = controlledAngularDamping;
        }

        private void TickPhysicsControl(float deltaTime, bool isGrounded)
        {
            if (!IsActive)
            {
                return;
            }

            elapsedControlTime += Mathf.Max(0f, deltaTime);
            bool isSettled = body.linearVelocity.sqrMagnitude <= settleSpeed * settleSpeed;
            settledFrames = isGrounded && isSettled ? settledFrames + 1 : 0;

            bool reachedMinimumAndSettled =
                elapsedControlTime >= minimumControlDuration &&
                settledFrames >= settleFixedFrames;
            if (reachedMinimumAndSettled || elapsedControlTime >= maximumControlDuration)
            {
                EndPhysicsControl(completeReactionAbility: true);
            }
        }

        private bool IsGrounded()
        {
            Bounds bounds = bodyCollider != null
                ? bodyCollider.bounds
                : new Bounds(transform.position, Vector3.one * 0.1f);
            Vector3 origin = bounds.center;
            float distance = bounds.extents.y + groundProbeDistance;
            int layerMask = groundLayers.value == 0
                ? Physics.DefaultRaycastLayers
                : groundLayers.value;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = GroundHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (Vector3.Dot(hit.normal, Vector3.up) >= minimumGroundNormal)
                {
                    return true;
                }
            }

            return false;
        }

        private void EndPhysicsControl(bool completeReactionAbility)
        {
            if (!IsActive)
            {
                return;
            }

            AbilityDefinition completedReaction = reactionAbility;
            IsActive = false;
            reactionAbility = null;
            elapsedControlTime = 0f;
            settledFrames = 0;

            if (body != null)
            {
                if (originalIsKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.useGravity = originalUseGravity;
                body.collisionDetectionMode = originalCollisionDetectionMode;
                body.linearDamping = originalLinearDamping;
                body.angularDamping = originalAngularDamping;
                body.isKinematic = originalIsKinematic;
            }

            PhysicsControlEnded?.Invoke();

            if (completeReactionAbility)
            {
                abilitySystem?.TryCompleteActiveAbility(completedReaction);
            }
        }

        private void OnAbilityCompleted(AbilityDefinition ability)
        {
            if (IsActive && ability == reactionAbility)
            {
                EndPhysicsControl(completeReactionAbility: false);
            }
        }

        private void OnAbilityCancelled(
            AbilityDefinition ability,
            GameplayTag cancelTag)
        {
            if (IsActive && ability == reactionAbility)
            {
                EndPhysicsControl(completeReactionAbility: false);
            }
        }

        private void OnValidate()
        {
            minimumControlDuration = Mathf.Max(0f, minimumControlDuration);
            maximumControlDuration = Mathf.Max(
                Mathf.Max(0.1f, minimumControlDuration),
                maximumControlDuration);
            settleSpeed = Mathf.Max(0f, settleSpeed);
            settleFixedFrames = Mathf.Max(1, settleFixedFrames);
            controlledLinearDamping = Mathf.Max(0f, controlledLinearDamping);
            controlledAngularDamping = Mathf.Max(0f, controlledAngularDamping);
            groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        }

        private void EnsureReferences()
        {
            body ??= GetComponent<Rigidbody>();
            abilitySystem ??= GetComponent<AbilitySystem>();
            bodyCollider ??= GetComponent<Collider>();
        }

        internal void ConfigureForTests(
            float minimumDuration,
            float maximumDuration,
            float speed,
            int fixedFrames)
        {
            minimumControlDuration = minimumDuration;
            maximumControlDuration = maximumDuration;
            settleSpeed = speed;
            settleFixedFrames = fixedFrames;
        }

        internal void TickForTests(float deltaTime, bool isGrounded)
        {
            TickPhysicsControl(deltaTime, isGrounded);
        }
    }
}
