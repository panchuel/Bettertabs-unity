using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace BetterTabs
{
    // Project-wide asset search. Queries run against the whole project rather than
    // the active tab folder, so results are the same wherever the search starts.
    internal class BetterSearchHandler
    {
        const double DebounceSeconds = 0.2;
        const int MaxResults = 500;

        string _committedQuery = "";
        string _pendingQuery = "";
        double _lastChangeTime = -1;

        readonly List<string> _results = new List<string>();

        public bool IsSearching => !string.IsNullOrEmpty(_committedQuery);
        public string CommittedQuery => _committedQuery;
        public IReadOnlyList<string> Results => _results;
        public int ResultCount => _results.Count;

        public bool Tick(string currentQuery)
        {
            if (currentQuery != _pendingQuery)
            {
                _pendingQuery = currentQuery;
                _lastChangeTime = EditorApplication.timeSinceStartup;
            }

            if (_pendingQuery == _committedQuery) return false;

            if (EditorApplication.timeSinceStartup - _lastChangeTime < DebounceSeconds)
                return false;

            _committedQuery = _pendingQuery;
            RefreshResults();
            return true;
        }

        public void ForceCommit(string query)
        {
            _pendingQuery = query;
            _committedQuery = query;
            _lastChangeTime = -1;
            RefreshResults();
        }

        public void Clear()
        {
            _pendingQuery = "";
            _committedQuery = "";
            _lastChangeTime = -1;
            _results.Clear();
        }

        void RefreshResults()
        {
            _results.Clear();
            if (string.IsNullOrEmpty(_committedQuery)) return;

            // Passed straight to AssetDatabase so the query behaves exactly like the
            // Project window search, including its filter syntax (t:Material, l:label,
            // partial names). Post-filtering here would break those filters.
            foreach (string guid in AssetDatabase.FindAssets(_committedQuery))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                _results.Add(assetPath);
                if (_results.Count >= MaxResults) break;
            }
        }

        public string Highlight(string assetPath)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(_committedQuery)) return name;

            int idx = name.ToLower().IndexOf(_committedQuery.ToLower());
            if (idx < 0) return name;

            return name.Substring(0, idx)
                + "<b>" + name.Substring(idx, _committedQuery.Length) + "</b>"
                + name.Substring(idx + _committedQuery.Length);
        }
    }
}
