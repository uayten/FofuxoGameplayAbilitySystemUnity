using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The runtime debugger: one actor, everything the ability system knows about
/// it right now, and the history of how it got there — the activations it
/// refused and why, the endings and their cancel tags, the effects it took and
/// dealt. Open it from Window > Fofuxo > Ability Debugger or from the actor's
/// own Inspector.
///
/// Every verdict on this page comes from the runtime: the "can activate now?"
/// rows are <see cref="AbilitySystem.EvaluateActivation"/>, the same call
/// <c>TryActivate</c> makes, so the window can never disagree with the game.
/// It lives in the editor assembly and the runtime ships nothing of it; what
/// the runtime does ship — the history ring and the counters — is off in a
/// release player and one bool read when off anywhere else.
/// </summary>
public sealed class AbilitySystemDebuggerWindow : EditorWindow
{
    private const string StatePrefix = "Fofuxo.GAS.Debugger.";
    private const int MaxHistoryRows = 200;
    private const double RepaintInterval = 0.1;

    private readonly List<AbilitySystem> actors = new();
    private readonly List<AbilityEvent> events = new();
    private readonly List<AbilityDebuggerModel.ActivationReading> audit = new();
    private readonly List<AbilityLoadoutValidationIssue> issues = new();
    private string[] actorNames = System.Array.Empty<string>();

    private AbilitySystem selected;
    private bool followSelection = true;
    private GameObject auditTarget;
    private int kindMask = AbilityDebuggerModel.AllKinds;
    private Vector2 scroll;
    private double lastRepaint;
    private bool actorsDirty = true;
    private GUIStyle rowStyle;

    [MenuItem("Window/Fofuxo/Ability Debugger")]
    public static AbilitySystemDebuggerWindow Open()
    {
        AbilitySystemDebuggerWindow window =
            GetWindow<AbilitySystemDebuggerWindow>("Ability Debugger");
        window.minSize = new Vector2(440f, 320f);
        window.Show();
        return window;
    }

    /// <summary>Opens on one actor and stops following the Hierarchy selection.</summary>
    public static AbilitySystemDebuggerWindow Open(AbilitySystem system)
    {
        AbilitySystemDebuggerWindow window = Open();
        if (system != null)
        {
            window.followSelection = false;
            SessionState.SetBool(StatePrefix + "Follow", false);
            window.selected = system;
            window.Repaint();
        }

        return window;
    }

    private void OnEnable()
    {
        followSelection = SessionState.GetBool(StatePrefix + "Follow", true);
        kindMask = SessionState.GetInt(StatePrefix + "KindMask", AbilityDebuggerModel.AllKinds);
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.hierarchyChanged += MarkActorsDirty;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Selection.selectionChanged += Repaint;
        AbilityDiagnostics.EventRecorded += OnEventRecorded;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.hierarchyChanged -= MarkActorsDirty;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Selection.selectionChanged -= Repaint;
        AbilityDiagnostics.EventRecorded -= OnEventRecorded;
    }

