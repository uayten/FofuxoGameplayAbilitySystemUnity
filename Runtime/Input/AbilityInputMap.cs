using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// One action of an <see cref="InputActionAsset"/>, and what it activates.
    /// An entry with no ability is a keybind the router still owns — it enables
    /// the action and watches its edges — but hands to game code through
    /// <c>AbilityInputRouter.ForwardedInput</c>. That is what keeps every combat
    /// keybind in one place even when the reaction is not an ability.
    /// </summary>
    [Serializable]
    public struct AbilityInputEntry
    {
        [Tooltip("Action in the map's Input Action Asset. Picked from a list in the Inspector, so a renamed action fails validation instead of going quiet.")]
        [SerializeField] private string actionName;
        [Tooltip("Ability this action activates. Leave empty to forward the input to game code instead.")]
        [SerializeField] private AbilityDefinition ability;

        public AbilityInputEntry(string actionName, AbilityDefinition ability)
        {
            this.actionName = actionName;
            this.ability = ability;
        }

        public string ActionName => actionName ?? string.Empty;
        public AbilityDefinition Ability => ability;

        /// <summary>True when the router activates nothing and the game reacts instead.</summary>
        public bool IsForwarded => ability == null;

        public override string ToString() =>
            $"{ActionName} -> {(ability == null ? "(forwarded)" : ability.name)}";
    }

    /// <summary>
    /// Which inputs activate which abilities, as an asset rather than a list
    /// buried in a prefab. A character swaps its controls the way it swaps its
    /// <see cref="AbilityLoadout"/>, two characters share one scheme by pointing
    /// at the same map, and the prefab shows a single field instead of an array
    /// that has to be read entry by entry.
    ///
    /// The map names actions rather than referencing them: an
    /// <c>InputActionReference</c> is a sub-asset to drag per action, while a
    /// name picked from the asset's own list is chosen once and validated
    /// afterwards. A renamed action becomes a validation error here, which is
    /// the failure the string form is usually accused of hiding.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GIM_NewInputMap",
        menuName = "Fofuxo/Abilities/Input Map")]
    public sealed class AbilityInputMap : ScriptableObject
    {
        [Tooltip("The asset the action names below are resolved against.")]
        [SerializeField] private InputActionAsset inputActionAsset;
        [SerializeField] private AbilityInputEntry[] entries = { };

        public InputActionAsset InputActionAsset => inputActionAsset;

        public IReadOnlyList<AbilityInputEntry> Entries =>
            entries ?? Array.Empty<AbilityInputEntry>();

        /// <summary>
        /// Resolves an entry's action against the asset. Null means the name no
        /// longer matches anything, which is what validation reports.
        /// </summary>
        public InputAction FindAction(string actionName)
        {
            return inputActionAsset == null || string.IsNullOrWhiteSpace(actionName)
                ? null
                : inputActionAsset.FindAction(actionName, false);
        }

        public bool TryValidate(out string error)
        {
            if (inputActionAsset == null)
            {
                error = "No Input Action Asset assigned, so no action name can resolve.";
                return false;
            }

            IReadOnlyList<AbilityInputEntry> map = Entries;
            for (int i = 0; i < map.Count; i++)
            {
                AbilityInputEntry entry = map[i];
                if (string.IsNullOrWhiteSpace(entry.ActionName))
                {
                    error = $"Entry {i + 1} has no action selected.";
                    return false;
                }

                if (FindAction(entry.ActionName) == null)
                {
                    error =
                        $"Entry {i + 1}: '{entry.ActionName}' is not an action of " +
                        $"'{inputActionAsset.name}'. It was renamed or removed.";
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(
                            map[j].ActionName, entry.ActionName, StringComparison.Ordinal))
                    {
                        error =
                            $"Entry {i + 1}: '{entry.ActionName}' is bound twice. One " +
                            "action drives one ability, or is forwarded once.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        internal void ConfigureForTests(InputActionAsset asset, params AbilityInputEntry[] map)
        {
            inputActionAsset = asset;
            entries = map ?? Array.Empty<AbilityInputEntry>();
        }
    }
}
