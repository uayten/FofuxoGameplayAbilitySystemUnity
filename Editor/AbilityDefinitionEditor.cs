using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AbilityDefinition), true)]
public sealed class AbilityDefinitionEditor : Editor
{
    private bool showDamageBox = true;
    private bool showAdvanced;
    private Editor previewClipEditor;

    private void OnDisable()
    {
        if (previewClipEditor != null)
        {
            DestroyImmediate(previewClipEditor);
            previewClipEditor = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        AbilityDefinition ability = (AbilityDefinition)target;
        if (TryFindDamageTrigger(out SerializedProperty damageTrigger))
        {
            DrawAttackInspector(ability, damageTrigger);
        }
        else
        {
            DrawDefaultInspector();
        }

        serializedObject.ApplyModifiedProperties();
        DrawAnimationPreview(ability);
        DrawResolvedTimeline(ability);
        DrawValidation(ability);

        if (!TryFindDamageTrigger(out _))
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ability))))
            {
                if (GUILayout.Button("Add Embedded Box Damage Effect"))
                {
                    AddEmbeddedBoxEffect(ability);
                }
            }
        }
    }

    private void DrawAttackInspector(
        AbilityDefinition ability,
        SerializedProperty damageTrigger)
    {
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        DrawProperty("abilityId");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        DrawProperty("animationClip");
        DrawProperty("animatorStateName");
        DrawProperty("animationBlendDuration");
        DrawProperty("previewAnimationClip", "Preview Clip");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combat Frames", EditorStyles.boldLabel);
        SerializedProperty damageFrame = damageTrigger.FindPropertyRelative("frame");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            damageFrame,
            new GUIContent(
                "Damage Frame",
                "The oriented damage query executes once on this 1-based frame."));
        if (EditorGUI.EndChangeCheck())
        {
            int frame = Mathf.Max(1, damageFrame.intValue);
            damageFrame.intValue = frame;
            serializedObject.FindProperty("startupEndFrame").intValue = frame - 1;
            serializedObject.FindProperty("activeEndFrame").intValue = frame;
        }

        DrawProperty("movementUnlockFrame", "Movement Unlock Frame");
        DrawProperty("comboContinuationFrame", "Combo Continue Frame");
        DrawProperty("comboInputEndFrame", "Combo Input End Frame");
        EditorGUILayout.HelpBox(
            "An Attack press before Combo Continue Frame is buffered. It starts " +
            "the next step when that frame arrives, unless pressed after Combo " +
            "Input End Frame.",
            MessageType.Info);

        SerializedProperty effectReference = damageTrigger.FindPropertyRelative("effect");
        AbilityEffectDefinition effect =
            effectReference.objectReferenceValue as AbilityEffectDefinition;
        EditorGUILayout.Space();
        string damageLabel = effect is BoxDamageEffectDefinition
            ? "Damage Box"
            : "Damage Query";
        showDamageBox = EditorGUILayout.Foldout(showDamageBox, damageLabel, true);
        if (showDamageBox)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(effectReference, new GUIContent("Effect"));
                DrawEmbeddedEffect(effect);
            }
        }

        EditorGUILayout.Space();
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
        if (showAdvanced)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DrawProperty("cooldown");
                DrawProperty("cooldownStartPolicy");
                DrawProperty("recoveryEndFrame", "Ability End Frame");
                DrawProperty("costs", includeChildren: true);
                DrawProperty("maxCharges");
                DrawProperty("chargeRestoreTime");
                DrawProperty("allowedCancellation");
                DrawProperty("lockMovementDuringAbility");
                DrawProperty("requiredTags", includeChildren: true);
                DrawProperty("blockedTags", includeChildren: true);
                DrawProperty("grantedTags", includeChildren: true);
                DrawProperty("effectTriggers", includeChildren: true);
                DrawProperty("cueTriggers", includeChildren: true);
                DrawProperty("baseAiWeight");
            }
        }
    }

    private void DrawProperty(
        string propertyName,
        string displayName = null,
        bool includeChildren = false)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        GUIContent label = string.IsNullOrEmpty(displayName)
            ? null
            : new GUIContent(displayName);
        if (label == null)
        {
            EditorGUILayout.PropertyField(property, includeChildren);
        }
        else
        {
            EditorGUILayout.PropertyField(property, label, includeChildren);
        }
    }

    private bool TryFindDamageTrigger(out SerializedProperty damageTrigger)
    {
        SerializedProperty triggers = serializedObject.FindProperty("effectTriggers");
        if (triggers != null)
        {
            for (int i = 0; i < triggers.arraySize; i++)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
                Object effect = trigger.FindPropertyRelative("effect").objectReferenceValue;
                if (effect is MeleeDamageEffectDefinition ||
                    effect is BoxDamageEffectDefinition ||
                    effect is CapsuleDamageEffectDefinition)
                {
                    damageTrigger = trigger;
                    return true;
                }
            }
        }

        damageTrigger = null;
        return false;
    }

    private static void DrawEmbeddedEffect(AbilityEffectDefinition effect)
    {
        if (effect == null)
        {
            return;
        }

        SerializedObject serializedEffect = new(effect);
        serializedEffect.Update();
        SerializedProperty property = serializedEffect.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script")
            {
                continue;
            }

            EditorGUILayout.PropertyField(property, true);
        }

        if (serializedEffect.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(effect);
        }
    }

    private void DrawAnimationPreview(AbilityDefinition ability)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation Preview", EditorStyles.boldLabel);
        if (targets.Length > 1)
        {
            EditorGUILayout.HelpBox(
                "Select a single ability to preview its animation.",
                MessageType.Info);
            return;
        }

        AnimationClip clip = ability != null ? ability.PreviewClip : null;
        if (clip == null)
        {
            EditorGUILayout.HelpBox(
                "Assign an Animation Clip (or a Preview Clip override) to preview it here.",
                MessageType.Info);
            return;
        }

        Editor.CreateCachedEditor(clip, null, ref previewClipEditor);
        if (previewClipEditor == null)
        {
            return;
        }

        previewClipEditor.OnPreviewSettings();
        Rect previewRect = GUILayoutUtility.GetRect(
            GUIContent.none,
            GUIStyle.none,
            GUILayout.Height(240f),
            GUILayout.ExpandWidth(true));
        previewClipEditor.OnInteractivePreviewGUI(previewRect, GUIStyle.none);
    }

    private static void DrawResolvedTimeline(AbilityDefinition ability)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Resolved Timeline", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Frame Rate", ability.FrameRate.ToString("0.###"));
        EditorGUILayout.LabelField("Duration", $"{ability.Duration:0.###} s");
        EditorGUILayout.LabelField(
            "Phases",
            $"Startup 1-{ability.StartupEndFrame}, " +
            $"Active {ability.StartupEndFrame + 1}-{ability.ActiveEndFrame}, " +
            $"Recovery {ability.ActiveEndFrame + 1}-{ability.RecoveryEndFrame}");
    }

    private static void DrawValidation(AbilityDefinition ability)
    {
        if (ability.TryValidate(out string validationError))
        {
            EditorGUILayout.HelpBox("Ability configuration is valid.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(validationError, MessageType.Error);
        }
    }

    private static void AddEmbeddedBoxEffect(AbilityDefinition ability)
    {
        BoxDamageEffectDefinition effect = CreateInstance<BoxDamageEffectDefinition>();
        effect.name = $"{ability.name}_BoxDamage";
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
