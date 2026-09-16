using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// Inspector for the input map. The action of an entry is chosen from the
    /// actions the assigned <c>InputActionAsset</c> actually has, so the name is
    /// authored once from a list instead of typed — and when the asset renames an
    /// action afterwards, the entry says so in red rather than going quiet.
    /// </summary>
    [CustomEditor(typeof(AbilityInputMap))]
    public sealed class AbilityInputMapEditor : UnityEditor.Editor
    {
        private static readonly List<string> ActionNames = new();
        private static readonly List<GUIContent> ActionLabels = new();

        public override void OnInspectorGUI()
        {
            AbilityInputMap map = (AbilityInputMap)target;
            AssetNamingConvention.DrawViolationBox(map);

            serializedObject.Update();
            SerializedProperty asset = serializedObject.FindProperty("inputActionAsset");
            EditorGUILayout.PropertyField(asset, new GUIContent("Input Action Asset"));

            InputActionAsset actions = asset.objectReferenceValue as InputActionAsset;
            CollectActions(actions);

            EditorGUILayout.Space();
            SerializedProperty entries = serializedObject.FindProperty("entries");
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Bindings",
                    "An entry with no ability is still owned by the router: it enables " +
                    "the action and forwards it to game code."),
                EditorStyles.boldLabel);

            for (int i = 0; i < entries.arraySize; i++)
            {
                DrawEntry(entries.GetArrayElementAtIndex(i), entries, i, actions);
            }

            DrawAddRemove(entries, actions);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (map.TryValidate(out string error))
            {
                EditorGUILayout.HelpBox(
                    DescribeMap(map), MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }

        private static void DrawEntry(
            SerializedProperty entry,
            SerializedProperty entries,
            int index,
            InputActionAsset actions)
        {
            SerializedProperty actionName = entry.FindPropertyRelative("actionName");
            SerializedProperty ability = entry.FindPropertyRelative("ability");

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawActionField(actionName, actions);
                EditorGUILayout.PropertyField(ability, GUIContent.none);
                if (GUILayout.Button(
                        new GUIContent("−", "Remove this binding."),
                        EditorStyles.miniButton,
                        GUILayout.Width(22f)))
                {
                    entries.DeleteArrayElementAtIndex(index);
                    GUIUtility.ExitGUI();
                }
            }

            if (ability.objectReferenceValue == null)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(
                        $"forwarded to game code as \"{actionName.stringValue}\"",
                        EditorStyles.miniLabel);
                }
            }
        }

        /// <summary>
        /// A popup while the asset can answer what its actions are, and a plain
        /// text field when it cannot — an entry authored against an asset that is
        /// not assigned yet is still readable and still editable.
        /// </summary>
        private static void DrawActionField(SerializedProperty actionName, InputActionAsset actions)
        {
            if (actions == null)
            {
                EditorGUILayout.PropertyField(actionName, GUIContent.none);
                return;
            }

            int current = ActionNames.IndexOf(actionName.stringValue);
            int picked = EditorGUILayout.Popup(
                current,
                ActionLabels.ToArray(),
                GUILayout.MinWidth(90f));
            if (picked != current && picked >= 0 && picked < ActionNames.Count)
            {
                actionName.stringValue = ActionNames[picked];
            }

            if (current < 0 && !string.IsNullOrEmpty(actionName.stringValue))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        $"! {actionName.stringValue}",
                        "This action is not in the asset any more."),
                    EditorStyles.miniLabel,
                    GUILayout.Width(110f));
            }
        }

        private static void DrawAddRemove(SerializedProperty entries, InputActionAsset actions)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(actions == null))
                {
                    if (GUILayout.Button("Add binding", GUILayout.Width(110f)))
                    {
                        int index = entries.arraySize;
                        entries.InsertArrayElementAtIndex(index);
                        SerializedProperty added = entries.GetArrayElementAtIndex(index);
                        added.FindPropertyRelative("actionName").stringValue =
                            FirstUnboundAction(entries, index);
                        added.FindPropertyRelative("ability").objectReferenceValue = null;
                    }
                }
            }
        }

        /// <summary>
        /// The first action of the asset that no other entry already claims, so
        /// adding several bindings in a row does not create duplicates that
        /// validation would then refuse.
        /// </summary>
        private static string FirstUnboundAction(SerializedProperty entries, int ignoredIndex)
        {
            for (int i = 0; i < ActionNames.Count; i++)
            {
                bool taken = false;
                for (int e = 0; e < entries.arraySize && !taken; e++)
                {
                    if (e == ignoredIndex)
                    {
                        continue;
                    }

                    taken = entries.GetArrayElementAtIndex(e)
                        .FindPropertyRelative("actionName").stringValue == ActionNames[i];
                }

                if (!taken)
                {
                    return ActionNames[i];
                }
            }

            return ActionNames.Count > 0 ? ActionNames[0] : string.Empty;
        }

        private static void CollectActions(InputActionAsset actions)
        {
            ActionNames.Clear();
            ActionLabels.Clear();
            if (actions == null)
            {
                return;
            }

            foreach (InputActionMap actionMap in actions.actionMaps)
            {
                foreach (InputAction action in actionMap.actions)
                {
                    ActionNames.Add(action.name);
                    // The map name is context, not part of the stored value: two
                    // maps may both have an action called Attack.
                    ActionLabels.Add(new GUIContent($"{actionMap.name}/{action.name}"));
                }
            }
        }

        private static string DescribeMap(AbilityInputMap map)
        {
            int activating = 0;
            int forwarded = 0;
            IReadOnlyList<AbilityInputEntry> entries = map.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsForwarded)
                {
                    forwarded++;
                }
                else
                {
                    activating++;
                }
            }

            return entries.Count == 0
                ? "No bindings yet, so this map activates nothing."
                : $"Valid: {activating} ability binding(s), {forwarded} forwarded to game code.";
        }
    }
}
