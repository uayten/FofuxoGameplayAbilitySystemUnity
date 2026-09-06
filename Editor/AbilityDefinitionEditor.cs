using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AbilityDefinition), true)]
public sealed class AbilityDefinitionEditor : Editor
{
    private bool showDamageBox = true;
    private bool showAdvanced;

    // Never inline-initialized: Unity can restore inspector editors via
    // deserialization without running field initializers, which leaves an
    // inline-created helper null forever on the live instance.
    private AbilityAnimationPreview animationPreview;

    private void OnEnable()
    {
        animationPreview ??= new AbilityAnimationPreview();
    }

    private void OnDisable()
    {
        if (animationPreview != null)
        {
            animationPreview.Dispose();
            animationPreview = null;
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

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        DrawProperty("previewAnimationClip", "Preview Clip");
        DrawProperty("previewModel", "Preview Model");

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

    public override bool HasPreviewGUI()
    {
        return targets.Length == 1 &&
            target is AbilityDefinition ability &&
            ability.PreviewClip != null;
    }

    public override GUIContent GetPreviewTitle()
    {
        if (target is AbilityDefinition ability && ability.PreviewClip != null)
        {
            return new GUIContent($"{ability.PreviewClip.name} (Ability Preview)");
        }

        return base.GetPreviewTitle();
    }

    public override void OnPreviewSettings()
    {
        if (target is not AbilityDefinition ability || ability.PreviewClip == null)
        {
            return;
        }

        animationPreview ??= new AbilityAnimationPreview();
        animationPreview.DrawSettings(ability.PreviewClip, ability.PreviewModel);
    }

    public override void OnInteractivePreviewGUI(Rect previewRect, GUIStyle background)
    {
        if (target is not AbilityDefinition ability || ability.PreviewClip == null)
        {
            return;
        }

        animationPreview ??= new AbilityAnimationPreview();
        if (animationPreview.DrawViewport(
            previewRect,
            ability.PreviewClip,
            ability.PreviewModel,
            GetDamageFrame()))
        {
            Repaint();
        }
    }

    private int GetDamageFrame()
    {
        if (TryFindDamageTrigger(out SerializedProperty damageTrigger))
        {
            SerializedProperty frame = damageTrigger.FindPropertyRelative("frame");
            if (frame != null)
            {
                return Mathf.Max(0, frame.intValue);
            }
        }

        return 0;
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
