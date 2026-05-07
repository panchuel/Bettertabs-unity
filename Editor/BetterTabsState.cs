using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTabs
{
    [Serializable]
    public class BetterTabSnapshot
    {
        public string folderPath;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";
    }

    [Serializable]
    public class BetterTabsState
    {
        public List<string> tabPaths = new List<string>();
        public int selectedIndex = 0;
        public List<BetterTabSnapshot> snapshots = new List<BetterTabSnapshot>();
    }
}
