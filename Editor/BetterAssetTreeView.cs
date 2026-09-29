using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BetterTabs
{
    // Asset tree backed by the native TreeView, used by both panels. Multi-selection,
    // arrow-key navigation, virtualisation and expand state come from the control
    // itself; this class supplies the data and the asset-specific interactions
    // (rename, drag out to the editor, context menu, drop into folders).
    internal class BetterAssetTreeView : VisualElement
    {
        const float RowHeight = 24f;
        const float DragThreshold = 4f;
        const int LeftButtonMask = 1;

        // A row's key is its asset path, or for a sub-asset (a sprite inside a texture, a
        // mesh inside a model) "path|localFileId". '|' can never appear in an asset path.
        const char SubAssetSeparator = '|';

        // Stand-in child that gives a file its expand arrow before its sub-assets are
        // loaded. They are only read when the file is expanded: loading every texture and
        // model of the project up front would make every rebuild slow.
        const string PlaceholderSuffix = "|*";

        readonly bool _showTypeColumn;
        readonly bool _showAddButton;
        string _rootPath = "Assets";

        readonly TreeView _tree;
        readonly Dictionary<int, string> _idToPath = new Dictionary<int, string>();
        readonly Dictionary<string, int> _pathToId = new Dictionary<string, int>();
        // Ids of the items in the tree as last built. _pathToId keeps every path ever seen
        // (so ids stay stable), but only these can be expanded or selected right now.
        readonly HashSet<int> _builtIds = new HashSet<int>();
        // Name and icon of each sub-asset row, read from the Project window's own model
        // without loading the object.
        readonly Dictionary<string, SubAssetRow> _subAssetRows = new Dictionary<string, SubAssetRow>();

        // Loaded only for the type column, and only for rows actually shown.
        readonly Dictionary<string, Object> _subAssetObjects = new Dictionary<string, Object>();
        int _nextId;
        bool _isBuilt;
        bool _isRestoringExpansion;

        string _renamingPath;

        // Drag-out state
        Vector2 _pressPos;
        string _pressPath;
        bool _pressed;
        bool _dragStarted;

        // Supplies the palette index tagged onto a path, so a coloured tab shows the
        // same tint on the rows that point at it. Null means nothing is tagged.
        public Func<string, int> TabColorProvider;

        // Alternative to tinting the icon: supplies the colour a row's background is
        // washed with, already carrying its alpha. Transparent means no wash. A tree
        // sets one provider or the other, never both.
        public Func<string, Color> RowBackgroundProvider;

        public event Action<string> ItemSelected;
        public event Action<string> ItemActivated;
        public event Action<List<string>> ItemsDragged;
        public event Action RefreshRequested;

        // A folder was expanded or collapsed by the user, so the owner can persist it.
        public event Action ExpandedChanged;

        public string RootPath => _rootPath;

        public BetterAssetTreeView(bool multiSelect, bool showTypeColumn, bool showAddButton)
        {
            style.flexGrow = 1;
            AddToClassList("bt-tree");
            _showTypeColumn = showTypeColumn;
            _showAddButton = showAddButton;

            _tree = new TreeView
            {
                fixedItemHeight = RowHeight,
                selectionType = multiSelect ? SelectionType.Multiple : SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _tree.style.flexGrow = 1;
            _tree.makeItem = MakeRow;
            _tree.bindItem = BindRow;
            _tree.unbindItem = UnbindRow;
            _tree.selectionChanged += OnSelectionChanged;
            _tree.itemsChosen += OnItemsChosen;
            _tree.itemExpandedChanged += OnItemExpandedChanged;
            Add(_tree);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            // Empty space below the rows falls back to the root folder (rows above
            // stop propagation, so these only fire on the gap).
            RegisterCallback<DragUpdatedEvent>(OnEmptyDragUpdated);
            RegisterCallback<DragPerformEvent>(OnEmptyDragPerform);
            RegisterCallback<ContextClickEvent>(OnEmptyContextClick);
        }

        // ── Ids ───────────────────────────────────────────────────────────────
        // Stable per path so expand state survives a rebuild.

        int IdFor(string path)
        {
            if (_pathToId.TryGetValue(path, out int existing)) return existing;
            int id = ++_nextId;
            _pathToId[path] = id;
            _idToPath[id] = path;
            return id;
        }

        string PathFor(int id) => _idToPath.TryGetValue(id, out string p) ? p : null;

        public static bool IsSubAssetKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.IndexOf(SubAssetSeparator) >= 0;
        }

        public static string MainPathOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int separator = key.IndexOf(SubAssetSeparator);
            return separator < 0 ? key : key.Substring(0, separator);
        }

        // The object a row stands for: the main asset for a path, the sub-asset for a
        // "path|localFileId" key.
        public static Object LoadObject(string key)
        {
            if (!IsSubAssetKey(key)) return AssetDatabase.LoadAssetAtPath<Object>(key);

            int separator = key.IndexOf(SubAssetSeparator);
            if (!long.TryParse(key.Substring(separator + 1), out long wantedId)) return null;

            foreach (Object representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(key.Substring(0, separator)))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(representation, out string guid, out long localId)
                    && localId == wantedId)
                    return representation;
            }
            return null;
        }

        // The key a tree would use for an object selected anywhere in the editor.
        public static string KeyFor(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsMainAsset(obj)) return path;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId)) return path;
            return path + SubAssetSeparator + localId;
        }

        // ── Data ──────────────────────────────────────────────────────────────

        // Switches the tree to another folder (the active tab root on the right panel) and
        // restores the folders that tab had open. The expansion is applied here, once the
        // new items exist: applying it before the switch expanded nothing.
        public void SetRoot(string rootPath, List<string> expandedPaths)
        {
            if (string.IsNullOrEmpty(rootPath)) return;
            if (_isBuilt && rootPath == _rootPath) return;

            _rootPath = rootPath;
            BuildTree(expandedPaths);
        }

        // Rebuilds from disk (an asset was added, moved or deleted) keeping every folder
        // that was open still open.
        public void Rebuild()
        {
            List<string> expanded = new List<string>();
            if (_isBuilt) SaveExpandedState(expanded);
            BuildTree(expanded);
        }

        void BuildTree(List<string> expandedPaths)
        {
            _builtIds.Clear();
            _subAssetRows.Clear();
            _subAssetObjects.Clear();
            List<TreeViewItemData<string>> roots = new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(TrackedId(_rootPath), _rootPath, BuildChildren(_rootPath))
            };
            _tree.SetRootItems(roots);
            _tree.Rebuild();
            _isBuilt = true;

            _isRestoringExpansion = true;
            _tree.ExpandItem(IdFor(_rootPath));
            LoadExpandedState(expandedPaths);
            _isRestoringExpansion = false;
        }

        int TrackedId(string path)
        {
            int id = IdFor(path);
            _builtIds.Add(id);
            return id;
        }

        List<TreeViewItemData<string>> BuildChildren(string folder)
        {
            List<TreeViewItemData<string>> children = new List<TreeViewItemData<string>>();

            string[] subFolders = AssetDatabase.GetSubFolders(folder);
            Array.Sort(subFolders, StringComparer.OrdinalIgnoreCase);
            foreach (string sub in subFolders)
                children.Add(new TreeViewItemData<string>(TrackedId(sub), sub, BuildChildren(sub)));

            HashSet<string> filesWithSubAssets = FilesWithSubAssets(folder);

            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(folder))
            {
                if (file.EndsWith(".meta")) continue;
                files.Add(file.Replace('\\', '/'));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                List<TreeViewItemData<string>> subAssets = null;
                if (filesWithSubAssets.Contains(file))
                {
                    string placeholder = file + PlaceholderSuffix;
                    subAssets = new List<TreeViewItemData<string>>
                    {
                        new TreeViewItemData<string>(TrackedId(placeholder), placeholder)
                    };
                }
                children.Add(new TreeViewItemData<string>(TrackedId(file), file, subAssets));
            }

            return children;
        }

        // HierarchyIterator is the model behind Unity's Project window. For each item right
        // inside a folder it knows, without loading anything, whether the item has
        // children: for a file that means visible sub-assets, of any type (sprites, meshes,
        // clips, Input Actions maps, a ScriptableObject's added objects…). So a file gets
        // an expand arrow here exactly when it gets one in the Project window.
        static HashSet<string> FilesWithSubAssets(string folder)
        {
            HashSet<string> files = new HashSet<string>();
            HierarchyIterator property = new HierarchyIterator(folder);

            // No expanded ids: only the folder's own items are visited, never their children.
            while (property.Next(null))
            {
                if (property.isFolder || !property.hasChildren) continue;
                files.Add(AssetDatabase.GUIDToAssetPath(property.guid));
            }
            return files;
        }

        // Swaps a file's placeholder for its real sub-assets, the first time it is expanded.
        void LoadSubAssetsIfNeeded(int fileId)
        {
            string file = PathFor(fileId);
            if (string.IsNullOrEmpty(file) || IsSubAssetKey(file)) return;
            if (!_pathToId.TryGetValue(file + PlaceholderSuffix, out int placeholderId)) return;
            if (!_builtIds.Contains(placeholderId)) return;

            _tree.TryRemoveItem(placeholderId, false);
            _builtIds.Remove(placeholderId);

            foreach (KeyValuePair<string, SubAssetRow> subAsset in ReadSubAssets(file))
            {
                _subAssetRows[subAsset.Key] = subAsset.Value;
                _tree.AddItem(new TreeViewItemData<string>(TrackedId(subAsset.Key), subAsset.Key), fileId, -1, false);
            }

            _tree.RefreshItems();
        }

        // Walks the file's folder in the Project window's model with only this file
        // expanded: the rows one level below it are its sub-assets, in the order and with
        // the names and icons the Project window shows. Nothing is loaded.
        static List<KeyValuePair<string, SubAssetRow>> ReadSubAssets(string file)
        {
            List<KeyValuePair<string, SubAssetRow>> subAssets = new List<KeyValuePair<string, SubAssetRow>>();
            string folder = Path.GetDirectoryName(file)?.Replace('\\', '/');
            string fileGuid = AssetDatabase.AssetPathToGUID(file);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileGuid)) return subAssets;

            HierarchyIterator property = new HierarchyIterator(folder);
            EntityId[] expanded = null;
            int fileDepth = -1;

            while (property.Next(expanded))
            {
                if (fileDepth < 0)
                {
                    if (property.isFolder || property.guid != fileGuid) continue;

                    // Found the file: expand it so the next rows are its children.
                    fileDepth = property.depth;
                    expanded = new[] { property.entityId };
                    continue;
                }

                if (property.depth <= fileDepth) break;
                if (property.depth != fileDepth + 1) continue;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.entityId, out string guid, out long localId)) continue;

                SubAssetRow row = new SubAssetRow();
                row.Name = property.name;
                row.Icon = property.icon;
                subAssets.Add(new KeyValuePair<string, SubAssetRow>(file + SubAssetSeparator + localId, row));
            }

            return subAssets;
        }

        // ── Rows ──────────────────────────────────────────────────────────────

        VisualElement MakeRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("bt-row");

            Image icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("bt-row__icon");
            row.Add(icon);

            Label label = new Label();
            label.AddToClassList("bt-row__label");
            row.Add(label);

            TextField field = new TextField { isDelayed = false };
            field.AddToClassList("bt-row__field");
            field.style.display = DisplayStyle.None;
            row.Add(field);

            Label type = new Label();
            type.AddToClassList("bt-row__type");
            type.style.display = _showTypeColumn ? DisplayStyle.Flex : DisplayStyle.None;
            row.Add(type);

            Button add = new Button { text = "+", tooltip = "Create asset here" };
            add.AddToClassList("bt-row__add");
            add.style.display = DisplayStyle.None;
            row.Add(add);

            row.RegisterCallback<PointerDownEvent>(OnRowPointerDown);
            row.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            row.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            row.RegisterCallback<ContextClickEvent>(OnRowContextClick);
            row.RegisterCallback<DragUpdatedEvent>(OnRowDragUpdated);
            row.RegisterCallback<DragPerformEvent>(OnRowDragPerform);
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            string path = _tree.GetItemDataForIndex<string>(index);
            row.userData = path;

            Image icon = row.Q<Image>(className: "bt-row__icon");
            Label label = row.Q<Label>(className: "bt-row__label");
            TextField field = row.Q<TextField>(className: "bt-row__field");
            Button add = row.Q<Button>(className: "bt-row__add");

            if (IsSubAssetKey(path))
            {
                BindSubAssetRow(row, path, icon, label, field, add);
                return;
            }

            icon.image = AssetDatabase.GetCachedIcon(path);
            // All three are assigned unconditionally: rows are recycled, so an untagged
            // path has to actively clear what the last one left behind. A colour the
            // user picked for a tab outranks the per-type colour.
            icon.tintColor = BetterTabColors.GetTint(
                TabColorProvider == null ? BetterTabColors.None : TabColorProvider(path),
                BetterAssetTypeColors.TintForPath(path));
            row.EnableInClassList("bt-row--deprecated", BetterAssetTypeColors.IsDeprecated(path));
            ApplyRowBackground(row, path);

            bool renaming = path == _renamingPath;
            label.style.display = renaming ? DisplayStyle.None : DisplayStyle.Flex;
            field.style.display = renaming ? DisplayStyle.Flex : DisplayStyle.None;
            label.text = path == _rootPath ? _rootPath : Path.GetFileNameWithoutExtension(path);

            if (_showTypeColumn)
            {
                Label type = row.Q<Label>(className: "bt-row__type");
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                type.text = (assetType != null && !AssetDatabase.IsValidFolder(path)) ? assetType.Name : "";
            }

            bool showAdd = _showAddButton && path == _rootPath;
            add.style.display = showAdd ? DisplayStyle.Flex : DisplayStyle.None;
            if (showAdd)
                add.clickable = new Clickable(() => ShowCreateMenu(_rootPath));

            if (renaming)
            {
                field.SetValueWithoutNotify(Path.GetFileNameWithoutExtension(path));
                field.RegisterCallback<KeyDownEvent>(OnRenameKeyDown, TrickleDown.TrickleDown);
                // Focus and select once the field is actually laid out.
                field.schedule.Execute(() =>
                {
                    field.Focus();
                    field.SelectAll();
                }).ExecuteLater(0);
            }
        }

        // Rows are recycled, so everything a file or folder row may have set is reset here.
        void BindSubAssetRow(VisualElement row, string key, Image icon, Label label, TextField field, Button add)
        {
            bool hasRow = _subAssetRows.TryGetValue(key, out SubAssetRow subAsset);

            icon.image = hasRow ? subAsset.Icon : null;
            icon.tintColor = Color.white;
            row.EnableInClassList("bt-row--deprecated", false);
            row.style.backgroundImage = StyleKeyword.None;

            label.style.display = DisplayStyle.Flex;
            field.style.display = DisplayStyle.None;
            label.text = hasRow ? subAsset.Name : string.Empty;

            if (_showTypeColumn)
            {
                Label type = row.Q<Label>(className: "bt-row__type");
                Object subAssetObject = hasRow ? SubAssetObject(key) : null;
                type.text = subAssetObject != null ? subAssetObject.GetType().Name : string.Empty;
            }

            add.style.display = DisplayStyle.None;
        }

        Object SubAssetObject(string key)
        {
            if (_subAssetObjects.TryGetValue(key, out Object cached)) return cached;

            Object loaded = LoadObject(key);
            _subAssetObjects[key] = loaded;
            return loaded;
        }

        void UnbindRow(VisualElement row, int index)
        {
            TextField field = row.Q<TextField>(className: "bt-row__field");
            field.UnregisterCallback<KeyDownEvent>(OnRenameKeyDown, TrickleDown.TrickleDown);
            row.userData = null;
        }

        static string RowPath(VisualElement row) => row?.userData as string;

        // ── Selection ─────────────────────────────────────────────────────────

        void OnSelectionChanged(IEnumerable<object> items)
        {
            string last = null;
            foreach (object o in items) last = o as string;
            if (!string.IsNullOrEmpty(last)) ItemSelected?.Invoke(last);
        }

        void OnItemsChosen(IEnumerable<object> items)
        {
            foreach (object o in items)
            {
                if (o is string path) { ItemActivated?.Invoke(path); return; }
            }
        }

        public List<string> GetSelectedPaths()
        {
            List<string> paths = new List<string>();
            foreach (object o in _tree.selectedItems)
                if (o is string p) paths.Add(p);
            return paths;
        }

        public void SetSelectedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            SetSelectedPaths(new List<string> { path });
        }

        // Mirrors a selection made elsewhere. Paths not in this tree are left out, and an
        // empty list clears it: a stale selection here next to the new one elsewhere is
        // what showed two rows marked at once.
        public void SetSelectedPaths(List<string> paths)
        {
            List<int> ids = new List<int>();
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path) && _pathToId.TryGetValue(path, out int id) && _builtIds.Contains(id))
                    ids.Add(id);
            }
            _tree.SetSelectionByIdWithoutNotify(ids);
        }

        // A left-to-right alpha ramp in the row's colour, strongest at its left edge.
        // The gradient texture is white, so one texture covers the whole palette and
        // the per-row tint multiplies through it.
        void ApplyRowBackground(VisualElement row, string path)
        {
            Color tint = RowBackgroundProvider == null ? Color.clear : RowBackgroundProvider(path);
            if (tint.a <= 0f)
            {
                row.style.backgroundImage = StyleKeyword.None;
                return;
            }

            row.style.backgroundImage = Background.FromTexture2D(BetterTabColors.RowGradient);
            row.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            row.style.unityBackgroundImageTintColor = tint;
        }

        // Re-binds the visible rows without rebuilding the tree: enough for a colour
        // change, which touches nothing structural.
        public void RefreshRows()
        {
            _tree.RefreshItems();
        }

        public void ExpandToPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // A sub-asset sits under its file: open the folders down to the file, then the
            // file itself, loading its sub-assets so the row exists to be selected.
            if (IsSubAssetKey(path))
            {
                string file = MainPathOf(path);
                ExpandToPath(file);
                if (_pathToId.TryGetValue(file, out int fileId) && _builtIds.Contains(fileId))
                {
                    _tree.ExpandItem(fileId);
                    LoadSubAssetsIfNeeded(fileId);
                }
                return;
            }
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            List<string> chain = new List<string>();
            while (!string.IsNullOrEmpty(parent) && parent.StartsWith(_rootPath))
            {
                chain.Add(parent);
                if (parent == _rootPath) break;
                parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
            }
            for (int i = chain.Count - 1; i >= 0; i--)
                if (_pathToId.TryGetValue(chain[i], out int id) && _builtIds.Contains(id)) _tree.ExpandItem(id);
        }

        public void ScrollToPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !_pathToId.TryGetValue(path, out int id)) return;
            _tree.ScrollToItemById(id);
        }

        // ── Keyboard ──────────────────────────────────────────────────────────

        void OnKeyDown(KeyDownEvent evt)
        {
            if (_renamingPath != null) return;

            List<string> selected = GetSelectedPaths();
            if (selected.Count == 0) return;

            if (evt.keyCode == KeyCode.F2)
            {
                StartRename(selected[selected.Count - 1]);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Delete)
            {
                selected.RemoveAll(IsSubAssetKey);
                if (selected.Count == 0) return;

                if (BetterTabsInteractionHandler.DeleteMultiple(selected))
                {
                    Rebuild();
                    RefreshRequested?.Invoke();
                }
                evt.StopPropagation();
            }
        }

        // ── Rename ────────────────────────────────────────────────────────────

        public void StartRename(string path)
        {
            if (string.IsNullOrEmpty(path) || path == _rootPath || IsSubAssetKey(path)) return;
            _renamingPath = path;
            _tree.RefreshItems();
        }

        void OnRenameKeyDown(KeyDownEvent evt)
        {
            if (_renamingPath == null) return;

            // Registered on the field in TrickleDown, so stopping propagation here keeps the
            // key from ever reaching the text input underneath: nothing else to prevent.
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                TextField field = evt.currentTarget as TextField;
                CommitRename(field != null ? field.value : null);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CancelRename();
                evt.StopPropagation();
            }
        }

        void CommitRename(string newName)
        {
            string path = _renamingPath;
            _renamingPath = null;

            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(newName)
                && newName != Path.GetFileNameWithoutExtension(path))
            {
                string error = AssetDatabase.RenameAsset(path, newName);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"BetterTabs: Rename failed — {error}");
                AssetDatabase.Refresh();
                Rebuild();
                RefreshRequested?.Invoke();
                return;
            }
            _tree.RefreshItems();
        }

        void CancelRename()
        {
            _renamingPath = null;
            _tree.RefreshItems();
            _tree.Focus();
        }

        // ── Pointer: drag out, alt-click expand ───────────────────────────────

        void OnRowPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            VisualElement row = evt.currentTarget as VisualElement;
            string path = RowPath(row);
            if (path == null) return;

            // Alt+click toggles the whole subtree, like the Project window.
            if (evt.altKey && AssetDatabase.IsValidFolder(path)
                && _pathToId.TryGetValue(path, out int id))
            {
                if (_tree.IsExpanded(id)) _tree.CollapseItem(id, true);
                else _tree.ExpandItem(id, true);
                evt.StopPropagation();
                return;
            }

            _pressPos = evt.position;
            _pressPath = path;
            _pressed = true;
            _dragStarted = false;
        }

        void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed || _dragStarted) return;

            // The press can be released outside the row it started on, where that row's
            // PointerUp never arrives. A move with no button held is then a plain hover,
            // and Unity refuses to start a drag outside MouseDown/MouseDrag.
            if ((evt.pressedButtons & LeftButtonMask) == 0)
            {
                _pressed = false;
                return;
            }

            if ((evt.position - (Vector3)_pressPos).magnitude < DragThreshold) return;

            // The row that was pressed, not the one under the pointer now: a fast drag
            // can already be over a neighbour by the time the threshold is crossed.
            List<string> paths = GetSelectedPaths();
            string row = _pressPath;
            if (!string.IsNullOrEmpty(row) && !paths.Contains(row))
            {
                paths.Clear();
                paths.Add(row);
            }
            if (paths.Count == 0) return;

            _dragStarted = true;
            _pressed = false;

            // The owner starts the editor drag (so it can be dropped on object fields,
            // the tab bar, or any other window). Starting it here too would raise
            // "Starting multiple Drags".
            ItemsDragged?.Invoke(paths);
        }

        void OnRowPointerUp(PointerUpEvent evt)
        {
            _pressed = false;
            _dragStarted = false;
        }

        // ── Drop into folders ─────────────────────────────────────────────────

        void OnRowDragUpdated(DragUpdatedEvent evt)
        {
            string target = DropFolderFor(RowPath(evt.currentTarget as VisualElement));
            if (target == null) return;
            DragAndDrop.visualMode = BetterDropHandler.VisualModeFor();
            evt.StopPropagation();
        }

        void OnRowDragPerform(DragPerformEvent evt)
        {
            string target = DropFolderFor(RowPath(evt.currentTarget as VisualElement));
            if (target == null) return;

            DropInto(target);
            evt.StopPropagation();
        }

        void OnEmptyDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = BetterDropHandler.VisualModeFor();
            // Must stop here: the built-in collection dragger throws on drag events
            // it has no controller for.
            evt.StopImmediatePropagation();
        }

        void OnEmptyDragPerform(DragPerformEvent evt)
        {
            DropInto(_rootPath);
            evt.StopImmediatePropagation();
        }

        void OnEmptyContextClick(ContextClickEvent evt)
        {
            ShowCreateMenu(_rootPath);
            evt.StopPropagation();
        }

        // Moves assets already in the project and imports files dropped in from the
        // OS file browser; the handler decides which, per path.
        void DropInto(string folder)
        {
            if (!BetterDropHandler.PerformDrop(folder)) return;
            Rebuild();
            RefreshRequested?.Invoke();
        }

        static string DropFolderFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        // ── Context menu ──────────────────────────────────────────────────────

        void OnRowContextClick(ContextClickEvent evt)
        {
            string path = RowPath(evt.currentTarget as VisualElement);
            if (string.IsNullOrEmpty(path)) return;
            if (IsSubAssetKey(path))
            {
                evt.StopPropagation();
                return;
            }

            if (AssetDatabase.IsValidFolder(path)) ShowCreateMenu(path);
            else
                BetterTabsInteractionHandler.BuildContextMenu(
                    path, StartRename, OnAssetChanged, OnRefreshNeeded).ShowAsContext();

            evt.StopPropagation();
        }

        void ShowCreateMenu(string folderPath)
        {
            BetterTabsInteractionHandler.BuildFolderContextMenu(
                folderPath, StartRename, OnAssetChanged, OnRefreshNeeded).ShowAsContext();
        }

        void OnAssetChanged(string path) => OnRefreshNeeded();

        void OnRefreshNeeded()
        {
            Rebuild();
            RefreshRequested?.Invoke();
        }

        // ── Expanded state persistence ────────────────────────────────────────

        void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            // Deferred: changing the tree's items from inside its own expand callback is
            // not safe.
            if (args.isExpanded)
            {
                int id = args.id;
                schedule.Execute(() => LoadSubAssetsIfNeeded(id));
            }

            // Restoring a saved state is not a user change, and saving it back mid-restore
            // would write a half-applied list.
            if (_isRestoringExpansion) return;
            ExpandedChanged?.Invoke();
        }

        // List variants: the right panel stores expand state per tab.
        public void SaveExpandedState(List<string> target)
        {
            if (target == null) return;
            target.Clear();
            foreach (KeyValuePair<string, int> kv in _pathToId)
                if (_builtIds.Contains(kv.Value) && _tree.IsExpanded(kv.Value)) target.Add(kv.Key);
        }

        public void LoadExpandedState(List<string> expandedPaths)
        {
            if (expandedPaths == null) return;

            bool wasRestoring = _isRestoringExpansion;
            _isRestoringExpansion = true;
            foreach (string path in expandedPaths)
            {
                if (string.IsNullOrEmpty(path) || !_pathToId.TryGetValue(path, out int id) || !_builtIds.Contains(id))
                    continue;

                _tree.ExpandItem(id);
                LoadSubAssetsIfNeeded(id);
            }
            _isRestoringExpansion = wasRestoring;
        }

        public string SaveExpandedState()
        {
            List<string> expanded = new List<string>();
            SaveExpandedState(expanded);
            return string.Join("|", expanded);
        }

        public void LoadExpandedState(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            LoadExpandedState(new List<string>(data.Split('|')));
        }

        struct SubAssetRow
        {
            public string Name;
            public Texture Icon;
        }
    }
}
