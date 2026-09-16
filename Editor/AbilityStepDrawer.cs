using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// Draws one ability step, inserting a preview button directly under its
    /// animation fields. The button points the ability's single preview clip at
    /// this step's clip, so it belongs beside the clip it copies rather than in a
    /// separate row under the list.
    ///
    /// Every field is drawn through the default handler for its own property, so
    /// headers, tooltips and attribute drawers keep working and a new field on
    /// <see cref="AbilityStep"/> appears here with no change to this file.
    ///
    /// The frame numbers are the exception: they are authored by dragging in the
    /// ability's timeline lanes, so they collapse into one folded group here. The
    /// group is not a second way to author a timeline — it writes the same fields
    /// the lanes write — it is where you type frame 31 when a pixel of lane is
    /// worth less than a keystroke.
    /// </summary>
    [CustomPropertyDrawer(typeof(AbilityStep))]
    public sealed class AbilityStepDrawer : PropertyDrawer
    {
        /// <summary>Last animation field: the button goes right after it.</summary>
        private const string ButtonAnchorField = "animationBlendDuration";
        private const string AnimationClipField = "animationClip";
        private const string PreviewClipField = "previewAnimationClip";

        /// <summary>
        /// The fields the timeline lanes drag. Their <c>Header</c> decorators travel
        /// with them into the group, which is why the group needs no headings of
        /// its own.
        /// </summary>
        private static readonly HashSet<string> FrameFields = new()
        {
            "startupEndFrame",
            "activeEndFrame",
            "recoveryEndFrame",
            "fallbackFrameRate",
            "movementUnlockFrame",
            "comboContinuationFrame",
            "comboInputEndFrame",
            "comboInputGraceFrames",
            "displacementStartFrame",
            "displacementEndFrame",
        };

        public override float GetPropertyHeight(
            SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            bool framesExpanded = IsFramesExpanded(property);
            bool framesHeaderCounted = false;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (FrameFields.Contains(child.name))
                {
                    if (!framesHeaderCounted)
                    {
                        height += spacing + EditorGUIUtility.singleLineHeight;
                        framesHeaderCounted = true;
                    }

                    if (!framesExpanded)
                    {
                        continue;
                    }
                }

                height += spacing + EditorGUI.GetPropertyHeight(child, true);
                if (child.name == ButtonAnchorField)
                {
                    height += spacing + EditorGUIUtility.singleLineHeight;
                }
            }

            return height;
        }

        public override void OnGUI(
            Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect header = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded =
                EditorGUI.Foldout(header, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawFields(position, property);
                }
            }

            EditorGUI.EndProperty();
        }

        private static void DrawFields(Rect position, SerializedProperty property)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float y = position.y + EditorGUIUtility.singleLineHeight;
            bool framesExpanded = IsFramesExpanded(property);
            bool framesHeaderDrawn = false;

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;

                if (FrameFields.Contains(child.name))
                {
                    if (!framesHeaderDrawn)
                    {
                        y += spacing;
                        framesExpanded = DrawFramesFoldout(
                            new Rect(
                                position.x,
                                y,
                                position.width,
                                EditorGUIUtility.singleLineHeight),
                            property,
                            framesExpanded);
                        y += EditorGUIUtility.singleLineHeight;
                        framesHeaderDrawn = true;
                    }

                    if (!framesExpanded)
                    {
                        continue;
                    }
                }

                float height = EditorGUI.GetPropertyHeight(child, true);
                y += spacing;
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, height), child, true);
                y += height;

                if (child.name != ButtonAnchorField)
                {
                    continue;
                }

                y += spacing;
                DrawPreviewButton(
                    new Rect(
                        position.x,
                        y,
                        position.width,
                        EditorGUIUtility.singleLineHeight),
                    property);
                y += EditorGUIUtility.singleLineHeight;
            }
        }

        /// <summary>
        /// The folded group header. Its state is kept per step of per asset — a
        /// drawer instance is shared by every element of the list, so anything
        /// stored on the drawer itself would open all four swings of a combo at
        /// once.
        /// </summary>
        private static bool DrawFramesFoldout(
            Rect rect, SerializedProperty property, bool expanded)
        {
            bool value = EditorGUI.Foldout(
                EditorGUI.IndentedRect(rect),
                expanded,
                new GUIContent(
                    "Frames (numeric)",
                    "The same fields the timeline lanes drag, for when an exact " +
                    "frame is faster to type than to aim at."),
                true);
            if (value != expanded)
            {
                SessionState.SetBool(FramesFoldoutKey(property), value);
            }

            return value;
        }

        private static bool IsFramesExpanded(SerializedProperty property)
        {
            return SessionState.GetBool(FramesFoldoutKey(property), false);
        }

        /// <summary>
        /// Keyed by the asset's GUID rather than by an instance id, which changes
        /// across a domain reload and would reopen every group at the worst moment.
        /// </summary>
        private static string FramesFoldoutKey(SerializedProperty property)
        {
            Object owner = property.serializedObject.targetObject;
            string ownerKey =
                owner != null &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(owner, out string guid, out long _)
                    ? guid
                    : owner == null ? "none" : owner.name;
            return $"Fofuxo.GAS.StepFrames.{ownerKey}.{property.propertyPath}";
        }

        private static void DrawPreviewButton(Rect rect, SerializedProperty step)
        {
            SerializedProperty previewClip =
                step.serializedObject.FindProperty(PreviewClipField);
            SerializedProperty animationClip =
                step.FindPropertyRelative(AnimationClipField);
            if (previewClip == null || animationClip == null)
            {
                return;
            }

            Object clip = animationClip.objectReferenceValue;
            bool isCurrent = clip != null && clip == previewClip.objectReferenceValue;
            GUIContent label = new(
                isCurrent ? "● Previewing This Step" : "Preview This Step",
                clip == null
                    ? "Assign an Animation Clip to preview this step."
                    : $"Plays {clip.name} in the preview panel at the bottom of "
                      + "the Inspector.");

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUI.Button(
                        EditorGUI.IndentedRect(rect), label, EditorStyles.miniButton))
                {
                    previewClip.objectReferenceValue = clip;
                }
            }
        }
    }
}
