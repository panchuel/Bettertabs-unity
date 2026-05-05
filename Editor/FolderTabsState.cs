using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolderTabs
{
    [Serializable]
    public class FolderTabSnapshot
    {
        public string folderPath;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";
    }

    [Serializable]
    public class FolderTabsState
    {
        public List<string> tabPaths = new List<string>();
        public int selectedIndex = 0;
        public List<FolderTabSnapshot> snapshots = new List<FolderTabSnapshot>();
    }
}
