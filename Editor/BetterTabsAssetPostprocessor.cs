using UnityEditor;

namespace BetterTabs
{
    internal class BetterTabsAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (BetterTabsWindow.CheckPathsAffectTabs(importedAssets)
                || BetterTabsWindow.CheckPathsAffectTabs(deletedAssets)
                || BetterTabsWindow.CheckPathsAffectTabs(movedAssets)
                || BetterTabsWindow.CheckPathsAffectTabs(movedFromAssetPaths))
            {
                BetterTabsWindow.RequestRefresh();
            }
        }
    }
}
