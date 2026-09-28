using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// Inspector for an ability and its steps. The step list is the centre of the
    /// UI because that is where a combo is authored now: one asset, one step per
    /// swing, each with its own clip and frame windows.
    ///
    /// An ability with no steps is a normal ability, not an unfinished one — its
    /// work is code in a derived type, and everything below the timeline still
    /// applies to it. What that type adds appears in its own section, under the
    /// name of the class that declares it.
    /// </summary>
    [CustomEditor(typeof(AbilityDefinition), true)]
    public sealed class AbilityDefinitionEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Every property this Inspector lays out by hand. What is left over is
        /// drawn by <see cref="DrawDeclaredSections"/>, so a derived ability's
        /// own fields are editable without touching this file, and a field added to
        /// the base class can never go silently invisible.
        /// </summary>
        private readonly HashSet<string> handledProperties = new();

        private bool embeddedEffectsExpanded = true;
        private UnityEditor.Editor previewClipEditor;
        private AnimationClip previewClipEditorTarget;
        private readonly AbilityTimelineView timelineView = new();

        private void OnEnable()
        {
            UpdatePreviewClipEditor();
        }

        /// <summary>
        /// Fires when the Inspector stops showing this ability, which is what
        /// selecting another asset does. Unsaved edits survive in memory but are
        /// discarded by a domain reload, so leaving them behind silently is the one
        /// way to lose authoring work here.
        /// </summary>
        private void OnDisable()
        {
            DestroyPreviewClipEditor();
            WarnIfLeavingUnsavedChanges();
        }

        private void WarnIfLeavingUnsavedChanges()
        {
            AbilityDefinition ability = target as AbilityDefinition;
            if (ability == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(ability);
            if (string.IsNullOrEmpty(assetPath) || !HasUnsavedChanges(ability, assetPath))
            {
                return;
            }

            Debug.LogWarning(
                $"'{ability.name}' still has unsaved changes. Select it again and " +
                "press Save Ability, or press Ctrl+S, before a domain reload discards them.",
                ability);
        }

        public override void OnInspectorGUI()
        {
            AbilityDefinition ability = (AbilityDefinition)target;
            DrawSaveBar(ability);
            DrawNamingViolations(ability);

            serializedObject.Update();

            DrawProperty("abilityId", "Ability Id");

            if (ability is TimelineAbilityDefinition timelineAbility)
            {
                EditorGUILayout.Space();
                DrawSteps(timelineAbility);

                EditorGUILayout.Space();
                timelineView.Draw(serializedObject, timelineAbility);
            }

            // What makes this kind of ability what it is comes before what every
            // ability shares.
            DrawDeclaredSections(derivedTypes: true);

            EditorGUILayout.Space();
            DrawProperty("requiresTarget");
            DrawProperty("minimumRange");
            DrawProperty("maximumRange");
            DrawProperty("maximumFacingAngle");
            DrawProperty("targetAssist", "Target Assist");

            EditorGUILayout.Space();
            DrawProperty("commitPolicy", "Commit Policy");
            DrawProperty("cooldownGameplayEffect", "Cooldown Gameplay Effect");
            DrawProperty("cooldownStartPolicy");
            DrawCancellation(ability);
            DrawProperty("lockMovementDuringAbility");
            DrawActivationInput(ability);
            DrawProperty("activationTriggers", "Activation Triggers", true);

            EditorGUILayout.Space();
            DrawExclusionGroup(ability);

            EditorGUILayout.Space();
            DrawProperty("costGameplayEffect", "Cost Gameplay Effect");
            DrawProperty("costPeriod", "Cost Period");
            DrawProperty("maxCharges");
            DrawProperty("chargeRestoreTime");

            EditorGUILayout.Space();
            DrawProperty("requiredTags", null, true);
            DrawProperty("blockedTags", null, true);
            DrawProperty("grantedTags", null, true);
            DrawProperty("activeEffects", "Active Effects", true);
            DrawProperty("onParryEffects", null, true);
            DrawProperty("baseAiWeight");

            DrawDeclaredSections(derivedTypes: false);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawValidation(ability);
            DrawEmbeddedEffects(ability);
            DrawPreviewSection(ability);
        }

        /// <summary>
        /// The tag list only means something under Only Listed Tags, so it is the
        /// one field here that appears with the policy rather than beside it.
        /// </summary>
        private void DrawCancellation(AbilityDefinition ability)
        {
            MarkHandled("cancelledByTags");
            DrawProperty("cancelPolicy", "Cancel Policy");
            if (ability.CancelPolicy == AbilityCancelPolicy.OnlyListedTags)
            {
                DrawProperty("cancelledByTags", "Cancelled By Tags", true);
            }
        }

        /// <summary>
        /// The hold duration only means something to a policy that waits for one,
        /// so it appears with the policy rather than beside it.
        /// </summary>
        private void DrawActivationInput(AbilityDefinition ability)
        {
            MarkHandled("activationHoldDuration");
            DrawProperty("activationInputPolicy", "Activation Input");
            if (ability.ActivationInputPolicy != AbilityActivationInputPolicy.OnPress)
            {
                DrawProperty("activationHoldDuration", "Hold Duration");
            }
        }

        /// <summary>
        /// The group tag is only read by the policies scoped to a group, and leaving
        /// it empty under one of those is a validation error — so it is drawn where
        /// the policy that needs it is chosen.
        /// </summary>
        private void DrawExclusionGroup(AbilityDefinition ability)
        {
            MarkHandled("groupTag");
            DrawProperty("groupExclusionPolicy", "Exclusion Policy");
            if (!ability.UsesGroupScopedExclusion)
            {
                EditorGUILayout.HelpBox(
                    ability.GroupExclusionPolicy == AbilityGroupExclusionPolicy.CancelAnyActive
                        ? "Cancel Any Active: this ability interrupts whatever is " +
                          "running and takes its place, unless that ability refuses " +
                          "to be cancelled."
                        : "Block While Any Active: one ability at a time, which is " +
                          "how every ability behaves until it opts out here.",
                    MessageType.Info);
                return;
            }

            DrawProperty("groupTag", "Group Tag");
            EditorGUILayout.HelpBox(
                ability.GroupExclusionPolicy ==
                AbilityGroupExclusionPolicy.BlockWhileSameGroupActive
                    ? "Only an ability in the same group blocks this one. Abilities " +
                      "in other groups keep running alongside it."
                    : "This ability cancels the running abilities in its group and " +
                      "takes their place. Abilities in other groups are left alone.",
                MessageType.Info);
        }

        private void DrawSteps(TimelineAbilityDefinition ability)
        {
            MarkHandled(
                "steps",
                "stepAdvancement",
                "stepAdvanceEventTag",
                "stepTimeoutPolicy");
            SerializedProperty steps = serializedObject.FindProperty("steps");
            EditorGUILayout.PropertyField(
                steps,
                new GUIContent(
                    "Steps",
                    "Optional. One entry per swing, several for a combo. None is an " +
                    "ability with no timeline, whose work is code in a derived type " +
                    "and which runs until something ends it."),
                true);

            if (steps.arraySize > 1)
            {
                DrawProperty("stepAdvancement", "Advancement");
                EditorGUILayout.HelpBox(DescribeAdvancement(ability), MessageType.Info);

                if (ability.StepAdvancement == AbilityStepAdvancement.OnEvent)
                {
                    DrawProperty("stepAdvanceEventTag", "Step Advance Event");
                }

                if (ability.WaitsToAdvance)
                {
                    DrawProperty("stepTimeoutPolicy", "On Timeout");
                }
            }

            // Nothing to hang an effect on without a step, and nothing to write it
            // into without an asset on disk.
            using (new EditorGUI.DisabledScope(
                ability.StepCount == 0 ||
                string.IsNullOrEmpty(AssetDatabase.GetAssetPath(ability))))
            {
                if (GUILayout.Button("Add Embedded Damage Effect To Last Step"))
                {
                    AddEmbeddedDamageEffect(ability);
                }
            }
        }

        private static string DescribeAdvancement(TimelineAbilityDefinition ability)
        {
            switch (ability.StepAdvancement)
            {
                case AbilityStepAdvancement.Manual:
                    return "Manual: each step waits for another input inside its Combo " +
                           "Continue / Combo Input End window, plus any Late Grace " +
                           "Frames the step allows.";
                case AbilityStepAdvancement.OnEvent:
                    return "On Event: each step waits for a gameplay event carrying " +
                           "the Step Advance Event tag. The step's combo window still " +
                           "decides when the advance lands.";
                case AbilityStepAdvancement.OnCondition:
                    return "On Condition: the ability's Can Advance Step override, or " +
                           "the system's Step Advance Condition delegate, is polled " +
                           "while the step's combo window is open.";
                default:
                    return "Automatic: every step chains straight into the next one.";
            }
        }

        /// <summary>
        /// Fields the layout owns but does not always show — a step list of one hides
        /// its advancement mode, and the cancel tags only appear under their policy.
        /// They stay out of the leftover section on the passes that skip them.
        /// </summary>
        private void MarkHandled(params string[] propertyNames)
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                handledProperties.Add(propertyNames[i]);
            }
        }

        /// <summary>
        /// Draws whatever the hand-written layout does not, one section per class
        /// that declares fields, instead of one undifferentiated pile under the
        /// name of the concrete type, which is what a single heading gave.
        ///
        /// Called twice. With <paramref name="derivedTypes"/> it draws the fields
        /// of the derived types, right under the identity and before everything
        /// every ability shares, parent before child, because they are what this
        /// ability is about. Without it, it draws any base field that was added
        /// without a line in the layout, at the bottom, so it can never go
        /// silently invisible.
        /// </summary>
        private void DrawDeclaredSections(bool derivedTypes)
        {
            // The preview pane at the bottom owns this one and draws it later.
            MarkHandled("previewAnimationClip");

            Dictionary<System.Type, List<SerializedProperty>> byDeclaringType = new();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" ||
                    handledProperties.Contains(iterator.propertyPath))
                {
                    continue;
                }

                System.Type declaring = ResolveDeclaringType(
                    target.GetType(), iterator.propertyPath);
                if (!byDeclaringType.TryGetValue(declaring, out List<SerializedProperty> group))
                {
                    group = new List<SerializedProperty>();
                    byDeclaringType.Add(declaring, group);
                }

                group.Add(iterator.Copy());
            }

            foreach (System.Type type in InheritanceChain(target.GetType()))
            {
                if ((type != typeof(AbilityDefinition)) != derivedTypes ||
                    !byDeclaringType.TryGetValue(type, out List<SerializedProperty> group))
                {
                    continue;
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    new GUIContent(
                        ObjectNames.NicifyVariableName(type.Name),
                        $"Declared by {type.FullName}."),
                    EditorStyles.boldLabel);
                for (int i = 0; i < group.Count; i++)
                {
                    EditorGUILayout.PropertyField(group[i], true);
                }
            }
        }

        /// <summary>
        /// The class that declares the serialized field behind a property, which is
        /// the only thing that says which level of the hierarchy a field belongs to.
        /// Unity's own iterator flattens the whole chain into one list.
        /// </summary>
        private static System.Type ResolveDeclaringType(
            System.Type concreteType, string fieldName)
        {
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            for (System.Type type = concreteType; type != null; type = type.BaseType)
            {
                if (type.GetField(fieldName, Flags) != null)
                {
                    return type;
                }
            }

            // A property with no field behind it — an array element path, or a
            // Unity-internal one. It belongs to the concrete type as far as the
            // author is concerned.
            return concreteType;
        }

        /// <summary>
        /// The ability's hierarchy from <see cref="AbilityDefinition"/> down to the
        /// concrete type, parent before child, so a derived section reads as what
        /// the parent kind adds and then what this kind adds.
        /// </summary>
        private static List<System.Type> InheritanceChain(System.Type concreteType)
        {
            List<System.Type> chain = new();
            for (System.Type type = concreteType;
                 type != null && typeof(AbilityDefinition).IsAssignableFrom(type);
                 type = type.BaseType)
            {
                chain.Add(type);
            }

            chain.Reverse();
            return chain;
        }

        private void DrawProperty(
            string propertyName, string displayName = null, bool includeChildren = false)
        {
            handledProperties.Add(propertyName);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(displayName))
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
            else
            {
                EditorGUILayout.PropertyField(property, new GUIContent(displayName), includeChildren);
            }
        }

        /// <summary>
        /// Inspector edits only raise a dirty flag. A domain reload -- entering or
        /// leaving play mode, recompiling, or restarting the Editor -- reloads the
        /// asset from disk and drops whatever was never written, so the ability
        /// needs an explicit save that does not depend on saving the scene.
        /// </summary>
        private static void DrawSaveBar(AbilityDefinition ability)
        {
            string assetPath = AssetDatabase.GetAssetPath(ability);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            bool dirty = HasUnsavedChanges(ability, assetPath);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    dirty ? "Unsaved changes" : "Saved",
                    dirty ? EditorStyles.boldLabel : EditorStyles.label);

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("Save Ability", GUILayout.Width(110f)))
                    {
                        SaveAbility(ability, assetPath);
                    }
                }
            }

            if (dirty && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Edited during play mode. Leaving play mode reloads this asset " +
                    "from disk and discards the change unless it is saved first.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Reports the ability's own name and the name of every effect embedded in
        /// it. The embedded ones have no Inspector of their own, so this is the only
        /// place they are ever seen.
        /// </summary>
        private static void DrawNamingViolations(AbilityDefinition ability)
        {
            AssetNamingConvention.DrawViolationBox(ability);

            string assetPath = AssetDatabase.GetAssetPath(ability);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != ability)
                {
                    AssetNamingConvention.DrawViolationBox(embedded);
                }
            }
        }

        private static bool HasUnsavedChanges(AbilityDefinition ability, string assetPath)
        {
            if (EditorUtility.IsDirty(ability))
            {
                return true;
            }

            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != ability && EditorUtility.IsDirty(embedded))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the ability and its embedded effects, which live as sub-assets of
        /// the same file and can be dirty on their own.
        /// </summary>
        private static void SaveAbility(AbilityDefinition ability, string assetPath)
        {
            EditorUtility.SetDirty(ability);
            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != ability)
                {
                    EditorUtility.SetDirty(embedded);
                }
            }

            AssetDatabase.SaveAssetIfDirty(ability);
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

            if (ability.TryGetAuthoringWarning(out string warning))
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        /// <summary>
        /// Embedded effects are sub-assets of this file, so they cannot be edited
        /// from the object field alone.
        /// </summary>
        private void DrawEmbeddedEffects(AbilityDefinition ability)
        {
            string assetPath = AssetDatabase.GetAssetPath(ability);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            List<GameplayEffectDefinition> effects = new();
            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != ability &&
                    embedded is GameplayEffectDefinition effect)
                {
                    effects.Add(effect);
                }
            }

            if (effects.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            embeddedEffectsExpanded = EditorGUILayout.Foldout(
                embeddedEffectsExpanded,
                $"Embedded Effects ({effects.Count})",
                true,
                EditorStyles.foldoutHeader);
            if (!embeddedEffectsExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (GameplayEffectDefinition effect in effects)
                {
                    DrawEmbeddedEffect(effect);
                }
            }
        }

        /// <summary>
        /// One embedded effect behind its own foldout.
        /// </summary>
        private static void DrawEmbeddedEffect(GameplayEffectDefinition effect)
        {
            string key = EmbeddedEffectFoldoutKey(effect);
            bool wasExpanded = SessionState.GetBool(key, false);
            bool expanded = EditorGUILayout.Foldout(wasExpanded, effect.name, true);
            if (expanded != wasExpanded)
            {
                SessionState.SetBool(key, expanded);
            }

            if (!expanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
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

                GameplayEffectDefinitionEditor.DrawValidationBox(effect);
            }
        }

        /// <summary>
        /// Unity's own inspector-expanded flag is keyed by type, not by object, so
        /// two damage effects embedded in the same ability would share one
        /// triangle and open together. The local file id is unique inside the
        /// asset and survives a rename, so key the state on that.
        /// </summary>
        private static string EmbeddedEffectFoldoutKey(GameplayEffectDefinition effect)
        {
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                effect, out string guid, out long localId)
                ? $"Fofuxo.GAS.EmbeddedEffect.{guid}.{localId}"
                : $"Fofuxo.GAS.EmbeddedEffect.{effect.name}";
        }

        private static void AddEmbeddedDamageEffect(TimelineAbilityDefinition ability)
        {
            SerializedObject serializedAbility = new(ability);
            SerializedProperty steps = serializedAbility.FindProperty("steps");
            if (steps.arraySize == 0)
            {
                return;
            }

            DamageEffectDefinition effect = CreateInstance<DamageEffectDefinition>();
            effect.name = $"{ability.name}_Damage";
            effect.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(effect, ability);

            SerializedProperty step = steps.GetArrayElementAtIndex(steps.arraySize - 1);
            SerializedProperty triggers = step.FindPropertyRelative("effectTriggers");
            int newIndex = triggers.arraySize;
            triggers.InsertArrayElementAtIndex(newIndex);
            SerializedProperty trigger = triggers.GetArrayElementAtIndex(newIndex);
            AbilityStep lastStep = ability.StepAt(ability.StepCount - 1);
            trigger.FindPropertyRelative("frame").intValue =
                lastStep == null ? 1 : Mathf.Max(1, lastStep.ActiveEndFrame);
            trigger.FindPropertyRelative("effect").objectReferenceValue = effect;
            SerializedProperty level = trigger.FindPropertyRelative("level");
            if (level != null)
            {
                level.intValue = 1;
            }
            serializedAbility.ApplyModifiedProperties();

            EditorUtility.SetDirty(effect);
            EditorUtility.SetDirty(ability);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- preview

        /// <summary>
        /// The ability's single preview clip, at the end of the Inspector next to
        /// the panel it drives. It is authoring scaffolding, not gameplay data.
        /// </summary>
        private void DrawPreviewSection(AbilityDefinition ability)
        {
            EditorGUILayout.Space();
            serializedObject.Update();
            DrawProperty("previewAnimationClip", "Preview Animation Clip");
            serializedObject.ApplyModifiedProperties();

            UpdatePreviewClipEditor();

            if (ability.PreviewClip == null)
            {
                EditorGUILayout.HelpBox(
                    "No Preview Animation Clip, so the preview panel is hidden. "
                    + "Assign any clip to inspect it here — it does not have to be "
                    + "one this ability plays"
                    + (ability is TimelineAbilityDefinition
                        ? ", and Preview This Step inside a step borrows that step's clip."
                        : "."),
                    MessageType.Info);
            }
        }

        private AnimationClip ResolvePreviewClip()
        {
            return targets.Length == 1 && target is AbilityDefinition ability
                ? ability.PreviewClip
                : null;
        }

        public override bool HasPreviewGUI()
        {
            return previewClipEditor != null && previewClipEditor.HasPreviewGUI();
        }

        public override GUIContent GetPreviewTitle()
        {
            AnimationClip clip = ResolvePreviewClip();
            return new GUIContent(clip == null ? "Preview" : clip.name);
        }

        public override void OnPreviewSettings()
        {
            if (previewClipEditor != null)
            {
                previewClipEditor.OnPreviewSettings();
            }
        }

        public override void OnPreviewGUI(Rect previewRect, GUIStyle background)
        {
            previewClipEditor?.OnPreviewGUI(previewRect, background);
        }

        public override void OnInteractivePreviewGUI(Rect previewRect, GUIStyle background)
        {
            previewClipEditor?.OnInteractivePreviewGUI(previewRect, background);
        }

        public override bool RequiresConstantRepaint()
        {
            return previewClipEditor != null && previewClipEditor.RequiresConstantRepaint();
        }

        private void UpdatePreviewClipEditor()
        {
            AnimationClip previewClip = ResolvePreviewClip();
            if (previewClip == previewClipEditorTarget && previewClipEditor != null)
            {
                return;
            }

            DestroyPreviewClipEditor();
            previewClipEditorTarget = previewClip;
            if (previewClip == null)
            {
                return;
            }

            System.Type editorType = System.Type.GetType("UnityEditor.AnimationClipEditor, UnityEditor");
            previewClipEditor = editorType != null
                ? CreateEditor(previewClip, editorType)
                : CreateEditor(previewClip);
            if (previewClipEditor != null)
            {
                InitializePreviewTimeRange(previewClipEditor, previewClip);
            }
        }

        private static void InitializePreviewTimeRange(
            UnityEditor.Editor clipEditor,
            AnimationClip clip)
        {
            // A directly inspected AnimationClip receives its playback range from
            // Unity's Inspector pipeline. A hosted editor does not, so its internal
            // TimeControl otherwise keeps the one-second default.
            if (!clipEditor.HasPreviewGUI())
            {
                return;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            System.Reflection.FieldInfo avatarPreviewField =
                clipEditor.GetType().GetField("m_AvatarPreview", Flags);
            object avatarPreview = avatarPreviewField?.GetValue(clipEditor);
            System.Reflection.FieldInfo timeControlField =
                avatarPreview?.GetType().GetField("timeControl", Flags);
            object timeControl = timeControlField?.GetValue(avatarPreview);
            if (timeControl == null)
            {
                return;
            }

            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(clip);
            System.Type timeControlType = timeControl.GetType();
            System.Reflection.FieldInfo currentTimeField =
                timeControlType.GetField("currentTime", Flags);
            System.Reflection.FieldInfo startTimeField =
                timeControlType.GetField("startTime", Flags);
            System.Reflection.FieldInfo stopTimeField =
                timeControlType.GetField("stopTime", Flags);
            if (currentTimeField == null ||
                startTimeField == null ||
                stopTimeField == null)
            {
                return;
            }

            startTimeField.SetValue(timeControl, settings.startTime);
            stopTimeField.SetValue(timeControl, settings.stopTime);

            float currentTime = (float)currentTimeField.GetValue(timeControl);
            if (float.IsNaN(currentTime) ||
                float.IsInfinity(currentTime) ||
                currentTime < settings.startTime ||
                currentTime > settings.stopTime)
            {
                currentTimeField.SetValue(timeControl, settings.startTime);
            }
        }

        private void DestroyPreviewClipEditor()
        {
            if (previewClipEditor != null)
            {
                DestroyImmediate(previewClipEditor);
                previewClipEditor = null;
            }

            previewClipEditorTarget = null;
        }
    }
}
