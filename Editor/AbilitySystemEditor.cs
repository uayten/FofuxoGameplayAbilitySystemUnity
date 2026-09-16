using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One panel for everything the ability system needs on an actor. The settings
/// still live on the components that own them — the attribute set aggregates
/// values, the effect container owns their lifetimes, the router owns the
/// keybinds — because merging their data would force an ability system onto
/// every barrel that has health. What is merged here is the *reading* of them:
/// a prefab is understood from one Inspector instead of four, and what is
/// missing is added from the same place.
///
/// In play mode the same panel becomes the readout: which ability is running,
/// which step, which frame, what the combo is waiting for.
/// </summary>
[CustomEditor(typeof(AbilitySystem))]
public sealed class AbilitySystemEditor : Editor
{
    private readonly List<AbilityLoadoutValidationIssue> loadoutIssues = new();
    private readonly Dictionary<Object, SerializedObject> siblings = new();
    private bool attributesExpanded = true;
    private bool inputExpanded = true;
    private bool physicsExpanded;
    private bool diagnosticsExpanded;

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    private void OnDisable()
    {
        siblings.Clear();
    }

    public override void OnInspectorGUI()
    {
        AbilitySystem system = (AbilitySystem)target;

        serializedObject.Update();
        EditorGUILayout.LabelField("Abilities", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("loadout"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("animator"));
        serializedObject.ApplyModifiedProperties();

        DrawLoadoutAudit(system);

        EditorGUILayout.Space();
        DrawAttributes(system);

        EditorGUILayout.Space();
        DrawInput(system);

        EditorGUILayout.Space();
        DrawPhysics(system);

        EditorGUILayout.Space();
        DrawDiagnostics(system);

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            DrawRuntimeReadout(system);
        }
    }

    // ------------------------------------------------------------- abilities

    /// <summary>
    /// The same rules the loadout Inspector reports, next to the field that
    /// points at it — an actor with no loadout can activate nothing, and that is
    /// worth saying on the actor rather than one selection away.
    /// </summary>
    private void DrawLoadoutAudit(AbilitySystem system)
    {
        AbilityLoadout loadout = system.Loadout;
        if (loadout == null)
        {
            EditorGUILayout.HelpBox(
                "No loadout, so this actor can activate no ability at all.",
                MessageType.Warning);
            return;
        }

        if (!loadout.TryValidate(loadoutIssues))
        {
            for (int i = 0; i < loadoutIssues.Count; i++)
            {
                EditorGUILayout.HelpBox(loadoutIssues[i].Message, MessageType.Error);
            }

            return;
        }

        EditorGUILayout.LabelField(
            $"{loadout.Abilities.Count} ability(ies) granted",
            EditorStyles.miniLabel);
    }

    // ------------------------------------------------------------ attributes

