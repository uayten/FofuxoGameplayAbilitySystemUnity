using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// Inspector for the reusable physics force effect. The curves are authored per
    /// level, so the useful thing to show next to them is what a level actually
    /// resolves to: the velocity change the target receives, how long it stays in
    /// the air, and roughly how far it travels. Authoring a raw number and guessing
    /// the result is the mistake this preview exists to prevent.
    /// </summary>
    [CustomEditor(typeof(PhysicsForceEffectDefinition))]
    public sealed class PhysicsForceEffectDefinitionEditor : GameplayEffectDefinitionEditor
    {
        private static readonly int[] PreviewLevels = { 1, 25, 50, 75, 100 };

        /// <summary>
        /// Matches the default <c>controlledLinearDamping</c> of
        /// <see cref="AbilityPhysicsBody"/>. Reach is an estimate for authoring, not
        /// a simulation: an actor whose physics body is tuned differently, or one
        /// that hits a wall, travels a different distance.
        /// </summary>
        private const float ReferenceLinearDamping = 2f;

        private bool previewExpanded = true;

        public override void OnInspectorGUI()
        {
            DrawSaveBar();
            DrawNamingViolation();
            DrawDefaultInspector();
            DrawEffectValidation();

            PhysicsForceEffectDefinition effect = (PhysicsForceEffectDefinition)target;
            EditorGUILayout.Space();
            previewExpanded = EditorGUILayout.Foldout(previewExpanded, "Resolved Levels", true);
            if (!previewExpanded)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "One velocity change applied with ForceMode.VelocityChange, so mass " +
                "does not change the result. Air time and reach assume flat ground, " +
                $"gravity {-Physics.gravity.y:0.##} m/s², and the default physics " +
                $"body damping of {ReferenceLinearDamping:0.##}.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(true))
            {
                DrawHeaderRow();
                for (int i = 0; i < PreviewLevels.Length; i++)
                {
                    DrawLevelRow(effect, PreviewLevels[i]);
                }
            }
        }

        private static void DrawHeaderRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Level", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                EditorGUILayout.LabelField("Horizontal", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Vertical", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Air Time", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Reach", EditorStyles.miniBoldLabel);
            }
        }

        private static void DrawLevelRow(PhysicsForceEffectDefinition effect, int level)
        {
            Vector3 velocityChange = effect.ResolveVelocityChange(level, Vector3.forward);
            float horizontal = new Vector2(velocityChange.x, velocityChange.z).magnitude;
            float vertical = velocityChange.y;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(level.ToString(), GUILayout.Width(48f));
                EditorGUILayout.LabelField($"{horizontal:0.##} m/s");
                EditorGUILayout.LabelField($"{vertical:0.##} m/s");
                EditorGUILayout.LabelField($"{ResolveAirTime(vertical):0.##} s");
                EditorGUILayout.LabelField($"~{ResolveReach(horizontal):0.##} m");
            }
        }

        /// <summary>Time before the body returns to its launch height.</summary>
        private static float ResolveAirTime(float verticalVelocity)
        {
            float gravity = Mathf.Abs(Physics.gravity.y);
            if (verticalVelocity <= 0f || gravity <= Mathf.Epsilon)
            {
                return 0f;
            }

            return 2f * verticalVelocity / gravity;
        }

        /// <summary>
        /// Horizontal travel of a body whose velocity decays under linear damping,
        /// which is the integral of that decay and therefore the distance the target
        /// covers before it settles.
        /// </summary>
        private static float ResolveReach(float horizontalVelocity)
        {
            return horizontalVelocity / ReferenceLinearDamping;
        }
    }
}
