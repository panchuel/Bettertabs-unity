using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FolderTabs
{
    internal class FolderSearchHandler
    {
        const double DebounceSeconds = 0.2;

        string _committedQuery = "";
        string _pendingQuery = "";
        double _lastChangeTime = -1;

        List<string> _results = new List<string>();
        string _searchedRootPath = null;

        public bool IsSearching => !string.IsNullOrEmpty(_committedQuery);
        public string CommittedQuery => _committedQuery;
        public IReadOnlyList<string> Results => _results;
        public int ResultCount => _results.Count;

        // Call from OnGUI every frame. Returns true when the committed query changed.
        public bool Tick(string currentQuery, string rootPath)
        {
            if (currentQuery != _pendingQuery)
            {
                _pendingQuery = currentQuery;
                _lastChangeTime = EditorApplication.timeSinceStartup;
            }

            if (_pendingQuery != _committedQuery)
            {
                double elapsed = EditorApplication.timeSinceStartup - _lastChangeTime;
                if (elapsed >= DebounceSeconds)
                {
                    _committedQuery = _pendingQuery;
                    RefreshResults(rootPath);
                    return true;
                }
                // Still debouncing — request another repaint after delay
                return false;
            }

            // Query hasn't changed but root might have
            if (_committedQuery != "" && rootPath != _searchedRootPath)
            {
                RefreshResults(rootPath);
                return true;
            }

            return false;
        }

        public void ForceCommit(string query, string rootPath)
        {
            _pendingQuery = query;
            _committedQuery = query;
            _lastChangeTime = -1;
            RefreshResults(rootPath);
        }

        public void Clear()
        {
            _pendingQuery = "";
            _committedQuery = "";
            _lastChangeTime = -1;
            _results.Clear();
            _searchedRootPath = null;
        }

        void RefreshResults(string rootPath)
        {
            _results.Clear();
            _searchedRootPath = rootPath;

            if (string.IsNullOrEmpty(_committedQuery) || string.IsNullOrEmpty(rootPath))
                return;

            var lower = _committedQuery.ToLower();
            var guids = AssetDatabase.FindAssets("", new string[] { rootPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(assetPath))
                    continue;
                var fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (fileName.ToLower().Contains(lower))
                    _results.Add(assetPath);
            }
        }

        // Returns a RichText string with matching substring wrapped in <b> tags.
        public string Highlight(string assetPath)
        {
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(_committedQuery))
                return name;

            var lower = name.ToLower();
            var queryLower = _committedQuery.ToLower();
            int idx = lower.IndexOf(queryLower);
            if (idx < 0)
                return name;

            return name.Substring(0, idx)
                + "<b>" + name.Substring(idx, _committedQuery.Length) + "</b>"
                + name.Substring(idx + _committedQuery.Length);
        }
    }
}
