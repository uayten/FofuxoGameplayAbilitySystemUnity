using UnityEngine;
using UnityEngine.InputSystem;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// The player half of the sample: locomotion read from one input action,
    /// and nothing else. Attack, roll and block are not here on purpose - they
    /// are bindings in the <see cref="AbilityInputMap"/> that
    /// <see cref="AbilityInputRouter"/> reads, which is the whole point of that
    /// asset. A controller that activated abilities by name would be the
    /// coupling the package exists to avoid.
    /// </summary>
    [RequireComponent(typeof(SampleFighter))]
    public sealed class SamplePlayer : MonoBehaviour
    {
        [Tooltip("The map whose action asset the movement action is read from.")]
        [SerializeField] private AbilityInputMap inputMap;

        [Tooltip("Name of the Vector2 action that moves the fighter.")]
        [SerializeField] private string moveActionName = "Move";

        [Tooltip("Movement is read in this transform's space. Empty uses the world axes.")]
        [SerializeField] private Transform cameraSpace;

        private SampleFighter fighter;
        private InputAction move;

        private void Awake()
        {
            fighter = GetComponent<SampleFighter>();
            move = inputMap != null ? inputMap.FindAction(moveActionName) : null;
            if (move == null)
            {
                Debug.LogWarning(
                    $"'{moveActionName}' is not in the input map; the sample player will not move.",
                    this);
            }
        }

        private void OnEnable()
        {
            move?.Enable();
        }

        private void OnDisable()
        {
            move?.Disable();
        }

        private void Update()
        {
            if (move == null)
            {
                return;
            }

            Vector2 input = move.ReadValue<Vector2>();
            Vector3 forward = cameraSpace != null ? cameraSpace.forward : Vector3.forward;
            Vector3 right = cameraSpace != null ? cameraSpace.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            fighter.Move(forward.normalized * input.y + right.normalized * input.x);
        }
    }
}
