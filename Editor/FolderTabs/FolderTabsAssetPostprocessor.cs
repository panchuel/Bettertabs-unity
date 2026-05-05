using UnityEditor;

namespace FolderTabs
{
    internal class FolderTabsAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (FolderTabsWindow.CheckPathsAffectTabs(importedAssets)
                || FolderTabsWindow.CheckPathsAffectTabs(deletedAssets)
                || FolderTabsWindow.CheckPathsAffectTabs(movedAssets)
                || FolderTabsWindow.CheckPathsAffectTabs(movedFromAssetPaths))
            {
                FolderTabsWindow.RequestRefresh();
            }
        }
    }
}
