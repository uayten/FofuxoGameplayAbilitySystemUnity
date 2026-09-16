using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// Turns Input System actions into activations, reading one <see
    /// cref="AbilityInputMap"/>. The only place the package touches input, and
    /// it lives in its own assembly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AbilitySystem))]
    public sealed class AbilityInputRouter : MonoBehaviour
    {
        [Tooltip("Which inputs activate which abilities. An asset, so a character swaps its controls the way it swaps its loadout.")]
        [SerializeField] private AbilityInputMap inputMap;
        [SerializeField] private Transform explicitTarget;
        [SerializeField, FormerlySerializedAs("findEnemyTargetWhenMissing")]
        private bool findFallbackTargetWhenMissing = true;
        [Tooltip("Seconds a rejected input is retried. Zero disables buffering.")]
        [SerializeField, Min(0f)] private float bufferWindow;

        private AbilitySystem abilitySystem;
        private readonly List<InputAction> subscribedActions = new();
        private readonly Dictionary<InputAction, AbilityInputEntry> boundActions = new();
        /// <summary>
        /// When each ability waiting on a held input was pressed. Only abilities
        /// whose policy is On Hold or On Release ever appear here, so an On Press
        /// binding costs nothing extra.
        /// </summary>
        private readonly Dictionary<AbilityDefinition, float> heldSince = new();
        private readonly List<AbilityDefinition> expiredHolds = new();
        private AbilityDefinition bufferedAbility;
        private AbilityContext bufferedContext;
        private float bufferExpiry;
        private bool hasBufferedInput;

        /// <summary>
        /// Per-instance hook used when no explicit target is set. Assign it
        /// from game code to resolve targets your own way (for example, the
        /// nearest enemy). Takes precedence over
        /// <see cref="GlobalFallbackTargetResolver"/>.
        /// </summary>
        public Func<GameObject> FallbackTargetResolver { get; set; }

        /// <summary>
        /// Game-wide hook used when no explicit target is set and the instance
        /// <see cref="FallbackTargetResolver"/> is null. Useful for single-target
        /// games that can resolve a default target globally.
        /// </summary>
        public static Func<GameObject> GlobalFallbackTargetResolver { get; set; }

        /// <summary>
        /// Fires for a map entry with no ability attached: the router owns the
        /// keybind (enable + performed) while the game owner keeps its own
        /// reaction — a combo it drives itself, a guard, a menu. That is what
        /// keeps every combat keybind in one asset even when what it does is not
        /// an ability. The string is the action name from the map.
        /// </summary>
        public event Action<string> ForwardedInput;

        /// <summary>
        /// Where the hold policies read the clock. EditMode has no advancing
        /// <c>Time.time</c>, so the tests drive it from here; nothing else
        /// should assign it.
        /// </summary>
        internal Func<float> TimeSource { get; set; }

        private float Now => TimeSource?.Invoke() ?? Time.time;

        private void Awake()
        {
            abilitySystem = GetComponent<AbilitySystem>();
        }

        /// <summary>
        /// Reports that the input bound to <paramref name="ability"/> went down.
        /// An On Press ability activates here; On Hold and On Release start
        /// their timer instead.
        ///
        /// Public because the router is not the only input owner: a game that
        /// reads its own devices drives the same policies through this pair
        /// rather than reimplementing them.
        /// </summary>
        /// <returns>True when the press activated the ability.</returns>
        public bool NotifyInputPressed(AbilityDefinition ability)
        {
            if (ability == null || abilitySystem == null)
            {
                return false;
            }

            // Tasks waiting on this edge hear it whatever the activation policy
            // does with it, so a step can wait for a press that activates
            // nothing — and so the wait tasks and the policies never disagree
            // about what a press was.
            abilitySystem.NotifyAbilityInput(AbilityInputEdge.Pressed, ability);
            if (ability.ActivationInputPolicy == AbilityActivationInputPolicy.OnPress)
            {
                TryActivateBinding(ability);
                return abilitySystem.ActiveAbility == ability;
            }

            heldSince[ability] = Now;
            return false;
        }

        /// <summary>
        /// Reports that the input bound to <paramref name="ability"/> came back
        /// up. An On Release ability activates here when it was held for at
        /// least its hold duration; a shorter press activates nothing.
        /// </summary>
        /// <returns>True when the release activated the ability.</returns>
        public bool NotifyInputReleased(AbilityDefinition ability)
        {
            if (ability == null || abilitySystem == null)
            {
                return false;
            }

            abilitySystem.NotifyAbilityInput(AbilityInputEdge.Released, ability);
            if (!heldSince.TryGetValue(ability, out float pressedAt))
            {
                return false;
            }

            heldSince.Remove(ability);
            if (ability.ActivationInputPolicy != AbilityActivationInputPolicy.OnRelease ||
                Now - pressedAt < ability.ActivationHoldDuration)
            {
                return false;
            }

            TryActivateBinding(ability);
            return abilitySystem.ActiveAbility == ability;
        }

        private void OnEnable()
        {
            if (inputMap == null)
            {
                return;
            }

            IReadOnlyList<AbilityInputEntry> entries = inputMap.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AbilityInputEntry entry = entries[i];
                InputAction action = inputMap.FindAction(entry.ActionName);
                if (action == null)
                {
                    Debug.LogWarning(
                        $"AbilityInputRouter on '{name}' could not find action " +
                        $"'{entry.ActionName}' in '{inputMap.name}'. It was renamed or " +
                        "removed, and nothing is bound to it.",
                        this);
                    continue;
                }

                // No other component enables the shared input asset, so the router
                // enables every action it subscribes to. Enabling is idempotent.
                action.Enable();
                Subscribe(action);
                boundActions[action] = entry;
            }
        }

        /// <summary>
        /// Every action is watched on all three edges. Which edge activates is
        /// the ability's own <see cref="AbilityActivationInputPolicy"/>, decided
        /// per press rather than per subscription, so one action bound to
        /// several abilities can mix policies.
        /// </summary>
        private void Subscribe(InputAction action)
        {
            action.started += OnAbilityInputStarted;
            action.performed += OnAbilityInputPerformed;
            action.canceled += OnAbilityInputCanceled;
            subscribedActions.Add(action);
        }

        private void OnDisable()
        {
            foreach (InputAction action in subscribedActions)
            {
                if (action != null)
                {
                    action.started -= OnAbilityInputStarted;
                    action.performed -= OnAbilityInputPerformed;
                    action.canceled -= OnAbilityInputCanceled;
                }
            }

            subscribedActions.Clear();
            boundActions.Clear();
            heldSince.Clear();
            expiredHolds.Clear();
            ClearBufferedInput();
        }

        private void Update()
        {
            TickHeldInputs();

            if (!hasBufferedInput || abilitySystem == null)
            {
                return;
            }

            if (Time.time > bufferExpiry)
            {
                ClearBufferedInput();
                return;
            }

            if (bufferedAbility != null &&
                abilitySystem.TryActivate(bufferedAbility, bufferedContext))
            {
                ClearBufferedInput();
            }
        }

        /// <summary>
        /// Activates an On Hold ability the moment its hold duration elapses,
        /// rather than waiting for the release: holding is the input, so the
        /// player should not have to let go to see it happen.
        /// </summary>
        internal void TickHeldInputs()
        {
            if (heldSince.Count == 0 || abilitySystem == null)
            {
                return;
            }

            float now = Now;
            expiredHolds.Clear();
            foreach (KeyValuePair<AbilityDefinition, float> entry in heldSince)
            {
                AbilityDefinition ability = entry.Key;
                if (ability.ActivationInputPolicy != AbilityActivationInputPolicy.OnHold ||
                    now - entry.Value < ability.ActivationHoldDuration)
                {
                    continue;
                }

                expiredHolds.Add(ability);
            }

            for (int i = 0; i < expiredHolds.Count; i++)
            {
                // Dropped before activating: an ability that fails here should
                // not retry on the next frame off the same press.
                heldSince.Remove(expiredHolds[i]);
                TryActivateBinding(expiredHolds[i]);
            }

            expiredHolds.Clear();
        }

        private void OnAbilityInputStarted(InputAction.CallbackContext inputContext)
        {
            // Only the waiting policies need the down edge. An On Press ability
            // activates from performed, as it always has, so a binding with an
            // interaction attached still fires where the interaction says.
            if (TryResolveBinding(inputContext.action, out AbilityDefinition ability, out _) &&
                ability != null &&
                ability.ActivationInputPolicy != AbilityActivationInputPolicy.OnPress)
            {
                NotifyInputPressed(ability);
            }
        }

        private void OnAbilityInputCanceled(InputAction.CallbackContext inputContext)
        {
            if (TryResolveBinding(inputContext.action, out AbilityDefinition ability, out _) &&
                ability != null)
            {
                NotifyInputReleased(ability);
            }
        }

        private void OnAbilityInputPerformed(InputAction.CallbackContext inputContext)
        {
            if (abilitySystem == null ||
                !TryResolveBinding(
                    inputContext.action, out AbilityDefinition ability, out string actionName))
            {
                return;
            }

            if (ability == null)
            {
                ForwardedInput?.Invoke(actionName);
                return;
            }

            // On Hold and On Release own their own edges. A button action also
            // reports performed on the press, and acting on it here would make
            // every policy behave like On Press.
            if (ability.ActivationInputPolicy != AbilityActivationInputPolicy.OnPress)
            {
                return;
            }

            TryActivateBinding(ability);
        }

        /// <summary>
        /// Finds what an action is bound to. An explicit binding wins over a
        /// named one, matching the order the router has always resolved in.
        /// </summary>
        /// <param name="ability">
        /// Null for a named binding with no ability, which the router forwards
        /// to the game owner instead of activating.
        /// </param>
        private bool TryResolveBinding(
            InputAction action, out AbilityDefinition ability, out string actionName)
        {
            if (boundActions.TryGetValue(action, out AbilityInputEntry entry))
            {
                ability = entry.Ability;
                actionName = entry.ActionName;
                return true;
            }

            ability = null;
            actionName = string.Empty;
            return false;
        }

        private void TryActivateBinding(AbilityDefinition ability)
        {
            if (ability == null)
            {
                return;
            }

            // Pressing the same button again while a manual combo is running is
            // a request for its next step, not a new activation.
            if (abilitySystem.ActiveAbility == ability &&
                ability is TimelineAbilityDefinition timeline &&
                timeline.StepAdvancement == AbilityStepAdvancement.Manual &&
                abilitySystem.TryQueueStepAdvance())
            {
                ClearBufferedInput();
                return;
            }

            // Targetless abilities (rolls, self novas) declare RequiresTarget
            // false precisely so no target is resolved for them: a fallback
            // target would only subject them to a meaningless range gate.
            GameObject target = ability.RequiresTarget ? ResolveTarget() : null;
            AbilityContext context = AbilityContext.FromTarget(gameObject, target);
            bool activated = abilitySystem.TryActivate(ability, context);
            if (!activated && bufferWindow > 0f)
            {
                bufferedAbility = ability;
                bufferedContext = context;
                bufferExpiry = Time.time + bufferWindow;
                hasBufferedInput = true;
            }
            else if (activated)
            {
                ClearBufferedInput();
            }
        }

        private void ClearBufferedInput()
        {
            bufferedAbility = null;
            bufferedContext = default;
            bufferExpiry = 0f;
            hasBufferedInput = false;
        }

        private GameObject ResolveTarget()
        {
            if (explicitTarget != null && explicitTarget.gameObject.activeInHierarchy)
            {
                return explicitTarget.gameObject;
            }

            if (!findFallbackTargetWhenMissing)
            {
                return null;
            }

            return FallbackTargetResolver?.Invoke() ?? GlobalFallbackTargetResolver?.Invoke();
        }
    }
}
