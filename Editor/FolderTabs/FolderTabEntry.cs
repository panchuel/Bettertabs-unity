using System;
using System.Collections.Generic;

namespace FolderTabs
{
    [Serializable]
    public class FolderTabEntry
    {
        public string path;
        public string name;
        public List<string> expandedPaths = new List<string>();
        public string searchQuery = "";

        public FolderTabEntry(string assetPath)
        {
            path = assetPath;
            name = System.IO.Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(name))
                name = assetPath;
        }
    }
}
