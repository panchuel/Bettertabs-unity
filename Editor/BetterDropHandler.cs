using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    // Everything that can be dropped on a folder, from either panel.
    //
    // Two kinds arrive through the same event. An asset already in the project comes
    // through as a project-relative path and is MOVED. A file dragged in from the OS
    // file browser comes through as an absolute path outside the project, which
    // AssetDatabase.MoveAsset cannot touch at all — it has to be COPIED in and
    // imported, which is what Unity's own Project window does.
    internal static class BetterDropHandler
    {
        public static DragAndDropVisualMode VisualModeFor()
        {
            return HasExternalFiles() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Move;
        }

        // True if anything landed, so the caller knows to refresh.
        public static bool PerformDrop(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            if (!AssetDatabase.IsValidFolder(folder)) return false;

            DragAndDrop.AcceptDrag();

            bool changed = false;
            string[] dragged = DragAndDrop.paths;
            if (dragged == null) return false;

            foreach (string path in dragged)
            {
                if (string.IsNullOrEmpty(path)) continue;
                changed |= IsProjectPath(path)
                    ? MoveInside(path, folder)
                    : ImportFromDisk(path, folder);
            }

            if (!changed) return false;
            AssetDatabase.Refresh();
            return true;
        }

        static bool HasExternalFiles()
        {
            string[] paths = DragAndDrop.paths;
            if (paths == null) return false;

            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path) && !IsProjectPath(path)) return true;
            }
            return false;
        }

        // Unity hands project assets over as "Assets/..." while the OS hands over a
        // rooted path, so the prefix is what tells the two apart.
        static bool IsProjectPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized == "Assets"
                || normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || normalized.StartsWith("Packages/", StringComparison.Ordinal);
        }

        static bool MoveInside(string assetPath, string folder)
        {
            string destination = $"{folder}/{Path.GetFileName(assetPath)}";
            if (destination == assetPath) return false;

            string error = AssetDatabase.MoveAsset(assetPath, destination);
            if (string.IsNullOrEmpty(error)) return true;

            Debug.LogError($"BetterTabs: move failed — {error}");
            return false;
        }

        static bool ImportFromDisk(string sourcePath, string folder)
        {
            bool isDirectory = Directory.Exists(sourcePath);
            if (!isDirectory && !File.Exists(sourcePath)) return false;

            string name = Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(name)) return false;

            // Unique so a second drop of the same file does not overwrite the first.
            string destination = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}");
            string absolute = ToAbsolute(destination);

            try
            {
                if (isDirectory) CopyDirectory(sourcePath, absolute);
                else File.Copy(sourcePath, absolute);
            }
            catch (Exception e)
            {
                Debug.LogError($"BetterTabs: could not import \"{sourcePath}\" — {e.Message}");
                return false;
            }

            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ImportRecursive);
            return true;
        }

        // Asset paths are relative to the project root, which is the parent of Assets.
        static string ToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);

            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
