using System;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem
{
    /// <summary>
    /// How long an applied effect lives. Instant folds into base values and
    /// leaves nothing behind; Duration and Infinite produce an
    /// <see cref="ActiveGameplayEffect"/> that contributes to current values
    /// until it expires or is removed.
    /// </summary>
    public enum GameplayEffectDurationPolicy
    {
        Instant,
        Duration,
        Infinite
    }

    /// <summary>
    /// Which actor an attribute-backed magnitude reads from. The source is the
    /// actor that applied the effect, the target is the actor it landed on.
    /// </summary>
    public enum GameplayEffectCaptureSource
    {
        Source,
        Target
    }

    /// <summary>
    /// What a second application of the same effect does to the first.
    /// Matching is decided by <see cref="GameplayEffectStackScope"/>.
    /// </summary>
    public enum EffectStacking
    {
        /// <summary>Add a stack. Each stack contributes its own modifiers.</summary>
        Stack,
        /// <summary>Keep one instance and restart its duration.</summary>
        Refresh,
        /// <summary>Keep the first instance untouched and drop the new application.</summary>
        Ignore
    }

    /// <summary>
    /// What counts as the same effect for stacking. Per source is the default
    /// because two attackers applying the same debuff are two debuffs; per
    /// target collapses them into one regardless of who applied it.
    /// </summary>
    public enum GameplayEffectStackScope
    {
        BySource,
        ByTarget
    }

    /// <summary>Who an effect is applied to when its trigger fires.</summary>
    public enum GameplayEffectTargetMode
    {
        /// <summary>The actor running the ability.</summary>
        Owner,
        /// <summary>The actor the activation resolved, falling back to the owner.</summary>
        AbilityTarget,
        /// <summary>Everyone a <see cref="HitShape"/> query accepts.</summary>
        Shape
    }

    /// <summary>
    /// How a number is calculated for one application. The base value is
    /// authored; an attribute may be captured from the source or the target and
    /// scaled into it, and the whole result may be curved by the effect level.
    ///
    /// <para><b>Snapshot versus live.</b> A snapshot magnitude reads the captured
    /// attribute once, when the spec is created, and never changes afterwards: a
    /// poison whose damage was decided by the attacker Strength at the moment of
    /// the hit. A live magnitude re-reads it while the effect is active, so
    /// buffing the source mid-effect changes what the effect contributes. Instant
    /// effects cannot tell the two apart: they only ever evaluate once.</para>
    /// </summary>
    [Serializable]
    public struct GameplayEffectMagnitude
    {
        [Tooltip("Flat part of the magnitude, before attribute scaling and level scaling.")]
        [SerializeField] private float baseValue;
        [Tooltip("Optional attribute folded into the magnitude. Leave empty for a flat value.")]
        [SerializeField] private GameplayAttribute attribute;
        [Tooltip("Whose attribute is read: the actor that applied the effect, or the one it landed on.")]
        [SerializeField] private GameplayEffectCaptureSource capture;
        [Tooltip("Read the attribute once, when the effect is applied. Off re-reads it while the effect is active.")]
        [SerializeField] private bool snapshot;
        [Tooltip("Multiplies the captured attribute before it is added to the base value.")]
        [SerializeField] private float coefficient;
        [Tooltip("Optional multiplier by effect level (1-100). An empty curve means no level scaling.")]
        [SerializeField] private AnimationCurve byLevel;

        public GameplayEffectMagnitude(float baseValue)
        {
            this.baseValue = baseValue;
            attribute = default;
            capture = GameplayEffectCaptureSource.Source;
            snapshot = true;
            coefficient = 0f;
            byLevel = null;
        }

        public GameplayEffectMagnitude(
            float baseValue,
            GameplayAttribute attribute,
            float coefficient,
            GameplayEffectCaptureSource capture = GameplayEffectCaptureSource.Source,
            bool snapshot = true)
        {
            this.baseValue = baseValue;
            this.attribute = attribute;
            this.capture = capture;
            this.snapshot = snapshot;
            this.coefficient = coefficient;
            byLevel = null;
        }

        public float BaseValue => baseValue;
        public GameplayAttribute Attribute => attribute;
        public GameplayEffectCaptureSource Capture => capture;
        public bool Snapshot => snapshot;
        public float Coefficient => coefficient;

        /// <summary>True when this magnitude reads an attribute at all.</summary>
        public bool CapturesAttribute => !attribute.IsEmpty && coefficient != 0f;

        /// <summary>
        /// True when the magnitude has to be recalculated while the effect is
        /// active, because it reads an attribute it did not snapshot.
        /// </summary>
        public bool IsLive => CapturesAttribute && !snapshot;

        /// <summary>
        /// The level curve's value at a level, or one when no curve was
        /// authored.
        /// </summary>
        public float LevelMultiplier(int level)
        {
            return byLevel != null && byLevel.length > 0
                ? byLevel.Evaluate(Mathf.Clamp(level, 1, 100))
                : 1f;
        }

        /// <summary>
        /// Resolves the magnitude against one application. The spec owns the
        /// capture rules, so a snapshot value comes back identical every time
        /// and a live one reflects the attribute as it stands now.
        /// </summary>
        public float Evaluate(GameplayEffectSpec spec)
        {
            float value = baseValue;
            if (CapturesAttribute && spec != null)
            {
                value += coefficient * spec.GetCapturedAttribute(capture, attribute, snapshot);
            }

            return value * LevelMultiplier(spec?.Level ?? 1);
        }

        /// <summary>Same magnitude with a level curve on it.</summary>
        public GameplayEffectMagnitude WithLevelCurve(AnimationCurve curve)
        {
            GameplayEffectMagnitude magnitude = this;
            magnitude.byLevel = curve;
            return magnitude;
        }

        /// <summary>Clamps the authored magnitude fields into their legal ranges.</summary>
        public void Sanitize()
        {
            if (byLevel != null && byLevel.length == 0)
            {
                byLevel = null;
            }
        }
    }

    /// <summary>
    /// One attribute change an effect carries. Instant effects fold it into the
    /// base value; duration effects attach it as a live modifier and detach it
    /// when they end.
    /// </summary>
    [Serializable]
    public struct GameplayEffectModifier
    {
        [SerializeField] private GameplayAttribute attribute;
        [SerializeField] private AttributeOperation operation;
        [SerializeField] private GameplayEffectMagnitude magnitude;

        public GameplayEffectModifier(
            GameplayAttribute attribute,
            AttributeOperation operation,
            GameplayEffectMagnitude magnitude)
        {
            this.attribute = attribute;
            this.operation = operation;
            this.magnitude = magnitude;
        }

        public GameplayEffectModifier(
            GameplayAttribute attribute,
            AttributeOperation operation,
            float magnitude)
            : this(attribute, operation, new GameplayEffectMagnitude(magnitude))
        {
        }

        public GameplayAttribute Attribute => attribute;
        public AttributeOperation Operation => operation;
        public GameplayEffectMagnitude Magnitude => magnitude;
        public bool IsEmpty => attribute.IsEmpty;
    }

    /// <summary>
    /// Stacking rules for one effect. The policy decides what a second
    /// application does; the limit and the overflow fields decide what happens
    /// once the stack is full.
    /// </summary>
    [Serializable]
    public struct GameplayEffectStacking
    {
        [SerializeField] private EffectStacking policy;
        [Tooltip("Whether two sources applying this effect share one stack or keep one each.")]
        [SerializeField] private GameplayEffectStackScope scope;
        [Tooltip("Highest stack count. Zero means unlimited, and overflow never happens.")]
        [SerializeField, Min(0)] private int limit;
        [Tooltip("A new stack restarts the duration. Off lets the first application decide when the whole stack ends.")]
        [SerializeField] private bool refreshDurationOnStack;
        [Tooltip("A new stack restarts the period, delaying the next tick by a full period.")]
        [SerializeField] private bool resetPeriodOnStack;
        [Header("Overflow")]
        [Tooltip("Applied to the same target when an application arrives at a full stack.")]
        [SerializeField] private GameplayEffectDefinition[] overflowEffects;
        [Tooltip("An overflowing application does nothing else: no extra stack, no duration refresh.")]
        [SerializeField] private bool denyOverflowApplication;
        [Tooltip("An overflowing application removes the whole stack, after the overflow effects run.")]
        [SerializeField] private bool clearStackOnOverflow;

        public EffectStacking Policy => policy;
        public GameplayEffectStackScope Scope => scope;
        public int Limit => Mathf.Max(0, limit);
        public bool RefreshDurationOnStack => refreshDurationOnStack;
        public bool ResetPeriodOnStack => resetPeriodOnStack;
        public GameplayEffectDefinition[] OverflowEffects =>
            overflowEffects ?? Array.Empty<GameplayEffectDefinition>();
        public bool DenyOverflowApplication => denyOverflowApplication;
        public bool ClearStackOnOverflow => clearStackOnOverflow;

        /// <summary>The defaults a new effect asset starts from: refresh, per source.</summary>
        public static GameplayEffectStacking Default => new()
        {
            policy = EffectStacking.Refresh,
            scope = GameplayEffectStackScope.BySource,
            limit = 0,
            refreshDurationOnStack = true,
            resetPeriodOnStack = false,
            overflowEffects = Array.Empty<GameplayEffectDefinition>(),
            denyOverflowApplication = false,
            clearStackOnOverflow = false,
        };

        /// <summary>
        /// A stacking policy built in code, for a spec with no asset behind
        /// it.
        /// </summary>
        public static GameplayEffectStacking With(
            EffectStacking policy,
            int limit = 0,
            GameplayEffectStackScope scope = GameplayEffectStackScope.BySource)
        {
            GameplayEffectStacking stacking = Default;
            stacking.policy = policy;
            stacking.limit = Mathf.Max(0, limit);
            stacking.scope = scope;
            return stacking;
        }

        /// <summary>
        /// A copy that overflows into the given effects once the limit is
        /// reached.
        /// </summary>
        public GameplayEffectStacking WithOverflow(
            GameplayEffectDefinition[] effects,
            bool denyApplication = false,
            bool clearStack = false)
        {
            GameplayEffectStacking stacking = this;
            stacking.overflowEffects = effects ?? Array.Empty<GameplayEffectDefinition>();
            stacking.denyOverflowApplication = denyApplication;
            stacking.clearStackOnOverflow = clearStack;
            return stacking;
        }
    }

    /// <summary>
    /// Who an effect reaches. <see cref="GameplayEffectTargetMode.Shape"/> runs
    /// the shared per-frame acquisition, so two effects that fire on the same
    /// frame of the same step with the same shape hit the same actors.
    /// </summary>
    [Serializable]
    public struct GameplayEffectTargeting
    {
        [SerializeField] private GameplayEffectTargetMode mode;
        [Tooltip("Query volume. Only read in Shape mode, where it decides who is hit.")]
        [SerializeField] private HitShape shape;
        [Tooltip("Who inside the shape counts. Left at its defaults the owner is skipped and only live actors are accepted.")]
        [SerializeField] private AbilityTargetFilter filter;
        [SerializeField, Min(1)] private int maximumTargets;
        [Tooltip("Only reach the target the ability resolved. Off lets the effect sweep everyone in the shape.")]
        [SerializeField] private bool restrictToAbilityTarget;

        public GameplayEffectTargetMode Mode => mode;
        public HitShape Shape => shape;
        public AbilityTargetFilter Filter => filter;
        public int MaximumTargets => Mathf.Max(1, maximumTargets);
        public bool RestrictToAbilityTarget => restrictToAbilityTarget;

        public static GameplayEffectTargeting Owner => new()
        {
            mode = GameplayEffectTargetMode.Owner,
            maximumTargets = 1,
        };

        public static GameplayEffectTargeting AbilityTarget => new()
        {
            mode = GameplayEffectTargetMode.AbilityTarget,
            maximumTargets = 1,
        };

        /// <summary>
        /// Targeting that queries a shape, rather than reaching the owner or
        /// the ability's target.
        /// </summary>
        public static GameplayEffectTargeting FromShape(
            HitShape shape,
            int maximumTargets = 3,
            bool restrictToAbilityTarget = true)
        {
            return new GameplayEffectTargeting
            {
                mode = GameplayEffectTargetMode.Shape,
                shape = shape,
                maximumTargets = Mathf.Max(1, maximumTargets),
                restrictToAbilityTarget = restrictToAbilityTarget,
            };
        }

        /// <summary>
        /// Keeps the mode and replaces the volume. An effect that draws a shape
        /// without querying it — the debug draw — carries one this way.
        /// </summary>
        public GameplayEffectTargeting WithShape(HitShape hitShape)
        {
            GameplayEffectTargeting targeting = this;
            targeting.shape = hitShape;
            return targeting;
        }

        /// <summary>A copy that uses this filter.</summary>
        public GameplayEffectTargeting WithFilter(AbilityTargetFilter targetFilter)
        {
            GameplayEffectTargeting targeting = this;
            targeting.filter = targetFilter;
            return targeting;
        }

        /// <summary>
        /// The filter this effect actually runs: the authored one, narrowed to
        /// the ability resolved target when the effect may not sweep.
        /// </summary>
        public AbilityTargetFilter ResolveFilter(GameObject requestedTarget)
        {
            return restrictToAbilityTarget ? filter.RestrictedTo(requestedTarget) : filter;
        }

        /// <summary>Clamps the authored targeting fields into their legal ranges.</summary>
        public void Sanitize()
        {
            shape.Sanitize();
            maximumTargets = Mathf.Clamp(
                maximumTargets <= 0 ? 1 : maximumTargets, 1, AbilityTargetQuery.Capacity);
        }
    }
}
