using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Fofuxo.GameplayAbilitySystem
{
    [Serializable]
    public sealed class AbilityInputBinding
    {
        [SerializeField] private InputActionReference action;
        [SerializeField] private AbilityDefinition ability;

        public InputActionReference Action => action;
        public AbilityDefinition Ability => ability;
    }

    [Serializable]
    public sealed class AbilityNamedInputBinding
    {
        [SerializeField] private string actionName;
        [SerializeField] private AbilityDefinition ability;
        [SerializeField] private AbilitySequenceDefinition sequence;

        public string ActionName => actionName ?? string.Empty;
        public AbilityDefinition Ability => ability;
        public AbilitySequenceDefinition Sequence => sequence;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AbilitySystem))]
    public sealed class AbilityInputRouter : MonoBehaviour
    {
        [SerializeField] private AbilityInputBinding[] bindings = { };
        [SerializeField] private InputActionAsset inputActionAsset;
        [SerializeField] private AbilityNamedInputBinding[] namedBindings = { };
        [SerializeField] private Transform explicitTarget;
        [SerializeField, FormerlySerializedAs("findEnemyTargetWhenMissing")]
        private bool findFallbackTargetWhenMissing = true;

        private AbilitySystem abilitySystem;
        private readonly List<InputAction> subscribedActions = new();
        private readonly Dictionary<InputAction, AbilityNamedInputBinding> namedLookup = new();

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

        private void Awake()
        {
            abilitySystem = GetComponent<AbilitySystem>();
        }

        private void OnEnable()
        {
            foreach (AbilityInputBinding binding in bindings)
            {
                InputAction action = binding?.Action?.action;
                if (action == null)
                {
                    continue;
                }

                // No other component enables the shared input asset, so the router
                // enables every action it subscribes to. Enabling is idempotent.
                action.Enable();
                action.performed += OnAbilityInputPerformed;
                subscribedActions.Add(action);
            }

            if (inputActionAsset != null)
            {
                foreach (AbilityNamedInputBinding binding in namedBindings)
                {
                    if (binding == null ||
                        string.IsNullOrWhiteSpace(binding.ActionName) ||
                        (binding.Ability == null && binding.Sequence == null))
                    {
                        continue;
                    }

                    InputAction action = inputActionAsset.FindAction(binding.ActionName, false);
                    if (action == null)
                    {
                        Debug.LogWarning(
                            $"AbilityInputRouter on '{name}' could not find action " +
                            $"'{binding.ActionName}'. Check the input asset and the binding name.",
                            this);
                        continue;
                    }

                    action.Enable();
                    action.performed += OnAbilityInputPerformed;
                    subscribedActions.Add(action);
                    namedLookup[action] = binding;
                }
            }
        }

        private void OnDisable()
        {
            foreach (InputAction action in subscribedActions)
            {
                if (action != null)
                {
                    action.performed -= OnAbilityInputPerformed;
                }
            }

            subscribedActions.Clear();
            namedLookup.Clear();
        }

        private void OnAbilityInputPerformed(InputAction.CallbackContext inputContext)
        {
            if (abilitySystem == null)
            {
                return;
            }

            foreach (AbilityInputBinding binding in bindings)
            {
                if (binding?.Action?.action != inputContext.action || binding.Ability == null)
                {
                    continue;
                }

                TryActivateBinding(binding.Ability, null);
                return;
            }

            if (namedLookup.TryGetValue(inputContext.action, out AbilityNamedInputBinding namedBinding) &&
                namedBinding != null)
            {
                TryActivateBinding(namedBinding.Ability, namedBinding.Sequence);
            }
        }

        private void TryActivateBinding(AbilityDefinition ability, AbilitySequenceDefinition sequence)
        {
            GameObject target = ResolveTarget();
            AbilityContext context = AbilityContext.FromTarget(gameObject, target);
            if (sequence != null)
            {
                abilitySystem.TryActivateSequence(sequence, context);
                return;
            }

            if (ability != null)
            {
                abilitySystem.TryActivate(ability, context);
            }
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
