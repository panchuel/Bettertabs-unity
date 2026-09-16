using System;
using System.Collections.Generic;
using UnityEditor;

namespace BetterTabs
{
    public enum BetterTabKind
    {
        Folder = 0,
        Asset = 1,
        Prefab = 2,
        SceneObject = 3
    }

    [Serializable]
    public class BetterTabEntry
    {
        public BetterTabKind kind;
        public string path;            // Asset path for Folder/Asset/Prefab
        public string globalObjectId;  // Only for SceneObject
        public string name;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";

        // For Prefab/SceneObject tabs: selected child path inside the hierarchy
        // (slash-joined transform names from root: "Root/Child/SubChild").
        // Empty means the root itself is selected.
        public string hierarchySelectionPath = "";
        public float hierarchySplitterX = 220f;

        public BetterTabEntry(string assetPath)
        {
            path = assetPath;
            name = System.IO.Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(name))
                name = assetPath;

            if (AssetDatabase.IsValidFolder(assetPath))
                kind = BetterTabKind.Folder;
            else if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                kind = BetterTabKind.Prefab;
            else
                kind = BetterTabKind.Asset;
        }

        // Scene GameObject constructor.
        public BetterTabEntry(UnityEngine.GameObject sceneObject)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(sceneObject);
            globalObjectId = id.ToString();
            name = sceneObject.name;
            kind = BetterTabKind.SceneObject;
        }

        // Used by deserialization.
        public BetterTabEntry() { }
    }
}
