using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    internal static class BetterTabsPrefs
    {
        static string PrefsKey => $"BetterTabs_State_{Application.productName}";

        public static void Save(List<BetterTabEntry> tabs, int selectedIndex)
        {
            var state = new BetterTabsState
            {
                selectedIndex = selectedIndex
            };

            foreach (var tab in tabs)
            {
                state.tabPaths.Add(tab.path);
                state.snapshots.Add(new BetterTabSnapshot
                {
                    folderPath = tab.path,
                    expandedPaths = new List<string>(tab.expandedPaths),
                    searchQuery = tab.searchQuery ?? ""
                });
            }

            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));
        }

        public static bool Load(out List<BetterTabEntry> tabs, out int selectedIndex)
        {
            tabs = new List<BetterTabEntry>();
            selectedIndex = 0;

            if (!EditorPrefs.HasKey(PrefsKey))
                return false;

            var json = EditorPrefs.GetString(PrefsKey);
            if (string.IsNullOrEmpty(json))
                return false;

            var state = JsonUtility.FromJson<BetterTabsState>(json);
            if (state == null)
                return false;

            var snapMap = new System.Collections.Generic.Dictionary<string, BetterTabSnapshot>();
            if (state.snapshots != null)
                foreach (var snap in state.snapshots)
                    if (snap != null && snap.folderPath != null)
                        snapMap[snap.folderPath] = snap;

            foreach (var tabPath in state.tabPaths)
            {
                if (string.IsNullOrEmpty(tabPath))
                    continue;
                // Skip if the asset no longer exists (deleted or moved outside Unity).
                if (!AssetDatabase.IsValidFolder(tabPath) &&
                    AssetDatabase.AssetPathToGUID(tabPath) == "")
                    continue;

                var entry = new BetterTabEntry(tabPath);

                if (snapMap.TryGetValue(tabPath, out var snap))
                {
                    entry.expandedPaths = snap.expandedPaths ?? new List<string>();
                    entry.searchQuery = snap.searchQuery ?? "";
                }

                tabs.Add(entry);
            }

            selectedIndex = tabs.Count == 0 ? -1 : Mathf.Clamp(state.selectedIndex, 0, tabs.Count - 1);
            return tabs.Count > 0;
        }
    }
}
