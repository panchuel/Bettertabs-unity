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
                state.snapshots.Add(new BetterTabSnapshot
                {
                    kind = tab.kind,
                    path = tab.path,
                    globalObjectId = tab.globalObjectId,
                    name = tab.name,
                    expandedPaths = new List<string>(tab.expandedPaths),
                    searchQuery = tab.searchQuery ?? "",
                    hierarchySelectionPath = tab.hierarchySelectionPath ?? "",
                    hierarchySplitterX = tab.hierarchySplitterX
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
            if (state == null || state.snapshots == null)
                return false;

            foreach (var snap in state.snapshots)
            {
                if (snap == null) continue;

                BetterTabEntry entry;

                if (snap.kind == BetterTabKind.SceneObject)
                {
                    if (string.IsNullOrEmpty(snap.globalObjectId)) continue;
                    entry = new BetterTabEntry
                    {
                        kind = BetterTabKind.SceneObject,
                        globalObjectId = snap.globalObjectId,
                        name = snap.name ?? "GameObject"
                    };
                }
                else
                {
                    if (string.IsNullOrEmpty(snap.path)) continue;
                    // Skip assets that no longer exist.
                    if (!AssetDatabase.IsValidFolder(snap.path)
                        && AssetDatabase.AssetPathToGUID(snap.path) == "")
                        continue;

                    entry = new BetterTabEntry(snap.path);
                }

                entry.expandedPaths = snap.expandedPaths ?? new List<string>();
                entry.searchQuery = snap.searchQuery ?? "";
                entry.hierarchySelectionPath = snap.hierarchySelectionPath ?? "";
                if (snap.hierarchySplitterX > 0f)
                    entry.hierarchySplitterX = snap.hierarchySplitterX;

                tabs.Add(entry);
            }

            selectedIndex = tabs.Count == 0 ? -1 : Mathf.Clamp(state.selectedIndex, 0, tabs.Count - 1);
            return tabs.Count > 0;
        }
    }
}
