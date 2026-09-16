using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    public class BetterTabsWindow : EditorWindow
    {
        // ── State ────────────────────────────────────────────────────────────
        List<BetterTabEntry> _tabs = new List<BetterTabEntry>();
        int _selectedIndex = -1;

        // ── View toggle ──────────────────────────────────────────────────────
        bool _gridView = false;

        // ── Scroll positions ─────────────────────────────────────────────────
        Vector2 _assetScroll;

        // ── Folder drag-drop state ───────────────────────────────────────────
        bool _isDragHovering;

        // ── Tab reorder drag state ────────────────────────────────────────────

        // ── Closed-tab history (Ctrl+Shift+T) ────────────────────────────────
        // Keeps the index too, so reopening restores the tab where it was.
        readonly Stack<(BetterTabEntry tab, int index)> _closedTabStack =
            new Stack<(BetterTabEntry, int)>();

        // ── Tree & search ─────────────────────────────────────────────────────
        BetterSearchHandler _search;
        string _searchInputText = "";

        // ── Local asset selection ─────────────────────────────────────────────
        string _selectedAssetPath;

        // ── Embedded inspector ────────────────────────────────────────────────

        // ── Prefab / SceneObject hierarchy view ───────────────────────────────
        readonly Dictionary<string, GameObject> _loadedPrefabRoots = new Dictionary<string, GameObject>();
        float _previewHeight = 180f;
        bool _previewCollapsed;
        static string PreviewHeightKey => $"BetterTabs_PreviewHeight_{Application.productName}";
        static string PreviewCollapsedKey => $"BetterTabs_PreviewCollapsed_{Application.productName}";

        // ── Inline rename ─────────────────────────────────────────────────────

        // ── Dirty flag (set by postprocessor / projectChanged) ────────────────
        static bool s_dirty;
        static BetterTabsWindow s_instance;

        // ── Constants ────────────────────────────────────────────────────────
        const int TabHeight = 22;
        const float MinPanelW = 120f;
        const int GoPaneMode = 4;
        const int AssetRowHeight = 20;
        const int GridIconSize = 64;
        const int GridCellSize = 80;

        // ── Project panel ─────────────────────────────────────────────────────
        bool _projectPanelOpen;
        float _splitterX = 220f;
        BetterAssetTreeView _projectTree;
        Object _lastSyncedSelection;

        // ── UI Toolkit ────────────────────────────────────────────────────────
        BetterTabsBarView _tabBar;
        IMGUIContainer _rightContainer;
        BetterAssetTreeView _contentTree;
        BetterAssetInspectorView _assetInspector;
        BetterAssetListView _assetList;
        string _assetListKey;
        BetterGameObjectView _goView;
        VisualElement _rightPane;
        int _rightPaneMode = -1;
        TwoPaneSplitView _splitView;

        // Size of the IMGUI drawing area. Equals the window size while everything
        // still lives in one container; once areas move into their own containers
        // each one reports its own size, so layout code stays position-independent.
        float _viewW;
        float _viewH;

        // ── Layout bounds (set each frame while drawing) ──────────────────────
        float _rightX;
        float _rightW;

        static string ProjectPanelOpenKey => $"BetterTabs_ProjectPanelOpen_{Application.productName}";
        static string SplitterXKey => $"BetterTabs_SplitterX_{Application.productName}";
        static string HierarchySplitterKey => $"BetterTabs_HierarchySplitter_{Application.productName}";
        static string ProjectExpandedKey => $"BetterTabs_ProjectExpanded_{Application.productName}";

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
            {
                if (string.IsNullOrEmpty(p)) continue;
                foreach (var tab in s_instance._tabs)
                {
                    if (string.IsNullOrEmpty(tab.path)) continue;
                    if (p.StartsWith(tab.path)) return true;
                }
            }
            return false;
        }

        // ── Menu items ───────────────────────────────────────────────────────
        [MenuItem("Window/BetterTabs/Open BetterTabs")]
        public static void Open()
        {
            var w = GetWindow<BetterTabsWindow>();
            w.Show();
        }

        // Ctrl+T global shortcut — registered via ShortcutManager, not MenuItem,
        // so it does not appear as a visible menu entry.
        [Shortcut("BetterTabs/Add Tab from Selection", KeyCode.T, ShortcutModifiers.Action)]
        static void AddTabFromSelectionShortcut()
        {
            var w = GetWindow<BetterTabsWindow>();
            w.Show();
            w.Focus();
            w.OpenNewTab();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────
        void OnEnable()
        {
            s_instance = this;
            titleContent = new GUIContent("BetterTabs", LoadWindowIcon());
            _search = new BetterSearchHandler();

            _projectPanelOpen = EditorPrefs.GetBool(ProjectPanelOpenKey, false);
            _splitterX = EditorPrefs.GetFloat(SplitterXKey, 220f);
            _previewHeight = EditorPrefs.GetFloat(PreviewHeightKey, 180f);
            _previewCollapsed = EditorPrefs.GetBool(PreviewCollapsedKey, false);

            if (BetterTabsPrefs.Load(out var tabs, out var idx))
            {
                _tabs = tabs;
                _selectedIndex = tabs.Count == 0 ? -1 : Mathf.Clamp(idx, 0, tabs.Count - 1);
            }

            RestoreActiveTabState();
            SyncProjectTreeHighlight();

            EditorApplication.projectChanged += OnProjectChanged;
            Selection.selectionChanged += OnUnitySelectionChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            s_instance = null;
            EditorApplication.projectChanged -= OnProjectChanged;
            Selection.selectionChanged -= OnUnitySelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
            _assetInspector?.DestroyEditor();
            SaveAndUnloadAllPrefabs();
            _goView?.Invalidate();
            PersistActiveTabState();
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            EditorPrefs.SetBool(ProjectPanelOpenKey, _projectPanelOpen);
            EditorPrefs.SetFloat(SplitterXKey, CurrentSplitterX());
            if (_goView != null)
                EditorPrefs.SetFloat(HierarchySplitterKey, _goView.CurrentSplitterX());
            EditorPrefs.SetFloat(PreviewHeightKey, _previewHeight);
            EditorPrefs.SetBool(PreviewCollapsedKey, _previewCollapsed);
            if (_projectTree != null)
                EditorPrefs.SetString(ProjectExpandedKey, _projectTree.SaveExpandedState());
        }

        void OnProjectChanged()
        {
            RequestRefresh();
        }

        // Polling fallback: Selection.selectionChanged isn't always delivered to a
        // custom window depending on context, so also watch the selection each tick.
        void OnEditorUpdate()
        {
            // Both run here rather than while drawing: swapping the tree in or out
            // modifies the visual hierarchy, which throws during an IMGUI layout pass,
            // and the right IMGUI container is hidden in tree mode so it would never
            // see the dirty flag.
            UpdateRightPaneMode();

            if (s_dirty)
            {
                s_dirty = false;
                _projectTree?.Rebuild();
                _contentTree?.Rebuild();
                // Force the flat list to rebuild too: its contents just changed.
                _assetListKey = null;
                Repaint();
            }

            if (Selection.activeObject == _lastSyncedSelection) return;
            _lastSyncedSelection = Selection.activeObject;
            OnUnitySelectionChanged();
            // The + button is enabled only when the selection is not already a tab.
            UpdateTabBarButtons();
        }

        // Mirror the Unity selection into the left project panel when the user selects
        // an asset elsewhere (Project window, Inspector, or a field reference), so it
        // appears selected and scrolled into view.
        void OnUnitySelectionChanged()
        {
            if (!_projectPanelOpen || _projectTree == null) return;
            if (Selection.activeObject == null) return;

            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets")) return;

            // Already selected here (e.g. the tool set the Unity selection itself) →
            // don't clobber a multi-selection.
            if (_projectTree.GetSelectedPaths().Contains(path)) return;

            _projectTree.SetSelectedPath(path);
            _projectTree.ExpandToPath(path);
            ScrollProjectPanelToPath(path);
            Repaint();
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        // ── UI Toolkit root ──────────────────────────────────────────────────
        // Migration scaffolding: the window root is UI Toolkit, and the legacy IMGUI
        // drawing runs inside a single IMGUIContainer. Each migrated area moves out of
        // this container into real VisualElements, so the tool keeps working throughout.
        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.flexGrow = 1;
            // TrickleDown: seen before the trees, which consume plain arrow keys.
            root.RegisterCallback<WheelEvent>(OnRootWheel, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            StyleSheet sheet = LoadStyleSheet();
            if (sheet != null) root.styleSheets.Add(sheet);

            _tabBar = new BetterTabsBarView();
            _tabBar.TabSelected += SelectTab;
            _tabBar.TabClosed += RemoveTab;
            _tabBar.TabContextMenu += ShowTabContextMenu;
            _tabBar.TabMoved += OnTabMoved;
            _tabBar.AddClicked += AddTabFromSelection;
            _tabBar.PingClicked += () => PingTab(_selectedIndex);
            _tabBar.PanelToggleClicked += ToggleProjectPanel;
            _tabBar.SearchChanged += OnSearchChanged;
            _tabBar.UnitySearchClicked += OpenUnitySearchWindow;
            _tabBar.ViewToggleClicked += () =>
            {
                _gridView = !_gridView;
                UpdateTabBarButtons();
            };
            // Dropping assets on the bar creates tabs (was IMGUI DragPerform before).
            _tabBar.RegisterCallback<DragUpdatedEvent>(OnTabBarDragUpdated);
            _tabBar.RegisterCallback<DragPerformEvent>(OnTabBarDragPerform);
            root.Add(_tabBar);

            // Left/right panels split by a native TwoPaneSplitView (replaces the
            // hand-rolled splitter). Child 0 is the collapsible project panel.
            _splitView = new TwoPaneSplitView(0, Mathf.Max(MinPanelW, _splitterX), TwoPaneSplitViewOrientation.Horizontal);
            _splitView.style.flexGrow = 1;

            _projectTree = new BetterAssetTreeView(multiSelect: true, showTypeColumn: false, showAddButton: true);
            _projectTree.Rebuild();
            _projectTree.style.minWidth = MinPanelW;
            _projectTree.ItemSelected += OnProjectPanelItemClicked;
            _projectTree.ItemActivated += AddOrSelectTab;
            _projectTree.ItemsDragged += OnAssetDragged;
            _projectTree.RefreshRequested += RequestRefresh;
            _projectTree.LoadExpandedState(EditorPrefs.GetString(ProjectExpandedKey, ""));
            _splitView.Add(_projectTree);

            // Right pane: search toolbar on top, then either the native content tree
            // or the remaining IMGUI views (asset pin, prefab, grid, search results).
            _rightPane = new VisualElement();
            _rightPane.style.flexGrow = 1;
            _rightPane.style.minWidth = MinPanelW;

            _contentTree = new BetterAssetTreeView(multiSelect: false, showTypeColumn: true, showAddButton: false);
            _contentTree.ItemSelected += OnAssetSelected;
            _contentTree.ItemActivated += BetterTabsInteractionHandler.OpenAsset;
            _contentTree.ItemsDragged += OnAssetDragged;
            _contentTree.RefreshRequested += RequestRefresh;
            _rightPane.Add(_contentTree);

            _assetInspector = new BetterAssetInspectorView();
            _assetInspector.OpenRequested += BetterTabsInteractionHandler.OpenAsset;
            _assetInspector.PreviewHeightChanged += h =>
            {
                _previewHeight = h;
                EditorPrefs.SetFloat(PreviewHeightKey, h);
            };
            _assetInspector.PreviewCollapsedChanged += c =>
            {
                _previewCollapsed = c;
                EditorPrefs.SetBool(PreviewCollapsedKey, c);
            };
            _assetInspector.SetPreviewState(_previewHeight, _previewCollapsed);
            _rightPane.Add(_assetInspector);

            _assetList = new BetterAssetListView();
            _assetList.HighlightProvider = p => _search.Highlight(p);
            _assetList.ItemSelected += OnAssetSelected;
            _assetList.ItemActivated += OnListItemActivated;
            _assetList.ItemDragged += OnAssetDraggedSingle;
            _assetList.RenameRequested += StartRename;
            _assetList.RefreshRequested += RequestRefresh;
            _rightPane.Add(_assetList);

            _rightContainer = new IMGUIContainer(DrawRightPanelIMGUI);
            // Required so the IMGUI content still receives keys (F2, Delete, arrows)
            _rightContainer.focusable = true;
            _rightContainer.style.flexGrow = 1;
            _rightPane.Add(_rightContainer);

            _splitView.Add(_rightPane);

            root.Add(_splitView);
            // Deferred: the split view must resolve its layout before it can collapse.
            _splitView.schedule.Execute(ApplyProjectPanelVisibility);

            RefreshTabBar();
        }

        // Route keys (F2, Delete, arrows) to the IMGUI content when the window is focused.
        void OnFocus()
        {
            _rightContainer?.Focus();
        }

        // Uses the container rect so each migrated area reports its own drawing size.
        void UpdateViewSize(IMGUIContainer container)
        {
            Rect view = container != null ? container.contentRect : Rect.zero;
            _viewW = (view.width > 0f && !float.IsNaN(view.width)) ? view.width : position.width;
            _viewH = (view.height > 0f && !float.IsNaN(view.height)) ? view.height : position.height;
        }

        // Live width of the project panel, so the split position persists across sessions.
        float CurrentSplitterX()
        {
            if (_projectTree == null) return _splitterX;
            float w = _projectTree.resolvedStyle.width;
            return (w > 0f && !float.IsNaN(w)) ? w : _splitterX;
        }

        // Collapsing child 0 hides the project panel together with its splitter handle.
        void ApplyProjectPanelVisibility()
        {
            if (_splitView == null) return;
            if (_projectPanelOpen) _splitView.UnCollapse();
            else _splitView.CollapseChild(0);
        }

        // The search toolbar lives in its own strip so the tree below it can be a
        // real VisualElement rather than IMGUI.
        // Chooses between the native tree and the IMGUI views, and keeps the tree
        // pointed at the active tab folder.
        // Feeds the flat list with either search hits or the folder contents.
        void RefreshAssetList(bool searching)
        {
            if (_assetList == null) return;

            // This runs on every editor tick, and a rebuild re-requests an asset
            // preview for every item, so only rebuild when the contents change.
            string key = searching
                ? "search:" + _search.CommittedQuery
                : "folder:" + ActiveTabPath();

            if (key == _assetListKey)
            {
                _assetList.SetSelected(_selectedAssetPath);
                return;
            }
            _assetListKey = key;

            if (searching)
            {
                IReadOnlyList<string> results = _search.Results;
                string header = $"{results.Count} result{(results.Count == 1 ? "" : "s")}";
                _assetList.SetItems(results, BetterAssetListView.Mode.List, header);
            }
            else
            {
                _assetList.SetItems(FolderContents(ActiveTabPath()), BetterAssetListView.Mode.Grid);
            }
            _assetList.SetSelected(_selectedAssetPath);
        }

        // Direct children of a folder, folders first.
        static List<string> FolderContents(string rootPath)
        {
            List<string> folders = new List<string>();
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(rootPath)) return folders;

            foreach (string guid in AssetDatabase.FindAssets("", new[] { rootPath }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parent != rootPath) continue;

                if (AssetDatabase.IsValidFolder(assetPath)) folders.Add(assetPath);
                else files.Add(assetPath);
            }

            folders.AddRange(files);
            return folders;
        }

        void OnListItemActivated(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AddOrSelectTab(path);
            else BetterTabsInteractionHandler.OpenAsset(path);
        }

        // Grid and search results have no inline editing, so a rename request from
        // them switches to the tree, which owns renaming.
        void StartRename(string path)
        {
            if (_gridView) _gridView = false;
            if (_search.IsSearching) ClearSearch();

            UpdateRightPaneMode();
            _contentTree?.ExpandToPath(path);
            _contentTree?.StartRename(path);
        }

        // Embedded Unity Search takes over the right panel, so the active tab is
        // deselected while it is showing.
        // Opens Unity's Search window. Embedding it was tried and reverted: SearchWindow
        // assumes it lives in a real window host, so the embedded copy needed a growing
        // stack of workarounds against internal APIs.
        void OpenUnitySearchWindow()
        {
            SearchService.ShowContextual();
        }

        // Search is project-wide, so it is window state rather than tab state.
        void OnSearchChanged(string query)
        {
            _searchInputText = query ?? "";
            if (string.IsNullOrEmpty(_searchInputText)) _search.Clear();
            else _search.ForceCommit(_searchInputText);

            RefreshTabBar();
            UpdateRightPaneMode();
            Repaint();
        }

        // Built on demand: TwoPaneSplitView initialises from its first resolved
        // geometry, so creating it while the pane is hidden leaves its drag line dead.
        void EnsureGameObjectView()
        {
            if (_goView != null) return;

            _goView = new BetterGameObjectView(EditorPrefs.GetFloat(HierarchySplitterKey, 220f));
            _goView.SelectionChanged += OnHierarchySelectionChanged;
            _goView.ValueChanged += OnGameObjectEdited;
            _rightPane.Add(_goView);
        }

        // Resolves the tab target (loaded prefab root or scene object) and feeds it
        // to the GameObject view.
        void RefreshGameObjectView(BetterTabEntry tab)
        {
            if (_goView == null) return;

            if (tab.kind == BetterTabKind.Prefab)
            {
                GameObject root = GetOrLoadPrefabRoot(tab.path);
                if (root == null) _goView.ShowMessage("Could not load prefab.");
                else _goView.SetTarget(root, tab.hierarchySelectionPath);
                return;
            }

            if (!GlobalObjectId.TryParse(tab.globalObjectId, out GlobalObjectId gid))
            {
                _goView.ShowMessage("Invalid scene reference.");
                return;
            }

            GameObject sceneRoot = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
            if (sceneRoot == null)
                _goView.ShowMessage("Scene not loaded or GameObject missing.\nOpen the scene to inspect this object.");
            else
                _goView.SetTarget(sceneRoot, tab.hierarchySelectionPath);
        }

        void OnHierarchySelectionChanged(string path)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            _tabs[_selectedIndex].hierarchySelectionPath = path;
        }

        // Prefab tabs edit a loaded copy, so write it back after a change.
        void OnGameObjectEdited()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            BetterTabEntry tab = _tabs[_selectedIndex];
            if (tab.kind != BetterTabKind.Prefab) return;

            string prefabPath = tab.path;
            EditorApplication.delayCall += () =>
            {
                if (_loadedPrefabRoots.TryGetValue(prefabPath, out GameObject root) && root != null)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            };
        }

        void UpdateRightPaneMode()
        {
            if (_contentTree == null) return;

            // Search takes the whole panel: while it is active no tab renders, so
            // every other mode is forced off. They are mutually exclusive on purpose,
            // otherwise the tab content and the results would both be visible.
            bool searchMode = _search.IsSearching;

            bool hasTab = !searchMode && _tabs.Count > 0 && _selectedIndex >= 0;
            bool isFolder = hasTab && ActiveTabIsFolder();

            bool treeMode = isFolder && !_gridView;
            bool gridMode = isFolder && _gridView;
            bool listMode = searchMode || gridMode;

            bool assetMode = hasTab && _tabs[_selectedIndex].kind == BetterTabKind.Asset;
            bool goMode = hasTab
                && (_tabs[_selectedIndex].kind == BetterTabKind.Prefab
                    || _tabs[_selectedIndex].kind == BetterTabKind.SceneObject);

            int mode = searchMode ? 3
                : goMode ? GoPaneMode
                : assetMode ? 2
                : treeMode ? 1
                : listMode ? 3 : 0;

            if (treeMode) _contentTree.SetRoot(ActiveTabPath());
            if (assetMode) _assetInspector.SetAsset(ActiveTabPath());
            if (listMode) RefreshAssetList(searchMode);
            if (goMode)
            {
                EnsureGameObjectView();
                RefreshGameObjectView(_tabs[_selectedIndex]);
            }

            if (mode == _rightPaneMode) return;

            // Leaving this mode: the split view loses its width while hidden, so
            // capture it before it goes away.
            if (_rightPaneMode == GoPaneMode && _goView != null)
            {
                EditorPrefs.SetFloat(HierarchySplitterKey, _goView.CurrentSplitterX());
                _goView.Invalidate();
            }

            _rightPaneMode = mode;

            _contentTree.style.display = treeMode ? DisplayStyle.Flex : DisplayStyle.None;
            _assetInspector.style.display = assetMode ? DisplayStyle.Flex : DisplayStyle.None;
            _assetList.style.display = listMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (_goView != null)
            {
                _goView.style.display = goMode ? DisplayStyle.Flex : DisplayStyle.None;
                if (goMode)
                    _goView.RestoreSplitter(EditorPrefs.GetFloat(HierarchySplitterKey, 220f));
            }
            _rightContainer.style.display =
                (treeMode || assetMode || listMode || goMode)
                    ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void DrawRightPanelIMGUI()
        {
            UpdateViewSize(_rightContainer);

            HandleDragHover();

            // This container is the right panel, so content starts at its own origin.
            _rightX = 0f;
            _rightW = _viewW;

            // Only reached when no dedicated view applies (no tabs, or no selection).
            if (_tabs.Count == 0) DrawEmptyState();
            else
                GUI.Label(new Rect(0, 0, _viewW, _viewH), "No tab selected.",
                    EditorStyles.centeredGreyMiniLabel);

            if (_searchInputText != _search.CommittedQuery)
                Repaint();
        }

        void DrawEmptyState()
        {
            Rect r = new Rect(_rightX, 0, _rightW, _viewH);

            if (_isDragHovering)
            {
                EditorGUI.DrawRect(r, new Color(0.2f, 0.5f, 1f, 0.15f));
                DrawBorder(r, new Color(0.3f, 0.6f, 1f, 0.8f), 2f);
            }

            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 14,
                normal = { textColor = _isDragHovering ? new Color(0.5f, 0.8f, 1f) : Color.gray }
            };
            GUI.Label(r, "⊕ Drag a folder or asset here", style);
        }

        static StyleSheet LoadStyleSheet()
        {
            foreach (string guid in AssetDatabase.FindAssets("BetterTabs t:StyleSheet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("BetterTabs.uss"))
                    return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            return null;
        }

        void OnTabBarDragUpdated(DragUpdatedEvent evt)
        {
            if (!IsDraggingAsset()) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        }

        void OnTabBarDragPerform(DragPerformEvent evt)
        {
            if (!IsDraggingAsset()) return;

            DragAndDrop.AcceptDrag();
            foreach (string path in DragAndDrop.paths)
                if (!string.IsNullOrEmpty(path))
                    AddOrSelectTab(path);

            if (DragAndDrop.objectReferences != null)
                foreach (Object o in DragAndDrop.objectReferences)
                    if (o is GameObject go && !AssetDatabase.Contains(go))
                        AddOrSelectSceneObjectTab(go);

            _isDragHovering = false;
            _tabBar.SetDropHint(false);
            evt.StopPropagation();
        }

        // ── Tab bar plumbing ──────────────────────────────────────────────────

        void RefreshTabBar()
        {
            if (_tabBar == null) return;
            // No tab owns the panel during a search, so none is drawn as active.
            _tabBar.SetTabs(_tabs, _search.IsSearching ? -1 : _selectedIndex);
            UpdateTabBarButtons();
        }

        void UpdateTabBarButtons()
        {
            if (_tabBar == null) return;
            bool showPing = _selectedIndex >= 0 && _selectedIndex < _tabs.Count
                && (_tabs[_selectedIndex].kind == BetterTabKind.SceneObject
                    || _tabs[_selectedIndex].kind == BetterTabKind.Prefab);
            // The view toggle only applies to folder tabs that are not showing search.
            bool showView = _tabs.Count > 0 && ActiveTabIsFolder() && !_search.IsSearching;
            _tabBar.SetButtonState(CanAddSelection(), showPing, _projectPanelOpen,
                showView, _gridView);
        }

        bool CanAddSelection()
        {
            if (Selection.activeObject == null) return false;

            string selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(selPath))
                return !_tabs.Any(t => t.kind != BetterTabKind.SceneObject && t.path == selPath);

            if (Selection.activeObject is GameObject go && !AssetDatabase.Contains(go))
            {
                string gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                return !_tabs.Any(t => t.kind == BetterTabKind.SceneObject && t.globalObjectId == gid);
            }
            return false;
        }

        void AddTabFromSelection()
        {
            if (Selection.activeObject == null) return;

            string selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(selPath)) AddOrSelectTab(selPath);
            else if (Selection.activeObject is GameObject go && !AssetDatabase.Contains(go))
                AddOrSelectSceneObjectTab(go);
        }

        void ToggleProjectPanel()
        {
            _projectPanelOpen = !_projectPanelOpen;
            if (_projectPanelOpen)
            {
                _projectTree.Rebuild();
                SyncProjectTreeHighlight();
            }
            ApplyProjectPanelVisibility();
            EditorPrefs.SetBool(ProjectPanelOpenKey, _projectPanelOpen);
            UpdateTabBarButtons();
        }

        // The view already moved the element, so only the model is updated here;
        // rebuilding mid-drag would break the pointer capture.
        void OnTabMoved(int from, int to)
        {
            if (from < 0 || from >= _tabs.Count || to < 0 || to >= _tabs.Count) return;

            BetterTabEntry moved = _tabs[from];
            _tabs.RemoveAt(from);
            _tabs.Insert(to, moved);

            if (_selectedIndex == from) _selectedIndex = to;
            else if (from < _selectedIndex && to >= _selectedIndex) _selectedIndex--;
            else if (from > _selectedIndex && to <= _selectedIndex) _selectedIndex++;

            BetterTabsPrefs.Save(_tabs, _selectedIndex);
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
        // ── Content area ──────────────────────────────────────────────────────
        // ── Asset pin view (embedded inspector) ──────────────────────────────
        // ── Hierarchy + Inspector view (Prefab / SceneObject tabs) ───────────
        static string TabKey(BetterTabEntry tab)
        {
            return tab.kind == BetterTabKind.SceneObject
                ? "scene|" + tab.globalObjectId
                : tab.kind + "|" + tab.path;
        }

        // ── Tree view ─────────────────────────────────────────────────────────
        // ── Search results ────────────────────────────────────────────────────
        // ── Grid view ─────────────────────────────────────────────────────────
        // ── Project panel ─────────────────────────────────────────────────────
        void SyncProjectTreeHighlight()
        {
            if (_projectTree == null) return;
            string path = !string.IsNullOrEmpty(_selectedAssetPath) ? _selectedAssetPath : ActiveTabPath();
            if (string.IsNullOrEmpty(path)) return;
            _projectTree.SetHighlight(path);
            _projectTree.ExpandToPath(path);
            ScrollProjectPanelToPath(path);
        }

        void ScrollProjectPanelToPath(string path)
        {
            if (!_projectPanelOpen || _projectTree == null) return;
            _projectTree.ScrollToPath(path);
        }

        // ── Folder drag-drop handling ─────────────────────────────────────────
        // Tracks whether a drag operation is currently in progress over the
        // window — used to show drop-zone highlights on ALL valid targets
        // (tab bar + active folder content area). Does NOT consume the event;
        // tree renderers handle row-specific drops themselves.
        void HandleDragHover()
        {
            var ev = Event.current;
            var windowRect = new Rect(0, 0, _viewW, _viewH);

            if (ev.type == EventType.DragUpdated)
            {
                if (windowRect.Contains(ev.mousePosition) && IsDraggingAsset())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (!_isDragHovering) { _isDragHovering = true; _tabBar?.SetDropHint(true); Repaint(); }
                }
                else if (_isDragHovering)
                {
                    _isDragHovering = false;
                    _tabBar?.SetDropHint(false);
                    Repaint();
                }
            }
            else if (ev.type == EventType.DragExited)
            {
                _isDragHovering = false;
                _tabBar?.SetDropHint(false);
                Repaint();
            }
        }

        // Runs AFTER tree renderers have drawn — if the drop hit the tab bar
        // area (and wasn't consumed elsewhere), create a tab from the payload.

        bool IsDraggingAsset()
        {
            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) return true;
            if (DragAndDrop.objectReferences != null)
            {
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is GameObject go && !AssetDatabase.Contains(go))
                        return true;
            }
            return false;
        }


        static void DrawBorder(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        // ── Asset selection / interaction callbacks ────────────────────────────

        void OnProjectPanelItemClicked(string path)
        {
            _selectedAssetPath = path;
            _projectTree.SetHighlight(path);
            string capturedPath = path;
            EditorApplication.delayCall += () =>
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(capturedPath);
                if (obj != null) Selection.activeObject = obj;
            };
            Repaint();
        }

        void OnAssetSelected(string path)
        {
            _selectedAssetPath = path;
            // Defer Selection.activeObject so it doesn't steal keyboard focus from this
            // window during event processing (which breaks F2 right after selecting).
            string capturedPath = path;
            EditorApplication.delayCall += () =>
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(capturedPath);
                if (obj != null) Selection.activeObject = obj;
            };
            if (_projectPanelOpen)
            {
                _projectTree.SetHighlight(path);
                _projectTree.ExpandToPath(path);
                ScrollProjectPanelToPath(path);
            }
            Repaint();
        }

        void OnAssetDragged(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            var objs = new List<Object>();
            foreach (var path in paths)
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (obj != null) objs.Add(obj);
            }
            if (objs.Count == 0) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objs.ToArray();
            DragAndDrop.paths = paths.ToArray();
            string label = objs.Count == 1
                ? Path.GetFileNameWithoutExtension(paths[0])
                : $"{objs.Count} items";
            DragAndDrop.StartDrag(label);
        }

        void OnAssetDraggedSingle(string path)
        {
            OnAssetDragged(new List<string> { path });
        }

        void OnAssetModified(string path)
        {
            if (_selectedAssetPath == path)
                _selectedAssetPath = null;
            Repaint();
        }

        // ── Prefab content lifecycle ──────────────────────────────────────────

        GameObject GetOrLoadPrefabRoot(string prefabPath)
        {
            if (_loadedPrefabRoots.TryGetValue(prefabPath, out var root) && root != null)
                return root;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                _loadedPrefabRoots[prefabPath] = root;
                return root;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BetterTabs] Failed to load prefab contents '{prefabPath}': {e.Message}");
                return null;
            }
        }

        void SaveAndUnloadPrefab(string prefabPath)
        {
            if (!_loadedPrefabRoots.TryGetValue(prefabPath, out var root) || root == null)
            {
                _loadedPrefabRoots.Remove(prefabPath);
                return;
            }

            // Final flush in case a pending delayCall hasn't run yet.
            try { PrefabUtility.SaveAsPrefabAsset(root, prefabPath); }
            catch (System.Exception e) { Debug.LogError($"[BetterTabs] Save prefab failed '{prefabPath}': {e.Message}"); }

            try { PrefabUtility.UnloadPrefabContents(root); }
            catch { /* ignore */ }

            _loadedPrefabRoots.Remove(prefabPath);
        }

        void SaveAndUnloadAllPrefabs()
        {
            var keys = new List<string>(_loadedPrefabRoots.Keys);
            foreach (var k in keys) SaveAndUnloadPrefab(k);
        }

        void RevertPrefab(string prefabPath)
        {
            if (_loadedPrefabRoots.TryGetValue(prefabPath, out var root) && root != null)
            {
                try { PrefabUtility.UnloadPrefabContents(root); } catch { /* ignore */ }
            }
            _loadedPrefabRoots.Remove(prefabPath);
            _goView?.Invalidate();
        }

        // ── Stacked component editors ─────────────────────────────────────────

        // ── Inline rename ─────────────────────────────────────────────────────

        // ── Keyboard shortcuts & scroll navigation ────────────────────────────
        // Window-level shortcuts. These used to live in the IMGUI panels, but the
        // left pane is no longer IMGUI and the right one is hidden in tree mode.
        // Registered with the ShortcutManager and scoped to this window: matching
        // Ctrl+letter combos on KeyDownEvent is unreliable (the key code and the
        // character arrive on separate events).
        [Shortcut("BetterTabs/Close Active Tab", typeof(BetterTabsWindow), KeyCode.W, ShortcutModifiers.Action)]
        static void CloseActiveTabShortcut(ShortcutArguments args)
        {
            if (args.context is BetterTabsWindow w && w._selectedIndex >= 0)
                w.RemoveTab(w._selectedIndex);
        }

        [Shortcut("BetterTabs/Reopen Closed Tab", typeof(BetterTabsWindow), KeyCode.T,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void ReopenClosedTabShortcut(ShortcutArguments args)
        {
            if (args.context is BetterTabsWindow w) w.ReopenClosedTab();
        }

        void OnRootWheel(WheelEvent evt)
        {
            if (!evt.shiftKey || _tabs.Count <= 1) return;

            // On Windows, Shift+Scroll arrives as horizontal delta.
            float raw = Mathf.Abs(evt.delta.y) > 0.01f ? evt.delta.y : evt.delta.x;
            int dir = raw > 0 ? -1 : 1;
            if (BetterTabsSettings.InvertScroll) dir = -dir;

            if (evt.ctrlKey || evt.commandKey) MoveTab(_selectedIndex, dir);
            else
            {
                int next = (_selectedIndex + dir + _tabs.Count) % _tabs.Count;
                if (next != _selectedIndex) SelectTab(next);
            }
            evt.StopPropagation();
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            bool ctrl = evt.ctrlKey || evt.commandKey;

            if (evt.shiftKey && !ctrl && _tabs.Count > 1
                && (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow))
            {
                int dir = evt.keyCode == KeyCode.RightArrow ? 1 : -1;
                int next = (_selectedIndex + dir + _tabs.Count) % _tabs.Count;
                if (next != _selectedIndex) SelectTab(next);
                evt.StopPropagation();
                return;
            }

        }

        void OpenNewTab()
        {
            // Use the active selection if it has an asset path or is a scene GameObject.
            if (Selection.activeObject != null)
            {
                string selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(selPath))
                {
                    AddOrSelectTab(selPath);
                    return;
                }
                if (Selection.activeObject is GameObject sceneGO && !AssetDatabase.Contains(sceneGO))
                {
                    AddOrSelectSceneObjectTab(sceneGO);
                    return;
                }
            }
            // Fallback: OS folder picker.
            string folder = EditorUtility.OpenFolderPanel("Add Folder Tab", "Assets", "");
            if (string.IsNullOrEmpty(folder)) return;
            if (folder.StartsWith(Application.dataPath))
                folder = "Assets" + folder.Substring(Application.dataPath.Length);
            if (!string.IsNullOrEmpty(folder))
                AddOrSelectTab(folder);
        }

        void ReopenClosedTab()
        {
            while (_closedTabStack.Count > 0)
            {
                (BetterTabEntry entry, int originalIndex) = _closedTabStack.Pop();
                if (entry == null) continue;

                // Skip if already open.
                bool alreadyOpen = false;
                foreach (var t in _tabs)
                {
                    if (entry.kind == BetterTabKind.SceneObject)
                    {
                        if (t.kind == BetterTabKind.SceneObject && t.globalObjectId == entry.globalObjectId)
                        { alreadyOpen = true; break; }
                    }
                    else if (!string.IsNullOrEmpty(entry.path) && t.path == entry.path)
                    { alreadyOpen = true; break; }
                }
                if (alreadyOpen) continue;

                // Re-insert the original entry (preserves expanded paths, search,
                // selection) back at the position it was closed from.
                int insertAt = Mathf.Clamp(originalIndex, 0, _tabs.Count);
                _tabs.Insert(insertAt, entry);
                SelectTab(insertAt);
                BetterTabsPrefs.Save(_tabs, _selectedIndex);
                return;
            }
        }

        void MoveTab(int index, int direction)
        {
            if (index < 0 || index >= _tabs.Count || _tabs.Count <= 1) return;
            // Wrap-around so both scroll directions always produce movement.
            int target = (index + direction + _tabs.Count) % _tabs.Count;

            (_tabs[index], _tabs[target]) = (_tabs[target], _tabs[index]);
            if (_selectedIndex == index) _selectedIndex = target;
            else if (_selectedIndex == target) _selectedIndex = index;

            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            RefreshTabBar();
            Repaint();
        }

        // ── Tab management ────────────────────────────────────────────────────
        void AddOrSelectTab(string path)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].kind != BetterTabKind.SceneObject && _tabs[i].path == path)
                { SelectTab(i); return; }
            }
            _tabs.Add(new BetterTabEntry(path));
            SelectTab(_tabs.Count - 1);
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void AddOrSelectSceneObjectTab(GameObject go)
        {
            if (go == null) return;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].kind == BetterTabKind.SceneObject && _tabs[i].globalObjectId == id)
                { SelectTab(i); return; }
            }
            _tabs.Add(new BetterTabEntry(go));
            SelectTab(_tabs.Count - 1);
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void SelectTab(int index)
        {
            PersistActiveTabState();
            _selectedIndex = index;
            _selectedAssetPath = null;
            _assetScroll = Vector2.zero;

            var tab = _tabs[index];
            if (tab.kind == BetterTabKind.SceneObject)
            {
                if (GlobalObjectId.TryParse(tab.globalObjectId, out var gid))
                {
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                    if (obj != null) Selection.activeObject = obj;
                }
            }
            else if (!AssetDatabase.IsValidFolder(tab.path))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(tab.path);
                if (obj != null) Selection.activeObject = obj;
            }

            RestoreActiveTabState();
            SyncProjectTreeHighlight();
            RefreshTabBar();
            _tabBar?.ScrollToSelected();
            Repaint();
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
        }

        void RemoveTab(int index)
        {
            var removed = _tabs[index];
            // Save & unload prefab contents if no other tab still references it
            if (removed.kind == BetterTabKind.Prefab && !string.IsNullOrEmpty(removed.path))
            {
                bool stillReferenced = false;
                for (int i = 0; i < _tabs.Count; i++)
                {
                    if (i == index) continue;
                    if (_tabs[i].kind == BetterTabKind.Prefab && _tabs[i].path == removed.path)
                    { stillReferenced = true; break; }
                }
                if (!stillReferenced) SaveAndUnloadPrefab(removed.path);
            }
            _closedTabStack.Push((removed, index));
            _tabs.RemoveAt(index);
            if (_tabs.Count == 0)
            {
                _selectedIndex = -1;
                _search.Clear();
                _searchInputText = "";
            }
            else
            {
                _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _tabs.Count - 1);
                RestoreActiveTabState();
            }
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            RefreshTabBar();
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
            var tab = _tabs[index];
            Object obj = null;
            if (tab.kind == BetterTabKind.SceneObject)
            {
                if (GlobalObjectId.TryParse(tab.globalObjectId, out var gid))
                    obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            }
            else if (!string.IsNullOrEmpty(tab.path))
            {
                obj = AssetDatabase.LoadAssetAtPath<Object>(tab.path);
            }
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
            _contentTree?.SaveExpandedState(tab.expandedPaths);
            tab.searchQuery = _search.CommittedQuery;
        }

        void RestoreActiveTabState()
        {
            _search.Clear();
            _searchInputText = "";

            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            var tab = _tabs[_selectedIndex];

            _contentTree?.LoadExpandedState(tab.expandedPaths);

            if (!string.IsNullOrEmpty(tab.searchQuery))
            {
                _searchInputText = tab.searchQuery;
                _search.ForceCommit(tab.searchQuery);
            }
        }

        void OnFoldoutToggled()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
            {
                _contentTree?.SaveExpandedState(_tabs[_selectedIndex].expandedPaths);
                BetterTabsPrefs.Save(_tabs, _selectedIndex);
            }
        }

        void ClearSearch()
        {
            _searchInputText = "";
            _search.Clear();
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count)
                _tabs[_selectedIndex].searchQuery = "";
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
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

        bool ActiveTabIsFolder()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return false;
            return _tabs[_selectedIndex].kind == BetterTabKind.Folder;
        }

        internal static Texture2D GetTabIconFor(BetterTabEntry tab)
        {
            if (tab.kind == BetterTabKind.SceneObject)
                return EditorGUIUtility.IconContent("GameObject Icon").image as Texture2D;
            if (tab.kind == BetterTabKind.Folder)
                return EditorGUIUtility.FindTexture("Folder Icon");
            if (!string.IsNullOrEmpty(tab.path))
            {
                var icon = AssetDatabase.GetCachedIcon(tab.path) as Texture2D;
                if (icon != null) return icon;
            }
            return EditorGUIUtility.FindTexture("DefaultAsset Icon");
        }

        static Texture2D LoadWindowIcon()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Script BetterTabsWindow"))
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
