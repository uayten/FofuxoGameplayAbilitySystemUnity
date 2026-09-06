using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Animation preview for the ability Inspector, drawn in the Inspector's
/// native bottom preview pane (the same place the clip Inspector shows its
/// preview), with transport controls in the pane toolbar.
///
/// It renders the preview clip on a hidden model instance with
/// <see cref="PreviewRenderUtility"/> and poses it with
/// <see cref="AnimationClip.SampleAnimation"/>. Hosting the clip's own editor
/// as a nested sub-editor is not supported: the project's custom clip
/// inspector forwards the preview to Unity's internal AnimationClipEditor,
/// whose preview state is missing off the main preview pane and throws a
/// NullReferenceException.
///
/// The model is resolved the same way everywhere else in the Fofuxo tooling:
/// nearest rigged model up from the clip's folder, remembered per folder. The
/// lookup goes through the animation tools by reflection so this package keeps
/// no dependency on them; without them (or without a configured model) the
/// pane explains what to do instead of failing.
/// </summary>
sealed class AbilityAnimationPreview
{
    private const float CameraMargin = 1.6f;

    private static readonly GUIStyle previewBackground = BuildBackground();
    private static GUIStyle overlayLabel;
    private static Func<AnimationClip, GameObject> previewModelGetter;
    private static bool previewModelLookupDone;

    private PreviewRenderUtility previewUtility;
    private GameObject previewInstance;
    private AnimationClip currentClip;
    private GameObject currentModel;
    private Vector3 previewCenter;
    private float previewDistance = 5f;
    private bool playing;
    private float previewTime;
    private double lastTick;

    /// <summary>
    /// Resolves the model and builds the preview instance. An explicit model
    /// override wins; otherwise the model comes from the clip's parent
    /// folder. False when there is nothing to show yet; the viewport then
    /// draws the reason.
    /// </summary>
    public bool Prepare(AnimationClip clip, GameObject modelOverride)
    {
        GameObject model = modelOverride != null
            ? modelOverride
            : ResolvePreviewModel(clip);
        if (model == null)
        {
            DisposeContent();
            return false;
        }

        EnsureReady(clip, model);
        return previewUtility != null && previewInstance != null;
    }

    /// <summary>
    /// Transport row for the preview pane toolbar: play toggle plus scrub.
    /// </summary>
    public void DrawSettings(AnimationClip clip, GameObject modelOverride)
    {
        if (!Prepare(clip, modelOverride))
        {
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            playing = GUILayout.Toggle(
                playing,
                playing ? "Pause" : "Play",
                EditorStyles.miniButton,
                GUILayout.Width(64f));
            float scrubbed = GUILayout.HorizontalSlider(
                previewTime,
                0f,
                ClipLength(clip),
                GUILayout.ExpandWidth(true));
            if (!Mathf.Approximately(scrubbed, previewTime))
            {
                previewTime = scrubbed;
            }
        }
    }

    /// <summary>
    /// Renders the posed model into the preview pane rect, with a time and
    /// damage-frame readout overlaid at the bottom. Returns true while
    /// playing so the host keeps repainting.
    /// </summary>
    public bool DrawViewport(Rect rect, AnimationClip clip, GameObject modelOverride, int damageFrame)
    {
        // Built up front: both branches below draw labels with it.
        overlayLabel ??= BuildOverlayLabel();

        if (!Prepare(clip, modelOverride))
        {
            GUI.Label(
                rect,
                "No preview model for this clip.\n" +
                "Set Preview Model in the Preview section above,\n" +
                "or set it on the clip; empty falls back to the clip's folder.",
                overlayLabel);
            return false;
        }

        float length = ClipLength(clip);
        if (playing)
        {
            double now = EditorApplication.timeSinceStartup;
            if (lastTick > 0d)
            {
                previewTime += (float)(now - lastTick);
            }

            lastTick = now;
            if (previewTime >= length)
            {
                previewTime %= length;
            }
        }
        else
        {
            lastTick = 0d;
        }

        clip.SampleAnimation(previewInstance, previewTime);

        // Root motion would walk the character out of frame; the preview shows
        // the motion, the timeline owns the travel.
        previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        if (Event.current.type == EventType.Repaint)
        {
            // Lights and camera are reapplied every frame: BeginPreview resets
            // camera state behind our back.
            SetupLights();
            ApplyCamera();
            previewUtility.BeginPreview(rect, previewBackground);
            previewUtility.Render();
            Texture previewTexture = previewUtility.EndPreview();
            GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);
        }

