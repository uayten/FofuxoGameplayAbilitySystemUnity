using System;
using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The ability timeline as lanes of draggable markers: one block per step, and
/// inside each block a row for the phases, the combo window, movement, every
/// tag window, the effect triggers and the cues. Dragging a marker writes the
/// frame it lands on, so the frame fields are authored where their meaning is
/// visible instead of as numbers that only make sense next to each other.
///
/// Each step gets its own ruler across the full width rather than sharing one
/// continuous ruler with the others. Steps never overlap in time — a combo
/// advance cuts the running step short — so a shared ruler would spend its
/// pixels on a relationship that does not exist, and spend them worst on the
/// long combos that need the resolution most.
///
/// Every value written here goes back through the serialized fields and then
/// through <see cref="AbilityStep.Sanitize"/>, which is the runtime's own
/// clamping. The view owns no rule about what a legal frame is.
/// </summary>
internal sealed class AbilityTimelineView
{
    private const float LaneHeight = 16f;
    private const float LaneSpacing = 2f;
    private const float RulerHeight = 13f;
    private const float StepHeaderHeight = 16f;
    private const float StepSpacing = 10f;
    private const float GutterWidth = 74f;
    private const float SeedButtonWidth = 16f;
    private const float GrabRadius = 6f;
    private const float MarkerWidth = 3f;
    private const float MinimumTickSpacing = 30f;
    private const float MinimumLabelWidth = 42f;
    private const string DragUndoName = "Edit Ability Timeline";

    /// <summary>Frame counts a ruler tick is allowed to step by.</summary>
    private static readonly int[] TickSteps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

    private static readonly Color LaneBackground = new(0f, 0f, 0f, 0.18f);
    private static readonly Color StartupColor = new(0.30f, 0.45f, 0.75f, 0.70f);
    private static readonly Color ActiveColor = new(0.80f, 0.33f, 0.30f, 0.80f);
    private static readonly Color RecoveryColor = new(0.32f, 0.50f, 0.42f, 0.65f);
    private static readonly Color ComboColor = new(0.90f, 0.72f, 0.20f, 0.50f);
    private static readonly Color GraceColor = new(0.90f, 0.72f, 0.20f, 0.20f);
    private static readonly Color MovementColor = new(0.35f, 0.70f, 0.45f, 0.40f);
    private static readonly Color DisplacementColor = new(0.30f, 0.60f, 0.85f, 0.50f);
    private static readonly Color TagWindowColor = new(0.62f, 0.42f, 0.80f, 0.50f);
    private static readonly Color EffectColor = new(0.94f, 0.36f, 0.30f, 1f);
    private static readonly Color CueColor = new(0.30f, 0.78f, 0.85f, 1f);
    private static readonly Color EdgeColor = new(1f, 1f, 1f, 0.55f);
    private static readonly Color SelectionColor = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Color TickColor = new(1f, 1f, 1f, 0.12f);

    private static GUIStyle rightAlignedMiniLabel;

