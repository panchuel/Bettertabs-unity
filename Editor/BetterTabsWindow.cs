using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

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
        Vector2 _tabScroll;
        Vector2 _assetScroll;
        bool _scrollToSelected;
        float _tabScrollTarget;
        double _tabScrollTime;

        // ── Folder drag-drop state ───────────────────────────────────────────
        bool _isDragHovering;

        // ── Tab reorder drag state ────────────────────────────────────────────
        int _dragTabIndex = -1;
        int _pressedTabIndex = -1;
        float _dragTabOffsetX;
        float _dragTabCurrentX;

        // ── Closed-tab history (Ctrl+Shift+T) ────────────────────────────────
        readonly Stack<BetterTabEntry> _closedTabStack = new Stack<BetterTabEntry>();

        // ── Tree & search ─────────────────────────────────────────────────────
        BetterTreeRenderer _tree;
        BetterSearchHandler _search;
        string _searchInputText = "";

        // ── Local asset selection ─────────────────────────────────────────────
        string _selectedAssetPath;

        // ── Embedded inspector ────────────────────────────────────────────────
        Editor _assetEditor;
        string _assetEditorPath;
        Vector2 _inspectorScroll;

        // ── Prefab / SceneObject hierarchy view ───────────────────────────────
        readonly Dictionary<string, GameObject> _loadedPrefabRoots = new Dictionary<string, GameObject>();
        readonly Dictionary<string, BetterHierarchyRenderer> _hierarchyRenderers = new Dictionary<string, BetterHierarchyRenderer>();
        readonly List<Editor> _stackedEditors = new List<Editor>();
        string _stackedEditorsKey;
        Vector2 _hierarchyScroll;
        Vector2 _hierarchyInspectorScroll;
        bool _hierarchySplitterDragging;
        float _previewHeight = 180f;
        bool _previewCollapsed;
        bool _previewPressed;
        bool _previewDragging;
        float _previewPressedMouseY;
        const float PreviewHeaderH = 22f;
        const float PreviewSettingsW = 110f;
        const float PreviewMinH = 60f;
        const float PreviewMaxH = 600f;
        const float DragThresholdPx = 3f;
        static string PreviewHeightKey => $"BetterTabs_PreviewHeight_{Application.productName}";
        static string PreviewCollapsedKey => $"BetterTabs_PreviewCollapsed_{Application.productName}";

        // ── Inline rename ─────────────────────────────────────────────────────
        string _renamingPath;
        string _renameBuffer;

        // ── Dirty flag (set by postprocessor / projectChanged) ────────────────
        static bool s_dirty;
        static BetterTabsWindow s_instance;

        // ── Constants ────────────────────────────────────────────────────────
        const int TabHeight = 22;
        const int AssetRowHeight = 20;
        const int GridIconSize = 64;
        const int GridCellSize = 80;

        // ── Project panel ─────────────────────────────────────────────────────
        bool _projectPanelOpen;
        float _splitterX = 220f;
        bool _splitterDragging;
        BetterProjectTreeRenderer _projectTree;
        Vector2 _projectScroll;
        bool _leftPanelHasFocus;
        Object _lastSyncedSelection;

        // ── Layout bounds (set each frame in OnGUI) ───────────────────────────
        float _rightX;
        float _rightW;

        static string ProjectPanelOpenKey => $"BetterTabs_ProjectPanelOpen_{Application.productName}";
        static string SplitterXKey => $"BetterTabs_SplitterX_{Application.productName}";
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
            _tree = new BetterTreeRenderer(OnFoldoutToggled);

            _projectTree = new BetterProjectTreeRenderer();
            _projectTree.Setup(
                OnProjectPanelItemClicked,
                AddOrSelectTab,
                OnAssetDragged,
                () => { _projectTree.InvalidateCache(); RequestRefresh(); },
                ScrollProjectPanelToPath);
            _projectPanelOpen = EditorPrefs.GetBool(ProjectPanelOpenKey, false);
            _splitterX = EditorPrefs.GetFloat(SplitterXKey, 220f);
            _projectTree.LoadExpandedState(EditorPrefs.GetString(ProjectExpandedKey, ""));
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
            if (_assetEditor != null) { DestroyImmediate(_assetEditor); _assetEditor = null; _assetEditorPath = null; }
            SaveAndUnloadAllPrefabs();
            DestroyStackedEditors();
            PersistActiveTabState();
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
            EditorPrefs.SetBool(ProjectPanelOpenKey, _projectPanelOpen);
            EditorPrefs.SetFloat(SplitterXKey, _splitterX);
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
            if (Selection.activeObject == _lastSyncedSelection) return;
            _lastSyncedSelection = Selection.activeObject;
            OnUnitySelectionChanged();
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

            _projectTree.SetSelection(path);
            _projectTree.ExpandToPath(path);
            ScrollProjectPanelToPath(path);
            Repaint();
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        void OnGUI()
        {
            if (s_dirty)
            {
                s_dirty = false;
                _tree.InvalidateCache();
                _projectTree?.InvalidateCache();
                Repaint();
            }

            if (Event.current.type == EventType.MouseLeaveWindow)
            {
                _dragTabIndex = -1;
                _pressedTabIndex = -1;
                if (_splitterDragging) _splitterDragging = false;
            }

            HandleKeyboardShortcuts();
            HandleDragHover();

            DrawTabBar();

            // Layout bounds for the right panel
            _rightX = _projectPanelOpen ? _splitterX + 4 : 0;
            _rightW = position.width - _rightX;

            // Track which panel has keyboard focus based on last click (don't consume)
            if (Event.current.type == EventType.MouseDown && Event.current.mousePosition.y > TabHeight)
                _leftPanelHasFocus = _projectPanelOpen && Event.current.mousePosition.x < _splitterX;

            // Left project panel
            if (_projectPanelOpen)
            {
                DrawProjectPanel(new Rect(0, TabHeight, _splitterX, position.height - TabHeight));
                DrawSplitter();
            }

            // Right panel content
            if (_tabs.Count == 0)
            {
                DrawEmptyState();
            }
            else
            {
                if (ActiveTabIsFolder()) DrawSearchToolbar();
                DrawContent();
            }

            // Fallback: only consume the drop if no folder row in the trees did.
            HandleDragPerformFallback();

            if (_isDragHovering)
                DrawDropOverlay();

            if (_searchInputText != _search.CommittedQuery)
                Repaint();
        }

        // ── Empty state ──────────────────────────────────────────────────────
        void DrawEmptyState()
        {
            var r = new Rect(_rightX, 0, _rightW, position.height);

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
            GUI.Label(r, "⊕ Drag a folder or asset here", style);
        }

        // ── Tab bar ──────────────────────────────────────────────────────────
        void DrawTabBar()
        {
            bool showPingBtn = _selectedIndex >= 0 && _selectedIndex < _tabs.Count
                && (_tabs[_selectedIndex].kind == BetterTabKind.SceneObject
                    || _tabs[_selectedIndex].kind == BetterTabKind.Prefab);
            float rightReserved = showPingBtn ? 78f : 54f;
            const float arrowW = 16f;
            float fullBarWidth = position.width - rightReserved;
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

            bool hasOverflow = total > fullBarWidth;
            float scrollOffsetX = hasOverflow ? arrowW : 0f;
            float barWidth = fullBarWidth - (hasOverflow ? arrowW * 2f : 0f);
            var barRect = new Rect(scrollOffsetX, 0, barWidth, TabHeight);

            // Auto-scroll to keep selected tab in view
            if (_scrollToSelected && _selectedIndex >= 0 && _selectedIndex < count)
            {
                _scrollToSelected = false;
                float tabStart = starts[_selectedIndex];
                float tabEnd   = tabStart + widths[_selectedIndex];
                float target   = _tabScrollTarget;
                if (tabStart < target)
                    target = tabStart;
                else if (tabEnd > target + barWidth)
                    target = tabEnd - barWidth;
                _tabScrollTarget = Mathf.Clamp(target, 0f, Mathf.Max(0f, total - barWidth));
            }

            // Clamp target in case the window was resized
            _tabScrollTarget = Mathf.Clamp(_tabScrollTarget, 0f, Mathf.Max(0f, total - barWidth));

            // Smooth scroll animation
            {
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - _tabScrollTime), 0f, 0.05f);
                _tabScrollTime = now;
                _tabScroll.x = Mathf.Lerp(_tabScroll.x, _tabScrollTarget, dt * 14f);
                if (Mathf.Abs(_tabScroll.x - _tabScrollTarget) < 0.5f)
                    _tabScroll.x = _tabScrollTarget;
                else
                    Repaint();
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

            for (int i = 0; i < count; i++)
            {
                if (i == _dragTabIndex) continue;

                var tabIcon = GetTabIcon(_tabs[i]);
                DrawTabAt(i, starts[i], widths[i], tabIcon, active: i == _selectedIndex, ghost: false);

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
                    GetTabIcon(_tabs[_dragTabIndex]), active: _dragTabIndex == _selectedIndex, ghost: true);
            }

            GUI.EndScrollView();

            // ── Overflow arrows ───────────────────────────────────────────────
            if (hasOverflow)
            {
                bool canLeft  = _tabScrollTarget > 0.5f;
                bool canRight = _tabScrollTarget < total - barWidth - 0.5f;

                using (new EditorGUI.DisabledScope(!canLeft))
                {
                    if (GUI.Button(new Rect(0, 0, arrowW, TabHeight), "‹", EditorStyles.toolbarButton))
                        _tabScrollTarget = Mathf.Max(0f, _tabScrollTarget - 80f);
                }
                using (new EditorGUI.DisabledScope(!canRight))
                {
                    if (GUI.Button(new Rect(scrollOffsetX + barWidth, 0, arrowW, TabHeight), "›", EditorStyles.toolbarButton))
                        _tabScrollTarget = Mathf.Min(total - barWidth, _tabScrollTarget + 80f);
                }
            }

            // ── + button — adds the active Project / Hierarchy selection ─────
            string selPath = null;
            GameObject selSceneGO = null;
            bool canAdd = false;
            if (Selection.activeObject != null)
            {
                selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(selPath))
                {
                    canAdd = !_tabs.Any(t => t.kind != BetterTabKind.SceneObject && t.path == selPath);
                }
                else if (Selection.activeObject is GameObject go && !AssetDatabase.Contains(go))
                {
                    selSceneGO = go;
                    var gid = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                    canAdd = !_tabs.Any(t => t.kind == BetterTabKind.SceneObject && t.globalObjectId == gid);
                }
            }

            var plusRect = new Rect(position.width - rightReserved, 1, 24, TabHeight - 2);
            using (new EditorGUI.DisabledScope(!canAdd))
            {
                if (GUI.Button(plusRect, new GUIContent("+"), EditorStyles.toolbarButton))
                {
                    if (selSceneGO != null) AddOrSelectSceneObjectTab(selSceneGO);
                    else AddOrSelectTab(selPath);
                }
            }

            // ── Ping button (only for Prefab / SceneObject tabs) ──────────────
            if (showPingBtn)
            {
                var pingIcon = EditorGUIUtility.IconContent("d_SearchJump Icon");
                var pingContent = pingIcon != null && pingIcon.image != null
                    ? new GUIContent(pingIcon.image, "Ping in Hierarchy / Project")
                    : new GUIContent("⊙", "Ping");
                var pingRect = new Rect(position.width - 54, 1, 22, TabHeight - 2);
                if (GUI.Button(pingRect, pingContent, EditorStyles.toolbarButton))
                    PingTab(_selectedIndex);
            }

            // ── Project panel toggle ──────────────────────────────────────────
            var panelIcon = EditorGUIUtility.FindTexture("d_Project");
            var panelContent = panelIcon != null
                ? new GUIContent(panelIcon, _projectPanelOpen ? "Hide Project Panel" : "Show Project Panel")
                : new GUIContent(_projectPanelOpen ? "◁" : "▷", _projectPanelOpen ? "Hide Project Panel" : "Show Project Panel");

            var panelBtnRect = new Rect(position.width - 28, 1, 26, TabHeight - 2);
            if (GUI.Button(panelBtnRect, panelContent, EditorStyles.toolbarButton))
            {
                _projectPanelOpen = !_projectPanelOpen;
                if (_projectPanelOpen)
                {
                    _projectTree.InvalidateCache();
                    SyncProjectTreeHighlight();
                }
                EditorPrefs.SetBool(ProjectPanelOpenKey, _projectPanelOpen);
            }

            if (toRemove >= 0)
                RemoveTab(toRemove);
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
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
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
            var toolbarRect = new Rect(_rightX, y, _rightW, TabHeight);
            GUI.Box(toolbarRect, GUIContent.none, EditorStyles.toolbar);

            float clearBtnW = _searchInputText.Length > 0 ? 20 : 0;
            float viewBtnW = 26;
            float fieldW = _rightW - clearBtnW - viewBtnW - 6;

            var searchRect = new Rect(_rightX + 2, y + 2, fieldW, TabHeight - 4);

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
                var clearRect = new Rect(_rightX + 2 + fieldW, y + 2, clearBtnW - 2, TabHeight - 4);
                if (GUI.Button(clearRect, "×", EditorStyles.toolbarButton))
                    ClearSearch();
            }

            var toggleRect = new Rect(_rightX + _rightW - viewBtnW - 2, y + 1, viewBtnW, TabHeight - 2);
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
            if (_selectedIndex < 0 || _selectedIndex >= _tabs.Count)
            {
                var emptyRect = new Rect(_rightX, TabHeight, _rightW, position.height - TabHeight);
                GUI.Label(emptyRect, "No tab selected.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var tab = _tabs[_selectedIndex];

            switch (tab.kind)
            {
                case BetterTabKind.Asset:
                {
                    var pinRect = new Rect(_rightX, TabHeight, _rightW, position.height - TabHeight);
                    DrawAssetPinView(pinRect, tab.path);
                    return;
                }
                case BetterTabKind.Prefab:
                case BetterTabKind.SceneObject:
                {
                    var pinRect = new Rect(_rightX, TabHeight, _rightW, position.height - TabHeight);
                    DrawPrefabOrGameObjectView(pinRect, tab);
                    return;
                }
            }

            // Folder tab → tree / grid / search
            string rootPath = tab.path;
            float topOffset = TabHeight * 2;
            var listRect = new Rect(_rightX, topOffset, _rightW, position.height - topOffset);

            if (_search.IsSearching)
            {
                DrawSearchResults(listRect);
                return;
            }

            if (_gridView)
                DrawGridView(listRect, rootPath);
            else
                DrawTreeView(listRect, rootPath);

            var ev = Event.current;
            if (ev.type == EventType.ContextClick && listRect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildFolderContextMenu(
                    rootPath, StartRename, OnAssetModified, RequestRefresh)
                    .ShowAsContext();
                ev.Use();
            }

            // Fallback drop zone: any unhandled drop in the right panel goes
            // into the tab's root folder. Specific folder rows above already
            // consumed their own drops via ev.Use().
            _tree.HandleDropOnFolder(listRect, rootPath, ev);

            // Visual: highlight the folder content area while dragging.
            if (_isDragHovering)
            {
                EditorGUI.DrawRect(listRect, new Color(0.2f, 0.5f, 1f, 0.06f));
                DrawBorder(listRect, new Color(0.3f, 0.6f, 1f, 0.6f), 1.5f);
            }
        }

        // ── Asset pin view (embedded inspector) ──────────────────────────────
        void DrawAssetPinView(Rect r, string path)
        {
            if (_assetEditorPath != path)
            {
                if (_assetEditor != null) { DestroyImmediate(_assetEditor); _assetEditor = null; }
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (obj != null)
                {
                    _assetEditor = Editor.CreateEditor(obj);
                    if (_assetEditor != null)
                    {
                        // Modern Unity: editors gate property rendering via the
                        // "expanded" foldout state + firstInspectedEditor flag.
                        // Both properties are internal, so set via reflection.
                        const System.Reflection.BindingFlags bf =
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public;

                        typeof(Editor).GetProperty("firstInspectedEditor", bf)
                            ?.SetValue(_assetEditor, true);
                        typeof(Editor).GetProperty("alwaysAllowExpansion", bf)
                            ?.SetValue(_assetEditor, true);
                        UnityEditorInternal.InternalEditorUtility
                            .SetIsInspectorExpanded(obj, true);
                    }
                }
                _assetEditorPath = path;
                _inspectorScroll = Vector2.zero;
            }

            // Custom toolbar header
            GUI.Box(new Rect(r.x, r.y, r.width, TabHeight), GUIContent.none, EditorStyles.toolbar);
            Texture2D icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            if (icon != null)
                GUI.DrawTexture(new Rect(r.x + 4, r.y + 3, 16, 16), icon, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 52, TabHeight),
                Path.GetFileNameWithoutExtension(path),
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });
            if (GUI.Button(new Rect(r.xMax - 50, r.y + 1, 48, TabHeight - 2), "Open", EditorStyles.toolbarButton))
                BetterTabsInteractionHandler.OpenAsset(path);

            if (_assetEditor == null)
            {
                GUI.Label(new Rect(r.x, r.y + TabHeight, r.width, r.height - TabHeight),
                    "Cannot inspect this asset.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Reserve preview panel at the bottom
            bool hasPreview = _assetEditor.HasPreviewGUI();
            float previewBlockH = 0f;
            if (hasPreview)
            {
                previewBlockH = PreviewHeaderH
                    + (_previewCollapsed ? 0f : _previewHeight);
            }

            var inspectorArea = new Rect(r.x, r.y + TabHeight, r.width, r.height - TabHeight - previewBlockH);

            // Inspector content
            GUILayout.BeginArea(inspectorArea);
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
            _assetEditor.DrawHeader();
            _assetEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            if (hasPreview)
                DrawPreviewPanel(new Rect(r.x, r.yMax - previewBlockH, r.width, previewBlockH));

            if (!EditorApplication.isPlaying && _assetEditor.RequiresConstantRepaint()) Repaint();
        }

        // ── Hierarchy + Inspector view (Prefab / SceneObject tabs) ───────────
        void DrawPrefabOrGameObjectView(Rect r, BetterTabEntry tab)
        {
            // Resolve root GameObject for the tab.
            GameObject root = null;
            string statusMessage = null;

            if (tab.kind == BetterTabKind.Prefab)
            {
                root = GetOrLoadPrefabRoot(tab.path);
                if (root == null) statusMessage = "Could not load prefab.";
            }
            else // SceneObject
            {
                if (GlobalObjectId.TryParse(tab.globalObjectId, out var gid))
                {
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                    root = obj as GameObject;
                    if (root == null) statusMessage = "Scene not loaded or GameObject missing.\nOpen the scene to inspect this object.";
                }
                else statusMessage = "Invalid scene reference.";
            }

            if (root == null)
            {
                GUI.Label(new Rect(r.x, r.y + 20, r.width, 60),
                    statusMessage ?? "Unavailable", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // ── Split: hierarchy (left) + inspector (right) ──────────────────
            float splitterX = Mathf.Clamp(tab.hierarchySplitterX, 120f, r.width - 200f);
            tab.hierarchySplitterX = splitterX;

            var hierarchyRect = new Rect(r.x, r.y, splitterX, r.height);
            var splitterRect  = new Rect(r.x + splitterX, r.y, 4f, r.height);
            var inspectorRect = new Rect(r.x + splitterX + 4f, r.y,
                r.width - splitterX - 4f, r.height);

            DrawHierarchyPanel(hierarchyRect, tab, root);
            DrawHierarchySplitter(splitterRect, tab);

            // Resolve selected GameObject
            var selectedTransform = string.IsNullOrEmpty(tab.hierarchySelectionPath)
                ? root.transform
                : BetterHierarchyRenderer.FindByPath(root.transform, tab.hierarchySelectionPath);
            var selectedGO = selectedTransform != null ? selectedTransform.gameObject : root;

            DrawGameObjectInspectorPanel(inspectorRect, tab, selectedGO);
        }

        void DrawHierarchyPanel(Rect r, BetterTabEntry tab, GameObject root)
        {
            // Background
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
                ? new Color(0.19f, 0.19f, 0.19f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f));

            // Get or create renderer for this tab
            string key = TabKey(tab);
            if (!_hierarchyRenderers.TryGetValue(key, out var renderer))
            {
                renderer = new BetterHierarchyRenderer();
                _hierarchyRenderers[key] = renderer;
                // Expand the selection chain on first render
                if (!string.IsNullOrEmpty(tab.hierarchySelectionPath))
                    renderer.ExpandPathChain(tab.hierarchySelectionPath);
                else
                    renderer.ExpandPathChain(root.transform.name);
            }
            renderer.Setup(root.transform, tab.hierarchySelectionPath,
                path => { tab.hierarchySelectionPath = path; Repaint(); });

            float contentH = renderer.MeasureHeight();
            var contentRect = new Rect(0, 0, r.width - 16, Mathf.Max(contentH, r.height));

            _hierarchyScroll = GUI.BeginScrollView(r, _hierarchyScroll, contentRect);
            GUILayout.BeginArea(new Rect(0, 0, contentRect.width, Mathf.Max(contentH, 1f)));
            renderer.Draw(contentRect.width);
            GUILayout.EndArea();
            GUI.EndScrollView();
        }

        void DrawHierarchySplitter(Rect splitterRect, BetterTabEntry tab)
        {
            EditorGUI.DrawRect(splitterRect, new Color(0.1f, 0.1f, 0.1f, 1f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && splitterRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                _hierarchySplitterDragging = true;
                ev.Use();
            }
            if (_hierarchySplitterDragging)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    tab.hierarchySplitterX = Mathf.Clamp(ev.mousePosition.x - _rightX, 120f, _rightW - 200f);
                    ev.Use();
                    Repaint();
                }
                else if (ev.type == EventType.MouseUp)
                {
                    _hierarchySplitterDragging = false;
                    BetterTabsPrefs.Save(_tabs, _selectedIndex);
                    ev.Use();
                }
            }
        }

        void DrawGameObjectInspectorPanel(Rect r, BetterTabEntry tab, GameObject target)
        {
            string key = TabKey(tab) + "|" + (tab.hierarchySelectionPath ?? "");
            RefreshStackedEditors(key, target);

            GUILayout.BeginArea(r);
            _hierarchyInspectorScroll = EditorGUILayout.BeginScrollView(_hierarchyInspectorScroll);

            EditorGUI.BeginChangeCheck();
            foreach (var e in _stackedEditors)
            {
                if (e == null) continue;
                try
                {
                    e.DrawHeader();
                    e.OnInspectorGUI();
                    EditorGUILayout.Space(2);
                }
                catch (System.Exception ex)
                {
                    GUILayout.Label($"Inspector error: {ex.Message}", EditorStyles.helpBox);
                }
            }
            if (EditorGUI.EndChangeCheck() && tab.kind == BetterTabKind.Prefab)
            {
                string prefabPath = tab.path;
                EditorApplication.delayCall += () =>
                {
                    if (_loadedPrefabRoots.TryGetValue(prefabPath, out var rt) && rt != null)
                        PrefabUtility.SaveAsPrefabAsset(rt, prefabPath);
                };
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static string TabKey(BetterTabEntry tab)
        {
            return tab.kind == BetterTabKind.SceneObject
                ? "scene|" + tab.globalObjectId
                : tab.kind + "|" + tab.path;
        }

        void DrawPreviewPanel(Rect r)
        {
            var ev = Event.current;

            // ── Header bar (acts as click-toggle AND drag-to-resize handle) ──
            var headerRect = new Rect(r.x, r.y, r.width, PreviewHeaderH);
            GUI.Box(headerRect, GUIContent.none, EditorStyles.toolbar);

            // Drag area = header minus the right-side preview settings buttons
            var dragRect = new Rect(headerRect.x, headerRect.y,
                headerRect.width - PreviewSettingsW, headerRect.height);
            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.SplitResizeUpDown);

            if (ev.type == EventType.MouseDown && dragRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                _previewPressed = true;
                _previewDragging = false;
                _previewPressedMouseY = ev.mousePosition.y;
                ev.Use();
            }
            else if (_previewPressed && ev.type == EventType.MouseDrag)
            {
                if (!_previewDragging
                    && Mathf.Abs(ev.mousePosition.y - _previewPressedMouseY) > DragThresholdPx)
                {
                    _previewDragging = true;
                    if (_previewCollapsed)
                    {
                        _previewCollapsed = false;
                        EditorPrefs.SetBool(PreviewCollapsedKey, false);
                    }
                }
                if (_previewDragging)
                {
                    _previewHeight = Mathf.Clamp(
                        position.height - ev.mousePosition.y - PreviewHeaderH,
                        PreviewMinH, PreviewMaxH);
                    Repaint();
                    ev.Use();
                }
            }
            else if (_previewPressed && ev.type == EventType.MouseUp)
            {
                if (_previewDragging)
                {
                    EditorPrefs.SetFloat(PreviewHeightKey, _previewHeight);
                }
                else
                {
                    // Simple click → toggle collapse
                    _previewCollapsed = !_previewCollapsed;
                    EditorPrefs.SetBool(PreviewCollapsedKey, _previewCollapsed);
                }
                _previewPressed = false;
                _previewDragging = false;
                ev.Use();
                Repaint();
            }

            // Chevron + label
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            GUI.Label(new Rect(headerRect.x + 6, headerRect.y, dragRect.width - 10, headerRect.height),
                (_previewCollapsed ? "▶  " : "▼  ") + Path.GetFileNameWithoutExtension(_assetEditorPath),
                labelStyle);

            // Preview settings (right side)
            if (!_previewCollapsed)
            {
                var settingsRect = new Rect(headerRect.xMax - PreviewSettingsW + 4, headerRect.y + 2,
                    PreviewSettingsW - 8, headerRect.height - 4);
                GUILayout.BeginArea(settingsRect);
                GUILayout.BeginHorizontal();
                _assetEditor.OnPreviewSettings();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();

                var contentRect = new Rect(r.x, r.y + PreviewHeaderH,
                    r.width, r.height - PreviewHeaderH);
                _assetEditor.DrawPreview(contentRect);
            }
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
                BetterTabsWindow.RequestRefresh);

            float contentHeight = _tree.MeasureHeight(rootPath);
            var contentRect = new Rect(0, 0, listRect.width - 16, Mathf.Max(contentHeight, listRect.height));

            _assetScroll = GUI.BeginScrollView(listRect, _assetScroll, contentRect);

            GUILayout.BeginArea(new Rect(0, 0, contentRect.width, contentHeight));
            _tree.Draw(rootPath, contentRect.width, OnAssetDraggedSingle);
            GUILayout.EndArea();

            GUI.EndScrollView();

            // Keyboard navigation may have moved the selection off-screen → scroll to it
            string navPath = _tree.ConsumePendingScrollPath();
            if (!string.IsNullOrEmpty(navPath))
            {
                float y = _tree.GetYPositionOf(navPath);
                if (y >= 0f)
                {
                    if (y < _assetScroll.y)
                        _assetScroll.y = y;
                    else if (y + AssetRowHeight > _assetScroll.y + listRect.height)
                        _assetScroll.y = y + AssetRowHeight - listRect.height;
                    Repaint();
                }
            }

            // Keep repainting while a rename is grabbing focus so it paints correctly
            if (_tree.HasPendingRenameFocus) Repaint();
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

            if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                if (ev.clickCount == 2) { BetterTabsInteractionHandler.OpenAsset(path); ev.Use(); }
                else { OnAssetSelected(path); ev.Use(); }
            }
            if (ev.type == EventType.ContextClick && rowRect.Contains(ev.mousePosition))
            {
                BetterTabsInteractionHandler.BuildContextMenu(path, StartRename, OnAssetModified, RequestRefresh)
                    .ShowAsContext();
                ev.Use();
            }
            if (ev.type == EventType.MouseDrag && rowRect.Contains(ev.mousePosition))
            { OnAssetDraggedSingle(path); ev.Use(); }

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
            var folders = new List<string>();
            var files   = new List<string>();
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parent != rootPath) continue;
                if (AssetDatabase.IsValidFolder(assetPath)) folders.Add(assetPath);
                else                                        files.Add(assetPath);
            }
            var assets = new List<string>(folders.Count + files.Count);
            assets.AddRange(folders);
            assets.AddRange(files);

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
            bool isFolder = AssetDatabase.IsValidFolder(path);

            Texture2D icon;
            if (isFolder)
                icon = EditorGUIUtility.FindTexture("Folder Icon");
            else
            {
                icon = AssetPreview.GetAssetPreview(AssetDatabase.LoadAssetAtPath<Object>(path));
                if (icon == null) icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
            }

            var ev = Event.current;

            bool isSelected = path == _selectedAssetPath;
            if (isSelected)
                EditorGUI.DrawRect(cellRect, new Color(0.17f, 0.36f, 0.53f, 0.6f));

            if (ev.type == EventType.MouseDown && cellRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                if (ev.clickCount == 2)
                {
                    if (isFolder) AddOrSelectTab(path);
                    else BetterTabsInteractionHandler.OpenAsset(path);
                    ev.Use();
                }
                else { OnAssetSelected(path); ev.Use(); }
            }
            if (ev.type == EventType.ContextClick && cellRect.Contains(ev.mousePosition))
            {
                var menu = isFolder
                    ? BetterTabsInteractionHandler.BuildFolderContextMenu(path, StartRename, OnAssetModified, RequestRefresh)
                    : BetterTabsInteractionHandler.BuildContextMenu(path, StartRename, OnAssetModified, RequestRefresh);
                menu.ShowAsContext();
                ev.Use();
            }
            if (ev.type == EventType.MouseDrag && cellRect.Contains(ev.mousePosition))
            { OnAssetDraggedSingle(path); ev.Use(); }

            var iconRect = new Rect(cellRect.x + (GridCellSize - GridIconSize) / 2, cellRect.y + 4, GridIconSize, GridIconSize);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            var labelRect = new Rect(cellRect.x + 2, cellRect.y + GridCellSize - 18, GridCellSize - 4, 16);
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = isSelected ? Color.white : EditorStyles.miniLabel.normal.textColor }
            };
            GUI.Label(labelRect, TruncateWithEllipsis(Path.GetFileName(path), labelStyle, labelRect.width), labelStyle);
        }

        static string TruncateWithEllipsis(string text, GUIStyle style, float maxWidth)
        {
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text[..mid] + "…")).x <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return lo == 0 ? "…" : text[..lo] + "…";
        }

        // ── Project panel ─────────────────────────────────────────────────────
        void DrawProjectPanel(Rect r)
        {
            // Background
            EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
                ? new Color(0.19f, 0.19f, 0.19f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f));

            // Scroll content
            var contentArea = new Rect(r.x, r.y, r.width, r.height);
            float contentH = _projectTree.MeasureHeight();
            float panelW = contentArea.width;
            var contentRect = new Rect(0, 0, panelW - 16, Mathf.Max(contentH, contentArea.height));

            _projectScroll = GUI.BeginScrollView(contentArea, _projectScroll, contentRect);
            GUILayout.BeginArea(new Rect(0, 0, contentRect.width, Mathf.Max(contentH, 1f)));
            _projectTree.Draw(contentRect.width, _leftPanelHasFocus);
            GUILayout.EndArea();
            GUI.EndScrollView();

            // Keep repainting while a rename is grabbing focus so it paints correctly
            if (_projectTree.HasPendingRenameFocus) Repaint();
        }

        void DrawSplitter()
        {
            var splitterRect = new Rect(_splitterX, TabHeight, 4, position.height - TabHeight);
            EditorGUI.DrawRect(splitterRect, new Color(0.1f, 0.1f, 0.1f, 1f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            var ev = Event.current;
            if (ev.type == EventType.MouseDown && splitterRect.Contains(ev.mousePosition) && ev.button == 0)
            {
                _splitterDragging = true;
                ev.Use();
            }

            if (_splitterDragging)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    _splitterX = Mathf.Clamp(ev.mousePosition.x, 100f, position.width - 200f);
                    _rightX = _splitterX + 4;
                    _rightW = position.width - _rightX;
                    ev.Use();
                    Repaint();
                }
                if (ev.type == EventType.MouseUp)
                {
                    _splitterDragging = false;
                    EditorPrefs.SetFloat(SplitterXKey, _splitterX);
                    ev.Use();
                }
            }
        }

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
            float y = _projectTree.GetYPositionOf(path);
            if (y < 0f) return;
            float panelH = position.height - TabHeight * 2f;
            if (y < _projectScroll.y || y + 20f > _projectScroll.y + panelH)
            {
                _projectScroll.y = Mathf.Max(0f, y - panelH * 0.35f);
                Repaint();
            }
        }

        // ── Folder drag-drop handling ─────────────────────────────────────────
        // Tracks whether a drag operation is currently in progress over the
        // window — used to show drop-zone highlights on ALL valid targets
        // (tab bar + active folder content area). Does NOT consume the event;
        // tree renderers handle row-specific drops themselves.
        void HandleDragHover()
        {
            var ev = Event.current;
            var windowRect = new Rect(0, 0, position.width, position.height);

            if (ev.type == EventType.DragUpdated)
            {
                if (windowRect.Contains(ev.mousePosition) && IsDraggingAsset())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (!_isDragHovering) { _isDragHovering = true; Repaint(); }
                }
                else if (_isDragHovering)
                {
                    _isDragHovering = false;
                    Repaint();
                }
            }
            else if (ev.type == EventType.DragExited)
            {
                _isDragHovering = false;
                Repaint();
            }
        }

        // Runs AFTER tree renderers have drawn — if the drop hit the tab bar
        // area (and wasn't consumed elsewhere), create a tab from the payload.
        void HandleDragPerformFallback()
        {
            var ev = Event.current;
            if (ev.type != EventType.DragPerform) return;
            var tabBarRect = new Rect(0, 0, position.width, TabHeight);
            if (!tabBarRect.Contains(ev.mousePosition)) return;
            if (!IsDraggingAsset()) return;

            DragAndDrop.AcceptDrag();
            foreach (var path in DragAndDrop.paths)
                if (!string.IsNullOrEmpty(path))
                    AddOrSelectTab(path);
            if (DragAndDrop.objectReferences != null)
            {
                foreach (var o in DragAndDrop.objectReferences)
                {
                    if (o is GameObject go && !AssetDatabase.Contains(go))
                        AddOrSelectSceneObjectTab(go);
                }
            }
            _isDragHovering = false;
            ev.Use();
            Repaint();
        }

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

        void DrawDropOverlay()
        {
            var r = new Rect(0, 0, position.width, TabHeight);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.5f, 1f, 0.15f));
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
            _tree.InvalidateCache();
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
            DestroyStackedEditors(); // force editor refresh on next draw
        }

        // ── Stacked component editors ─────────────────────────────────────────

        void RefreshStackedEditors(string key, GameObject target)
        {
            if (_stackedEditorsKey == key && _stackedEditors.Count > 0 && _stackedEditors[0] != null)
                return;

            DestroyStackedEditors();
            _stackedEditorsKey = key;
            if (target == null) return;

            const System.Reflection.BindingFlags bf =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;
            var firstProp = typeof(Editor).GetProperty("firstInspectedEditor", bf);
            var expandProp = typeof(Editor).GetProperty("alwaysAllowExpansion", bf);

            void MakeVisible(Editor e, bool first)
            {
                if (e == null) return;
                firstProp?.SetValue(e, first);
                expandProp?.SetValue(e, true);
                UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(e.target, true);
            }

            // 1) GameObject header editor
            var goEditor = Editor.CreateEditor(target);
            MakeVisible(goEditor, true);
            _stackedEditors.Add(goEditor);

            // 2) One editor per component
            var comps = target.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue; // missing script
                var ce = Editor.CreateEditor(comps[i]);
                MakeVisible(ce, false);
                _stackedEditors.Add(ce);
            }
        }

        void DestroyStackedEditors()
        {
            foreach (var e in _stackedEditors)
                if (e != null) DestroyImmediate(e);
            _stackedEditors.Clear();
            _stackedEditorsKey = null;
        }

        // ── Inline rename ─────────────────────────────────────────────────────

        void StartRename(string path)
        {
            _renamingPath = path;
            _renameBuffer = Path.GetFileNameWithoutExtension(path);
            _selectedAssetPath = path;
            _tree?.RequestRenameFocus();
            Repaint();
        }

        void CommitRename(string newName)
        {
            if (_renamingPath == null) return;
            string path = _renamingPath;
            _renamingPath = null;
            _renameBuffer = null;
            _tree.ResetRenameState();

            string trimmed = newName.Trim();
            string oldName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(trimmed) && trimmed != oldName)
            {
                string err = AssetDatabase.RenameAsset(path, trimmed);
                if (!string.IsNullOrEmpty(err))
                    Debug.LogError($"BetterTabs: Rename failed — {err}");
                AssetDatabase.Refresh();
                _tree.InvalidateCache();
            }
            Repaint();
        }

        void CancelRename()
        {
            _renamingPath = null;
            _renameBuffer = null;
            _tree.ResetRenameState();
            Repaint();
        }

        // ── Keyboard shortcuts & scroll navigation ────────────────────────────
        void HandleKeyboardShortcuts()
        {
            var ev = Event.current;

            // Shift+Scroll → cycle tabs   |   Ctrl+Shift+Scroll → move tab
            if (ev.type == EventType.ScrollWheel && ev.shift && _tabs.Count > 1)
            {
                // On Windows, Shift+Scroll arrives as horizontal scroll (delta.y=0, delta.x!=0).
                float rawScroll = Mathf.Abs(ev.delta.y) > 0.01f ? ev.delta.y : ev.delta.x;
                int dir = rawScroll > 0 ? -1 : 1;
                if (BetterTabsSettings.InvertScroll) dir = -dir;
                if (ev.control || ev.command)
                {
                    MoveTab(_selectedIndex, dir);
                }
                else
                {
                    int next = (_selectedIndex + dir + _tabs.Count) % _tabs.Count;
                    if (next != _selectedIndex) SelectTab(next);
                }
                ev.Use();
                return;
            }

            if (ev.type != EventType.KeyDown) return;
            bool ctrl = ev.control || ev.command;

            // Shift+Left/Right → cycle between anchored tabs
            if (ev.shift && !ctrl && _tabs.Count > 1
                && (ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.RightArrow))
            {
                int dir = ev.keyCode == KeyCode.RightArrow ? 1 : -1;
                int next = (_selectedIndex + dir + _tabs.Count) % _tabs.Count;
                if (next != _selectedIndex) SelectTab(next);
                ev.Use();
                return;
            }

            // Ctrl+W → close active tab
            if (ctrl && !ev.shift && ev.keyCode == KeyCode.W && _selectedIndex >= 0)
            {
                RemoveTab(_selectedIndex);
                ev.Use();
                return;
            }

            // Ctrl+Shift+T → reopen last closed tab
            if (ctrl && ev.shift && ev.keyCode == KeyCode.T)
            {
                ReopenClosedTab();
                ev.Use();
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
                var entry = _closedTabStack.Pop();
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

                // Re-insert the original entry (preserves expanded paths, search, selection).
                _tabs.Add(entry);
                SelectTab(_tabs.Count - 1);
                BetterTabsPrefs.Save(_tabs, _selectedIndex);
                return;
            }
        }

        void MoveTab(int index, int direction)
        {
            if (index < 0 || index >= _tabs.Count || _tabs.Count <= 1) return;
            // Wrap-around so both scroll directions always produce movement.
            int target = (index + direction + _tabs.Count) % _tabs.Count;
            SwapTabs(index, target);
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
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
            _scrollToSelected = true;
            _selectedAssetPath = null;
            _renamingPath = null;
            _renameBuffer = null;
            _assetScroll = Vector2.zero;
            _tree.InvalidateCache();

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
            _closedTabStack.Push(removed);
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
            BetterTabsPrefs.Save(_tabs, _selectedIndex);
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

        static Texture2D GetTabIcon(BetterTabEntry tab)
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
