using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Slow motion and hit-stop for watching a swing land, owned by the Editor
/// and by nothing in the runtime. The runtime records what happened
/// (<see cref="AbilityDiagnostics.EventRecorded"/>); this listens and decides
/// what to do with time, so a shipped game carries no time scale code of the
/// package's, and a consumer that wants a real hit-stop writes its own with
/// the same event.
///
/// <c>Time.timeScale</c> is written only while a control is engaged, and the
/// value found on the way in is handed back on the way out — a game that sets
/// its own scale is left alone until one of these takes over.
/// </summary>
[InitializeOnLoad]
internal static class AbilityTimeControls
{
    internal enum Decision
    {
        None,
        Freeze,
        Pause
    }

    /// <summary>The settings <see cref="Decide"/> reads, copied out so it is a pure function of them.</summary>
    internal readonly struct Settings
    {
        public Settings(bool hitStop, bool pauseOnRejection, bool pauseOnCancel)
        {
            HitStop = hitStop;
            PauseOnRejection = pauseOnRejection;
            PauseOnCancel = pauseOnCancel;
        }

        public bool HitStop { get; }
        public bool PauseOnRejection { get; }
        public bool PauseOnCancel { get; }
    }

    private const string PrefsPrefix = "Fofuxo.GAS.TimeControls.";
    public const float MinimumScale = 0.01f;
    public const float MaximumHitStopSeconds = 2f;

    private static bool slowMotion;
    private static float slowMotionScale;
    private static bool hitStop;
    private static float hitStopSeconds;
    private static bool pauseOnRejection;
    private static bool pauseOnCancel;

    private static bool engaged;
    private static float restoreScale = 1f;
    private static bool frozen;
    private static double hitStopUntil;

    static AbilityTimeControls()
    {
        slowMotion = EditorPrefs.GetBool(PrefsPrefix + "SlowMotion", false);
        slowMotionScale = Mathf.Clamp(
            EditorPrefs.GetFloat(PrefsPrefix + "SlowMotionScale", 0.25f), MinimumScale, 1f);
        hitStop = EditorPrefs.GetBool(PrefsPrefix + "HitStop", false);
        hitStopSeconds = Mathf.Clamp(
            EditorPrefs.GetFloat(PrefsPrefix + "HitStopSeconds", 0.08f),
            MinimumScale,
            MaximumHitStopSeconds);
        pauseOnRejection = EditorPrefs.GetBool(PrefsPrefix + "PauseOnRejection", false);
        pauseOnCancel = EditorPrefs.GetBool(PrefsPrefix + "PauseOnCancel", false);

        AbilityDiagnostics.EventRecorded += OnRecorded;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    /// <summary>Runs the game at <see cref="SlowMotionScale"/> while playing.</summary>
    public static bool SlowMotion
    {
        get => slowMotion;
        set
        {
            if (slowMotion == value)
            {
                return;
            }

            slowMotion = value;
            EditorPrefs.SetBool(PrefsPrefix + "SlowMotion", value);
            ApplyScale();
        }
    }

    public static float SlowMotionScale
    {
        get => slowMotionScale;
        set
        {
            float clamped = Mathf.Clamp(value, MinimumScale, 1f);
            if (Mathf.Approximately(clamped, slowMotionScale))
            {
                return;
            }

            slowMotionScale = clamped;
            EditorPrefs.SetFloat(PrefsPrefix + "SlowMotionScale", clamped);
            ApplyScale();
        }
    }

    /// <summary>Freezes time for <see cref="HitStopSeconds"/> whenever an effect of the actor lands.</summary>
    public static bool HitStop
    {
        get => hitStop;
        set
        {
            if (hitStop == value)
            {
                return;
            }

            hitStop = value;
            EditorPrefs.SetBool(PrefsPrefix + "HitStop", value);
        }
    }

    public static float HitStopSeconds
    {
        get => hitStopSeconds;
        set
        {
            float clamped = Mathf.Clamp(value, MinimumScale, MaximumHitStopSeconds);
            if (Mathf.Approximately(clamped, hitStopSeconds))
            {
                return;
            }

            hitStopSeconds = clamped;
            EditorPrefs.SetFloat(PrefsPrefix + "HitStopSeconds", clamped);
        }
    }

    /// <summary>Pauses the Editor on the next refused activation, so the history shows why.</summary>
    public static bool PauseOnRejection
    {
        get => pauseOnRejection;
        set
        {
            if (pauseOnRejection == value)
            {
                return;
            }

            pauseOnRejection = value;
            EditorPrefs.SetBool(PrefsPrefix + "PauseOnRejection", value);
        }
    }

    /// <summary>Pauses the Editor on the next cancellation, teardown excluded.</summary>
    public static bool PauseOnCancel
    {
        get => pauseOnCancel;
        set
        {
            if (pauseOnCancel == value)
            {
                return;
            }

            pauseOnCancel = value;
            EditorPrefs.SetBool(PrefsPrefix + "PauseOnCancel", value);
        }
    }

    /// <summary>True while a hit-stop holds time at zero.</summary>
    public static bool IsFrozen => frozen;

    /// <summary>True while this class owns <c>Time.timeScale</c>.</summary>
    public static bool IsEngaged => engaged;

    internal static Settings CurrentSettings => new(hitStop, pauseOnRejection, pauseOnCancel);

    /// <summary>
    /// What one recorded event asks of time. Pure, so the rule is pinned by a
    /// test instead of by entering Play Mode: a landed effect freezes, a
    /// refused one does not; a rejection or a cancellation pauses when asked,
    /// except the cancellation that means the actor is going away.
    /// </summary>
    internal static Decision Decide(in AbilityEvent recorded, in Settings settings)
    {
        switch (recorded.Kind)
        {
            case AbilityEventKind.EffectDelivered:
                return settings.HitStop ? Decision.Freeze : Decision.None;
            case AbilityEventKind.ActivationRejected:
                return settings.PauseOnRejection ? Decision.Pause : Decision.None;
            case AbilityEventKind.AbilityCancelled:
                return settings.PauseOnCancel &&
                       recorded.Tag != CommonGameplayTags.CancelOwnerTeardown
                    ? Decision.Pause
                    : Decision.None;
            default:
                return Decision.None;
        }
    }

    private static void OnRecorded(AbilitySystem system, AbilityEvent recorded)
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        switch (Decide(in recorded, CurrentSettings))
        {
            case Decision.Freeze:
                frozen = true;
                hitStopUntil = EditorApplication.timeSinceStartup + hitStopSeconds;
                ApplyScale();
                break;
            case Decision.Pause:
                EditorApplication.isPaused = true;
                break;
        }
    }

    private static void Update()
    {
        if (frozen && EditorApplication.timeSinceStartup >= hitStopUntil)
        {
            frozen = false;
            ApplyScale();
        }
    }

    private static void ApplyScale()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        bool wantsControl = frozen || slowMotion;
        if (wantsControl)
        {
            if (!engaged)
            {
                restoreScale = Time.timeScale;
                engaged = true;
            }

            Time.timeScale = frozen ? 0f : slowMotionScale;
        }
        else if (engaged)
        {
            Time.timeScale = restoreScale;
            engaged = false;
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.EnteredPlayMode:
                ApplyScale();
                break;
            case PlayModeStateChange.ExitingPlayMode:
                frozen = false;
                if (engaged)
                {
                    Time.timeScale = restoreScale;
                    engaged = false;
                }

                break;
        }
    }
}