    /// <summary>Built on demand: styles cannot be touched during static init.</summary>
    private static GUIStyle RightAlignedMiniLabel =>
        rightAlignedMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
        };

    private readonly List<HandleHit> hits = new();
    private Handle dragged;
    private Handle selected;
    private int controlId;
    /// <summary>Undo group a drag opened, or -1 while nothing is being dragged.</summary>
    private int dragUndoGroup = -1;

    /// <summary>What a marker points at, and therefore what a drag writes.</summary>
    private enum HandleKind
    {
        None,
        StartupEnd,
        ActiveEnd,
        RecoveryEnd,
        ComboStart,
        ComboEnd,
        ComboGrace,
        MovementUnlock,
        DisplacementStart,
        DisplacementEnd,
        TagWindowStart,
        TagWindowEnd,
        EffectTrigger,
        CueTrigger,
    }

    /// <summary>
    /// One draggable value, addressed by where it lives rather than by a
    /// <c>SerializedProperty</c>: the property is resolved again on every pass,
    /// so a step list edited between two repaints cannot leave a stale handle
    /// pointing at an element that moved.
    /// </summary>
    private readonly struct Handle : IEquatable<Handle>
    {
        internal Handle(int stepIndex, HandleKind kind, int elementIndex = 0)
        {
            StepIndex = stepIndex;
            Kind = kind;
            ElementIndex = elementIndex;
        }

        internal int StepIndex { get; }
        internal HandleKind Kind { get; }
        internal int ElementIndex { get; }
        internal bool IsValid => Kind != HandleKind.None;

        public bool Equals(Handle other)
        {
            return StepIndex == other.StepIndex &&
                   Kind == other.Kind &&
                   ElementIndex == other.ElementIndex;
        }

        public override bool Equals(object obj) => obj is Handle other && Equals(other);

        public override int GetHashCode() =>
            (StepIndex * 397 ^ (int)Kind) * 397 ^ ElementIndex;
    }

    /// <summary>
    /// A handle's grab area plus the lane it belongs to, recorded while the
    /// lanes are laid out so the pick and the drag both read the same geometry
    /// the user is looking at.
    /// </summary>
    private readonly struct HandleHit
    {
        internal HandleHit(Handle handle, Rect grab, Rect lane, int frameCount)
        {
            Handle = handle;
            Grab = grab;
            Lane = lane;
            FrameCount = frameCount;
        }

        internal Handle Handle { get; }
        internal Rect Grab { get; }
        internal Rect Lane { get; }
        internal int FrameCount { get; }
    }

    internal void Draw(
        SerializedObject serializedObject, TimelineAbilityDefinition ability)
    {
        EditorGUILayout.LabelField(
            ability.IsCombo
                ? $"Timeline — {ability.StepCount} steps, {ability.TotalDuration:0.000} s total"
                : "Timeline",
            EditorStyles.boldLabel);
        if (ability.StepCount == 0)
        {
            EditorGUILayout.HelpBox(
                "No steps yet. Add one to the Steps list above and its lanes appear here.",
                MessageType.Info);
            return;
        }

        hits.Clear();
        controlId = GUIUtility.GetControlID(FocusType.Passive);

        float height = 0f;
        for (int i = 0; i < ability.StepCount; i++)
        {
            height += MeasureStep(ability, i) + StepSpacing;
        }

        Rect block = GUILayoutUtility.GetRect(
            0f, height, GUILayout.ExpandWidth(true));

        // The lanes are drawn into that one reserved rect, so they are skipped
        // on the layout pass, where the rect is not resolved yet. The selection
        // row below is not: it is laid out by GUILayout, which demands the same
        // sequence of controls on every event.
        if (Event.current.type != EventType.Layout)
        {
            float y = block.y;
            for (int i = 0; i < ability.StepCount; i++)
            {
                float stepHeight = MeasureStep(ability, i);
                DrawStep(
                    serializedObject,
                    ability,
                    i,
                    new Rect(block.x, y, block.width, stepHeight));
                y += stepHeight + StepSpacing;
            }

            HandleEvents(serializedObject, ability, block);
        }

        DrawSelectionRow(serializedObject, ability);
    }

    /// <summary>
    /// Height of one step block. Measured before anything is drawn, because the
    /// layout has to reserve the space in the pass that cannot see it yet.
    /// </summary>
    private static float MeasureStep(TimelineAbilityDefinition ability, int index)
    {
        AbilityStep step = ability.StepAt(index);
        float rowsHeight = LaneHeight + LaneSpacing;
        if (step == null)
        {
            return StepHeaderHeight + rowsHeight;
        }

        // Phases, movement and effects always have a lane: an empty movement or
        // effect lane is a fact about the step worth seeing.
        int rows = 3;
        if (ability.StepCount > 1)
        {
            rows++;
        }

        if (step.HasDisplacement)
        {
            rows++;
        }

        rows += step.TagWindows.Count;
        if (step.CueTriggers != null && step.CueTriggers.Count > 0)
        {
            rows++;
        }

        return StepHeaderHeight + RulerHeight + rows * rowsHeight;
    }

    private void DrawStep(
        SerializedObject serializedObject,
        TimelineAbilityDefinition ability,
        int index,
        Rect block)
    {
        AbilityStep step = ability.StepAt(index);
        if (step == null)
        {
            GUI.Label(block, $"Step {index + 1}: empty", EditorStyles.miniLabel);
            return;
        }

        int frames = step.RecoveryEndFrame;
        DrawStepHeader(step, index, new Rect(block.x, block.y, block.width, StepHeaderHeight));

        float laneX = block.x + GutterWidth;
        float laneWidth = Mathf.Max(1f, block.width - GutterWidth);
        float y = block.y + StepHeaderHeight;

        DrawRuler(new Rect(laneX, y, laneWidth, RulerHeight), frames);
        y += RulerHeight;

        DrawPhaseLane(block.x, laneX, laneWidth, ref y, index, step, frames);

        if (ability.StepCount > 1)
        {
            DrawComboLane(
                serializedObject, ability, block.x, laneX, laneWidth, ref y, index, step, frames);
        }

        DrawMovementLane(
            serializedObject, ability, block.x, laneX, laneWidth, ref y, index, step, frames);

        if (step.HasDisplacement)
        {
            DrawDisplacementLane(block.x, laneX, laneWidth, ref y, index, step, frames);
        }

        DrawTagWindowLanes(block.x, laneX, laneWidth, ref y, index, step, frames);
        DrawEffectLane(block.x, laneX, laneWidth, ref y, index, step, frames);

        if (step.CueTriggers != null && step.CueTriggers.Count > 0)
        {
            DrawCueLane(block.x, laneX, laneWidth, ref y, index, step, frames);
        }
    }

    private static void DrawStepHeader(AbilityStep step, int index, Rect rect)
    {
        string clip = step.AnimationClip == null ? "no clip" : step.AnimationClip.name;
        string driver = step.AnimationFrameCount >= step.RecoveryEndFrame && step.AnimationClip != null
            ? " (clip length)"
            : string.Empty;
        GUI.Label(
            rect,
            $"Step {index + 1} — {clip} · {step.FrameRate:0.#} fps · " +
            $"{step.RecoveryEndFrame} frames{driver} · {step.Duration:0.000} s",
            EditorStyles.miniBoldLabel);
    }

    private static void DrawRuler(Rect rect, int frames)
    {
        int tickStep = ResolveTickStep(AbilityTimelineLayout.FrameWidth(rect, frames));
        for (int frame = 1; frame <= frames; frame += tickStep)
        {
            float x = AbilityTimelineLayout.MarkerX(rect, frames, frame);
            EditorGUI.DrawRect(new Rect(x, rect.y + rect.height - 4f, 1f, 4f), TickColor);
            GUI.Label(
                new Rect(x + 2f, rect.y - 1f, 34f, rect.height),
                frame.ToString(),
                EditorStyles.miniLabel);
        }
    }

    /// <summary>The coarsest tick that still leaves the labels readable.</summary>
    private static int ResolveTickStep(float frameWidth)
    {
        for (int i = 0; i < TickSteps.Length; i++)
        {
            if (TickSteps[i] * frameWidth >= MinimumTickSpacing)
            {
                return TickSteps[i];
            }
        }

        return TickSteps[^1];
    }

    private void DrawPhaseLane(
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Phases",
            "Startup, Active and Recovery. Damage is not applied by a phase: " +
            "an effect trigger applies it.");

        DrawSpan(lane, frames, 1, step.StartupEndFrame, StartupColor, "Startup");
        DrawSpan(lane, frames, step.StartupEndFrame + 1, step.ActiveEndFrame, ActiveColor, "Active");
        DrawSpan(lane, frames, step.ActiveEndFrame + 1, frames, RecoveryColor, "Recovery");

        DrawEdge(lane, frames, step.StartupEndFrame,
            new Handle(index, HandleKind.StartupEnd));
        DrawEdge(lane, frames, step.ActiveEndFrame,
            new Handle(index, HandleKind.ActiveEnd));
        DrawEdge(lane, frames, frames,
            new Handle(index, HandleKind.RecoveryEnd));
    }

    private void DrawComboLane(
        SerializedObject serializedObject, TimelineAbilityDefinition ability,
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Combo",
            "Frames where an input carries the activation into the next step, " +
            "plus the late grace frames past the window.");

        if (step.ComboContinuationFrame <= 0)
        {
            DrawEmptyLane(
                lane,
                "waits for the step to complete",
                gutterX,
                () => SeedFrame(
                    serializedObject,
                    ability,
                    new Handle(index, HandleKind.ComboStart),
                    step.ActiveEndFrame));
            return;
        }

        int end = step.ComboInputEndFrame > 0 ? step.ComboInputEndFrame : frames;
        DrawSpan(lane, frames, step.ComboContinuationFrame, end, ComboColor,
            step.ComboInputEndFrame > 0 ? "combo" : "combo (to end)");

        if (step.ComboInputGraceFrames > 0)
        {
            DrawSpan(lane, frames, end + 1, step.ComboInputDeadlineFrame, GraceColor, "grace");
            DrawEdge(lane, frames, step.ComboInputDeadlineFrame,
                new Handle(index, HandleKind.ComboGrace));
        }

        DrawEdge(lane, frames, step.ComboContinuationFrame,
            new Handle(index, HandleKind.ComboStart));
        DrawEdge(lane, frames, end, new Handle(index, HandleKind.ComboEnd));
    }

    private void DrawMovementLane(
        SerializedObject serializedObject, TimelineAbilityDefinition ability,
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Movement",
            "First frame that unlocks owner movement. Zero keeps it locked " +
            "until the step completes.");

        if (step.MovementUnlockFrame <= 0)
        {
            DrawEmptyLane(
                lane,
                "locked for the whole step",
                gutterX,
                () => SeedFrame(
                    serializedObject,
                    ability,
                    new Handle(index, HandleKind.MovementUnlock),
                    Mathf.Min(frames, step.ActiveEndFrame + 1)));
            return;
        }

        DrawSpan(lane, frames, step.MovementUnlockFrame, frames, MovementColor, "unlocked");
        DrawEdge(lane, frames, step.MovementUnlockFrame,
            new Handle(index, HandleKind.MovementUnlock));
    }

    private void DrawDisplacementLane(
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Displacement",
            "Frames that move the owner, and how far they move them.");

        DrawSpan(
            lane, frames, step.DisplacementStartFrame, step.DisplacementEndFrame,
            DisplacementColor,
            $"{step.DisplacementDistance:0.##} m {step.DisplacementDirection}");
        DrawEdge(lane, frames, step.DisplacementStartFrame,
            new Handle(index, HandleKind.DisplacementStart));
        DrawEdge(lane, frames, step.DisplacementEndFrame,
            new Handle(index, HandleKind.DisplacementEnd));
    }

    private void DrawTagWindowLanes(
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        IReadOnlyList<AbilityStepTagWindow> windows = step.TagWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            AbilityStepTagWindow window = windows[i];
            string tag = window.Tag.IsEmpty ? "(no tag)" : window.Tag.Value;
            Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, ShortTag(tag), tag);

            int end = window.RunsToEndOfStep ? frames : window.EndFrame;
            DrawSpan(lane, frames, window.StartFrame, end, TagWindowColor,
                window.RunsToEndOfStep ? "to end" : string.Empty);
            DrawEdge(lane, frames, window.StartFrame,
                new Handle(index, HandleKind.TagWindowStart, i));
            DrawEdge(lane, frames, end, new Handle(index, HandleKind.TagWindowEnd, i));
        }
    }

    private void DrawEffectLane(
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Effects",
            "Where this step applies its effects. An empty lane means the step " +
            "hits nothing.");

        IReadOnlyList<AbilityEffectTrigger> triggers = step.EffectTriggers;
        if (triggers == null || triggers.Count == 0)
        {
            DrawEmptyLane(lane, "no effect trigger", gutterX, null);
            return;
        }

        for (int i = 0; i < triggers.Count; i++)
        {
            AbilityEffectTrigger trigger = triggers[i];
            string label = trigger.Effect == null
                ? $"{i + 1}: (none)"
                : $"{i + 1}: {trigger.Effect.name}";
            DrawMarker(
                lane, frames, trigger.Frame, EffectColor,
                new Handle(index, HandleKind.EffectTrigger, i), label);
        }
    }

    private void DrawCueLane(
        float gutterX, float laneX, float laneWidth, ref float y,
        int index, AbilityStep step, int frames)
    {
        Rect lane = BeginLane(gutterX, laneX, laneWidth, ref y, "Cues",
            "Cosmetic cue tags. A cue never changes gameplay state.");

        IReadOnlyList<GameplayCueTrigger> triggers = step.CueTriggers;
        for (int i = 0; i < triggers.Count; i++)
        {
            GameplayCueTrigger trigger = triggers[i];
            DrawMarker(
                lane, frames, trigger.Frame, CueColor,
                new Handle(index, HandleKind.CueTrigger, i),
                trigger.Cue.IsEmpty ? "(no tag)" : ShortTag(trigger.Cue.Value));
        }
    }

    // ------------------------------------------------------------- primitives

    private static Rect BeginLane(
        float gutterX, float laneX, float laneWidth, ref float y,
        string label, string tooltip)
    {
        Rect lane = new(laneX, y, laneWidth, LaneHeight);
        GUI.Label(
            new Rect(gutterX, y, GutterWidth - 4f, LaneHeight),
            new GUIContent(label, tooltip),
            EditorStyles.miniLabel);
        EditorGUI.DrawRect(lane, LaneBackground);
        y += LaneHeight + LaneSpacing;
        return lane;
    }

    /// <summary>
    /// A lane whose value is zero, which every one of these fields reads as
    /// "no window at all". The seed button writes a first value, because a
    /// marker that does not exist cannot be dragged into existence.
    /// </summary>
    private static void DrawEmptyLane(Rect lane, string message, float gutterX, Action seed)
    {
        GUI.Label(
            new Rect(lane.x + 4f, lane.y, lane.width - 8f, lane.height),
            message,
            EditorStyles.miniLabel);

        if (seed == null)
        {
            return;
        }

        Rect button = new(
            gutterX + GutterWidth - SeedButtonWidth - 4f,
            lane.y + 1f,
            SeedButtonWidth,
            LaneHeight - 2f);
        if (GUI.Button(button, new GUIContent("+", "Author a first frame here."), EditorStyles.miniButton))
        {
            seed();
        }
    }

    private static void DrawSpan(
        Rect lane, int frames, int from, int to, Color color, string label)
    {
        if (to < from)
        {
            return;
        }

        Rect span = AbilityTimelineLayout.Span(lane, frames, from, to);
        EditorGUI.DrawRect(span, color);
        if (!string.IsNullOrEmpty(label) && span.width >= MinimumLabelWidth)
        {
            GUI.Label(
                new Rect(span.x + 3f, span.y, span.width - 6f, span.height),
                label,
                EditorStyles.miniLabel);
        }
    }

    private void DrawEdge(Rect lane, int frames, int frame, Handle handle)
    {
        float x = AbilityTimelineLayout.MarkerX(lane, frames, frame);
        Rect edge = new(x - 1f, lane.y, 2f, lane.height);
        EditorGUI.DrawRect(edge, selected.Equals(handle) ? SelectionColor : EdgeColor);
        Register(handle, edge, lane, frames);
    }

    private void DrawMarker(
        Rect lane, int frames, int frame, Color color, Handle handle, string label)
    {
        float x = AbilityTimelineLayout.MarkerX(lane, frames, frame);
        Rect marker = new(x - MarkerWidth * 0.5f, lane.y, MarkerWidth, lane.height);
        EditorGUI.DrawRect(marker, selected.Equals(handle) ? SelectionColor : color);
        Register(handle, marker, lane, frames);

        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        float rightRoom = lane.xMax - marker.xMax - 4f;
        if (rightRoom >= MinimumLabelWidth)
        {
            GUI.Label(
                new Rect(marker.xMax + 3f, lane.y, rightRoom, lane.height),
                label,
                EditorStyles.miniLabel);
            return;
        }

        GUI.Label(
            new Rect(lane.x, lane.y, marker.x - lane.x - 3f, lane.height),
            label,
            RightAlignedMiniLabel);
    }


    private void Register(Handle handle, Rect grab, Rect lane, int frames)
    {
        hits.Add(new HandleHit(handle, grab, lane, frames));
        EditorGUIUtility.AddCursorRect(
            new Rect(grab.x - GrabRadius, grab.y, grab.width + GrabRadius * 2f, grab.height),
            MouseCursor.ResizeHorizontal);
    }

    private static string ShortTag(string tag)
    {
        int lastDot = tag.LastIndexOf('.');
        return lastDot >= 0 && lastDot < tag.Length - 1 ? tag[(lastDot + 1)..] : tag;
    }

    // ----------------------------------------------------------------- events

    private void HandleEvents(
        SerializedObject serializedObject, TimelineAbilityDefinition ability, Rect block)
    {
        Event current = Event.current;
        switch (current.type)
        {
            case EventType.MouseDown when current.button == 0 && block.Contains(current.mousePosition):
                if (TryPick(current.mousePosition, out Handle picked))
                {
                    selected = picked;
                    dragged = picked;
                    GUIUtility.hotControl = controlId;
                    GUIUtility.keyboardControl = 0;
                    // Each write records its own undo, because a snapshot taken
                    // here would be compared at the end of *this* frame, find
                    // nothing changed, and record nothing at all — the drag
                    // lands on later events. They are collapsed into one entry
                    // when the mouse comes up.
                    Undo.IncrementCurrentGroup();
                    dragUndoGroup = Undo.GetCurrentGroup();
                    current.Use();
                }

                break;

            case EventType.MouseDrag when GUIUtility.hotControl == controlId && dragged.IsValid:
                if (TryResolveHit(dragged, out HandleHit hit))
                {
                    SetFrame(
                        serializedObject,
                        ability,
                        dragged,
                        AbilityTimelineLayout.FrameAt(
                            hit.Lane, hit.FrameCount, current.mousePosition.x),
                        DragUndoName);
                }

                current.Use();
                break;

            case EventType.MouseUp when GUIUtility.hotControl == controlId:
                if (dragUndoGroup >= 0)
                {
                    Undo.SetCurrentGroupName(DragUndoName);
                    Undo.CollapseUndoOperations(dragUndoGroup);
                    dragUndoGroup = -1;
                }

                dragged = default;
                GUIUtility.hotControl = 0;
                current.Use();
                break;

            case EventType.ContextClick when block.Contains(current.mousePosition):
                if (TryPick(current.mousePosition, out Handle rightClicked))
                {
                    selected = rightClicked;
                    ShowContextMenu(serializedObject, ability, rightClicked);
                    current.Use();
                }

                break;

            case EventType.KeyDown when selected.IsValid && GUIUtility.keyboardControl == 0:
                int nudge = current.keyCode switch
                {
                    KeyCode.LeftArrow => -1,
                    KeyCode.RightArrow => 1,
                    _ => 0,
                };
                if (nudge != 0)
                {
                    SetFrame(
                        serializedObject,
                        ability,
                        selected,
                        ReadFrame(ability, selected) + nudge * (current.shift ? 5 : 1),
                        "Nudge Ability Timeline Marker");
                    current.Use();
                }

                break;
        }
    }

    private bool TryPick(Vector2 mouse, out Handle handle)
    {
        handle = default;
        float best = float.MaxValue;
        for (int i = 0; i < hits.Count; i++)
        {
            HandleHit hit = hits[i];
            if (mouse.y < hit.Grab.y || mouse.y > hit.Grab.yMax)
            {
                continue;
            }

            float distance = Mathf.Abs(mouse.x - hit.Grab.center.x);
            if (distance <= GrabRadius + hit.Grab.width * 0.5f && distance < best)
            {
                best = distance;
                handle = hit.Handle;
            }
        }

        return handle.IsValid;
    }

    private bool TryResolveHit(Handle handle, out HandleHit hit)
    {
        for (int i = 0; i < hits.Count; i++)
        {
            if (hits[i].Handle.Equals(handle))
            {
                hit = hits[i];
                return true;
            }
        }

        hit = default;
        return false;
    }

    /// <summary>
    /// The one place a zero is written back. Zero is not a frame — it is how
    /// every one of these fields says "no window", and dragging can never
    /// produce it, so the menu is where it lives.
    /// </summary>
    private void ShowContextMenu(
        SerializedObject serializedObject, TimelineAbilityDefinition ability, Handle handle)
    {
        GenericMenu menu = new();
        switch (handle.Kind)
        {
            case HandleKind.ComboEnd:
            case HandleKind.TagWindowEnd:
                menu.AddItem(
                    new GUIContent("Run to the end of the step"),
                    false,
                    () => ClearFrame(serializedObject, ability, handle));
                break;
            case HandleKind.ComboStart:
                menu.AddItem(
                    new GUIContent("Wait for the step to complete"),
                    false,
                    () => ClearFrame(serializedObject, ability, handle));
                break;
            case HandleKind.MovementUnlock:
                menu.AddItem(
                    new GUIContent("Keep movement locked"),
                    false,
                    () => ClearFrame(serializedObject, ability, handle));
                break;
            case HandleKind.ComboGrace:
                menu.AddItem(
                    new GUIContent("No late grace"),
                    false,
                    () => ClearFrame(serializedObject, ability, handle));
                break;
            case HandleKind.EffectTrigger:
                AbilityStep step = ability.StepAt(handle.StepIndex);
                GameplayEffectDefinition effect =
                    step != null && handle.ElementIndex < step.EffectTriggers.Count
                        ? step.EffectTriggers[handle.ElementIndex].Effect
                        : null;
                if (effect != null)
                {
                    menu.AddItem(
                        new GUIContent("Ping the effect"),
                        false,
                        () => EditorGUIUtility.PingObject(effect));
                }

                break;
        }

        if (menu.GetItemCount() == 0)
        {
            menu.AddDisabledItem(new GUIContent("Nothing to do here"));
        }

        menu.ShowAsContext();
    }

    // ------------------------------------------------------------------ value

    private static int ReadFrame(TimelineAbilityDefinition ability, Handle handle)
    {
        AbilityStep step = ability.StepAt(handle.StepIndex);
        if (step == null)
        {
            return 0;
        }

        switch (handle.Kind)
        {
            case HandleKind.StartupEnd:
                return step.StartupEndFrame;
            case HandleKind.ActiveEnd:
                return step.ActiveEndFrame;
            case HandleKind.RecoveryEnd:
                return step.RecoveryEndFrame;
            case HandleKind.ComboStart:
                return step.ComboContinuationFrame;
            case HandleKind.ComboEnd:
                return step.ComboInputEndFrame > 0
                    ? step.ComboInputEndFrame
                    : step.RecoveryEndFrame;
            case HandleKind.ComboGrace:
                return step.ComboInputDeadlineFrame;
            case HandleKind.MovementUnlock:
                return step.MovementUnlockFrame;
            case HandleKind.DisplacementStart:
                return step.DisplacementStartFrame;
            case HandleKind.DisplacementEnd:
                return step.DisplacementEndFrame;
            case HandleKind.TagWindowStart:
                return handle.ElementIndex < step.TagWindows.Count
                    ? step.TagWindows[handle.ElementIndex].StartFrame
                    : 0;
            case HandleKind.TagWindowEnd:
                if (handle.ElementIndex >= step.TagWindows.Count)
                {
                    return 0;
                }

                AbilityStepTagWindow window = step.TagWindows[handle.ElementIndex];
                return window.RunsToEndOfStep ? step.RecoveryEndFrame : window.EndFrame;
            case HandleKind.EffectTrigger:
                return handle.ElementIndex < step.EffectTriggers.Count
                    ? step.EffectTriggers[handle.ElementIndex].Frame
                    : 0;
            case HandleKind.CueTrigger:
                return handle.ElementIndex < step.CueTriggers.Count
                    ? step.CueTriggers[handle.ElementIndex].Frame
                    : 0;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Writes the frame a handle landed on. The grace handle is the one that is
    /// not a frame field: it is a count past the combo window, so it is stored
    /// as the distance between the two.
    /// </summary>
    private void SetFrame(
        SerializedObject serializedObject,
        TimelineAbilityDefinition ability,
        Handle handle,
        int frame,
        string undoName)
    {
        AbilityStep step = ability.StepAt(handle.StepIndex);
        if (step == null)
        {
            return;
        }

        // Every window lives inside the step, so the step's own end is the
        // ceiling — except for the end itself, which is what a longer step is
        // made of and is only floored by the clip.
        int ceiling = handle.Kind == HandleKind.RecoveryEnd
            ? int.MaxValue
            : step.RecoveryEndFrame;
        int clamped = Mathf.Clamp(frame, 1, ceiling);
        int raw = handle.Kind == HandleKind.ComboGrace
            ? Mathf.Max(0, clamped - step.ComboInputEndFrame)
            : clamped;
        SetRaw(serializedObject, ability, handle, raw, undoName);
    }

    /// <summary>A first value for a lane that had none.</summary>
    private void SeedFrame(
        SerializedObject serializedObject, TimelineAbilityDefinition ability, Handle handle, int frame)
    {
        SetFrame(
            serializedObject, ability, handle, frame, "Author Ability Timeline Frame");
    }

    /// <summary>Back to zero, which is the field's way of saying "no window".</summary>
    private void ClearFrame(
        SerializedObject serializedObject, TimelineAbilityDefinition ability, Handle handle)
    {
        SetRaw(serializedObject, ability, handle, 0, "Clear Ability Timeline Frame");
    }

    /// <summary>
    /// The serialized write itself, straight to the field a handle names. The
    /// value has already been decided; what happens after it lands is the
    /// runtime's own <see cref="AbilityStep.Sanitize"/>, so dragging the startup
    /// boundary past the active one pushes it exactly the way typing the number
    /// would.
    /// </summary>
    private void SetRaw(
        SerializedObject serializedObject,
        TimelineAbilityDefinition ability,
        Handle handle,
        int value,
        string undoName)
    {
        SerializedProperty property = ResolveProperty(serializedObject, handle);
        if (property == null || property.intValue == value)
        {
            return;
        }

        // In the same frame as the write it guards, always. Unity compares the
        // snapshot at the end of the frame it was taken in and records nothing
        // when that frame changed nothing, which is how an editor ends up
        // undoing the selection instead of the edit.
        Undo.RecordObject(ability, undoName);
        property.intValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        ability.StepAt(handle.StepIndex)?.Sanitize();
        EditorUtility.SetDirty(ability);
        serializedObject.Update();
        GUI.changed = true;
    }

    private static SerializedProperty ResolveProperty(
        SerializedObject serializedObject, Handle handle)
    {
        SerializedProperty steps = serializedObject.FindProperty("steps");
        if (steps == null || handle.StepIndex >= steps.arraySize)
        {
            return null;
        }

        SerializedProperty step = steps.GetArrayElementAtIndex(handle.StepIndex);
        switch (handle.Kind)
        {
            case HandleKind.StartupEnd:
                return step.FindPropertyRelative("startupEndFrame");
            case HandleKind.ActiveEnd:
                return step.FindPropertyRelative("activeEndFrame");
            case HandleKind.RecoveryEnd:
                return step.FindPropertyRelative("recoveryEndFrame");
            case HandleKind.ComboStart:
                return step.FindPropertyRelative("comboContinuationFrame");
            case HandleKind.ComboEnd:
                return step.FindPropertyRelative("comboInputEndFrame");
            case HandleKind.ComboGrace:
                return step.FindPropertyRelative("comboInputGraceFrames");
            case HandleKind.MovementUnlock:
                return step.FindPropertyRelative("movementUnlockFrame");
            case HandleKind.DisplacementStart:
                return step.FindPropertyRelative("displacementStartFrame");
            case HandleKind.DisplacementEnd:
                return step.FindPropertyRelative("displacementEndFrame");
            case HandleKind.TagWindowStart:
                return ElementProperty(step, "tagWindows", handle.ElementIndex, "startFrame");
            case HandleKind.TagWindowEnd:
                return ElementProperty(step, "tagWindows", handle.ElementIndex, "endFrame");
            case HandleKind.EffectTrigger:
                return ElementProperty(step, "effectTriggers", handle.ElementIndex, "frame");
            case HandleKind.CueTrigger:
                return ElementProperty(step, "cueTriggers", handle.ElementIndex, "frame");
            default:
                return null;
        }
    }

    private static SerializedProperty ElementProperty(
        SerializedProperty step, string arrayName, int index, string fieldName)
    {
        SerializedProperty array = step.FindPropertyRelative(arrayName);
        return array == null || index < 0 || index >= array.arraySize
            ? null
            : array.GetArrayElementAtIndex(index).FindPropertyRelative(fieldName);
    }

    // -------------------------------------------------------------- selection

    /// <summary>
    /// The exact frame of whatever was last clicked, typed rather than dragged.
    /// A lane is worth pixels for reading a timeline and worth nothing for
    /// landing on frame 31 rather than 30.
    /// </summary>
    private void DrawSelectionRow(SerializedObject serializedObject, TimelineAbilityDefinition ability)
    {
        if (!selected.IsValid || ability.StepAt(selected.StepIndex) == null)
        {
            EditorGUILayout.LabelField(
                "Click a marker to select it, drag it to move it, arrow keys nudge it.",
                EditorStyles.miniLabel);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                Describe(ability, selected), EditorStyles.miniLabel, GUILayout.MinWidth(120f));

            int current = ReadFrame(ability, selected);
            int edited = EditorGUILayout.IntField(current, GUILayout.Width(56f));
            if (edited != current)
            {
                SetFrame(
                    serializedObject, ability, selected, edited, "Set Ability Timeline Frame");
            }

            GameplayEffectDefinition effect = ResolveEffect(ability, selected);
            using (new EditorGUI.DisabledScope(effect == null))
            {
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f)) &&
                    effect != null)
                {
                    EditorGUIUtility.PingObject(effect);
                }
            }
        }
    }

    private static GameplayEffectDefinition ResolveEffect(TimelineAbilityDefinition ability, Handle handle)
    {
        if (handle.Kind != HandleKind.EffectTrigger)
        {
            return null;
        }

        AbilityStep step = ability.StepAt(handle.StepIndex);
        return step != null && handle.ElementIndex < step.EffectTriggers.Count
            ? step.EffectTriggers[handle.ElementIndex].Effect
            : null;
    }

    private static string Describe(TimelineAbilityDefinition ability, Handle handle)
    {
        AbilityStep step = ability.StepAt(handle.StepIndex);
        string what = handle.Kind switch
        {
            HandleKind.StartupEnd => "startup ends",
            HandleKind.ActiveEnd => "active ends",
            HandleKind.RecoveryEnd => "step ends",
            HandleKind.ComboStart => "combo window opens",
            HandleKind.ComboEnd => "combo window closes",
            HandleKind.ComboGrace => "late grace ends",
            HandleKind.MovementUnlock => "movement unlocks",
            HandleKind.DisplacementStart => "displacement starts",
            HandleKind.DisplacementEnd => "displacement ends",
            HandleKind.TagWindowStart => $"{TagName(step, handle)} opens",
            HandleKind.TagWindowEnd => $"{TagName(step, handle)} closes",
            HandleKind.EffectTrigger => $"effect {handle.ElementIndex + 1} fires",
            HandleKind.CueTrigger => $"cue {handle.ElementIndex + 1} fires",
            _ => "frame",
        };

        return $"Step {handle.StepIndex + 1} · {what} on frame";
    }

    private static string TagName(AbilityStep step, Handle handle)
    {
        if (step == null || handle.ElementIndex >= step.TagWindows.Count)
        {
            return "tag window";
        }

        GameplayTag tag = step.TagWindows[handle.ElementIndex].Tag;
        return tag.IsEmpty ? "tag window" : tag.Value;
    }
}
