using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inline animation preview for the ability Inspector.
///
/// Renders the preview clip on a hidden model instance with
/// <see cref="PreviewRenderUtility"/> and poses it with
/// <see cref="AnimationClip.SampleAnimation"/>. This exists because hosting the
/// clip's own editor as a nested sub-editor is not supported: the project's
/// custom clip inspector forwards the preview to Unity's internal
/// AnimationClipEditor, whose preview state is missing off the main preview
/// pane and throws a NullReferenceException.
///
/// The model is resolved the same way everywhere else in the Fofuxo tooling:
/// nearest rigged model up from the clip's folder, remembered per folder. The
/// lookup goes through the animation tools by reflection so this package keeps
/// no dependency on them; without them (or without a configured model) the
/// section explains what to do instead of failing.
/// </summary>
sealed class AbilityAnimationPreview
{
    private const float PreviewHeight = 240f;
    private const float CameraMargin = 1.6f;

    private static readonly GUIStyle previewBackground = BuildBackground();
    private static Func<AnimationClip, GameObject> previewModelGetter;
    private static bool previewModelLookupDone;

    private PreviewRenderUtility previewUtility;
    private GameObject previewInstance;
    private AnimationClip currentClip;
    private GameObject currentModel;
    private bool playing = true;
    private float previewTime;
    private double lastTick;

    /// <summary>
    /// Draws the transport controls and the rendered preview. Returns true
    /// while playing so the host editor can keep repainting.
    /// </summary>
    public bool Draw(AnimationClip clip, int damageFrame)
    {
        GameObject model = ResolvePreviewModel(clip);
        if (model == null)
        {
            EditorGUILayout.HelpBox(
                "No preview model for this clip. Open the clip and set Preview Model " +
                "in its Scene Preview block; the choice is remembered for the whole folder.",
                MessageType.Info);
            DisposeContent();
            return false;
        }

        EnsureReady(clip, model);
        if (previewUtility == null || previewInstance == null)
        {
            EditorGUILayout.HelpBox("Animation preview is unavailable.", MessageType.Warning);
            return false;
        }

        float length = Mathf.Max(clip.length, 0.001f);

        using (new EditorGUILayout.HorizontalScope())
        {
            playing = GUILayout.Toggle(
                playing,
                playing ? "Pause" : "Play",
                EditorStyles.miniButton,
                GUILayout.Width(60f));
            float scrubbed = GUILayout.HorizontalSlider(previewTime, 0f, length);
            if (!Mathf.Approximately(scrubbed, previewTime))
            {
                previewTime = scrubbed;
            }
        }

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

        Rect rect = GUILayoutUtility.GetRect(
            GUIContent.none,
            GUIStyle.none,
            GUILayout.Height(PreviewHeight),
            GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            previewUtility.BeginPreview(rect, previewBackground);
            previewUtility.Render();
            previewUtility.EndPreview();
        }

        DrawTimeLabel(clip, previewTime, length, damageFrame);
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
        previewTime = 0f;
        lastTick = 0d;
        playing = true;
        FrameCamera(clip);
    }

    private void FrameCamera(AnimationClip clip)
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

        Camera camera = previewUtility.camera;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
        float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * CameraMargin;
        Vector3 direction = new Vector3(0.5f, 0.35f, 1f).normalized;
        camera.transform.position = bounds.center + direction * distance;
        camera.transform.LookAt(bounds.center);
        camera.nearClipPlane = distance / 100f;
        camera.farClipPlane = distance * 100f;
    }

    private static void DrawTimeLabel(AnimationClip clip, float time, float length, int damageFrame)
    {
        float frameRate = Mathf.Max(clip.frameRate, 1f);
        int totalFrames = Mathf.Max(1, Mathf.FloorToInt(length * frameRate));
        int frame = Mathf.Clamp(Mathf.FloorToInt(time * frameRate), 0, totalFrames);
        EditorGUILayout.LabelField(
            $"{time:0.00} s   Frame {frame} / {totalFrames}   ({frameRate:0.#} fps)");
        if (damageFrame > 0)
        {
            float damageTime = (damageFrame - 1) / frameRate;
            string marker = frame + 1 == damageFrame ? "  ◀ now" : string.Empty;
            EditorGUILayout.LabelField(
                $"Damage frame {damageFrame} @ {damageTime:0.00} s{marker}");
        }
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
}
