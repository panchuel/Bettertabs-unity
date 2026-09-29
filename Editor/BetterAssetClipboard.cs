using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    // Copy / Cut / Paste of assets between folders inside BetterTabs. Paths are kept
    // rather than objects: pasting copies or moves the files through the AssetDatabase,
    // so GUIDs survive a cut exactly as they do when dragging in the Project window.
    internal static class BetterAssetClipboard
    {
        static readonly List<string> s_paths = new List<string>();
        static bool s_isCut;

        public static bool HasItems => s_paths.Count > 0;

        public static void Copy(List<string> paths)
        {
            Store(paths, false);
        }

        public static void Cut(List<string> paths)
        {
            Store(paths, true);
        }

        // Returns the paths that now exist in the folder, so the caller can select them.
        public static List<string> PasteInto(string folder)
        {
            List<string> pasted = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return pasted;

            foreach (string source in s_paths)
            {
                if (!AssetDatabase.AssetPathExists(source)) continue;

                // A folder cannot go inside itself or any of its own subfolders.
                if (folder == source || folder.StartsWith(source + "/")) continue;

                string destination = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Path.GetFileName(source)}");

                if (s_isCut)
                {
                    string sourceFolder = Path.GetDirectoryName(source)?.Replace('\\', '/');
                    if (sourceFolder == folder) continue;

                    string error = AssetDatabase.MoveAsset(source, destination);
                    if (string.IsNullOrEmpty(error)) pasted.Add(destination);
                    else Debug.LogError($"BetterTabs: move failed — {error}");
                }
                else if (AssetDatabase.CopyAsset(source, destination))
                {
                    pasted.Add(destination);
                }
            }

            // A cut is used up by its paste; a copy can be pasted again.
            if (s_isCut)
            {
                s_paths.Clear();
                s_isCut = false;
            }

            return pasted;
        }

        static void Store(List<string> paths, bool isCut)
        {
            s_paths.Clear();
            foreach (string path in paths)
            {
                // "Assets" itself and sub-assets (a sprite inside a texture) cannot be
                // copied or moved on their own.
                if (string.IsNullOrEmpty(path) || path == "Assets" || BetterAssetTreeView.IsSubAssetKey(path)) continue;
                s_paths.Add(path);
            }
            s_isCut = isCut;
        }
    }
}
