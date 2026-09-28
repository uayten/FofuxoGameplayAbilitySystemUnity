using UnityEditor;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Editor
{
    /// <summary>
    /// The explicit save every authored asset Inspector draws at the top. Inspector
    /// edits only raise a dirty flag, and a domain reload -- entering or leaving
    /// play mode, recompiling, or restarting the Editor -- reloads the asset from
    /// disk and drops whatever was never written. Saving the scene does not save an
    /// asset, so the asset needs a button of its own.
    ///
    /// An asset and the objects embedded in it are one file: they are dirty, saved
    /// and warned about together.
    /// </summary>
    internal static class AssetSaveBar
    {
        /// <summary>
        /// Draws the saved state and the save button, plus a warning when the edit
        /// happened in play mode and leaving it would discard the change.
        /// </summary>
        public static void Draw(Object asset, string buttonLabel)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            bool dirty = HasUnsavedChanges(asset, assetPath);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    dirty ? "Unsaved changes" : "Saved",
                    dirty ? EditorStyles.boldLabel : EditorStyles.label);

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button(buttonLabel, GUILayout.Width(110f)))
                    {
                        Save(asset, assetPath);
                    }
                }
            }

            if (dirty && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Edited during play mode. Leaving play mode reloads this asset " +
                    "from disk and discards the change unless it is saved first.",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Called when the Inspector stops showing the asset, which is what
        /// selecting another one does. Unsaved edits survive in memory but are
        /// discarded by a domain reload, so leaving them behind silently is the one
        /// way to lose authoring work.
        /// </summary>
        public static void WarnIfLeavingUnsavedChanges(Object asset, string buttonLabel)
        {
            if (asset == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath) || !HasUnsavedChanges(asset, assetPath))
            {
                return;
            }

            Debug.LogWarning(
                $"'{asset.name}' still has unsaved changes. Select it again and " +
                $"press {buttonLabel}, or press Ctrl+S, before a domain reload discards them.",
                asset);
        }

        private static bool HasUnsavedChanges(Object asset, string assetPath)
        {
            if (EditorUtility.IsDirty(asset))
            {
                return true;
            }

            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != asset && EditorUtility.IsDirty(embedded))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the asset and its embedded objects, which live as sub-assets of
        /// the same file and can be dirty on their own.
        /// </summary>
        private static void Save(Object asset, string assetPath)
        {
            EditorUtility.SetDirty(asset);
            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (embedded != null && embedded != asset)
                {
                    EditorUtility.SetDirty(embedded);
                }
            }

            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