    private void DrawAttributes(AbilitySystem system)
    {
        AttributeSet attributes = system.GetComponent<AttributeSet>();
        attributesExpanded = DrawSectionHeader(
            "Attributes",
            attributesExpanded,
            attributes,
            typeof(AttributeSet),
            "Health, stamina and the rest. A separate component because an actor " +
            "may have attributes and no abilities.");
        if (attributes == null || !attributesExpanded)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            DrawSiblingProperties(attributes, "definition", "initialValues", "regeneration");
        }
    }

    // ----------------------------------------------------------------- input

    private void DrawInput(AbilitySystem system)
    {
        AbilityInputRouter router = system.GetComponent<AbilityInputRouter>();
        inputExpanded = DrawSectionHeader(
            "Input",
            inputExpanded,
            router,
            typeof(AbilityInputRouter),
            "Which inputs activate which abilities, and where a target comes " +
            "from when the ability needs one.");
        if (router == null || !inputExpanded)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            DrawSiblingProperties(
                router,
                "inputMap",
                "explicitTarget",
                "findFallbackTargetWhenMissing",
                "bufferWindow");

            SerializedObject serializedRouter = Resolve(router);
            AbilityInputMap map =
                serializedRouter.FindProperty("inputMap").objectReferenceValue as AbilityInputMap;
            if (map == null)
            {
                EditorGUILayout.HelpBox(
                    "No input map, so this router activates nothing. Create one with " +
                    "Create > Fofuxo > Abilities > Input Map.",
                    MessageType.Warning);
            }
            else if (!map.TryValidate(out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                DrawMapSummary(map, system.Loadout);
            }
        }
    }

    /// <summary>
    /// Every binding in one list, and the one mistake this catches: an input
    /// bound to an ability the actor was never granted, which looks correct in
    /// both assets and does nothing in play mode.
    /// </summary>
    private static void DrawMapSummary(AbilityInputMap map, AbilityLoadout loadout)
    {
        IReadOnlyList<AbilityInputEntry> entries = map.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            AbilityInputEntry entry = entries[i];
            string target = entry.IsForwarded
                ? "forwarded to game code"
                : entry.Ability.name;
            EditorGUILayout.LabelField(entry.ActionName, target, EditorStyles.miniLabel);

            if (!entry.IsForwarded && loadout != null && !loadout.Contains(entry.Ability))
            {
                EditorGUILayout.HelpBox(
                    $"'{entry.ActionName}' activates '{entry.Ability.name}', which is " +
                    "not in this actor's loadout. The input will be refused.",
                    MessageType.Warning);
            }
        }
    }

    // --------------------------------------------------------------- physics

    private void DrawPhysics(AbilitySystem system)
    {
        AbilityPhysicsBody body = system.GetComponent<AbilityPhysicsBody>();
        physicsExpanded = DrawSectionHeader(
            "Physics",
            physicsExpanded,
            body,
            typeof(AbilityPhysicsBody),
            "Where a physics force lands on this actor: the body it takes over " +
            "and how it hands control back.");
        if (body == null || !physicsExpanded)
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            DrawAllSiblingProperties(body);
        }
    }

    // ----------------------------------------------------------- diagnostics

    private void DrawDiagnostics(AbilitySystem system)
    {
        AbilitySystemDebugger debugger = system.GetComponent<AbilitySystemDebugger>();
        diagnosticsExpanded = DrawSectionHeader(
            "Diagnostics",
            diagnosticsExpanded,
            debugger,
            typeof(AbilitySystemDebugger),
            "Optional. Logs every ability transition and cue to the Console while tuning.");
        if (debugger != null && diagnosticsExpanded)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DrawAllSiblingProperties(debugger);
            }
        }

        // The window is where the actor is actually debugged: the history of
        // refused activations and endings, the counters, the time controls.
        // The component above only echoes transitions to the Console.
        if (GUILayout.Button(new GUIContent(
                "Open Ability Debugger",
                "Live readout of this actor: running abilities, tags, attributes, " +
                "cooldowns, active effects, why each ability can or cannot activate " +
                "right now, and the timestamped event history.")))
        {
            AbilitySystemDebuggerWindow.Open(system);
        }
    }

    // --------------------------------------------------------------- runtime

    /// <summary>
    /// What the activation is doing right now, including the two numbers a
    /// manual combo is authored against: what is queued, and how long it has to
    /// land. Reading them from the log means reading them after the fact.
    /// </summary>
    private static void DrawRuntimeReadout(AbilitySystem system)
    {
        EditorGUILayout.LabelField("Running", EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            AbilityDefinition ability = system.ActiveAbility;
            if (ability == null)
            {
                EditorGUILayout.LabelField("Ability", "(idle)");
            }
            else
            {
                EditorGUILayout.LabelField("Ability", ability.AbilityId);
                if (ability is TimelineAbilityDefinition timeline)
                {
                    EditorGUILayout.LabelField(
                        "Step",
                        $"{system.ActiveStepIndex + 1}/{timeline.StepCount} · " +
                        $"{system.ActivePhase} · frame {system.ActiveFrame}");
                    if (timeline.WaitsToAdvance)
                    {
                        DrawComboReadout(system);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Step", "no timeline");
                }

                if (system.ActiveAbilityCount > 1)
                {
                    EditorGUILayout.LabelField(
                        "Concurrent", $"+{system.ActiveAbilityCount - 1}");
                }
            }

            EditorGUILayout.LabelField("Tags", DescribeTags(system));
            EditorGUILayout.LabelField("Last transition", system.LastTransition.ToString());
        }
    }

    private static void DrawComboReadout(AbilitySystem system)
    {
        AbilityStepIntent intent = system.QueuedStepIntent;
        EditorGUILayout.LabelField(
            "Queued input",
            intent.Status == AbilityStepIntentStatus.None
                ? "none"
                : $"{intent.Source} {intent.Status}" + (intent.IsLate ? " (late)" : string.Empty));

        int deadline = system.ComboInputDeadlineFrame;
        EditorGUILayout.LabelField(
            "Continuation",
            deadline > 0
                ? $"until frame {deadline} · {system.ComboInputTimeRemaining:0.000} s left"
                : $"until the step ends · {system.StepTimeRemaining:0.000} s left");

        if (system.IsHoldingLastStep)
        {
            EditorGUILayout.LabelField(" ", "holding the last step");
        }
    }

    private static string DescribeTags(AbilitySystem system)
    {
        string joined = string.Empty;
        foreach (GameplayTag tag in system.ActiveTags)
        {
            joined += joined.Length == 0 ? tag.Value : ", " + tag.Value;
        }

        return joined.Length == 0 ? "(none)" : joined;
    }

    // ------------------------------------------------------------- utilities

    /// <summary>
    /// A section title that doubles as the place a missing component is added,
    /// so setting an actor up never means hunting the Add Component menu for a
    /// type name. The slot type says what may be added there; for attributes
    /// that is the project's concrete sets, never the base class.
    /// </summary>
    private bool DrawSectionHeader(
        string title, bool expanded, Component present, System.Type slotType, string tooltip)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (present == null)
            {
                EditorGUILayout.LabelField(
                    new GUIContent(title + " — none", tooltip), EditorStyles.boldLabel);
                DrawAddButton(slotType);
                return expanded;
            }

            expanded = EditorGUILayout.Foldout(
                expanded, new GUIContent(title, tooltip), true, EditorStyles.foldoutHeader);
        }

        return expanded;
    }

    /// <summary>
    /// One button when a slot has one type to offer, a menu when the project
    /// has several. The component lands through Undo with no hide flags, so its
    /// own row stays in the Inspector: this panel reads a sibling, it does not
    /// replace it. Adding mid-layout changes what the rest of this frame would
    /// draw, hence the ExitGUI after the single-button path.
    /// </summary>
    private void DrawAddButton(System.Type slotType)
    {
        List<System.Type> choices = AbilitySystemComponentMenu.ConcreteTypes(slotType);
        GameObject owner = ((Component)target).gameObject;
        if (choices.Count == 0)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button(
                    new GUIContent("Add", $"No concrete {slotType.Name} exists in this project."),
                    EditorStyles.miniButton,
                    GUILayout.ExpandWidth(false));
            }

            return;
        }

        if (choices.Count == 1)
        {
            if (GUILayout.Button(
                    new GUIContent(
                        "Add " + choices[0].Name,
                        $"Adds a {choices[0].Name} to this GameObject."),
                    EditorStyles.miniButton,
                    GUILayout.ExpandWidth(false)))
            {
                AbilitySystemComponentMenu.Add(owner, choices[0]);
                GUIUtility.ExitGUI();
            }

            return;
        }

        if (EditorGUILayout.DropdownButton(
                new GUIContent("Add", $"Pick which {slotType.Name} this actor gets."),
                FocusType.Passive,
                EditorStyles.miniPullDown,
                GUILayout.ExpandWidth(false)))
        {
            GenericMenu menu = new();
            foreach (System.Type choice in choices)
            {
                System.Type captured = choice;
                menu.AddItem(
                    new GUIContent(captured.Name),
                    false,
                    () => AbilitySystemComponentMenu.Add(owner, captured));
            }

            menu.ShowAsContext();
        }
    }

    /// <summary>
    /// Draws named fields of a sibling component through its own
    /// <c>SerializedObject</c>: the value is written where it lives, and Undo
    /// and prefab overrides behave exactly as they would in that component's own
    /// Inspector.
    /// </summary>
    private void DrawSiblingProperties(Component sibling, params string[] propertyNames)
    {
        SerializedObject serialized = Resolve(sibling);
        serialized.Update();
        for (int i = 0; i < propertyNames.Length; i++)
        {
            SerializedProperty property = serialized.FindProperty(propertyNames[i]);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }

        serialized.ApplyModifiedProperties();
    }

    private void DrawAllSiblingProperties(Component sibling)
    {
        SerializedObject serialized = Resolve(sibling);
        serialized.Update();
        SerializedProperty property = serialized.GetIterator();
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

        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// Cached per component: rebuilding a SerializedObject every repaint drops
    /// the edit in progress, which shows up as a text field that will not accept
    /// typing.
    /// </summary>
    private SerializedObject Resolve(Component sibling)
    {
        if (!siblings.TryGetValue(sibling, out SerializedObject serialized) ||
            serialized.targetObject == null)
        {
            serialized = new SerializedObject(sibling);
            siblings[sibling] = serialized;
        }

        return serialized;
    }
}
