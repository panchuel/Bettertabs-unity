using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        // Prefabs edited since they were last written. Only these are ever saved: writing
        // an untouched prefab still counts as an asset change, and doing it for every
        // open tab on each domain reload made Multiplayer Play Mode clones (whose asset
        // database is read-only) report out-of-date assets on entering Play Mode.
        readonly HashSet<string> _dirtyPrefabs = new HashSet<string>();
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
        BetterSplitterTracker _projectSplitter;
        BetterAssetTreeView _projectTree;
        Object _lastSyncedSelection;

        // ── UI Toolkit ────────────────────────────────────────────────────────
        BetterTabsBarView _tabBar;
        BetterTabsToolbarView _toolbar;
        IMGUIContainer _rightContainer;

        // ── In-window pages (How to Use, Settings) ────────────────────────────
        enum Page { None, Help, Settings }

        static readonly string[] HelpIconNames = { "d__Help", "_Help" };
        static readonly string[] SettingsIconNames = { "d_Settings", "Settings", "d_SettingsIcon", "SettingsIcon" };

        Page _openPage = Page.None;
        VisualElement _pageHost;
        Label _pageTitle;
        ScrollView _pageScroll;
        Button _helpButton;
        Button _settingsButton;
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
        // One entry, no submenu: How to Use and Settings are pages of the window itself,
        // reached from the ? and gear at its bottom right.
        [MenuItem("Window/Panchuel/BetterTabs")]
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
            // A scene object tab can only be focused while its scene is open.
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;

            // Reference clicks made in Unity's own Inspector land here too, so the
            // left panel follows wherever the user is reading the object from.
            BetterNativeInspectorLink.Enable(RevealInProjectPanel);

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        void OnDisable()
        {
            s_instance = null;
            BetterNativeInspectorLink.Disable();
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.projectChanged -= OnProjectChanged;
            Selection.selectionChanged -= OnUnitySelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            _assetInspector?.DestroyEditor();
            SaveAndUnloadAllPrefabs();
            _goView?.Invalidate();
            PersistActiveTabState();
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            EditorPrefs.SetBool(ProjectPanelOpenKey, _projectPanelOpen);
            EditorPrefs.SetFloat(SplitterXKey, CurrentSplitterX());
            if (_goView != null)
                EditorPrefs.SetFloat(HierarchySplitterKey, _goView.CurrentSplitterX());
            // Straight from the view rather than from the mirror kept by its events,
            // for the same reason the splitters are: the live owner is the authority.
            if (_assetInspector != null)
            {
                _previewHeight = _assetInspector.CurrentPreviewHeight;
                _previewCollapsed = _assetInspector.IsPreviewCollapsed;
            }
            EditorPrefs.SetFloat(PreviewHeightKey, _previewHeight);
            EditorPrefs.SetBool(PreviewCollapsedKey, _previewCollapsed);
            if (_projectTree != null)
                EditorPrefs.SetString(ProjectExpandedKey, _projectTree.SaveExpandedState());
        }

        void OnProjectChanged()
        {
            RequestRefresh();
        }

        // A script reload disables every ScriptableObject, the inspector's importer editor
        // included, before this window's OnDisable runs. Destroying the editor there then
        // disabled it a second time, and an AssetImporterEditor reports a second OnDisable
        // as "OnEnable must call base.OnEnable". Destroying it here, before Unity touches
        // anything, disables it exactly once.
        void OnBeforeAssemblyReload()
        {
            _assetInspector?.DestroyEditor();
        }

        void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            UpdateTabBarButtons();
        }

        void OnSceneClosed(UnityEngine.SceneManagement.Scene scene)
        {
            UpdateTabBarButtons();
        }

        // Polling fallback: Selection.selectionChanged isn't always delivered to a
        // custom window depending on context, so also watch the selection each tick.
        void OnEditorUpdate()
        {
            // Picks up Inspector windows opened, docked or rebuilt since the last scan.
            BetterNativeInspectorLink.Tick();

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
                OnUnitySelectionChanged();
                Repaint();
            }

            if (Selection.activeObject == _lastSyncedSelection) return;
            _lastSyncedSelection = Selection.activeObject;
            OnUnitySelectionChanged();
            // The + button is enabled only when the selection is not already a tab.
            UpdateTabBarButtons();
        }

        // Unity's selection is the single source of truth for what is selected. Whatever
        // picked it (either BetterTabs panel, the Project window, the Inspector, a field
        // reference), both panels here are made to match it, including clearing them when
        // the selection is not an asset they hold. Each panel used to keep its own last
        // pick, which is how an old selection stayed marked next to the new one.
        void OnUnitySelectionChanged()
        {
            List<string> paths = SelectedAssetPaths();
            string activePath = Selection.activeObject != null
                ? BetterAssetTreeView.KeyFor(Selection.activeObject)
                : null;

            // The grid lists files only, so it follows the file a sub-asset belongs to.
            string activeFile = BetterAssetTreeView.MainPathOf(activePath);
            _selectedAssetPath = string.IsNullOrEmpty(activeFile) ? null : activeFile;
            _assetList?.SetSelected(_selectedAssetPath);

            if (_projectTree != null && !IsSameSelection(_projectTree.GetSelectedPaths(), paths))
            {
                // Expanded first: a row inside a collapsed folder cannot be selected.
                if (!string.IsNullOrEmpty(activePath)) _projectTree.ExpandToPath(activePath);
                _projectTree.SetSelectedPaths(paths);
                if (!string.IsNullOrEmpty(activePath)) ScrollProjectPanelToPath(activePath);
            }

            // The right tree is left as the tab's layout: only rows it already shows are marked.
            if (_contentTree != null && !IsSameSelection(_contentTree.GetSelectedPaths(), paths))
                _contentTree.SetSelectedPaths(paths);

            Repaint();
        }

        static List<string> SelectedAssetPaths()
        {
            List<string> paths = new List<string>();
            foreach (Object selected in Selection.objects)
            {
                string path = BetterAssetTreeView.KeyFor(selected);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets") && !paths.Contains(path))
                    paths.Add(path);
            }
            return paths;
        }

        static bool IsSameSelection(List<string> current, List<string> wanted)
        {
            if (current.Count != wanted.Count) return false;
            foreach (string path in wanted)
                if (!current.Contains(path)) return false;
            return true;
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        // ── UI Toolkit root ──────────────────────────────────────────────────
        // Migration scaffolding: the window root is UI Toolkit, and the legacy IMGUI
        // drawing runs inside a single IMGUIContainer. Each migrated area moves out of
        // this container into real VisualElements, so the tool keeps working throughout.
        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            // Carries the design tokens every other rule reads through var().
            root.AddToClassList("bt-root");
            root.style.flexGrow = 1;
            // TrickleDown: seen before the trees, which consume plain arrow keys.
            root.RegisterCallback<WheelEvent>(OnRootWheel, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            // Bubble phase: a text field (search, rename) handles its own copy/paste first
            // and stops it, so only commands aimed at the asset panels arrive here.
            root.RegisterCallback<ValidateCommandEvent>(OnRootValidateCommand);
            root.RegisterCallback<ExecuteCommandEvent>(OnRootExecuteCommand);

            StyleSheet sheet = LoadStyleSheet();
            if (sheet != null) root.styleSheets.Add(sheet);

            _tabBar = new BetterTabsBarView();
            _tabBar.TabSelected += SelectTab;
            _tabBar.TabClosed += RemoveTab;
            _tabBar.TabContextMenu += ShowTabContextMenu;
            _tabBar.TabMoved += OnTabMoved;
            _tabBar.AddClicked += AddTabFromSelection;
            _tabBar.UnsavedProvider = tab => tab.kind == BetterTabKind.Prefab && _dirtyPrefabs.Contains(tab.path);
            // Dropping assets on the bar creates tabs (was IMGUI DragPerform before).
            _tabBar.RegisterCallback<DragUpdatedEvent>(OnTabBarDragUpdated);
            _tabBar.RegisterCallback<DragPerformEvent>(OnTabBarDragPerform);
            root.Add(_tabBar);

            _toolbar = new BetterTabsToolbarView();
            _toolbar.SearchChanged += OnSearchChanged;
            _toolbar.CrumbClicked += OnBreadcrumbClicked;
            _toolbar.FocusClicked += () => PingTab(_selectedIndex);
            _toolbar.SaveClicked += SaveActivePrefab;
            _toolbar.PanelToggleClicked += ToggleProjectPanel;
            _toolbar.UnitySearchClicked += OpenUnitySearchWindow;
            _toolbar.ViewModeChanged += grid =>
            {
                if (_gridView == grid) return;
                _gridView = grid;
                UpdateTabBarButtons();
            };
            root.Add(_toolbar);

            // Left/right panels split by a native TwoPaneSplitView (replaces the
            // hand-rolled splitter). Child 0 is the collapsible project panel.
            _splitView = new TwoPaneSplitView(0, Mathf.Max(MinPanelW, _splitterX), TwoPaneSplitViewOrientation.Horizontal);
            _splitView.style.flexGrow = 1;

            _projectTree = new BetterAssetTreeView(multiSelect: true, showTypeColumn: false, showAddButton: true);
            _projectTree.Rebuild();
            _projectTree.style.minWidth = MinPanelW;
            _projectTree.AddToClassList("bt-panel");
            _projectTree.ItemSelected += OnProjectPanelItemClicked;
            _projectTree.ItemActivated += OnProjectPanelItemActivated;
            _projectTree.ItemsDragged += OnAssetDragged;
            _projectTree.RefreshRequested += RequestRefresh;
            _projectTree.ExpandedChanged += SaveProjectPanelExpansion;
            _projectTree.RowBackgroundProvider = GetRowBackgroundTint;
            _projectTree.LoadExpandedState(EditorPrefs.GetString(ProjectExpandedKey, ""));
            _splitView.Add(_projectTree);

            // Built only once the pane it watches exists. Same reason as the hierarchy
            // pane: a collapsed panel measures 0 and a narrow window squeezes this one,
            // so only a drag of the divider is recorded as a chosen width.
            _projectSplitter = new BetterSplitterTracker(_splitView, _projectTree, _splitterX);

            // Right pane: search toolbar on top, then either the native content tree
            // or the remaining IMGUI views (asset pin, prefab, grid, search results).
            _rightPane = new VisualElement();
            _rightPane.AddToClassList("bt-content");
            _rightPane.style.flexGrow = 1;
            _rightPane.style.minWidth = MinPanelW;

            _contentTree = new BetterAssetTreeView(multiSelect: false, showTypeColumn: true, showAddButton: false);
            _contentTree.ItemSelected += OnAssetSelected;
            _contentTree.ItemActivated += BetterTabsInteractionHandler.OpenAsset;
            _contentTree.ItemsDragged += OnAssetDragged;
            _contentTree.RefreshRequested += RequestRefresh;
            _contentTree.ExpandedChanged += OnFoldoutToggled;
            _contentTree.TabColorProvider = GetTabColorForPath;
            _rightPane.Add(_contentTree);

            _assetInspector = new BetterAssetInspectorView();
            _assetInspector.OpenRequested += BetterTabsInteractionHandler.OpenAsset;
            _assetInspector.AssetReferenceClicked += RevealInProjectPanel;
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

            // The pages lie over the split view instead of replacing it: hiding the split
            // view before its first layout (the guide opens straight away the first time)
            // would leave its divider dead.
            VisualElement body = new VisualElement();
            body.AddToClassList("bt-body");
            body.Add(_splitView);
            body.Add(BuildPageHost());
            root.Add(body);
            root.Add(BuildFooter());

            // Deferred: the split view must resolve its layout before it can collapse.
            _splitView.schedule.Execute(ApplyProjectPanelVisibility);

            RefreshTabBar();

            if (!BetterTabsSettings.HasSeenHelp)
            {
                BetterTabsSettings.HasSeenHelp = true;
                _openPage = Page.Help;
            }
            ApplyPage();
        }

        // ── Pages ─────────────────────────────────────────────────────────────
        // How to Use and Settings are pages of this window, not windows of their own:
        // they take the content area, with the tab bar still in place, and close with
        // their ×, with Esc, or by picking a tab.

        VisualElement BuildPageHost()
        {
            _pageHost = new VisualElement();
            _pageHost.AddToClassList("bt-page-host");

            VisualElement header = new VisualElement();
            header.AddToClassList("bt-page-host__header");

            _pageTitle = new Label();
            _pageTitle.AddToClassList("bt-page-host__title");
            header.Add(_pageTitle);

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("bt-toolbar__spacer");
            header.Add(spacer);

            Button close = new Button(ClosePage) { text = "×", tooltip = "Close (Esc)" };
            close.AddToClassList("bt-toolbar__btn");
            header.Add(close);
            _pageHost.Add(header);

            _pageScroll = new ScrollView(ScrollViewMode.Vertical);
            _pageScroll.AddToClassList("bt-page-host__scroll");
            _pageHost.Add(_pageScroll);

            return _pageHost;
        }

        // Thin strip along the bottom: the help and settings buttons sit at its right end.
        VisualElement BuildFooter()
        {
            VisualElement footer = new VisualElement();
            footer.AddToClassList("bt-footer");

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("bt-toolbar__spacer");
            footer.Add(spacer);

            _helpButton = MakeFooterButton(HelpIconNames, "?", "How to Use", () => TogglePage(Page.Help));
            footer.Add(_helpButton);

            _settingsButton = MakeFooterButton(SettingsIconNames, "⚙", "Settings", () => TogglePage(Page.Settings));
            footer.Add(_settingsButton);

            return footer;
        }

        static Button MakeFooterButton(string[] iconNames, string fallbackGlyph, string tooltip, System.Action onClick)
        {
            Button button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("bt-footer__btn");

            Texture2D icon = null;
            foreach (string iconName in iconNames)
            {
                icon = EditorGUIUtility.FindTexture(iconName);
                if (icon != null) break;
            }

            if (icon == null)
            {
                button.text = fallbackGlyph;
                return button;
            }

            Image image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("bt-footer__icon");
            button.Add(image);
            return button;
        }

        void OpenPage(Page page)
        {
            _openPage = page;
            ApplyPage();
        }

        void TogglePage(Page page)
        {
            if (_openPage == page) ClosePage();
            else OpenPage(page);
        }

        void ClosePage()
        {
            if (_openPage == Page.None) return;
            _openPage = Page.None;
            ApplyPage();
        }

        void ApplyPage()
        {
            // A page chosen before the UI exists (the first-open guide) is applied by CreateGUI.
            if (_pageHost == null) return;

            bool isOpen = _openPage != Page.None;
            _pageHost.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _helpButton.EnableInClassList("bt-footer__btn--active", _openPage == Page.Help);
            _settingsButton.EnableInClassList("bt-footer__btn--active", _openPage == Page.Settings);

            _pageScroll.Clear();
            if (!isOpen) return;

            // Rebuilt on every open so the settings always show their current values.
            _pageTitle.text = _openPage == Page.Help ? "How to Use" : "Settings";
            _pageScroll.Add(_openPage == Page.Help ? (VisualElement)new BetterTabsHelpView() : new BetterTabsSettingsView());
            _pageScroll.scrollOffset = Vector2.zero;
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
            return _projectSplitter != null ? _projectSplitter.Width : _splitterX;
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

            // Search results span the whole project, so they have no folder to drop into.
            _assetList.DropFolder = searching ? null : ActiveTabPath();

            if (key == _assetListKey)
            {
                _assetList.SetSelected(_selectedAssetPath);
                return;
            }
            _assetListKey = key;

            if (searching)
            {
                _assetList.SetItems(_search.Results, BetterAssetListView.Mode.List);
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

        // Like the Project window: a folder opens (here, as a tab) and anything else opens in
        // its own editor, so double-clicking a prefab enters Prefab Mode. Assets are pinned
        // as tabs by dragging them onto the window or with Ctrl+T.
        void OnProjectPanelItemActivated(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) AddOrSelectTab(path);
            else BetterTabsInteractionHandler.OpenAsset(path);
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
            ClosePage();
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
            _goView.AssetReferenceClicked += RevealInProjectPanel;
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

            // Not written here any more: the prefab is saved with the toolbar's Save (or
            // Ctrl+S), and automatically when its tab is left or closed. The first edit
            // flips the tab's unsaved mark and enables Save.
            if (_dirtyPrefabs.Add(tab.path)) RefreshTabBar();
        }

        void SaveActivePrefab()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            BetterTabEntry tab = _tabs[_selectedIndex];
            if (tab.kind != BetterTabKind.Prefab) return;

            SavePrefabIfDirty(tab.path);
            RefreshTabBar();
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

            if (treeMode) _contentTree.SetRoot(ActiveTabPath(), _tabs[_selectedIndex].expandedPaths);
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

        // Also used by the drag ghost, which is laid over other windows.
        internal static StyleSheet LoadStyleSheet()
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

            AddTabsFromDrag();
            evt.StopPropagation();
        }

        // Shared by every place a drop creates tabs: the tab bar and the empty panel.
        void AddTabsFromDrag()
        {
            DragAndDrop.AcceptDrag();

            if (DragAndDrop.paths != null)
                foreach (string path in DragAndDrop.paths)
                    // A file dragged from the OS file browser has no asset path yet, so
                    // there is nothing to pin; it only makes sense dropped on a folder.
                    if (!string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith("Assets/"))
                        AddOrSelectTab(path);

            if (DragAndDrop.objectReferences != null)
                foreach (Object o in DragAndDrop.objectReferences)
                    if (o is GameObject go && !AssetDatabase.Contains(go))
                        AddOrSelectSceneObjectTab(go);

            _isDragHovering = false;
            _tabBar?.SetDropHint(false);
        }

        // ── Tab bar plumbing ──────────────────────────────────────────────────

        void RefreshTabBar()
        {
            if (_tabBar == null) return;
            // No tab owns the panel during a search, so none is drawn as active.
            _tabBar.SetTabs(_tabs, _search.IsSearching ? -1 : _selectedIndex);
            UpdateTabBarButtons();
            // Closing or adding a tab changes which rows are tinted, not just picking
            // a colour does, so the trees are re-bound from the same place.
            _projectTree?.RefreshRows();
            _contentTree?.RefreshRows();
        }

        void UpdateTabBarButtons()
        {
            if (_tabBar == null) return;
            _tabBar.SetAddEnabled(CanAddSelection());

            if (_toolbar == null) return;
            // A hierarchy tab swaps the search box for the action that fits it. Focus in
            // Scene only exists while the object is actually in an open scene: a prefab
            // asset has no scene, and a scene object tab can outlive its scene being open.
            bool isHierarchyTab = _selectedIndex >= 0 && _selectedIndex < _tabs.Count
                && (_tabs[_selectedIndex].kind == BetterTabKind.SceneObject
                    || _tabs[_selectedIndex].kind == BetterTabKind.Prefab);
            bool showFocus = isHierarchyTab && IsSceneObjectLoaded(_tabs[_selectedIndex]);
            // The view toggle only applies to folder tabs that are not showing search.
            bool showView = _tabs.Count > 0 && ActiveTabIsFolder() && !_search.IsSearching;

            bool isPrefabTab = isHierarchyTab && _tabs[_selectedIndex].kind == BetterTabKind.Prefab;
            bool canSave = isPrefabTab && _dirtyPrefabs.Contains(_tabs[_selectedIndex].path);
            _toolbar.SetState(!isHierarchyTab, showView, _gridView, showFocus, _projectPanelOpen, isPrefabTab, canSave);
            UpdateBreadcrumb();
        }

        // Walkable path of the active tab: every segment but the last opens that
        // folder. A search or a scene object has no path to walk, so it shows one crumb.
        void UpdateBreadcrumb()
        {
            if (_toolbar == null) return;

            if (_search.IsSearching)
            {
                _toolbar.SetBreadcrumb(new List<string> { "Search results" }, null);
                return;
            }
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count)
            {
                _toolbar.SetBreadcrumb(null, null);
                return;
            }

            BetterTabEntry tab = _tabs[_selectedIndex];
            if (tab.kind == BetterTabKind.SceneObject || string.IsNullOrEmpty(tab.path))
            {
                _toolbar.SetBreadcrumb(new List<string> { tab.name }, null);
                return;
            }

            string[] parts = tab.path.Split('/');
            List<string> labels = new List<string>(parts.Length);
            List<string> paths = new List<string>(parts.Length);
            string running = "";
            for (int i = 0; i < parts.Length; i++)
            {
                running = i == 0 ? parts[0] : running + "/" + parts[i];
                labels.Add(parts[i]);
                paths.Add(running);
            }
            _toolbar.SetBreadcrumb(labels, paths);
        }

        static bool IsSceneObjectLoaded(BetterTabEntry tab)
        {
            if (tab.kind != BetterTabKind.SceneObject) return false;
            if (!GlobalObjectId.TryParse(tab.globalObjectId, out GlobalObjectId gid)) return false;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) != null;
        }

        void OnBreadcrumbClicked(string path)
        {
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;
            AddOrSelectTab(path);
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
                // Guarded like every other use of the tree in this class: the toolbar
                // button exists before CreateGUI has finished building the panels.
                _projectTree?.Rebuild();
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
            menu.AddItem(new GUIContent("Color…"), false, () => ShowTabColorPicker(index));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open in Project"), false, () => PingTab(index));
            menu.ShowAsContext();
        }

        void ShowTabColorPicker(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            BetterTabColorPicker.Show(rootVisualElement, _tabBar.GetTabWorldBound(index),
                _tabs[index].colorIndex, colorIndex => SetTabColor(index, colorIndex));
        }

        void SetTabColor(int index, int colorIndex)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (_tabs[index].colorIndex == colorIndex) return;

            _tabs[index].colorIndex = colorIndex;
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            RefreshTabBar();
            Repaint();
        }

        // Left panel: the tagged folder's row is washed with its colour, and every row
        // beneath it takes a darker, fainter shade of the same one. Where tags nest,
        // the deepest one wins, so a tagged subfolder overrides the tag above it.
        Color GetRowBackgroundTint(string path)
        {
            if (string.IsNullOrEmpty(path)) return Color.clear;

            int color = BetterTabColors.None;
            int matchedLength = -1;
            bool isTaggedRow = false;

            for (int i = 0; i < _tabs.Count; i++)
            {
                BetterTabEntry tab = _tabs[i];
                if (tab.kind == BetterTabKind.SceneObject) continue;
                if (string.IsNullOrEmpty(tab.path)) continue;
                if (!BetterTabColors.IsColored(tab.colorIndex)) continue;
                if (tab.path.Length <= matchedLength) continue;

                bool exact = path == tab.path;
                if (!exact && !path.StartsWith(tab.path + "/")) continue;

                color = tab.colorIndex;
                matchedLength = tab.path.Length;
                isTaggedRow = exact;
            }

            return BetterTabColors.GetRowTint(color, isTaggedRow);
        }

        // The colour is a property of the tab, so a tree row picks it up by matching
        // its path. Scene object tabs have no path and never tint a row.
        int GetTabColorForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return BetterTabColors.None;

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].kind == BetterTabKind.SceneObject) continue;
                if (_tabs[i].path == path) return _tabs[i].colorIndex;
            }
            return BetterTabColors.None;
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
        // Switching tabs reveals where the tab lives in the left panel. It is shown as the
        // panel's selection: a second "highlight" mark looked exactly like a selection and
        // left two rows marked at once.
        void SyncProjectTreeHighlight()
        {
            if (_projectTree == null) return;
            string path = ActiveTabPath();
            if (string.IsNullOrEmpty(path)) return;
            _projectTree.ExpandToPath(path);
            _projectTree.SetSelectedPath(path);
            ScrollProjectPanelToPath(path);
        }

        void ScrollProjectPanelToPath(string path)
        {
            if (!_projectPanelOpen || _projectTree == null) return;
            _projectTree.ScrollToPath(path);
        }

        // Selecting rather than only highlighting: this mirrors what clicking an asset
        // anywhere else in the window does, so the left panel ends up in one state.
        // The selection is set even with the panel closed, so opening it is already right.
        void RevealInProjectPanel(string path)
        {
            if (_projectTree == null || string.IsNullOrEmpty(path)) return;

            _projectTree.SetSelectedPath(path);
            _projectTree.ExpandToPath(path);
            ScrollProjectPanelToPath(path);
            Repaint();
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
            // The empty panel ("Drag a folder or asset here") only ever showed the hover
            // highlight: the drop itself was never handled, so nothing happened.
            else if (ev.type == EventType.DragPerform)
            {
                if (!windowRect.Contains(ev.mousePosition) || !IsDraggingAsset()) return;

                AddTabsFromDrag();
                ev.Use();
                Repaint();
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

        // The whole multi-selection goes to Unity, with the clicked row as the active one;
        // handing over only the last row collapsed the panel's selection to one item as
        // soon as Unity's selection was mirrored back.
        void OnProjectPanelItemClicked(string path)
        {
            _selectedAssetPath = path;
            List<string> selectedPaths = _projectTree.GetSelectedPaths();
            string activePath = path;
            EditorApplication.delayCall += () => SetUnitySelection(selectedPaths, activePath);
            Repaint();
        }

        static void SetUnitySelection(List<string> paths, string activePath)
        {
            List<Object> objects = new List<Object>();
            Object active = null;
            foreach (string path in paths)
            {
                Object obj = BetterAssetTreeView.LoadObject(path);
                if (obj == null) continue;
                objects.Add(obj);
                if (path == activePath) active = obj;
            }

            Selection.objects = objects.ToArray();
            if (active != null) Selection.activeObject = active;
        }

        void OnAssetSelected(string path)
        {
            _selectedAssetPath = path;
            // Defer Selection.activeObject so it doesn't steal keyboard focus from this
            // window during event processing (which breaks F2 right after selecting).
            string capturedPath = path;
            EditorApplication.delayCall += () =>
            {
                Object obj = BetterAssetTreeView.LoadObject(capturedPath);
                if (obj != null) Selection.activeObject = obj;
            };
            // The left panel follows through OnUnitySelectionChanged once Unity's selection
            // lands, like any other selection.
            Repaint();
        }

        void OnAssetDragged(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            var objs = new List<Object>();
            // Only whole files travel as paths: a sub-asset dropped on a folder must not
            // move the file it lives in, it can only be assigned to fields.
            List<string> filePaths = new List<string>();
            foreach (var path in paths)
            {
                var obj = BetterAssetTreeView.LoadObject(path);
                if (obj == null) continue;
                objs.Add(obj);
                if (!BetterAssetTreeView.IsSubAssetKey(path)) filePaths.Add(path);
            }
            if (objs.Count == 0) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objs.ToArray();
            DragAndDrop.paths = filePaths.ToArray();
            string label = objs.Count == 1
                ? objs[0].name
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

        void SavePrefabIfDirty(string prefabPath)
        {
            if (!_dirtyPrefabs.Contains(prefabPath)) return;
            if (!_loadedPrefabRoots.TryGetValue(prefabPath, out GameObject root) || root == null) return;

            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                _dirtyPrefabs.Remove(prefabPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BetterTabs] Save prefab failed '{prefabPath}': {e.Message}");
            }
        }

        void SaveAndUnloadPrefab(string prefabPath)
        {
            if (!_loadedPrefabRoots.TryGetValue(prefabPath, out var root) || root == null)
            {
                _loadedPrefabRoots.Remove(prefabPath);
                _dirtyPrefabs.Remove(prefabPath);
                return;
            }

            // Final flush in case a pending delayCall hasn't run yet; untouched prefabs
            // are not written at all.
            SavePrefabIfDirty(prefabPath);

            try { PrefabUtility.UnloadPrefabContents(root); }
            catch { /* ignore */ }

            _loadedPrefabRoots.Remove(prefabPath);
            _dirtyPrefabs.Remove(prefabPath);
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
            _dirtyPrefabs.Remove(prefabPath);
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

        // Scoped to this window, so while BetterTabs has focus Ctrl+S saves the prefab tab
        // instead of the open scene.
        [Shortcut("BetterTabs/Save Prefab", typeof(BetterTabsWindow), KeyCode.S, ShortcutModifiers.Action)]
        static void SavePrefabShortcut(ShortcutArguments args)
        {
            if (args.context is BetterTabsWindow w) w.SaveActivePrefab();
        }

        [Shortcut("BetterTabs/Reopen Closed Tab", typeof(BetterTabsWindow), KeyCode.T,
            ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void ReopenClosedTabShortcut(ShortcutArguments args)
        {
            if (args.context is BetterTabsWindow w) w.ReopenClosedTab();
        }

        // Left unbound on purpose: every free Ctrl/Ctrl+Shift letter is already taken
        // by Unity itself, so the binding is the user's to pick in Edit ▸ Shortcuts.
        [Shortcut("BetterTabs/Set Tab Color", typeof(BetterTabsWindow))]
        static void SetTabColorShortcut(ShortcutArguments args)
        {
            if (args.context is BetterTabsWindow w) w.ShowTabColorPicker(w._selectedIndex);
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

        const string CopyCommand = "Copy";
        const string CutCommand = "Cut";
        const string PasteCommand = "Paste";
        const string DuplicateCommand = "Duplicate";

        void OnRootValidateCommand(ValidateCommandEvent evt)
        {
            if (!TryGetCommandContext(evt.target as VisualElement, out List<string> selected, out string rootFolder)) return;
            if (CanRunCommand(evt.commandName, selected)) evt.StopPropagation();
        }

        void OnRootExecuteCommand(ExecuteCommandEvent evt)
        {
            if (!TryGetCommandContext(evt.target as VisualElement, out List<string> selected, out string rootFolder)) return;
            if (!CanRunCommand(evt.commandName, selected)) return;

            List<string> created = null;
            switch (evt.commandName)
            {
                case CopyCommand:
                    BetterAssetClipboard.Copy(selected);
                    break;

                case CutCommand:
                    BetterAssetClipboard.Cut(selected);
                    break;

                case PasteCommand:
                    created = BetterAssetClipboard.PasteInto(PasteFolderFor(selected, rootFolder));
                    break;

                case DuplicateCommand:
                    created = new List<string>();
                    foreach (string path in selected)
                        if (!BetterAssetTreeView.IsSubAssetKey(path))
                            created.Add(BetterTabsInteractionHandler.Duplicate(path));
                    break;

                default:
                    return;
            }

            if (created != null && created.Count > 0)
            {
                RequestRefresh();
                string active = created[created.Count - 1];
                EditorApplication.delayCall += () => SetUnitySelection(created, active);
            }

            evt.StopPropagation();
        }

        static bool CanRunCommand(string command, List<string> selected)
        {
            switch (command)
            {
                case PasteCommand:
                    return BetterAssetClipboard.HasItems;

                case CopyCommand:
                case CutCommand:
                case DuplicateCommand:
                    foreach (string path in selected)
                        if (path != "Assets" && !BetterAssetTreeView.IsSubAssetKey(path)) return true;
                    return false;

                default:
                    return false;
            }
        }

        // Which asset panel a command was aimed at, what is selected there and the folder
        // it shows. Commands anywhere else (a text field, the prefab inspector) are left alone.
        bool TryGetCommandContext(VisualElement target, out List<string> selected, out string rootFolder)
        {
            selected = null;
            rootFolder = null;
            if (target == null || target is TextField || target.GetFirstAncestorOfType<TextField>() != null) return false;

            if (_projectTree != null && _projectTree.Contains(target))
            {
                selected = _projectTree.GetSelectedPaths();
                rootFolder = _projectTree.RootPath;
                return true;
            }

            if (_contentTree != null && _contentTree.Contains(target))
            {
                selected = _contentTree.GetSelectedPaths();
                rootFolder = _contentTree.RootPath;
                return true;
            }

            // Search results span the whole project, so they have no folder to paste into.
            if (_assetList != null && _assetList.Contains(target) && !string.IsNullOrEmpty(_assetList.DropFolder))
            {
                selected = new List<string>();
                if (!string.IsNullOrEmpty(_assetList.SelectedPath)) selected.Add(_assetList.SelectedPath);
                rootFolder = _assetList.DropFolder;
                return true;
            }

            return false;
        }

        // Pasting lands in the selected folder, next to a selected file, or in the panel's
        // own folder when nothing is selected.
        static string PasteFolderFor(List<string> selected, string rootFolder)
        {
            if (selected.Count == 0) return rootFolder;

            string last = BetterAssetTreeView.MainPathOf(selected[selected.Count - 1]);
            return AssetDatabase.IsValidFolder(last) ? last : Path.GetDirectoryName(last)?.Replace('\\', '/');
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            if (_openPage != Page.None && evt.keyCode == KeyCode.Escape)
            {
                ClosePage();
                evt.StopPropagation();
                return;
            }

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
            ClosePage();

            // Leaving a prefab tab writes its pending changes.
            if (_selectedIndex >= 0 && _selectedIndex < _tabs.Count && _selectedIndex != index
                && _tabs[_selectedIndex].kind == BetterTabKind.Prefab)
                SavePrefabIfDirty(_tabs[_selectedIndex].path);

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

            // The closed prefab tabs are saved and released, as closing them one by one would.
            foreach (BetterTabEntry closed in _tabs)
            {
                if (closed != keep && closed.kind == BetterTabKind.Prefab && closed.path != keep.path)
                    SaveAndUnloadPrefab(closed.path);
            }

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
            if (IsContentTreeShowing(tab))
                _contentTree.SaveExpandedState(tab.expandedPaths);
            tab.searchQuery = _search.CommittedQuery;
        }

        void RestoreActiveTabState()
        {
            _search.Clear();
            _searchInputText = "";

            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;
            var tab = _tabs[_selectedIndex];

            // The folders this tab had open are applied by the content tree itself when it
            // switches to the tab's root (UpdateRightPaneMode): applying them here, before
            // the switch, expanded rows that did not exist yet.

            if (!string.IsNullOrEmpty(tab.searchQuery))
            {
                _searchInputText = tab.searchQuery;
                _search.ForceCommit(tab.searchQuery);
            }
        }

        // Saved on every expand or collapse, so a tab keeps its open folders through a
        // tab switch, a domain reload or the window being closed.
        void OnFoldoutToggled()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count) return;

            BetterTabEntry tab = _tabs[_selectedIndex];
            if (!IsContentTreeShowing(tab)) return;

            _contentTree.SaveExpandedState(tab.expandedPaths);
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
        }

        // The content tree keeps showing the last folder while an asset or prefab tab is
        // active, so its expansion only belongs to a tab whose root it currently shows.
        bool IsContentTreeShowing(BetterTabEntry tab)
        {
            return _contentTree != null && tab.kind == BetterTabKind.Folder && _contentTree.RootPath == tab.path;
        }

        void SaveProjectPanelExpansion()
        {
            if (_projectTree != null)
                EditorPrefs.SetString(ProjectExpandedKey, _projectTree.SaveExpandedState());
        }

        void ClearSearch()
        {
            _searchInputText = "";
            _toolbar?.ClearSearch();
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
