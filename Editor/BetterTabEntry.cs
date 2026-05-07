using System;
using System.Collections.Generic;

namespace BetterTabs
{
    [Serializable]
    public class BetterTabEntry
    {
        public string path;
        public string name;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";

        public BetterTabEntry(string assetPath)
        {
            path = assetPath;
            name = System.IO.Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(name))
                name = assetPath;
        }
    }
}
