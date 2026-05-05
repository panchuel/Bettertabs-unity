using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FolderTabs
{
    public class FolderTabsWindow : EditorWindow
    {
        // ── State ────────────────────────────────────────────────────────────
        List<FolderTabEntry> _tabs = new List<FolderTabEntry>();
        int _selectedIndex = -1;

        // ── View toggle ──────────────────────────────────────────────────────
        bool _gridView = false;

        // ── Scroll positions ─────────────────────────────────────────────────
        Vector2 _tabScroll;
        Vector2 _assetScroll;

        // ── Folder drag-drop state ───────────────────────────────────────────
        bool _isDragHovering;

        // ── Tab reorder drag state ────────────────────────────────────────────
        int _dragTabIndex = -1;
        int _pressedTabIndex = -1;
        float _dragTabOffsetX;
        float _dragTabCurrentX;

        // ── Tree & search ─────────────────────────────────────────────────────
        FolderTreeRenderer _tree;
        FolderSearchHandler _search;
        string _searchInputText = "";

        // ── Local asset selection ─────────────────────────────────────────────
        string _selectedAssetPath;

        // ── Inline rename ─────────────────────────────────────────────────────
        string _renamingPath;
        string _renameBuffer;

        // ── Dirty flag (set by postprocessor / projectChanged) ────────────────
        static bool s_dirty;
        static FolderTabsWindow s_instance;

        // ── Constants ────────────────────────────────────────────────────────
        const int TabHeight = 22;
        const int AssetRowHeight = 20;
        const int GridIconSize = 64;
        const int GridCellSize = 80;

        // ── Static API (used by postprocessor) ───────────────────────────────

        public static void RequestRefresh()
        {
            s_dirty = true;
            if (s_instance != null) s_instance.Repaint();
        }

        // Returns true if any of the paths belongs to an open tab root.
        public static bool CheckPathsAffectTabs(string[] paths)
        {
            if (s_instance == null || paths == null) return false;
            foreach (var p in paths)
                foreach (var tab in s_instance._tabs)
                    if (p.StartsWith(tab.path))
                        return true;
            return false;
        }

        // ── Menu item ────────────────────────────────────────────────────────
        [MenuItem("Window/Folder Tabs")]
        public static void Open()
        {
            var w = GetWindow<FolderTabsWindow>();
            w.Show();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────
        void OnEnable()
        {
            s_instance = this;
            titleContent = new GUIContent("Folder Tabs", LoadWindowIcon());
            _search = new FolderSearchHandler();
            _tree = new FolderTreeRenderer(OnFoldoutToggled);

            if (FolderTabsPrefs.Load(out var tabs, out var idx))
            {
                _tabs = tabs;
                _selectedIndex = tabs.Count == 0 ? -1 : Mathf.Clamp(idx, 0, tabs.Count - 1);
            }

            RestoreActiveTabState();

            EditorApplication.projectChanged += OnProjectChanged;
        }

        void OnDisable()
        {
            s_instance = null;
            EditorApplication.projectChanged -= OnProjectChanged;
            PersistActiveTabState();
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void OnProjectChanged()
        {
            RequestRefresh();
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        void OnGUI()
        {
            // Process dirty flag at the top of every frame
            if (s_dirty)
            {
                s_dirty = false;
                _tree.InvalidateCache();
                Repaint();
            }

            if (Event.current.type == EventType.MouseLeaveWindow)
            {
                _dragTabIndex = -1;
                _pressedTabIndex = -1;
            }

            HandleDragEvents();

            if (_tabs.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            DrawTabBar();
            DrawSearchToolbar();
            DrawContent();

            if (_isDragHovering)
                DrawDropOverlay();

            if (_searchInputText != _search.CommittedQuery)
                Repaint();
        }

        // ── Empty state ──────────────────────────────────────────────────────
        void DrawEmptyState()
        {
            var r = new Rect(0, 0, position.width, position.height);

            if (_isDragHovering)
            {
                EditorGUI.DrawRect(r, new Color(0.2f, 0.5f, 1f, 0.15f));
                DrawBorder(r, new Color(0.3f, 0.6f, 1f, 0.8f), 2f);
            }

            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 14,
                normal = { textColor = _isDragHovering ? new Color(0.5f, 0.8f, 1f) : Color.gray }
            };
            GUI.Label(r, "⊕ Drag a folder here", style);
        }

        // ── Tab bar ──────────────────────────────────────────────────────────
        void DrawTabBar()
        {
            // Reserve right side for + button (24px) and view toggle (26px)
            float rightReserved = 52f;
            var barRect = new Rect(0, 0, position.width - rightReserved, TabHeight);
            GUI.Box(new Rect(0, 0, position.width, TabHeight), GUIContent.none, EditorStyles.toolbar);

            int count = _tabs.Count;
            var widths = new float[count];
            var starts = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                float lw = EditorStyles.toolbarButton.CalcSize(new GUIContent(_tabs[i].name)).x;
                widths[i] = Mathf.Clamp(lw + 40, 80, 180);
                starts[i] = total;
                total += widths[i];
            }

            _tabScroll = GUI.BeginScrollView(barRect, _tabScroll,
                new Rect(0, 0, total, TabHeight),
                false, false, GUIStyle.none, GUIStyle.none);

            var ev = Event.current;
            int toRemove = -1;

            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    var tr = new Rect(starts[i], 0, widths[i], TabHeight);
                    if (!tr.Contains(ev.mousePosition)) continue;
                    var cr = new Rect(starts[i] + widths[i] - 16, 3, 14, 14);
                    if (cr.Contains(ev.mousePosition)) break;
                    _pressedTabIndex = i;
                    _dragTabOffsetX = ev.mousePosition.x - starts[i];
                    _dragTabCurrentX = starts[i];
                    ev.Use();
                    break;
                }
            }
            else if (ev.type == EventType.MouseDrag && _pressedTabIndex >= 0)
            {
                if (_dragTabIndex < 0) _dragTabIndex = _pressedTabIndex;
                _dragTabCurrentX = Mathf.Clamp(
                    ev.mousePosition.x - _dragTabOffsetX,
                    0f, total - widths[_dragTabIndex]);
                CheckTabSwap(starts, widths);
                ev.Use();
                Repaint();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                bool acted = false;
                if (_dragTabIndex >= 0) { CommitTabDrag(); acted = true; }
                else if (_pressedTabIndex >= 0) { SelectTab(_pressedTabIndex); acted = true; }
                _dragTabIndex = -1;
                _pressedTabIndex = -1;
                if (acted) ev.Use();
            }

            var folderIcon = EditorGUIUtility.FindTexture("Folder Icon");

            for (int i = 0; i < count; i++)
            {
                if (i == _dragTabIndex) continue;

                DrawTabAt(i, starts[i], widths[i], folderIcon, active: i == _selectedIndex, ghost: false);

                if (ev.type == EventType.ContextClick
                    && new Rect(starts[i], 0, widths[i], TabHeight).Contains(ev.mousePosition))
                {
                    ShowTabContextMenu(i);
                    ev.Use();
                }

                var closeRect = new Rect(starts[i] + widths[i] - 16, 3, 14, 14);
                if (GUI.Button(closeRect, "×", EditorStyles.miniLabel))
                    toRemove = i;
            }

            if (_dragTabIndex >= 0)
                EditorGUI.DrawRect(
                    new Rect(starts[_dragTabIndex], 0, widths[_dragTabIndex], TabHeight),
                    new Color(0.3f, 0.6f, 1f, 0.18f));

            if (_dragTabIndex >= 0 && _dragTabIndex < count)
            {
                DrawTabAt(_dragTabIndex, _dragTabCurrentX, widths[_dragTabIndex],
                    folderIcon, active: _dragTabIndex == _selectedIndex, ghost: true);
            }

            GUI.EndScrollView();

            // ── + dropdown button ─────────────────────────────────────────────
            var plusRect = new Rect(position.width - rightReserved, 1, 24, TabHeight - 2);
            if (EditorGUI.DropdownButton(plusRect, new GUIContent("+"), FocusType.Passive, EditorStyles.toolbarButton))
                ShowCreateMenu();

            if (toRemove >= 0)
                RemoveTab(toRemove);
        }

        void ShowCreateMenu()
        {
            string root = ActiveTabPath();
            if (string.IsNullOrEmpty(root)) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Folder"), false, () =>
            {
                FolderTabsCreator.CreateFolder(root, StartRename);
                RequestRefresh();
            });
            menu.AddItem(new GUIContent("C# Script"), false, () =>
            {
                FolderTabsCreator.CreateScript(root);
                RequestRefresh();
            });
            menu.AddItem(new GUIContent("Scene"), false, () =>
            {
                FolderTabsCreator.CreateScene(root);
                RequestRefresh();
            });
            menu.AddItem(new GUIContent("Material"), false, () =>
            {
                FolderTabsCreator.CreateMaterial(root);
                RequestRefresh();
            });
            menu.ShowAsContext();
        }

        void DrawTabAt(int index, float x, float width, Texture2D icon, bool active, bool ghost)
        {
            var drawRect = new Rect(x, 0, width, TabHeight);

            Color bg = ghost
                ? new Color(0.2f, 0.45f, 0.8f, 0.9f)
                : active
                    ? new Color(0.25f, 0.47f, 0.75f, 0.5f)
                    : Color.clear;

            if (bg.a > 0)
                EditorGUI.DrawRect(drawRect, bg);

            if (icon != null)
                GUI.DrawTexture(new Rect(x + 2, 2, 16, 16), icon, ScaleMode.ScaleToFit);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal
            };
            if (ghost) labelStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(x + 20, 2, width - 38, TabHeight - 4), _tabs[index].name, labelStyle);
        }

        // ── Tab reorder helpers ───────────────────────────────────────────────

        void CheckTabSwap(float[] starts, float[] widths)
        {
            if (_dragTabIndex < 0) return;

            if (_dragTabIndex > 0)
            {
                int left = _dragTabIndex - 1;
                if (_dragTabCurrentX < starts[left] + widths[left] * 0.5f)
                {
                    SwapTabs(_dragTabIndex, left);
                    _dragTabIndex--;
                    return;
                }
            }

            if (_dragTabIndex < _tabs.Count - 1)
            {
                int right = _dragTabIndex + 1;
                if (_dragTabCurrentX + widths[_dragTabIndex] > starts[right] + widths[right] * 0.5f)
                {
                    SwapTabs(_dragTabIndex, right);
                    _dragTabIndex++;
                }
            }
        }

        void SwapTabs(int i, int j)
        {
            (_tabs[i], _tabs[j]) = (_tabs[j], _tabs[i]);
            if (_selectedIndex == i) _selectedIndex = j;
            else if (_selectedIndex == j) _selectedIndex = i;
        }

        void CommitTabDrag()
        {
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
            Repaint();
        }

        void ShowTabContextMenu(int index)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Close"), false, () => RemoveTab(index));
            menu.AddItem(new GUIContent("Close Others"), false, () => CloseOthers(index));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open in Project"), false, () => PingTab(index));
            menu.ShowAsContext();
        }

        // ── Search toolbar ────────────────────────────────────────────────────
        void DrawSearchToolbar()
        {
            float y = TabHeight;
            var toolbarRect = new Rect(0, y, position.width, TabHeight);
            GUI.Box(toolbarRect, GUIContent.none, EditorStyles.toolbar);

            float clearBtnW = _searchInputText.Length > 0 ? 20 : 0;
            float viewBtnW = 26;
            float fieldW = position.width - clearBtnW - viewBtnW - 6;

            var searchRect = new Rect(2, y + 2, fieldW, TabHeight - 4);

            EditorGUI.BeginChangeCheck();
            _searchInputText = EditorGUI.TextField(searchRect, _searchInputText, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                bool changed = _search.Tick(_searchInputText, ActiveTabPath());
                if (changed) SyncSearchToTab();
            }
            else
            {
                bool changed = _search.Tick(_searchInputText, ActiveTabPath());
                if (changed) SyncSearchToTab();
            }

            if (clearBtnW > 0)
            {
                var clearRect = new Rect(2 + fieldW, y + 2, clearBtnW - 2, TabHeight - 4);
                if (GUI.Button(clearRect, "×", EditorStyles.toolbarButton))
                    ClearSearch();
            }

            var toggleRect = new Rect(position.width - viewBtnW - 2, y + 1, viewBtnW, TabHeight - 2);
            var viewIcon = _gridView
                ? EditorGUIUtility.FindTexture("UnityEditor.SceneView")
                : EditorGUIUtility.FindTexture("d_GridLayoutGroup Icon");

            var viewContent = viewIcon != null
                ? new GUIContent(viewIcon, _gridView ? "List view" : "Grid view")
                : new GUIContent(_gridView ? "≡" : "⊞", _gridView ? "List view" : "Grid view");

            if (GUI.Button(toggleRect, viewContent, EditorStyles.toolbarButton))
                _gridView = !_gridView;
        }

        // ── Content area ──────────────────────────────────────────────────────
        void DrawContent()
        {
            float topOffset = TabHeight * 2;
            var listRect = new Rect(0, topOffset, position.width, position.height - topOffset);

            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count)
            {
                GUI.Label(listRect, "No folder selected.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            string rootPath = ActiveTabPath();

            if (_search.IsSearching)
            {
                DrawSearchResults(listRect);
                return;
            }

            if (_gridView)
                DrawGridView(listRect, rootPath);
            else
                DrawTreeView(listRect, rootPath);
        }

        // ── Tree view ─────────────────────────────────────────────────────────
        void DrawTreeView(Rect listRect, string rootPath)
        {
            _tree.SetCallbacks(
                _selectedAssetPath,
                _renamingPath,
                _renameBuffer,
                OnAssetSelected,
                StartRename,
                CommitRename,
                CancelRename,
                OnAssetModified,
                FolderTabsWindow.RequestRefresh);

            float contentHeight = _tree.MeasureHeight(rootPath);
            var contentRect = new Rect(0, 0, listRect.width - 16, Mathf.Max(contentHeight, listRect.height));

            _assetScroll = GUI.BeginScrollView(listRect, _assetScroll, contentRect);

            GUILayout.BeginArea(new Rect(0, 0, contentRect.width, contentHeight));
            _tree.Draw(rootPath, contentRect.width, OnAssetDragged);
            GUILayout.EndArea();

            GUI.EndScrollView();
        }

        // ── Search results ────────────────────────────────────────────────────
        void DrawSearchResults(Rect listRect)
        {
            var results = _search.Results;
            float headerH = EditorGUIUtility.singleLineHeight + 4;

            var countRect = new Rect(listRect.x + 4, listRect.y, listRect.width - 8, headerH);
            var countStyle = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
            GUI.Label(countRect,
                $"{results.Count} result{(results.Count == 1 ? "" : "s")}", countStyle);

            var scrollArea = new Rect(listRect.x, listRect.y + headerH, listRect.width, listRect.height - headerH);
            float totalH = results.Count * AssetRowHeight;
            var contentRect = new Rect(0, 0, scrollArea.width - 16, totalH);

            _assetScroll = GUI.BeginScrollView(scrollArea, _assetScroll, contentRect);

            for (int i = 0; i < results.Count; i++)
            {
                var path = results[i];
                var rowRect = new Rect(0, i * AssetRowHeight, contentRect.width, AssetRowHeight);

                bool isSelected = path == _selectedAssetPath;
                if (isSelected)
                    EditorGUI.DrawRect(rowRect, new Color(0.17f, 0.36f, 0.53f, 1f));
                else if (i % 2 == 0)
                    EditorGUI.DrawRect(rowRect, new Color(0, 0, 0, 0.04f));

                DrawSearchResultRow(rowRect, path, isSelected);
            }

            GUI.EndScrollView();
        }

        void DrawSearchResultRow(Rect rowRect, string path, bool isSelected)
        {
            var ev = Event.current;

            if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
            {
                if (ev.button == 0)
                {
                    if (ev.clickCount == 2) { FolderTabsInteractionHandler.OpenAsset(path); ev.Use(); }
                    else { OnAssetSelected(path); ev.Use(); }
                }
                else if (ev.button == 1)
                {
                    var menu = FolderTabsInteractionHandler.BuildContextMenu(path, StartRename, OnAssetModified, RequestRefresh);
                    menu.ShowAsContext();
                    ev.Use();
                }
            }
            if (ev.type == EventType.MouseDrag && rowRect.Contains(ev.mousePosition))
            { OnAssetDragged(path); ev.Use(); }

            var icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            if (icon != null)
                GUI.DrawTexture(new Rect(rowRect.x + 2, rowRect.y + 2, 16, 16), icon, ScaleMode.ScaleToFit);

            var richStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            if (isSelected) richStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(rowRect.x + 22, rowRect.y, rowRect.width - 120, rowRect.height),
                _search.Highlight(path), richStyle);

            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = isSelected ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.5f, 0.5f, 0.5f) }
            };
            GUI.Label(new Rect(rowRect.xMax - 100, rowRect.y, 96, rowRect.height),
                AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "", typeStyle);
        }

        // ── Grid view ─────────────────────────────────────────────────────────
        void DrawGridView(Rect listRect, string rootPath)
        {
            var guids = AssetDatabase.FindAssets("", new string[] { rootPath });
            var assets = new List<string>();
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parent != rootPath) continue;
                if (!AssetDatabase.IsValidFolder(assetPath))
                    assets.Add(assetPath);
            }

            int cols = Mathf.Max(1, (int)(listRect.width / GridCellSize));
            int rows = Mathf.CeilToInt((float)assets.Count / cols);
            float totalH = rows * GridCellSize;
            var contentRect = new Rect(0, 0, listRect.width - 16, totalH);

            _assetScroll = GUI.BeginScrollView(listRect, _assetScroll, contentRect);

            for (int i = 0; i < assets.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                DrawGridCell(new Rect(col * GridCellSize, row * GridCellSize, GridCellSize, GridCellSize), assets[i]);
            }

            GUI.EndScrollView();
        }

        void DrawGridCell(Rect cellRect, string path)
        {
            var preview = AssetPreview.GetAssetPreview(AssetDatabase.LoadAssetAtPath<Object>(path));
            if (preview == null)
                preview = AssetDatabase.GetCachedIcon(path) as Texture2D;

            var ev = Event.current;

            bool isSelected = path == _selectedAssetPath;
            if (isSelected)
                EditorGUI.DrawRect(cellRect, new Color(0.17f, 0.36f, 0.53f, 0.6f));

            if (ev.type == EventType.MouseDown && cellRect.Contains(ev.mousePosition))
            {
                if (ev.button == 0)
                {
                    if (ev.clickCount == 2) { FolderTabsInteractionHandler.OpenAsset(path); ev.Use(); }
                    else { OnAssetSelected(path); ev.Use(); }
                }
                else if (ev.button == 1)
                {
                    var menu = FolderTabsInteractionHandler.BuildContextMenu(path, StartRename, OnAssetModified, RequestRefresh);
                    menu.ShowAsContext();
                    ev.Use();
                }
            }
            if (ev.type == EventType.MouseDrag && cellRect.Contains(ev.mousePosition))
            { OnAssetDragged(path); ev.Use(); }

            var iconRect = new Rect(cellRect.x + (GridCellSize - GridIconSize) / 2, cellRect.y + 4, GridIconSize, GridIconSize);
            if (preview != null)
                GUI.DrawTexture(iconRect, preview, ScaleMode.ScaleToFit);

            var labelRect = new Rect(cellRect.x + 2, cellRect.y + GridCellSize - 18, GridCellSize - 4, 16);
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = isSelected ? Color.white : EditorStyles.miniLabel.normal.textColor }
            };
            GUI.Label(labelRect, Path.GetFileNameWithoutExtension(path), labelStyle);
        }

        // ── Folder drag-drop handling ─────────────────────────────────────────
        void HandleDragEvents()
        {
            var ev = Event.current;
            var windowRect = new Rect(0, 0, position.width, position.height);

            if (ev.type == EventType.DragUpdated && windowRect.Contains(ev.mousePosition))
            {
                if (IsDraggingFolder())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    _isDragHovering = true;
                    ev.Use();
                    Repaint();
                }
            }
            else if (ev.type == EventType.DragPerform && windowRect.Contains(ev.mousePosition))
            {
                if (IsDraggingFolder())
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var path in DragAndDrop.paths)
                        if (AssetDatabase.IsValidFolder(path))
                            AddOrSelectTab(path);
                    _isDragHovering = false;
                    ev.Use();
                    Repaint();
                }
            }
            else if (ev.type == EventType.DragExited)
            {
                _isDragHovering = false;
                Repaint();
            }
        }

        bool IsDraggingFolder()
        {
            foreach (var path in DragAndDrop.paths)
                if (AssetDatabase.IsValidFolder(path))
                    return true;
            return false;
        }

        void DrawDropOverlay()
        {
            var r = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.5f, 1f, 0.08f));
            DrawBorder(r, new Color(0.3f, 0.6f, 1f, 0.8f), 2f);
        }

        static void DrawBorder(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        // ── Asset selection / interaction callbacks ────────────────────────────

        void OnAssetSelected(string path)
        {
            _selectedAssetPath = path;
            var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj != null) Selection.activeObject = obj;
            Repaint();
        }

        void OnAssetDragged(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj == null) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { obj };
            DragAndDrop.paths = new string[] { path };
            DragAndDrop.StartDrag(Path.GetFileNameWithoutExtension(path));
        }

        void OnAssetModified(string path)
        {
            if (_selectedAssetPath == path)
                _selectedAssetPath = null;
            _tree.InvalidateCache();
            Repaint();
        }

        // ── Inline rename ─────────────────────────────────────────────────────

        void StartRename(string path)
        {
            _renamingPath = path;
            _renameBuffer = Path.GetFileNameWithoutExtension(path);
            _selectedAssetPath = path;
            Repaint();
        }

        void CommitRename(string newName)
        {
            if (_renamingPath == null) return;
            string path = _renamingPath;
            _renamingPath = null;
            _renameBuffer = null;

            string trimmed = newName.Trim();
            string oldName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(trimmed) && trimmed != oldName)
            {
                string err = AssetDatabase.RenameAsset(path, trimmed);
                if (!string.IsNullOrEmpty(err))
                    Debug.LogError($"FolderTabs: Rename failed — {err}");
                AssetDatabase.Refresh();
                _tree.InvalidateCache();
            }
            Repaint();
        }

        void CancelRename()
        {
            _renamingPath = null;
            _renameBuffer = null;
            Repaint();
        }

        // ── Tab management ────────────────────────────────────────────────────
        void AddOrSelectTab(string path)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].path == path) { SelectTab(i); return; }
            }
            _tabs.Add(new FolderTabEntry(path));
            SelectTab(_tabs.Count - 1);
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void SelectTab(int index)
        {
            PersistActiveTabState();
            _selectedIndex = index;
            _selectedAssetPath = null;
            _renamingPath = null;
            _renameBuffer = null;
            _assetScroll = Vector2.zero;
            _tree.InvalidateCache();
            RestoreActiveTabState();
            Repaint();
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void RemoveTab(int index)
        {
            _tabs.RemoveAt(index);
            if (_tabs.Count == 0)
            {
                _selectedIndex = -1;
                _search.Clear();
                _searchInputText = "";
                _tree.InvalidateCache();
            }
            else
            {
                _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _tabs.Count - 1);
                _tree.InvalidateCache();
                RestoreActiveTabState();
            }
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
            Repaint();
        }

        void CloseOthers(int keepIndex)
        {
            var keep = _tabs[keepIndex];
            _tabs.Clear();
            _tabs.Add(keep);
            SelectTab(0);
        }

        void PingTab(int index)
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>(_tabs[index].path);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

        // ── Per-tab state persistence ─────────────────────────────────────────
        void PersistActiveTabState()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            var tab = _tabs[_selectedIndex];
            _tree.SaveExpandedState(tab.expandedPaths);
            tab.searchQuery = _search.CommittedQuery;
        }

        void RestoreActiveTabState()
        {
            _search.Clear();
            _searchInputText = "";

            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            var tab = _tabs[_selectedIndex];

            _tree.LoadExpandedState(tab.expandedPaths);

            if (!string.IsNullOrEmpty(tab.searchQuery))
            {
                _searchInputText = tab.searchQuery;
                _search.ForceCommit(tab.searchQuery, tab.path);
            }
        }

        void OnFoldoutToggled()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
            {
                _tree.SaveExpandedState(_tabs[_selectedIndex].expandedPaths);
                FolderTabsPrefs.Save(_tabs, _selectedIndex);
            }
        }

        void ClearSearch()
        {
            _searchInputText = "";
            _search.Clear();
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
                _tabs[_selectedIndex].searchQuery = "";
            FolderTabsPrefs.Save(_tabs, _selectedIndex);
            _assetScroll = Vector2.zero;
            Repaint();
        }

        void SyncSearchToTab()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
                _tabs[_selectedIndex].searchQuery = _search.CommittedQuery;
        }

        string ActiveTabPath() =>
            _selectedIndex >= 0 && _selectedIndex < _tabs.Count
                ? _tabs[_selectedIndex].path
                : null;

        static Texture2D LoadWindowIcon()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Script FolderTabsWindow"))
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!scriptPath.EndsWith("FolderTabsWindow.cs")) continue;
                var dir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                return AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Icons/icon.png");
            }
            return null;
        }
    }
}