        DrawOverlay(rect, clip, length, damageFrame);
        return playing;
    }

    public void Dispose()
    {
        DisposeContent();
        if (previewUtility != null)
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }
    }

    private void EnsureReady(AnimationClip clip, GameObject model)
    {
        if (previewUtility == null)
        {
            previewUtility = new PreviewRenderUtility();
        }

        if (clip == currentClip && model == currentModel && previewInstance != null)
        {
            return;
        }

        DisposeContent();
        currentClip = clip;
        currentModel = model;

        previewInstance = (GameObject)UnityEngine.Object.Instantiate(model);
        previewInstance.hideFlags = HideFlags.HideAndDontSave;
        previewInstance.name = $"Ability Preview - {clip.name}";

        // A controller would fight SampleAnimation for the pose.
        foreach (Animator animator in previewInstance.GetComponentsInChildren<Animator>())
        {
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
        }

        if (previewInstance.GetComponentInChildren<Animator>() == null)
        {
            previewInstance.AddComponent<Animator>().enabled = false;
        }

        previewUtility.AddSingleGO(previewInstance);

        playing = false;
        previewTime = 0f;
        lastTick = 0d;
        ComputeFraming(clip);
    }

    private void SetupLights()
    {
        // PreviewRenderUtility ships its lights disabled with a black ambient,
        // which renders an empty black viewport until configured here.
        previewUtility.ambientColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        Light[] lights = previewUtility.lights;
        if (lights != null && lights.Length > 0)
        {
            lights[0].enabled = true;
            lights[0].intensity = 1.25f;
            lights[0].transform.rotation = Quaternion.LookRotation(
                new Vector3(-0.45f, -0.75f, -0.55f));
        }

        if (lights != null && lights.Length > 1)
        {
            lights[1].enabled = true;
            lights[1].intensity = 0.45f;
            lights[1].transform.rotation = Quaternion.LookRotation(
                new Vector3(0.5f, -0.4f, 0.6f));
        }
    }

    private void ComputeFraming(AnimationClip clip)
    {
        clip.SampleAnimation(previewInstance, 0f);
        previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
        bool hasBounds = false;
        foreach (Renderer renderer in previewInstance.GetComponentsInChildren<Renderer>())
        {
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
        previewCenter = bounds.center;
        previewDistance = radius /
            Mathf.Tan(previewUtility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * CameraMargin;
    }

    private void ApplyCamera()
    {
        Camera camera = previewUtility.camera;
        Vector3 direction = new Vector3(0.5f, 0.35f, 1f).normalized;
        camera.transform.position = previewCenter + direction * previewDistance;
        camera.transform.LookAt(previewCenter);
        camera.nearClipPlane = previewDistance / 100f;
        camera.farClipPlane = previewDistance * 100f;
    }

    private void DrawOverlay(Rect rect, AnimationClip clip, float length, int damageFrame)
    {
        // Built here, not in the static constructor: EditorStyles is not
        // usable yet when the type initializer runs from GetPreviewTitle.
        overlayLabel ??= BuildOverlayLabel();

        float frameRate = Mathf.Max(clip.frameRate, 1f);
        int totalFrames = Mathf.Max(1, Mathf.FloorToInt(length * frameRate));
        int frame = Mathf.Clamp(Mathf.FloorToInt(previewTime * frameRate), 0, totalFrames);

        GUI.Label(
            new Rect(rect.x, rect.yMax - 30f, rect.width, 15f),
            $"{previewTime:0.00} s   Frame {frame} / {totalFrames}   ({frameRate:0.#} fps)",
            overlayLabel);
        if (damageFrame > 0)
        {
            float damageTime = (damageFrame - 1) / frameRate;
            string marker = frame + 1 == damageFrame ? "  ◀ now" : string.Empty;
            GUI.Label(
                new Rect(rect.x, rect.yMax - 15f, rect.width, 15f),
                $"Damage frame {damageFrame} @ {damageTime:0.00} s{marker}",
                overlayLabel);
        }
    }

    private static float ClipLength(AnimationClip clip)
    {
        return Mathf.Max(clip.length, 0.001f);
    }

    private static GameObject ResolvePreviewModel(AnimationClip clip)
    {
        if (!previewModelLookupDone)
        {
            previewModelLookupDone = true;
            Type type = Type.GetType(
                "FofuxoAnimationTools.Editor.ScenePreviewSpawner, Uayten.FofuxoAnimationTools.Editor");
            MethodInfo method = type?.GetMethod(
                "PreviewModel",
                BindingFlags.Public | BindingFlags.Static);
            if (method != null)
            {
                previewModelGetter = (Func<AnimationClip, GameObject>)Delegate.CreateDelegate(
                    typeof(Func<AnimationClip, GameObject>),
                    method);
            }
        }

        return previewModelGetter != null ? previewModelGetter(clip) : null;
    }

    private void DisposeContent()
    {
        if (previewInstance != null)
        {
            UnityEngine.Object.DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        currentClip = null;
        currentModel = null;
    }

    private static GUIStyle BuildBackground()
    {
        var style = new GUIStyle();
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, new Color(0.13f, 0.13f, 0.13f, 1f));
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        style.normal.background = texture;
        return style;
    }

    private static GUIStyle BuildOverlayLabel()
    {
        // Plain style on purpose: no EditorStyles dependency, so this is
        // safe whenever the first viewport draw happens.
        return new GUIStyle
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
    }
}
