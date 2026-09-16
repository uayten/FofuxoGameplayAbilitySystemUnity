using System.Collections.Generic;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The loadout audit, drawn where the mistake is made. Every rule comes from
/// <see cref="AbilityLoadout.TryValidate(List{AbilityLoadoutValidationIssue})"/>
/// — the Inspector formats the issues and owns none of them, so the audit can
/// never drift from what the runtime believes.
///
/// The <c>gas-asset-audit</c> shell script checks the same loadout rules with
/// Unity closed, where no C# of ours can run. That copy is deliberate and
/// one-way: this type is the rule of record, and the script follows it.
/// </summary>
[CustomEditor(typeof(AbilityLoadout))]
public sealed class AbilityLoadoutEditor : Editor
{
    private readonly List<AbilityLoadoutValidationIssue> issues = new();

    public override void OnInspectorGUI()
    {
        AssetNamingConvention.DrawViolationBox(target);
        DrawDefaultInspector();
        DrawAudit((AbilityLoadout)target);
    }

    private void DrawAudit(AbilityLoadout loadout)
    {
        EditorGUILayout.Space();
        if (loadout.TryValidate(issues))
        {
            EditorGUILayout.HelpBox(
                loadout.Abilities.Count == 0
                    ? "Loadout is empty. No ability can be activated with it."
                    : $"Loadout is valid: {loadout.Abilities.Count} ability(ies), no duplicate IDs.",
                loadout.Abilities.Count == 0 ? MessageType.Warning : MessageType.Info);
            return;
        }

        // One box per issue: a merged message hides how many slots are affected,
        // and the slot number is the only thing that makes the message useful.
        for (int i = 0; i < issues.Count; i++)
        {
            AbilityLoadoutValidationIssue issue = issues[i];
            EditorGUILayout.HelpBox(issue.Message, MessageType.Error);
            if (issue.Ability == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Select ability", GUILayout.Width(110f)))
                {
                    Selection.activeObject = issue.Ability;
                    EditorGUIUtility.PingObject(issue.Ability);
                }
            }
        }
    }
}