    /// <summary>
    /// A steady repaint while playing, instead of one per recorded event: a
    /// combo records several events a frame and the window only needs to be
    /// current, not synchronous.
    /// </summary>
    private void OnEditorUpdate()
    {
        if (EditorApplication.isPlaying &&
            !EditorApplication.isPaused &&
            EditorApplication.timeSinceStartup - lastRepaint >= RepaintInterval)
        {
            lastRepaint = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    private void OnEventRecorded(AbilitySystem system, AbilityEvent recorded)
    {
        // Paused or in edit mode the steady repaint is off, so an event that
        // lands then — a test, a manual call — still shows up.
        if (system == selected && !(EditorApplication.isPlaying && !EditorApplication.isPaused))
        {
            Repaint();
        }
    }

    private void MarkActorsDirty()
    {
        actorsDirty = true;
        Repaint();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        actorsDirty = true;
        Repaint();
    }

    private void OnGUI()
    {
        rowStyle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        RefreshActors();
        ResolveSelection();

        DrawToolbar();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawDiagnosticsSection();
        DrawTimeSection();
        if (selected == null)
        {
            EditorGUILayout.HelpBox(
                actors.Count == 0
                    ? "No Gameplay Ability System in the loaded scenes."
                    : "Pick an actor in the toolbar, or select one in the Hierarchy " +
                      "with Follow Selection on.",
                MessageType.Info);
        }
        else
        {
            DrawRunningSection();
            DrawTagsSection();
            DrawAttributesSection();
            DrawReadinessSection();
            DrawEffectsSection();
            DrawCuesSection();
            DrawAuditSection();
            DrawHistorySection();
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------- selection

    private void RefreshActors()
    {
        if (!actorsDirty)
        {
            return;
        }

        actorsDirty = false;
        AbilityDebuggerModel.CollectActors(actors);
        actorNames = new string[actors.Count];
        for (int i = 0; i < actors.Count; i++)
        {
            actorNames[i] = actors[i].name;
        }
    }

    private void ResolveSelection()
    {
        if (followSelection && Selection.activeGameObject != null)
        {
            AbilitySystem picked =
                Selection.activeGameObject.GetComponentInParent<AbilitySystem>(true);
            if (picked != null)
            {
                selected = picked;
            }
        }

        if (selected == null)
        {
            selected = actors.Count > 0 ? actors[0] : null;
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int index = selected == null ? -1 : actors.IndexOf(selected);
            int picked = EditorGUILayout.Popup(
                index, actorNames, EditorStyles.toolbarPopup, GUILayout.MinWidth(160f));
            if (picked != index && picked >= 0 && picked < actors.Count)
            {
                selected = actors[picked];
                SetFollow(false);
            }

            bool follow = GUILayout.Toggle(
                followSelection,
                new GUIContent(
                    "Follow Selection",
                    "Show the ability system on, or above, the selected GameObject."),
                EditorStyles.toolbarButton);
            if (follow != followSelection)
            {
                SetFollow(follow);
            }

            using (new EditorGUI.DisabledScope(selected == null))
            {
                if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    EditorGUIUtility.PingObject(selected.gameObject);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                EditorApplication.isPlaying
                    ? EditorApplication.isPaused ? "paused" : "playing"
                    : "edit mode",
                EditorStyles.miniLabel);
        }
    }

    private void SetFollow(bool follow)
    {
        followSelection = follow;
        SessionState.SetBool(StatePrefix + "Follow", follow);
    }

    // ------------------------------------------------------------ diagnostics

    private void DrawDiagnosticsSection()
    {
        if (!Section("Diagnostics", "Diagnostics"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            bool enabled = EditorGUILayout.Toggle(
                new GUIContent(
                    "Record",
                    "The master switch for the history and the counters. Off, a " +
                    "record site is one bool read and nothing is allocated."),
                AbilityDiagnostics.Enabled);
            if (enabled != AbilityDiagnostics.Enabled)
            {
                AbilityDiagnostics.Enabled = enabled;
            }

            int capacity = EditorGUILayout.IntField(
                new GUIContent(
                    "History capacity",
                    "Entries an actor's history holds. Applies to histories created " +
                    "from now on."),
                AbilityDiagnostics.HistoryCapacity);
            AbilityDiagnostics.HistoryCapacity = Mathf.Max(8, capacity);

            EditorGUILayout.Space(2f);
            DrawCounterHeader();
            DrawCounter(AbilityDiagnostics.TargetQueries);
            DrawCounter(AbilityDiagnostics.EffectApplications);
            DrawCounter(AbilityDiagnostics.Tasks);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset counters", GUILayout.Width(110f)))
                {
                    AbilityDiagnostics.ResetCounters();
                }
            }

            EditorGUILayout.LabelField(
                "Allocation is the Profiler's own per-frame counter, exact in the Editor " +
                "and development builds. The GAS.* profiler markers carry the same " +
                "sections into the Profiler window.",
                rowStyle);
        }
    }

    private static void DrawCounterHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(16f);
            GUILayout.Label("Counter", EditorStyles.miniBoldLabel, GUILayout.Width(150f));
            GUILayout.Label("Count", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
            GUILayout.Label("Avg ms", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
            GUILayout.Label("Max ms", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
            GUILayout.Label("Total ms", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
            GUILayout.Label("Alloc", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
        }
    }

    private static void DrawCounter(AbilityDiagnosticCounter counter)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(16f);
            GUILayout.Label(counter.Name, EditorStyles.miniLabel, GUILayout.Width(150f));
            GUILayout.Label(counter.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(70f));
            GUILayout.Label(
                counter.AverageMilliseconds.ToString("0.000"),
                EditorStyles.miniLabel,
                GUILayout.Width(60f));
            GUILayout.Label(
                counter.MaxMilliseconds.ToString("0.000"),
                EditorStyles.miniLabel,
                GUILayout.Width(60f));
            GUILayout.Label(
                counter.TotalMilliseconds.ToString("0.0"),
                EditorStyles.miniLabel,
                GUILayout.Width(70f));
            GUILayout.Label(FormatBytes(counter.AllocatedBytes), EditorStyles.miniLabel, GUILayout.Width(80f));
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
        {
            return bytes + " B";
        }

        if (bytes < 1024L * 1024L)
        {
            return (bytes / 1024f).ToString("0.0") + " KB";
        }

        return (bytes / (1024f * 1024f)).ToString("0.00") + " MB";
    }

    // ------------------------------------------------------------------ time

    private void DrawTimeSection()
    {
        if (!Section("Time", "Time"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            AbilityTimeControls.SlowMotion = EditorGUILayout.Toggle(
                new GUIContent("Slow motion", "Run the game at the scale below while playing."),
                AbilityTimeControls.SlowMotion);
            using (new EditorGUI.DisabledScope(!AbilityTimeControls.SlowMotion))
            {
                AbilityTimeControls.SlowMotionScale = EditorGUILayout.Slider(
                    new GUIContent("Time scale"),
                    AbilityTimeControls.SlowMotionScale,
                    AbilityTimeControls.MinimumScale,
                    1f);
            }

            AbilityTimeControls.HitStop = EditorGUILayout.Toggle(
                new GUIContent(
                    "Hit-stop",
                    "Freeze time for a moment whenever an effect lands on another actor."),
                AbilityTimeControls.HitStop);
            using (new EditorGUI.DisabledScope(!AbilityTimeControls.HitStop))
            {
                AbilityTimeControls.HitStopSeconds = EditorGUILayout.Slider(
                    new GUIContent("Hit-stop seconds"),
                    AbilityTimeControls.HitStopSeconds,
                    AbilityTimeControls.MinimumScale,
                    AbilityTimeControls.MaximumHitStopSeconds);
            }

            AbilityTimeControls.PauseOnRejection = EditorGUILayout.Toggle(
                new GUIContent(
                    "Pause on rejection",
                    "Pause the Editor on the next refused activation, so the history " +
                    "shows the code before anything else happens."),
                AbilityTimeControls.PauseOnRejection);
            AbilityTimeControls.PauseOnCancel = EditorGUILayout.Toggle(
                new GUIContent(
                    "Pause on cancel",
                    "Pause the Editor on the next cancellation. Owner teardown is ignored."),
                AbilityTimeControls.PauseOnCancel);

            string status;
            if (!EditorApplication.isPlaying)
            {
                status = "Applies in Play Mode. The runtime never writes Time.timeScale; " +
                         "these controls do, and hand back the value they found.";
            }
            else if (AbilityTimeControls.IsFrozen)
            {
                status = "frozen by hit-stop";
            }
            else if (AbilityTimeControls.IsEngaged)
            {
                status = $"time scale {Time.timeScale:0.00}, editor-owned";
            }
            else
            {
                status = $"time scale {Time.timeScale:0.00}, game-owned";
            }

            EditorGUILayout.LabelField(status, rowStyle);
        }
    }

    // --------------------------------------------------------------- running

    private void DrawRunningSection()
    {
        if (!Section("Running", "Running"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            IReadOnlyList<AbilityInstance> instances = selected.ActiveInstances;
            if (instances.Count == 0)
            {
                EditorGUILayout.LabelField("(idle)");
            }

            for (int i = 0; i < instances.Count; i++)
            {
                DrawInstance(instances[i]);
            }

            DrawTasks("Actor tasks", selected.ActorTasks);
            EditorGUILayout.LabelField("Last transition", selected.LastTransition.ToString(), rowStyle);
            EditorGUILayout.LabelField("Movement", selected.IsMovementLocked ? "locked" : "free");
        }
    }

    private void DrawInstance(AbilityInstance instance)
    {
        EditorGUILayout.LabelField(
            AbilityDebuggerModel.DescribeInstance(instance), EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.LabelField(
                "Target",
                instance.Context.Target == null ? "(none)" : instance.Context.Target.name);
            if (instance.Timeline != null && instance.Timeline.WaitsToAdvance)
            {
                AbilityStepIntent intent = instance.Intent;
                EditorGUILayout.LabelField(
                    "Queued input",
                    intent.Status == AbilityStepIntentStatus.None
                        ? "none"
                        : $"{intent.Source} {intent.Status}" +
                          (intent.IsLate ? " (late)" : string.Empty));
                int deadline = instance.ComboInputDeadlineFrame;
                EditorGUILayout.LabelField(
                    "Continuation",
                    deadline > 0
                        ? $"until frame {deadline} · {instance.ComboInputTimeRemaining:0.000} s left"
                        : $"until the step ends · {instance.StepTimeRemaining:0.000} s left");
                if (instance.IsHoldingLastStep)
                {
                    EditorGUILayout.LabelField(" ", "holding the last step");
                }
            }

            EditorGUILayout.LabelField("Hits registered", instance.RegisteredHitCount.ToString());
            if (instance.HasActiveDisplacement)
            {
                EditorGUILayout.LabelField("Displacement", "running");
            }

            DrawTasks("Tasks", instance.Tasks);
        }
    }

    private static void DrawTasks(string label, AbilityTaskScope scope)
    {
        IReadOnlyList<AbilityTask> tasks = scope.Tasks;
        if (tasks.Count == 0)
        {
            EditorGUILayout.LabelField(label, "(none)");
            return;
        }

        EditorGUILayout.LabelField(label, tasks.Count.ToString());
        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                EditorGUILayout.LabelField(tasks[i].GetType().Name, tasks[i].State.ToString());
            }
        }
    }

    // ------------------------------------------------------------------ state

    private void DrawTagsSection()
    {
        if (!Section("Tags", "Tags"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.LabelField(AbilityDebuggerModel.DescribeTags(selected), rowStyle);
        }
    }

    private void DrawAttributesSection()
    {
        if (!Section("Attributes", "Attributes"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            AttributeSet attributes = selected.GetComponent<AttributeSet>();
            if (attributes == null)
            {
                EditorGUILayout.LabelField("(no attribute set on this actor)");
                return;
            }

            int shown = 0;
            foreach (KeyValuePair<GameplayAttribute, AttributeValue> pair in attributes.Values)
            {
                EditorGUILayout.LabelField(
                    pair.Key.Id, AbilityDebuggerModel.DescribeAttribute(pair.Value), rowStyle);
                shown++;
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField("(no attributes authored)");
            }
        }
    }

    private void DrawReadinessSection()
    {
        if (!Section("Readiness", "Cooldowns and charges"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            AbilityLoadout loadout = selected.Loadout;
            if (loadout == null)
            {
                EditorGUILayout.LabelField("(no loadout)");
                return;
            }

            IReadOnlyList<AbilityDefinition> abilities = loadout.Abilities;
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDefinition ability = abilities[i];
                if (ability != null)
                {
                    EditorGUILayout.LabelField(
                        ability.AbilityId,
                        AbilityDebuggerModel.DescribeReadiness(selected, ability));
                }
            }
        }
    }

    private void DrawEffectsSection()
    {
        if (!Section("Effects", "Active effects"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            GameplayEffectContainer container = GameplayEffectContainer.Find(selected.gameObject);
            if (container == null)
            {
                EditorGUILayout.LabelField("(no effect container on this actor yet)");
                return;
            }

            IReadOnlyList<ActiveGameplayEffect> effects = container.ActiveEffects;
            if (effects.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
            }

            for (int i = 0; i < effects.Count; i++)
            {
                EditorGUILayout.LabelField(AbilityDebuggerModel.DescribeEffect(effects[i]), rowStyle);
            }
        }
    }

    /// <summary>
    /// The persistent cues live on the actor — what a presenter registering now
    /// would be caught up with — plus how many presenters listen and how many
    /// duplicate bursts were batched away.
    /// </summary>
    private void DrawCuesSection()
    {
        if (!Section("Cues", "Cues"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            GameplayCueDispatcher cues = selected.Cues;
            EditorGUILayout.LabelField(
                "Presenters",
                $"{cues.PresenterCount} · {cues.BatchedCount} duplicate burst(s) batched");
            IReadOnlyList<ActiveGameplayCue> active = cues.ActiveCues;
            if (active.Count == 0)
            {
                EditorGUILayout.LabelField("(no persistent cue live)");
            }

            for (int i = 0; i < active.Count; i++)
            {
                EditorGUILayout.LabelField(AbilityDebuggerModel.DescribeCue(active[i]), rowStyle);
            }
        }
    }

    // ----------------------------------------------------------------- audit

    /// <summary>
    /// The one place a "why can't I use this?" question is answered before it
    /// is asked in play: every granted ability run through the runtime's own
    /// activation check, against a target the user picks.
    /// </summary>
    private void DrawAuditSection()
    {
        if (!Section("Audit", "Can activate now?"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            auditTarget = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Against target",
                    "The target the activation context carries. Empty asks as a " +
                    "targetless activation would."),
                auditTarget,
                typeof(GameObject),
                true);

            AbilityLoadout loadout = selected.Loadout;
            if (loadout == null)
            {
                EditorGUILayout.HelpBox(
                    "No loadout, so this actor can activate no ability at all.",
                    MessageType.Warning);
                return;
            }

            issues.Clear();
            if (!loadout.TryValidate(issues))
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    EditorGUILayout.HelpBox(issues[i].Message, MessageType.Error);
                }
            }

            AbilityDebuggerModel.AuditActivations(selected, auditTarget, audit);
            for (int i = 0; i < audit.Count; i++)
            {
                AbilityDebuggerModel.ActivationReading reading = audit[i];
                EditorGUILayout.LabelField(
                    reading.Ability.AbilityId,
                    reading.Result.IsAccepted
                        ? "✔ ready"
                        : $"✖ {reading.Result.Rejection}: {reading.Result.Message}",
                    rowStyle);
            }

            EditorGUILayout.LabelField(
                "The same EvaluateActivation the runtime runs before TryActivate. " +
                "The window holds no rule of its own.",
                rowStyle);
        }
    }

    // --------------------------------------------------------------- history

    private void DrawHistorySection()
    {
        if (!Section("History", "History"))
        {
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int mask = EditorGUILayout.MaskField(
                    new GUIContent("Show"), kindMask, AbilityDebuggerModel.KindNames);
                if (mask != kindMask)
                {
                    kindMask = mask;
                    SessionState.SetInt(StatePrefix + "KindMask", mask);
                }

                using (new EditorGUI.DisabledScope(!selected.HasHistory))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                    {
                        selected.History.Clear();
                    }
                }
            }

            if (!AbilityDiagnostics.Enabled)
            {
                EditorGUILayout.HelpBox(
                    "Recording is off (Diagnostics > Record), so nothing new lands here.",
                    MessageType.Warning);
            }

            if (!selected.HasHistory)
            {
                EditorGUILayout.LabelField("(nothing recorded yet)");
                return;
            }

            AbilityEventHistory history = selected.History;
            events.Clear();
            history.CopyTo(events, newestFirst: true);
            float now = Time.time;
            int shown = 0;
            for (int i = 0; i < events.Count && shown < MaxHistoryRows; i++)
            {
                AbilityEvent recorded = events[i];
                if (!AbilityDebuggerModel.PassesFilter(in recorded, kindMask))
                {
                    continue;
                }

                EditorGUILayout.LabelField(
                    AbilityDebuggerModel.FormatEvent(in recorded, now), rowStyle);
                shown++;
            }

            EditorGUILayout.LabelField(
                $"{history.Count} held · {history.DroppedCount} dropped · " +
                $"{history.TotalRecorded} recorded",
                EditorStyles.miniLabel);
        }
    }

    // ------------------------------------------------------------- utilities

    /// <summary>
    /// A foldout whose state survives a domain reload, keyed per section. The
    /// foldout itself is drawn on every event; only what is under it depends
    /// on the state, which is the shape IMGUI's control counting allows.
    /// </summary>
    private static bool Section(string key, string title)
    {
        string fullKey = StatePrefix + key;
        bool expanded = SessionState.GetBool(fullKey, true);
        bool now = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
        if (now != expanded)
        {
            SessionState.SetBool(fullKey, now);
        }

        return now;
    }
}
