using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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
        // Button indices returned by EditorUtility.DisplayDialogComplex.
        const int OriginalPrefabChoice = 0;
        const int CancelChoice = 1;

        public static DragAndDropVisualMode VisualModeFor()
        {
            // Files from the OS and objects from the Hierarchy both create something new
            // in the folder, so they show the copy cursor; project assets are moved.
            return HasExternalFiles() || HasSceneObjects() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Move;
        }

        // True if anything landed, so the caller knows to refresh.
        public static bool PerformDrop(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            if (!AssetDatabase.IsValidFolder(folder)) return false;

            DragAndDrop.AcceptDrag();

            bool changed = false;
            string[] dragged = DragAndDrop.paths;
            if (dragged != null)
            {
                foreach (string path in dragged)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    changed |= IsProjectPath(path)
                        ? MoveInside(path, folder)
                        : ImportFromDisk(path, folder);
                }
            }

            // A drag from the Hierarchy carries no paths at all, only the scene objects,
            // which is why dropping one here used to do nothing.
            Object[] objects = DragAndDrop.objectReferences;
            if (objects != null)
            {
                foreach (Object dropped in objects)
                {
                    GameObject sceneObject = dropped as GameObject;
                    if (sceneObject != null && IsSceneObject(sceneObject))
                        changed |= CreatePrefab(sceneObject, folder);
                }
            }

            if (!changed) return false;
            AssetDatabase.Refresh();
            return true;
        }

        static bool HasSceneObjects()
        {
            Object[] objects = DragAndDrop.objectReferences;
            if (objects == null) return false;

            foreach (Object dropped in objects)
            {
                GameObject sceneObject = dropped as GameObject;
                if (sceneObject != null && IsSceneObject(sceneObject)) return true;
            }
            return false;
        }

        static bool IsSceneObject(GameObject gameObject)
        {
            return !EditorUtility.IsPersistent(gameObject);
        }

        // Same result as dropping a Hierarchy object on Unity's Project window: a new prefab
        // named after the object, with the scene object connected to it. An object that is
        // already a prefab instance gets Unity's own choice of an original prefab or a variant.
        static bool CreatePrefab(GameObject sceneObject, string folder)
        {
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{sceneObject.name}.prefab");

            if (PrefabUtility.IsOutermostPrefabInstanceRoot(sceneObject))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Create Prefab or Variant?",
                    $"Would you like to create a new original Prefab or a variant of '{sceneObject.name}'?",
                    "Original Prefab",
                    "Cancel",
                    "Prefab Variant");

                if (choice == CancelChoice) return false;

                // Saving an instance root as-is produces a variant; an original needs the
                // instance unpacked first, which is what Unity does for that choice.
                if (choice == OriginalPrefabChoice)
                    PrefabUtility.UnpackPrefabInstance(sceneObject, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(sceneObject, prefabPath, InteractionMode.UserAction, out bool success);
            if (!success) Debug.LogError($"BetterTabs: could not create a prefab from '{sceneObject.name}'.");
            return success;
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
