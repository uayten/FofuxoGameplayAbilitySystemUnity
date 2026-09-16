using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// Everything the sample's two actors share: locomotion the ability system
    /// is allowed to lock, the attributes a hit changes, and the two decisions
    /// the package deliberately leaves to a game - what happens when health
    /// reaches zero, and what a broken poise meter means.
    /// </summary>
    [RequireComponent(typeof(AbilitySystem))]
    [RequireComponent(typeof(AttributeSet))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SampleFighter : MonoBehaviour
    {
        /// <summary>Health, the attribute the sample's damage effects subtract from.</summary>
        public static readonly GameplayAttribute Health = new("Combat.Health");

        /// <summary>Poise: a second damage attribute, and the meter that floors an actor.</summary>
        public static readonly GameplayAttribute Poise = new("Combat.Poise");

        [Tooltip("Metres per second while the actor is free to move.")]
        [SerializeField, Min(0f)] private float moveSpeed = 4f;

        [Tooltip("Degrees per second the actor turns towards where it is going.")]
        [SerializeField, Min(0f)] private float turnSpeed = 720f;

        [Tooltip("Event sent to this actor's own ability system when its poise breaks.")]
        [SerializeField] private GameplayTag poiseBreakEvent = new("Event.Knockdown");

        [Tooltip("Poise restored per second once it has broken.")]
        [SerializeField, Min(0f)] private float poiseRecoveryPerSecond = 10f;

        private AbilitySystem system;
        private AttributeSet attributes;
        private Rigidbody body;
        private float maximumPoise;

        /// <summary>The actor's ability runtime.</summary>
        public AbilitySystem System => system;

        /// <summary>The actor's attributes.</summary>
        public AttributeSet Attributes => attributes;

        /// <summary>True once health has reached zero. A dead fighter stops deciding.</summary>
        public bool IsDead { get; private set; }

        private void Awake()
        {
            system = GetComponent<AbilitySystem>();
            attributes = GetComponent<AttributeSet>();
            body = GetComponent<Rigidbody>();
            body.freezeRotation = true;
            maximumPoise = attributes.GetCurrent(Poise);
            attributes.Changed += OnAttributeChanged;
        }

        private void OnDestroy()
        {
            if (attributes != null)
            {
                attributes.Changed -= OnAttributeChanged;
            }
        }

        private void Update()
        {
            if (IsDead || maximumPoise <= 0f)
            {
                return;
            }

            float poise = attributes.GetCurrent(Poise);
            if (poise < maximumPoise)
            {
                attributes.ApplyInstantModifier(new AttributeModifier(
                    Poise,
                    AttributeOperation.Add,
                    Mathf.Min(poiseRecoveryPerSecond * Time.deltaTime, maximumPoise - poise),
                    this));
            }
        }

        /// <summary>
        /// Moves the actor on a flat plane, unless an ability says otherwise.
        /// <see cref="AbilitySystem.IsMovementLocked"/> is the whole handshake:
        /// the game owns locomotion and asks, the ability system never drives
        /// the body except through its own displacement tasks.
        /// </summary>
        public void Move(Vector3 direction)
        {
            if (IsDead || system.IsMovementLocked)
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            body.MovePosition(body.position + direction * (moveSpeed * Time.deltaTime));
            Face(direction);
        }

        /// <summary>Turns the actor towards a direction without moving it.</summary>
        public void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction),
                turnSpeed * Time.deltaTime);
        }

        private void OnAttributeChanged(AttributeValueChanged change)
        {
            if (change.Attribute == Health && change.NewValue <= 0f && !IsDead)
            {
                Die();
                return;
            }

            // Poise is an attribute like any other; that it means "can be
            // floored" is this game's rule, and it lives here rather than in a
            // package field. The hit that broke it asked for nothing - its
            // damage effect sends no reaction event - so the actor asks itself.
            if (change.Attribute == Poise && change.NewValue <= 0f && !IsDead)
            {
                attributes.ApplyInstantModifier(new AttributeModifier(
                    Poise, AttributeOperation.Override, maximumPoise, this));
                system.TryHandleGameplayEvent(
                    poiseBreakEvent,
                    AbilityContext.FromTarget(gameObject, null),
                    out _);
            }
        }

        private void Die()
        {
            IsDead = true;
            system.SetLooseTag(CommonGameplayTags.Dead, true);
            system.ForceCancelActiveAbility(CommonGameplayTags.CancelOwnerTeardown);
        }
    }
}
