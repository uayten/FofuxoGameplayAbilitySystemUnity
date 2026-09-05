using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AbilityDefinition))]
public sealed class AbilityDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        AbilityDefinition ability = (AbilityDefinition)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Resolved Timeline", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Frame Rate", ability.FrameRate.ToString("0.###"));
        EditorGUILayout.LabelField("Duration", $"{ability.Duration:0.###} s");
        EditorGUILayout.LabelField(
            "Phases",
            $"Startup 1-{ability.StartupEndFrame}, " +
            $"Active {ability.StartupEndFrame + 1}-{ability.ActiveEndFrame}, " +
            $"Recovery {ability.ActiveEndFrame + 1}-{ability.RecoveryEndFrame}");

        if (ability.TryValidate(out string validationError))
        {
            EditorGUILayout.HelpBox("Ability configuration is valid.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(validationError, MessageType.Error);
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ability))))
        {
            if (GUILayout.Button("Add Embedded Melee Damage Effect"))
            {
                AddEmbeddedMeleeEffect(ability);
            }
        }
    }

    private static void AddEmbeddedMeleeEffect(AbilityDefinition ability)
    {
        MeleeDamageEffectDefinition effect =
            CreateInstance<MeleeDamageEffectDefinition>();
        effect.name = $"{ability.name}_MeleeDamage";
        effect.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(effect, ability);

        SerializedObject serializedAbility = new(ability);
        SerializedProperty triggers = serializedAbility.FindProperty("effectTriggers");
        int newIndex = triggers.arraySize;
        triggers.InsertArrayElementAtIndex(newIndex);
        SerializedProperty trigger = triggers.GetArrayElementAtIndex(newIndex);
        trigger.FindPropertyRelative("frame").intValue =
            Mathf.Max(1, ability.StartupEndFrame + 1);
        trigger.FindPropertyRelative("effect").objectReferenceValue = effect;
        serializedAbility.ApplyModifiedProperties();

        EditorUtility.SetDirty(effect);
        EditorUtility.SetDirty(ability);
        AssetDatabase.SaveAssets();
    }
}
