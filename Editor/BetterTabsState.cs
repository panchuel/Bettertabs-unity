using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTabs
{
    [Serializable]
    public class BetterTabSnapshot
    {
        public BetterTabKind kind;
        public string path;             // For Folder/Asset/Prefab
        public string globalObjectId;   // For SceneObject
        public string name;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";
        public string hierarchySelectionPath = "";
        public float hierarchySplitterX = 220f;
    }

    [Serializable]
    public class BetterTabsState
    {
        public int selectedIndex = 0;
        public List<BetterTabSnapshot> snapshots = new List<BetterTabSnapshot>();
    }
}
