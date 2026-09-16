using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    internal class BetterTreeRenderer
    {
        const int RowHeight = 20;
        const int IndentWidth = 12;

        readonly Dictionary<string, (List<string> files, List<string> folders)> _cache
            = new Dictionary<string, (List<string>, List<string>)>();

        readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

        System.Action _onChanged;


        // ── Interaction state (set each frame via SetCallbacks) ───────────────
        string _selectedAssetPath;
        string _renamingPath;
        string _renameBuffer;
        bool _pendingRenameFocus;
        string _currentRootPath;
        string _pendingScrollPath;

        System.Action<string> _onSelected;
        System.Action<string> _onRenameRequested;
        System.Action<string> _onRenameCommit;
        System.Action         _onRenameCancel;
        System.Action<string> _onAssetChanged;
        System.Action         _onRefreshRequested;

        public BetterTreeRenderer(System.Action onChanged)
        {
            _onChanged = onChanged;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void LoadExpandedState(List<string> expandedPaths)
        {
            _expanded.Clear();
            if (expandedPaths == null) return;
            foreach (var p in expandedPaths)
                _expanded[p] = true;
        }

        public void SaveExpandedState(List<string> target)
        {
            target.Clear();
            foreach (var kv in _expanded)
                if (kv.Value)
                    target.Add(kv.Key);
        }

        public void InvalidateCache() => _cache.Clear();

        public void ResetRenameState() => _pendingRenameFocus = false;

        public void RequestRenameFocus()
        {
            _pendingRenameFocus = true;
            // Release the previously focused text editor so IMGUI's shared (recycled)
            // editor re-initializes with the new field's text instead of keeping the
            // previously renamed item's text.
            GUIUtility.keyboardControl = 0;
        }

        // True while the rename field still needs focus — the window must keep
        // repainting so FocusTextInControl can take effect (otherwise the field
        // shows stale text until something else forces a repaint).
        public bool HasPendingRenameFocus => _pendingRenameFocus;

        public void InvalidatePath(string folderPath)
        {
            _cache.Remove(folderPath);
            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (parent != null) _cache.Remove(parent);
        }

        public float MeasureHeight(string rootPath)
        {
            float h = RowHeight; // root folder header itself
            if (IsRootExpanded(rootPath))
                h += MeasureFolder(rootPath, 1);
            return h;
        }

        bool IsRootExpanded(string rootPath)
        {
            if (!_expanded.ContainsKey(rootPath)) _expanded[rootPath] = true;
            return _expanded[rootPath];
        }

        public void SetCallbacks(
            string selectedAssetPath,
            string renamingPath,
            string renameBuffer,
            System.Action<string> onSelected,
            System.Action<string> onRenameRequested,
            System.Action<string> onRenameCommit,
            System.Action onRenameCancel,
            System.Action<string> onAssetChanged,
            System.Action onRefreshRequested)
        {
            _selectedAssetPath   = selectedAssetPath;
            // Initialize the buffer from the path itself when a new rename starts
            // (path changed), so it's always in sync and edits persist across frames.
            if (_renamingPath != renamingPath)
                _renameBuffer = string.IsNullOrEmpty(renamingPath)
                    ? ""
                    : Path.GetFileNameWithoutExtension(renamingPath);
            _renamingPath        = renamingPath;
            _onSelected          = onSelected;
            _onRenameRequested   = onRenameRequested;
            _onRenameCommit      = onRenameCommit;
            _onRenameCancel      = onRenameCancel;
            _onAssetChanged      = onAssetChanged;
            _onRefreshRequested  = onRefreshRequested;
        }

        public void Draw(string rootPath, float contentWidth,
            System.Action<string> onAssetDragged, bool hasFocus = true)
        {
            var ev = Event.current;
            _currentRootPath = rootPath;

            // Handle Escape/Return for an active rename BEFORE any TextField is drawn,
            // otherwise the focused TextField swallows the first key press. The buffer
            // persists across frames (see SetCallbacks), so it holds the edited text here.
            if (_renamingPath != null && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape)
                { _onRenameCancel?.Invoke(); ev.Use(); }
                else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
                { _onRenameCommit?.Invoke(_renameBuffer); ev.Use(); }
            }

            bool wasExpanded = IsRootExpanded(rootPath);
            bool nowExpanded = DrawFolderHeader(rootPath, 0, contentWidth, wasExpanded);
            if (nowExpanded != wasExpanded)
            {
                _expanded[rootPath] = nowExpanded;
                _onChanged?.Invoke();
            }
            if (nowExpanded)
                DrawFolder(rootPath, 1, contentWidth, onAssetDragged, ref _dummyY);
            if (hasFocus)
                HandleKeyboardForSelection();
        }

        float _dummyY;

        // ── Position lookup (for keyboard-nav auto-scroll) ────────────────────

        // Returns the Y offset of a path within the current tree, or -1 if not visible.
        public float GetYPositionOf(string targetPath)
        {
            if (string.IsNullOrEmpty(_currentRootPath) || string.IsNullOrEmpty(targetPath))
                return -1f;
            if (targetPath == _currentRootPath) return 0f;
            float y = RowHeight; // root header row
            if (!IsExpanded(_currentRootPath)) return -1f;
            return GetYInFolder(_currentRootPath, ref y, targetPath) ? y : -1f;
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

        // ── Measurement ──────────────────────────────────────────────────────

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

        // ── Draw ─────────────────────────────────────────────────────────────

        // Expand or collapse a folder and all of its nested subfolders (Alt+Click).
        void SetExpandedRecursive(string folderPath, bool expanded)
        {
            EnsureCached(folderPath);
            _expanded[folderPath] = expanded;
            var (files, folders) = _cache[folderPath];
            foreach (var sub in folders)
                SetExpandedRecursive(sub, expanded);
        }

        void DrawFolder(string folderPath, int depth, float contentWidth,
            System.Action<string> onDragged, ref float _unused)
        {
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];

            foreach (var sub in folders)
            {
                bool wasExpanded = IsExpanded(sub);
                bool nowExpanded = DrawFolderHeader(sub, depth, contentWidth, wasExpanded);

                if (nowExpanded != wasExpanded)
                {
                    _expanded[sub] = nowExpanded;
                    _onChanged?.Invoke();
                }

                if (nowExpanded)
                    DrawFolder(sub, depth + 1, contentWidth, onDragged, ref _unused);
            }

            foreach (var file in files)
                DrawAssetRow(file, depth, contentWidth, onDragged);
        }

        // ── Folder header ────────────────────────────────────────────────────

        bool DrawFolderHeader(string folderPath, int depth, float contentWidth, bool expanded)
        {
            var rect = GUILayoutUtility.GetRect(contentWidth, RowHeight);
            float indent = depth * IndentWidth + 2;
            var ev = Event.current;

            bool isRenaming = folderPath == _renamingPath;

            // ── Inline rename mode ────────────────────────────────────────────
            if (isRenaming)
            {
                var folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
                if (folderIcon != null)
                    GUI.DrawTexture(new Rect(rect.x + indent + 2, rect.y + 2, 16, 16), folderIcon, ScaleMode.ScaleToFit);

                // Unique control name per path so IMGUI's internal TextEditor doesn't
                // reuse the cached text from a previously renamed item.
                string controlName = "BetterTabsRename_" + folderPath;
                GUI.SetNextControlName(controlName);
                var nameArea = new Rect(rect.x + indent + 22, rect.y + 1, rect.width - indent - 60, rect.height - 2);
                string newName = EditorGUI.TextField(nameArea, _renameBuffer, EditorStyles.miniTextField);
                if (newName != _renameBuffer) _renameBuffer = newName;

                // Keep requesting focus until it's actually established, so IMGUI's
                // shared text editor is re-initialized with THIS field's text (otherwise
                // it may keep showing the previously renamed item's text).
                if (_pendingRenameFocus)
                {
                    EditorGUI.FocusTextInControl(controlName);
                    if (GUI.GetNameOfFocusedControl() == controlName)
                        _pendingRenameFocus = false;
                }

                // Click outside this row commits the rename
                if (ev.type == EventType.MouseDown && !rect.Contains(ev.mousePosition))
                    _onRenameCommit?.Invoke(_renameBuffer);

                return expanded;
            }

            // ── Normal foldout ────────────────────────────────────────────────
            bool isFolderSelected = folderPath == _selectedAssetPath;
            if (isFolderSelected)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 1f));

            var icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;

            // Foldout triangle only (small area), does NOT toggle on label click
            var foldoutRect = new Rect(rect.x + indent, rect.y, 16, rect.height);
            bool altHeld = ev.alt;
            bool result = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, false, EditorStyles.foldout);

            // Alt+Click on the foldout → expand/collapse all nested folders too
            if (result != expanded && altHeld)
                SetExpandedRecursive(folderPath, result);

            // Folder icon + label
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + indent + 16, rect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);
            var folderLabelStyle = new GUIStyle(EditorStyles.miniLabel);
            if (isFolderSelected) folderLabelStyle.normal.textColor = Color.white;
            var folderLabelRect = new Rect(rect.x + indent + 34, rect.y, rect.width - indent - 38, rect.height);
            GUI.Label(folderLabelRect, Path.GetFileName(folderPath), folderLabelStyle);

            // Click on label → select (don't collapse); double-click → toggle expand
            if (ev.type == EventType.MouseDown && folderLabelRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                // Commit any active rename on a different item before selecting
                if (_renamingPath != null && _renamingPath != folderPath)
                    _onRenameCommit?.Invoke(_renameBuffer);

                if (ev.clickCount == 2) result = !expanded;
                _onSelected?.Invoke(folderPath);
                ev.Use();
            }

            // ── Right-click context menu ───────────────────────────────────────
            if (ev.type == EventType.ContextClick && rect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildFolderContextMenu(
                    folderPath, _onRenameRequested, _onAssetChanged, _onRefreshRequested)
                    .ShowAsContext();
                ev.Use();
            }

            // ── Drop on this folder (GameObject → prefab; asset → move) ───────
            HandleDropOnFolder(rect, folderPath, ev);

            return result;
        }

        public void HandleDropOnFolder(Rect rect, string folderPath, Event ev)
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
                    if (parent == folderPath) continue; // already there
                    if (srcPath == folderPath) continue; // dropping a folder on itself
                    if (folderPath.StartsWith(srcPath + "/")) continue; // would create cycle

                    string fileName = Path.GetFileName(srcPath);
                    string destPath = $"{folderPath}/{fileName}";
                    string err = AssetDatabase.MoveAsset(srcPath, destPath);
                    if (!string.IsNullOrEmpty(err))
                        Debug.LogError($"BetterTabs: Move failed — {err}");
                }
                AssetDatabase.Refresh();
            }

            _cache.Remove(folderPath);
            _onRefreshRequested?.Invoke();
            ev.Use();
        }

        // ── Asset row ────────────────────────────────────────────────────────

        void DrawAssetRow(string path, int depth, float contentWidth,
            System.Action<string> onDragged)
        {
            var rect = GUILayoutUtility.GetRect(contentWidth, RowHeight);
            float indent = depth * IndentWidth + 2;

            var ev = Event.current;
            bool isSelected = path == _selectedAssetPath;
            bool isRenaming = path == _renamingPath;

            // ── Background ───────────────────────────────────────────────────
            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 1f));
            else
            {
                int row = Mathf.RoundToInt(rect.y / RowHeight);
                if (row % 2 == 0)
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.04f));
            }

            // ── Mouse events ─────────────────────────────────────────────────
            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition) && ev.button == 0)
            {
                // Commit any active rename on a different item before selecting
                if (_renamingPath != null && _renamingPath != path)
                    _onRenameCommit?.Invoke(_renameBuffer);

                if (ev.clickCount == 2) { BetterTabsInteractionHandler.OpenAsset(path); ev.Use(); }
                else { _onSelected?.Invoke(path); ev.Use(); }
            }

            if (ev.type == EventType.ContextClick && rect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildContextMenu(
                    path, _onRenameRequested, _onAssetChanged, _onRefreshRequested)
                    .ShowAsContext();
                ev.Use();
            }

            if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
            { onDragged?.Invoke(path); ev.Use(); }

            if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                && rect.Contains(ev.mousePosition))
                HandleIncomingDrop(path, ev);

            // ── Icon ─────────────────────────────────────────────────────────
            var icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + indent, rect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);

            // ── Name / rename field ──────────────────────────────────────────
            var nameArea = new Rect(rect.x + indent + 20, rect.y, contentWidth - indent - 120, rect.height);

            if (isRenaming)
            {
                // Unique control name per path so IMGUI's internal TextEditor doesn't
                // reuse the cached text from a previously renamed item.
                string controlName = "BetterTabsRename_" + path;
                GUI.SetNextControlName(controlName);
                string newName = EditorGUI.TextField(nameArea, _renameBuffer,
                    isSelected ? SelectedTextField() : EditorStyles.miniTextField);
                if (newName != _renameBuffer) _renameBuffer = newName;

                // Keep requesting focus until it's actually established, so IMGUI's
                // shared text editor is re-initialized with THIS field's text (otherwise
                // it may keep showing the previously renamed item's text).
                if (_pendingRenameFocus)
                {
                    EditorGUI.FocusTextInControl(controlName);
                    if (GUI.GetNameOfFocusedControl() == controlName)
                        _pendingRenameFocus = false;
                }

                // Click outside this row commits the rename
                if (ev.type == EventType.MouseDown && !rect.Contains(ev.mousePosition))
                    _onRenameCommit?.Invoke(_renameBuffer);
            }
            else
            {
                var labelStyle = new GUIStyle(EditorStyles.miniLabel);
                if (isSelected) labelStyle.normal.textColor = Color.white;
                GUI.Label(nameArea, Path.GetFileNameWithoutExtension(path), labelStyle);
            }

            // ── Type label ───────────────────────────────────────────────────
            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = isSelected ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.5f, 0.5f, 0.5f) }
            };
            GUI.Label(new Rect(rect.xMax - 100, rect.y, 96, rect.height),
                AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "", typeStyle);
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────────

        void HandleKeyboardForSelection()
        {
            if (_renamingPath != null) return;

            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            // Arrow navigation (Shift reserved for tab navigation on the window)
            if (!ev.shift && HandleArrowNavigation(ev)) return;

            // The remaining shortcuts need a current selection
            if (string.IsNullOrEmpty(_selectedAssetPath)) return;

            if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
            { BetterTabsInteractionHandler.OpenAsset(_selectedAssetPath); ev.Use(); }
            else if (ev.keyCode == KeyCode.F2)
            { _onRenameRequested?.Invoke(_selectedAssetPath); ev.Use(); }
            else if (ev.keyCode == KeyCode.Delete)
            {
                if (BetterTabsInteractionHandler.Delete(_selectedAssetPath))
                {
                    InvalidateCache();
                    _selectedAssetPath = null;
                    _onRefreshRequested?.Invoke();
                }
                ev.Use();
            }
        }

        // ── Keyboard navigation ───────────────────────────────────────────────────

        bool HandleArrowNavigation(Event ev)
        {
            switch (ev.keyCode)
            {
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    ev.Use();
                    return true;
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    ev.Use();
                    return true;
                case KeyCode.RightArrow:
                    if (!string.IsNullOrEmpty(_selectedAssetPath) && AssetDatabase.IsValidFolder(_selectedAssetPath))
                    {
                        if (!IsExpanded(_selectedAssetPath))
                        { _expanded[_selectedAssetPath] = true; _onChanged?.Invoke(); }
                        else
                            MoveSelection(1);
                    }
                    ev.Use();
                    return true;
                case KeyCode.LeftArrow:
                    if (!string.IsNullOrEmpty(_selectedAssetPath)
                        && AssetDatabase.IsValidFolder(_selectedAssetPath) && IsExpanded(_selectedAssetPath))
                    {
                        _expanded[_selectedAssetPath] = false;
                        _onChanged?.Invoke();
                    }
                    else if (!string.IsNullOrEmpty(_selectedAssetPath) && _selectedAssetPath != _currentRootPath)
                    {
                        string parent = Path.GetDirectoryName(_selectedAssetPath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(parent) && parent.StartsWith(_currentRootPath ?? ""))
                            SelectSingle(parent);
                    }
                    ev.Use();
                    return true;
            }
            return false;
        }

        void MoveSelection(int delta)
        {
            if (string.IsNullOrEmpty(_currentRootPath)) return;
            var paths = new List<string>();
            CollectVisiblePaths(_currentRootPath, paths);
            if (paths.Count == 0) return;

            int idx = string.IsNullOrEmpty(_selectedAssetPath) ? -1 : paths.IndexOf(_selectedAssetPath);
            idx = Mathf.Clamp(idx + delta, 0, paths.Count - 1);
            SelectSingle(paths[idx]);
        }

        void CollectVisiblePaths(string folderPath, List<string> result)
        {
            result.Add(folderPath);
            if (!IsExpanded(folderPath)) return;
            EnsureCached(folderPath);
            var (files, folders) = _cache[folderPath];
            foreach (var sub in folders)
                CollectVisiblePaths(sub, result);
            foreach (var file in files)
                result.Add(file);
        }

        void SelectSingle(string path)
        {
            _selectedAssetPath = path;
            _pendingScrollPath = path;
            _onSelected?.Invoke(path);
        }

        // Consumed by the window after Draw to scroll the panel to the keyboard selection.
        public string ConsumePendingScrollPath()
        {
            string p = _pendingScrollPath;
            _pendingScrollPath = null;
            return p;
        }

        // ── Incoming drag-drop ───────────────────────────────────────────────

        void HandleIncomingDrop(string rowPath, Event ev)
        {
            if (DragAndDrop.paths == null || DragAndDrop.paths.Length == 0) return;
            string rowFolder = Path.GetDirectoryName(rowPath)?.Replace('\\', '/');

            if (ev.type == EventType.DragUpdated)
            { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; ev.Use(); }
            else if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var droppedPath in DragAndDrop.paths)
                {
                    string droppedParent = Path.GetDirectoryName(droppedPath)?.Replace('\\', '/');
                    if (droppedParent == rowFolder)
                        _onSelected?.Invoke(droppedPath);
                    else
                        ShowMoveOrCopyMenu(droppedPath, rowFolder);
                }
                ev.Use();
            }
        }

        void ShowMoveOrCopyMenu(string srcPath, string destFolder)
        {
            var menu = new GenericMenu();
            string fileName = Path.GetFileName(srcPath);
            string destPath = $"{destFolder}/{fileName}";

            menu.AddItem(new GUIContent("Move here"), false, () =>
            {
                string err = AssetDatabase.MoveAsset(srcPath, destPath);
                if (!string.IsNullOrEmpty(err))
                    Debug.LogError($"BetterTabs: Move failed — {err}");
                AssetDatabase.Refresh();
            });
            menu.AddItem(new GUIContent("Copy here"), false, () =>
            {
                AssetDatabase.CopyAsset(srcPath, AssetDatabase.GenerateUniqueAssetPath(destPath));
                AssetDatabase.Refresh();
            });
            menu.ShowAsContext();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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

        static GUIStyle SelectedTextField()
        {
            var s = new GUIStyle(EditorStyles.miniTextField);
            s.normal.textColor = Color.white;
            return s;
        }
    }
}
