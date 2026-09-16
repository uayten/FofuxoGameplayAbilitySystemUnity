using System;
using Fofuxo.GameplayAbilitySystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The asset naming convention, enforced where it is cheapest to obey: in the
/// Inspector of the asset that breaks it. A misnamed asset is reported with the
/// name it should have and a button that performs the rename, so the rule never
/// depends on the author remembering the prefix table.
/// </summary>
internal static class AssetNamingConvention
{
    /// <summary>Every prefix the convention defines, plus retired ones so a
    /// rename replaces them instead of stacking a new prefix on top.</summary>
    private static readonly string[] KnownPrefixes =
    {
        "GA_", "GAL_", "GAS_", "ABS_", "GTA_", "GE_", "GC_", "GIM_"
    };

    /// <summary>
    /// The prefix an object must carry, or null for a type the convention does
    /// not cover. Effects resolve to a category when their subclass implies one;
    /// an ambiguous effect only has to start with <c>GE_</c>.
    /// </summary>
    internal static string ExpectedPrefix(UnityEngine.Object asset)
    {
        return asset switch
        {
            AbilityDefinition => "GA_",
            AbilityLoadout => "GAL_",
            AttributeSetDefinition => "ABS_",
            TargetAssistDefinition => "GTA_",
            AbilityInputMap => "GIM_",
            GameplayEffectDefinition => "GE_",
            _ => null
        };
    }

    /// <summary>
    /// The prefix a rename should apply. Wider than <see cref="ExpectedPrefix"/>
    /// for effects, whose category cannot be validated but can be guessed.
    /// </summary>
    private static string SuggestedPrefix(UnityEngine.Object asset)
    {
        return asset switch
        {
            DamageEffectDefinition => "GE_Damage_",
            DebugDrawEffectDefinition => "GE_Debug_",
            _ => ExpectedPrefix(asset)
        };
    }

    internal static bool IsCompliant(UnityEngine.Object asset)
    {
        string prefix = ExpectedPrefix(asset);
        return prefix == null || (asset != null && asset.name.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The compliant form of the current name: the right prefix followed by the
    /// existing body. The body is never invented — an author who wants
    /// <c>GE_Damage_FergusAttack01</c> out of <c>Attack01</c> still types the
    /// owner, because only they know it.
    /// </summary>
    internal static string SuggestName(UnityEngine.Object asset)
    {
        string prefix = SuggestedPrefix(asset);
        if (prefix == null || asset == null)
        {
            return null;
        }

        string body = asset.name;
        foreach (string known in KnownPrefixes)
        {
            if (body.StartsWith(known, StringComparison.Ordinal))
            {
                body = body.Substring(known.Length);
                break;
            }
        }

        return body.Length == 0 ? prefix.TrimEnd('_') : prefix + body;
    }

    /// <summary>
    /// Draws nothing while the name is compliant. Otherwise reports the
    /// violation and offers the rename, which routes through
    /// <see cref="AssetDatabase.RenameAsset"/> for a main asset so the
    /// <c>.meta</c> and its GUID follow the file.
    /// </summary>
    internal static void DrawViolationBox(UnityEngine.Object asset)
    {
        if (asset == null || IsCompliant(asset))
        {
            return;
        }

        string suggestion = SuggestName(asset);
        EditorGUILayout.HelpBox(
            $"'{asset.name}' does not follow the asset naming convention. " +
            $"Expected a name starting with '{ExpectedPrefix(asset)}'.",
            MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"Rename to '{suggestion}'", GUILayout.Height(20f)))
            {
                Rename(asset, suggestion);
            }
        }
    }

    /// <summary>
    /// Renames a main asset through the AssetDatabase and an embedded sub-asset
    /// by its object name, which is where a sub-asset's identity lives.
    /// </summary>
    internal static void Rename(UnityEngine.Object asset, string newName)
    {
        if (asset == null || string.IsNullOrEmpty(newName) || newName == asset.name)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (AssetDatabase.IsMainAsset(asset))
        {
            string error = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"Could not rename '{asset.name}': {error}", asset);
                return;
            }
        }
        else
        {
            Undo.RecordObject(asset, "Rename Embedded Asset");
            asset.name = newName;
            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
    }
}

/// <summary>
/// The convention warning for the package definitions that have no Inspector of
/// their own. <see cref="AbilityDefinition"/> draws it from its own editor,
/// where it also covers the effects embedded in the ability, and
/// <see cref="AbilityLoadout"/> from <c>AbilityLoadoutEditor</c>, where the
/// naming box sits above the loadout audit.
/// </summary>
[CustomEditor(typeof(AttributeSetDefinition))]
public sealed class AttributeSetDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AssetNamingConvention.DrawViolationBox(target);
        DrawDefaultInspector();
    }
}

[CustomEditor(typeof(TargetAssistDefinition))]
public sealed class TargetAssistDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AssetNamingConvention.DrawViolationBox(target);
        DrawDefaultInspector();
    }
}
