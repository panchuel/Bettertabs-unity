using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    internal class BetterProjectTreeRenderer
    {
        const int RowHeight = 20;
        const int IndentWidth = 12;
        const string Root = "Assets";

        readonly Dictionary<string, (List<string> files, List<string> folders)> _cache
            = new Dictionary<string, (List<string>, List<string>)>();
        readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();
        readonly HashSet<string> _selectedPaths = new HashSet<string>();

        string _highlightedPath;
        string _lastSelectedPath;
        string _pressedPath;
        int _pressedFrame;
        string _renamingPath;
        string _renameBuffer;
        bool _pendingRenameFocus;


        System.Action<string> _onItemSelected;
        System.Action<string> _onItemActivated;
        System.Action<List<string>> _onItemsDragged;
        System.Action _onRefresh;
        System.Action<string> _onScrollTo;

        public void Setup(
            System.Action<string> onItemSelected,
            System.Action<string> onItemActivated,
            System.Action<List<string>> onItemsDragged,
            System.Action onRefresh,
            System.Action<string> onScrollTo = null)
        {
            _onItemSelected = onItemSelected;
            _onItemActivated = onItemActivated;
            _onItemsDragged = onItemsDragged;
            _onRefresh = onRefresh;
            _onScrollTo = onScrollTo;
        }

        public void SetHighlight(string path)
        {
            _highlightedPath = path;
        }

        // Replace the current selection with a single path (used to mirror the Unity
        // selection into this panel).
        public void SetSelection(string path)
        {
            _selectedPaths.Clear();
            if (!string.IsNullOrEmpty(path))
            {
                _selectedPaths.Add(path);
                _lastSelectedPath = path;
                _highlightedPath = path;
            }
        }

        public void InvalidateCache() => _cache.Clear();

        // True while the rename field still needs focus — the window must keep
        // repainting so FocusTextInControl can take effect (otherwise the field
        // shows stale text until something else forces a repaint).
        public bool HasPendingRenameFocus => _pendingRenameFocus;

        public void StartRename(string path)
        {
            // Expand to path and highlight it
            ExpandToPath(path);
            _highlightedPath = path;

            // Scroll to path
            _onScrollTo?.Invoke(path);

            // Start renaming
            _renamingPath = path;
            _renameBuffer = Path.GetFileNameWithoutExtension(path);
            _pendingRenameFocus = true;
            // Release the previously focused text editor so IMGUI's shared (recycled)
            // editor re-initializes with the new field's text instead of keeping the
            // previously renamed item's text.
            GUIUtility.keyboardControl = 0;
        }

        public void ExpandToPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            string start = AssetDatabase.IsValidFolder(path)
                ? path
                : Path.GetDirectoryName(path)?.Replace('\\', '/');

            string current = start;
            while (!string.IsNullOrEmpty(current) && current != "." && current.StartsWith("Assets"))
            {
                _expanded[current] = true;
                string parent = Path.GetDirectoryName(current)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
        }

        public float GetYPositionOf(string targetPath)
        {
            float y = 0f;
            return GetYInFolder(Root, ref y, targetPath) ? y : -1f;
        }

        bool GetYInFolder(string folderPath, ref float y, string target)
        {
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];

            foreach (var sub in folders)
            {
                if (sub == target) return true;
                y += RowHeight;
                if (IsExpanded(sub) && GetYInFolder(sub, ref y, target))
                    return true;
            }

            foreach (var file in files)
            {
                if (file == target) return true;
                y += RowHeight;
            }

            return false;
        }

        public string SaveExpandedState()
        {
            var parts = new List<string>();
            foreach (var kv in _expanded)
                if (kv.Value) parts.Add(kv.Key);
            return string.Join("|", parts);
        }

        public void LoadExpandedState(string serialized)
        {
            _expanded.Clear();
            if (string.IsNullOrEmpty(serialized)) return;
            foreach (var p in serialized.Split('|'))
                if (!string.IsNullOrEmpty(p))
                    _expanded[p] = true;
        }

        public float MeasureHeight()
        {
            float h = RowHeight; // root folder header
            if (IsRootExpanded()) h += MeasureFolder(Root, 1);
            return h;
        }

        public void Draw(float contentWidth, bool hasFocus = true)
        {
            var ev = Event.current;

            // Handle Escape/Return for an active rename BEFORE any TextField is drawn,
            // otherwise the focused TextField swallows the first key press. The buffer
            // is internal and persists across frames, so it holds the edited text here.
            if (_renamingPath != null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape)
                { CancelRename(); ev.Use(); }
                else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                { CommitRename(); ev.Use(); }
            }

            // F2 to rename selected item (only when this panel has focus)
            if (hasFocus && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F2 && !string.IsNullOrEmpty(_lastSelectedPath))
            {
                StartRename(_lastSelectedPath);
                ev.Use();
            }

            // Delete to remove all selected items (only when this panel has focus)
            if (hasFocus && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Delete && _selectedPaths.Count > 0)
            {
                if (BetterTabsInteractionHandler.DeleteMultiple(new List<string>(_selectedPaths)))
                {
                    InvalidateCache();
                    _selectedPaths.Clear();
                    _lastSelectedPath = null;
                    _onRefresh?.Invoke();
                }
                ev.Use();
            }

            // Arrow-key navigation (only when this panel has focus and not renaming;
            // Shift+arrows are reserved for tab navigation on the window)
            if (hasFocus && _renamingPath == null && ev.type == EventType.KeyDown && !ev.shift)
                HandleArrowNavigation(ev);

            bool wasExpanded = IsRootExpanded();
            bool nowExpanded = DrawFolderRow(Root, 0, contentWidth, wasExpanded);
            if (nowExpanded != wasExpanded) _expanded[Root] = nowExpanded;
            if (nowExpanded) DrawFolder(Root, 1, contentWidth);
        }

        bool IsRootExpanded()
        {
            if (!_expanded.ContainsKey(Root)) _expanded[Root] = true;
            return _expanded[Root];
        }

        // ── Measurement ──────────────────────────────────────────────────────────

        float MeasureFolder(string folderPath, int depth)
        {
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];

            float h = files.Count * RowHeight;
            foreach (var sub in folders)
            {
                h += RowHeight;
                if (IsExpanded(sub))
                    h += MeasureFolder(sub, depth + 1);
            }
            return h;
        }

        // ── Draw ─────────────────────────────────────────────────────────────────

        // Expand or collapse a folder and all of its nested subfolders (Alt+Click).
        void SetExpandedRecursive(string folderPath, bool expanded)
        {
            EnsureCached(folderPath);
            _expanded[folderPath] = expanded;
            var (files, folders) = _cache[folderPath];
            foreach (var sub in folders)
                SetExpandedRecursive(sub, expanded);
        }

        void DrawFolder(string folderPath, int depth, float contentWidth)
        {
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];

            foreach (var sub in folders)
            {
                bool wasExpanded = IsExpanded(sub);
                bool nowExpanded = DrawFolderRow(sub, depth, contentWidth, wasExpanded);
                if (nowExpanded != wasExpanded)
                    _expanded[sub] = nowExpanded;

                if (nowExpanded)
                    DrawFolder(sub, depth + 1, contentWidth);
            }

            foreach (var file in files)
                DrawFileRow(file, depth, contentWidth);
        }

        bool DrawFolderRow(string folderPath, int depth, float contentWidth, bool expanded)
        {
            var rect = GUILayoutUtility.GetRect(contentWidth, RowHeight);
            float indent = depth * IndentWidth + 2;
            var ev = Event.current;

            bool isHighlighted = folderPath == _highlightedPath;
            bool isSelected = IsSelected(folderPath);
            bool isRenaming = folderPath == _renamingPath;

            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 0.7f));
            else if (isHighlighted)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 0.3f));

            // ── Inline rename ─────────────────────────────────────────────────
            if (isRenaming)
            {
                var folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
                if (folderIcon != null)
                    GUI.DrawTexture(new Rect(rect.x + indent + 2, rect.y + 2, 16, 16), folderIcon, ScaleMode.ScaleToFit);

                // Unique control name per path so IMGUI's shared text editor doesn't
                // reuse the cached text from a previously renamed item.
                string controlName = "BetterProjectRename_" + folderPath;
                GUI.SetNextControlName(controlName);
                var renameFieldRect = new Rect(rect.x + indent + 22, rect.y + 1, rect.width - indent - 26, rect.height - 2);
                string newName = EditorGUI.TextField(renameFieldRect, _renameBuffer, EditorStyles.miniTextField);
                if (newName != _renameBuffer) _renameBuffer = newName;

                // Keep requesting focus until it's actually established
                if (_pendingRenameFocus)
                {
                    EditorGUI.FocusTextInControl(controlName);
                    if (GUI.GetNameOfFocusedControl() == controlName)
                        _pendingRenameFocus = false;
                }

                // Click outside the rename row commits it
                if (ev.type == EventType.MouseDown && !rect.Contains(ev.mousePosition) && _renamingPath != null)
                    CommitRename();

                return expanded;
            }

            // ── Foldout (must be drawn BEFORE click handling) ───────────────────
            var foldoutRect = new Rect(rect.x + indent, rect.y, 20, rect.height);
            // For Root, exclude button area from click zone
            float labelRectWidth = (folderPath == Root) ? rect.width - indent - 50 : rect.width - indent - 24;
            var labelRect = new Rect(rect.x + indent + 20, rect.y, labelRectWidth, rect.height);
            bool altHeld = ev.alt;
            bool result = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true, EditorStyles.foldout);

            // Alt+Click on the foldout → expand/collapse all nested folders too
            if (result != expanded && altHeld)
                SetExpandedRecursive(folderPath, result);

            // ── Click: select/highlight; double-click → open as tab ──────────
            // Only handle clicks in label area, not foldout triangle
            if (ev.type == EventType.MouseDown && labelRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                // Commit any active rename on a different item before selecting
                if (_renamingPath != null && _renamingPath != folderPath)
                    CommitRename();

                if (ev.clickCount == 2)
                { _onItemActivated?.Invoke(folderPath); ev.Use(); }
                else
                {
                    _pressedPath = folderPath;
                    _pressedFrame = Time.frameCount;
                    // If already selected and no modifiers, keep selection for potential drag
                    if (!IsSelected(folderPath) || ev.control || ev.shift)
                        SelectPath(folderPath, ev.control, ev.shift);
                    ev.Use();
                }
            }

            // ── MouseUp: detect if it was a pure click (no drag) ───────────────
            if (ev.type == EventType.MouseUp && _pressedPath == folderPath && Time.frameCount == _pressedFrame)
            {
                // Pure click without drag → change selection to just this item
                if (!ev.control && !ev.shift && IsSelected(folderPath) && _selectedPaths.Count > 1)
                {
                    _selectedPaths.Clear();
                    _selectedPaths.Add(folderPath);
                    _lastSelectedPath = folderPath;
                    _onItemSelected?.Invoke(folderPath);
                }
                _pressedPath = null;
            }

            // ── Drag: allow dragging folder to tab bar or other panels ────────
            // Note: don't consume MouseDrag event — let ScrollView handle auto-scroll
            if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                _onItemsDragged?.Invoke(GetDragPaths(folderPath));

            // ── Draw folder label ─────────────────────────────────────────────
            var icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel);
            if (isSelected || isHighlighted) labelStyle.normal.textColor = Color.white;

            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + indent + 2, rect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);

            // For Assets folder, reserve space for the "+" button
            float labelWidth = (folderPath == Root) ? rect.width - indent - 50 : rect.width - indent - 26;
            GUI.Label(
                new Rect(rect.x + indent + 22, rect.y, labelWidth, rect.height),
                Path.GetFileName(folderPath),
                labelStyle);

            // Draw "+" button for Assets folder
            if (folderPath == Root)
            {
                var buttonRect = new Rect(rect.x + rect.width - 24, rect.y + 2, 20, rect.height - 4);
                if (GUI.Button(buttonRect, "+", EditorStyles.miniButton))
                {
                    BetterTabsInteractionHandler.BuildFolderContextMenu(
                        folderPath,
                        p => StartRename(p),
                        _ => { InvalidateCache(); _onRefresh?.Invoke(); },
                        () => { InvalidateCache(); _onRefresh?.Invoke(); })
                        .ShowAsContext();
                }
            }

            // ── Context menu ──────────────────────────────────────────────────
            if (ev.type == EventType.ContextClick && rect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildFolderContextMenu(
                    folderPath,
                    p => StartRename(p),
                    _ => { InvalidateCache(); _onRefresh?.Invoke(); },
                    () => { InvalidateCache(); _onRefresh?.Invoke(); })
                    .ShowAsContext();
                ev.Use();
            }

            // ── Drop GameObject from scene → create prefab in this folder ────
            HandleGameObjectDropOnFolder(rect, folderPath, ev);

            return result;
        }

        void HandleGameObjectDropOnFolder(Rect rect, string folderPath, Event ev)
        {
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!rect.Contains(ev.mousePosition)) return;

            bool hasSceneGO = false;
            if (DragAndDrop.objectReferences != null)
            {
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is GameObject go && !AssetDatabase.Contains(go)) { hasSceneGO = true; break; }
            }
            bool hasAssetPaths = DragAndDrop.paths != null && DragAndDrop.paths.Length > 0;
            if (!hasSceneGO && !hasAssetPaths) return;

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                ev.Use();
                return;
            }

            DragAndDrop.AcceptDrag();

            if (hasSceneGO)
            {
                foreach (var o in DragAndDrop.objectReferences)
                {
                    if (o is GameObject go && !AssetDatabase.Contains(go))
                    {
                        string p = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{go.name}.prefab");
                        PrefabUtility.SaveAsPrefabAssetAndConnect(go, p, InteractionMode.UserAction);
                    }
                }
            }

            if (hasAssetPaths)
            {
                foreach (var srcPath in DragAndDrop.paths)
                {
                    if (string.IsNullOrEmpty(srcPath)) continue;
                    string parent = Path.GetDirectoryName(srcPath)?.Replace('\\', '/');
                    if (parent == folderPath) continue;
                    if (srcPath == folderPath) continue;
                    if (folderPath.StartsWith(srcPath + "/")) continue;

                    string fileName = Path.GetFileName(srcPath);
                    string destPath = $"{folderPath}/{fileName}";
                    string err = AssetDatabase.MoveAsset(srcPath, destPath);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"BetterTabs: Move failed — {err}");
                }
                AssetDatabase.Refresh();
            }

            InvalidateCache();
            _onRefresh?.Invoke();
            ev.Use();
        }

        void DrawFileRow(string path, int depth, float contentWidth)
        {
            var rect = GUILayoutUtility.GetRect(contentWidth, RowHeight);
            float indent = depth * IndentWidth + 2;
            var ev = Event.current;

            bool isHighlighted = path == _highlightedPath;
            bool isSelected = IsSelected(path);
            bool isRenaming = path == _renamingPath;

            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 0.7f));
            else if (isHighlighted)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 0.3f));
            else
            {
                int row = Mathf.RoundToInt(rect.y / RowHeight);
                if (row % 2 == 0)
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));
            }

            var icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + indent, rect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);

            // ── Inline rename ─────────────────────────────────────────────────
            if (isRenaming)
            {
                // Unique control name per path so IMGUI's shared text editor doesn't
                // reuse the cached text from a previously renamed item.
                string controlName = "BetterProjectRename_" + path;
                GUI.SetNextControlName(controlName);
                var renameFieldRect = new Rect(rect.x + indent + 20, rect.y + 1, rect.width - indent - 24, rect.height - 2);
                string newName = EditorGUI.TextField(renameFieldRect, _renameBuffer, EditorStyles.miniTextField);
                if (newName != _renameBuffer) _renameBuffer = newName;

                // Keep requesting focus until it's actually established
                if (_pendingRenameFocus)
                {
                    EditorGUI.FocusTextInControl(controlName);
                    if (GUI.GetNameOfFocusedControl() == controlName)
                        _pendingRenameFocus = false;
                }

                // Click outside the rename row commits it
                if (ev.type == EventType.MouseDown && !rect.Contains(ev.mousePosition) && _renamingPath != null)
                    CommitRename();

                return;
            }

            var labelStyle = new GUIStyle(EditorStyles.miniLabel);
            if (isSelected || isHighlighted) labelStyle.normal.textColor = Color.white;
            GUI.Label(
                new Rect(rect.x + indent + 20, rect.y, contentWidth - indent - 20, rect.height),
                Path.GetFileNameWithoutExtension(path),
                labelStyle);

            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition) && ev.button == 0)
            {
                // Commit any active rename on a different item before selecting
                if (_renamingPath != null && _renamingPath != path)
                    CommitRename();

                if (ev.clickCount == 2) { _onItemActivated?.Invoke(path); ev.Use(); }
                else
                {
                    _pressedPath = path;
                    _pressedFrame = Time.frameCount;
                    // If already selected and no modifiers, keep selection for potential drag
                    if (!IsSelected(path) || ev.control || ev.shift)
                        SelectPath(path, ev.control, ev.shift);
                    ev.Use();
                }
            }

            // ── MouseUp: detect if it was a pure click (no drag) ───────────────
            if (ev.type == EventType.MouseUp && _pressedPath == path && Time.frameCount == _pressedFrame)
            {
                // Pure click without drag → change selection to just this item
                if (!ev.control && !ev.shift && IsSelected(path) && _selectedPaths.Count > 1)
                {
                    _selectedPaths.Clear();
                    _selectedPaths.Add(path);
                    _lastSelectedPath = path;
                    _onItemSelected?.Invoke(path);
                }
                _pressedPath = null;
            }

            if (ev.type == EventType.ContextClick && rect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildContextMenu(
                    path,
                    p => StartRename(p),
                    _ => { InvalidateCache(); _onRefresh?.Invoke(); },
                    () => { InvalidateCache(); _onRefresh?.Invoke(); })
                    .ShowAsContext();
                ev.Use();
            }

            // Note: don't consume MouseDrag event — let ScrollView handle auto-scroll
            if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                _onItemsDragged?.Invoke(GetDragPaths(path));
        }

        // ── Inline rename ─────────────────────────────────────────────────────

        void CommitRename()
        {
            if (_renamingPath == null) return;
            string path = _renamingPath;
            _renamingPath = null;
            _pendingRenameFocus = false;

            string trimmed = (_renameBuffer ?? "").Trim();
            string oldName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(trimmed) && trimmed != oldName)
            {
                string err = AssetDatabase.RenameAsset(path, trimmed);
                if (!string.IsNullOrEmpty(err))
                    Debug.LogError($"BetterTabs: Rename failed — {err}");
                AssetDatabase.Refresh();
                InvalidateCache();
                _onRefresh?.Invoke();
            }
            _renameBuffer = null;
        }

        void CancelRename()
        {
            _renamingPath = null;
            _renameBuffer = null;
            _pendingRenameFocus = false;
        }

        // ── Keyboard navigation ───────────────────────────────────────────────────

        void HandleArrowNavigation(Event ev)
        {
            switch (ev.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    ev.Use();
                    break;
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    ev.Use();
                    break;
                case KeyCode.RightArrow:
                    // On a collapsed folder → expand it; otherwise move into first child
                    if (!string.IsNullOrEmpty(_lastSelectedPath) && AssetDatabase.IsValidFolder(_lastSelectedPath))
                    {
                        if (!IsExpanded(_lastSelectedPath))
                            _expanded[_lastSelectedPath] = true;
                        else
                            MoveSelection(1);
                    }
                    ev.Use();
                    break;
                case KeyCode.LeftArrow:
                    // On an expanded folder → collapse it; otherwise move to parent folder
                    if (!string.IsNullOrEmpty(_lastSelectedPath)
                        && AssetDatabase.IsValidFolder(_lastSelectedPath) && IsExpanded(_lastSelectedPath))
                    {
                        _expanded[_lastSelectedPath] = false;
                    }
                    else if (!string.IsNullOrEmpty(_lastSelectedPath))
                    {
                        string parent = Path.GetDirectoryName(_lastSelectedPath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(parent) && parent != Root && parent.StartsWith(Root))
                            SelectSingle(parent);
                    }
                    ev.Use();
                    break;
            }
        }

        void MoveSelection(int delta)
        {
            var paths = new List<string>();
            CollectAllPaths(Root, paths);
            if (paths.Count == 0) return;

            int idx = string.IsNullOrEmpty(_lastSelectedPath) ? -1 : paths.IndexOf(_lastSelectedPath);
            idx = Mathf.Clamp(idx + delta, 0, paths.Count - 1);
            SelectSingle(paths[idx]);
        }

        void SelectSingle(string path)
        {
            _selectedPaths.Clear();
            _selectedPaths.Add(path);
            _lastSelectedPath = path;
            _onItemSelected?.Invoke(path);
            _onScrollTo?.Invoke(path);
        }

        // ── Selection helpers ────────────────────────────────────────────────────

        public HashSet<string> GetSelectedPaths() => _selectedPaths;

        List<string> GetDragPaths(string draggedPath)
        {
            if (_selectedPaths.Contains(draggedPath))
                return new List<string>(_selectedPaths);
            return new List<string> { draggedPath };
        }

        bool IsSelected(string path) => _selectedPaths.Contains(path);

        void SelectPath(string path, bool additive, bool rangeSelect)
        {
            if (rangeSelect && !string.IsNullOrEmpty(_lastSelectedPath))
            {
                // Shift+click: select range from last to current
                _selectedPaths.Clear();
                SelectRangeBetween(_lastSelectedPath, path);
                _onItemSelected?.Invoke(path);
            }
            else if (additive)
            {
                // Ctrl+click: toggle selection
                if (_selectedPaths.Contains(path))
                    _selectedPaths.Remove(path);
                else
                    _selectedPaths.Add(path);
                _onItemSelected?.Invoke(path);
            }
            else
            {
                // Normal click: select only this
                _selectedPaths.Clear();
                _selectedPaths.Add(path);
                _onItemSelected?.Invoke(path);
            }
            _lastSelectedPath = path;
        }

        void SelectRangeBetween(string from, string to)
        {
            var paths = new List<string>();
            CollectAllPaths(Root, paths);

            int fromIdx = paths.IndexOf(from);
            int toIdx = paths.IndexOf(to);
            if (fromIdx < 0 || toIdx < 0) return;

            int start = Mathf.Min(fromIdx, toIdx);
            int end = Mathf.Max(fromIdx, toIdx);
            for (int i = start; i <= end; i++)
                _selectedPaths.Add(paths[i]);
        }

        void CollectAllPaths(string folderPath, List<string> result)
        {
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];

            foreach (var sub in folders)
            {
                result.Add(sub);
                if (IsExpanded(sub))
                    CollectAllPaths(sub, result);
            }

            foreach (var file in files)
                result.Add(file);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        bool IsExpanded(string path) => _expanded.TryGetValue(path, out var v) && v;

        void EnsureCached(string folderPath)
        {
            if (_cache.ContainsKey(folderPath)) return;

            var files = new List<string>();
            var folders = new List<string>();

            var guids = AssetDatabase.FindAssets("", new string[] { folderPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parent != folderPath) continue;

                if (AssetDatabase.IsValidFolder(assetPath))
                    folders.Add(assetPath);
                else
                    files.Add(assetPath);
            }

            _cache[folderPath] = (files, folders);
        }
    }
}
