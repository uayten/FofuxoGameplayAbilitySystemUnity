using Fofuxo.GameplayAbilitySystem;
using UnityEditor;

/// <summary>
/// Inspector for every gameplay effect asset. It draws the naming violation,
/// the default fields, and then the one thing the default inspector cannot say:
/// whether the values authored above will actually do anything.
///
/// It is registered for derived classes too, so a new effect subclass gets both
/// boxes without shipping an editor. A subclass that wants its own panel
/// derives from this one and calls <see cref="DrawEffectValidation"/>, rather
/// than replacing the editor and losing the box.
/// </summary>
[CustomEditor(typeof(GameplayEffectDefinition), true)]
public class GameplayEffectDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawNamingViolation();
        DrawDefaultInspector();
        DrawEffectValidation();
    }

    /// <summary>
    /// Only a standalone effect asset is named by this rule from its own
    /// Inspector; an embedded one is reported by the ability that owns it.
    /// </summary>
    protected void DrawNamingViolation()
    {
        if (AssetDatabase.IsMainAsset(target))
        {
            AssetNamingConvention.DrawViolationBox(target);
        }
    }

    /// <summary>
    /// The same <c>TryValidate</c> the runtime calls before an activation, and
    /// the same one the ability that fires this effect calls. There is one copy
    /// of the rules and the Inspector is not it.
    /// </summary>
    protected void DrawEffectValidation()
    {
        if (target is GameplayEffectDefinition effect)
        {
            EditorGUILayout.Space();
            DrawValidationBox(effect);
        }
    }

    /// <summary>
    /// Shared with the ability Inspector, which draws the same box under every
    /// effect embedded in the ability asset.
    /// </summary>
    internal static void DrawValidationBox(GameplayEffectDefinition effect)
    {
        if (effect.TryValidate(out string error))
        {
            EditorGUILayout.HelpBox("Effect configuration is valid.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
