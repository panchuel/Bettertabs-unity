using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Asset tree backed by the native TreeView, used by both panels. Multi-selection,
    // arrow-key navigation, virtualisation and expand state come from the control
    // itself; this class supplies the data and the asset-specific interactions
    // (rename, drag out to the editor, context menu, drop into folders).
    internal class BetterAssetTreeView : VisualElement
    {
        const float RowHeight = 18f;
        const float DragThreshold = 4f;

        readonly bool _showTypeColumn;
        readonly bool _showAddButton;
        string _rootPath = "Assets";

        readonly TreeView _tree;
        readonly Dictionary<int, string> _idToPath = new Dictionary<int, string>();
        readonly Dictionary<string, int> _pathToId = new Dictionary<string, int>();
        int _nextId;

        string _renamingPath;
        string _highlightedPath;

        // Drag-out state
        Vector2 _pressPos;
        bool _pressed;
        bool _dragStarted;

        public event Action<string> ItemSelected;
        public event Action<string> ItemActivated;
        public event Action<List<string>> ItemsDragged;
        public event Action RefreshRequested;

        public BetterAssetTreeView(bool multiSelect, bool showTypeColumn, bool showAddButton)
        {
            style.flexGrow = 1;
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

        // ── Data ──────────────────────────────────────────────────────────────

        // Switches the tree to another folder (the active tab root on the right panel).
        public void SetRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || rootPath == _rootPath) return;
            _rootPath = rootPath;
            Rebuild();
        }

        public void Rebuild()
        {
            List<TreeViewItemData<string>> roots = new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(IdFor(_rootPath), _rootPath, BuildChildren(_rootPath))
            };
            _tree.SetRootItems(roots);
            _tree.Rebuild();
            _tree.ExpandItem(IdFor(_rootPath));
        }

        List<TreeViewItemData<string>> BuildChildren(string folder)
        {
            List<TreeViewItemData<string>> children = new List<TreeViewItemData<string>>();

            string[] subFolders = AssetDatabase.GetSubFolders(folder);
            Array.Sort(subFolders, StringComparer.OrdinalIgnoreCase);
            foreach (string sub in subFolders)
                children.Add(new TreeViewItemData<string>(IdFor(sub), sub, BuildChildren(sub)));

            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(folder))
            {
                if (file.EndsWith(".meta")) continue;
                files.Add(file.Replace('\\', '/'));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
                children.Add(new TreeViewItemData<string>(IdFor(file), file));

            return children;
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

            icon.image = AssetDatabase.GetCachedIcon(path);

            bool renaming = path == _renamingPath;
            label.style.display = renaming ? DisplayStyle.None : DisplayStyle.Flex;
            field.style.display = renaming ? DisplayStyle.Flex : DisplayStyle.None;
            label.text = path == _rootPath ? _rootPath : Path.GetFileNameWithoutExtension(path);

            row.EnableInClassList("bt-row--highlighted", path == _highlightedPath && !renaming);

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
            if (string.IsNullOrEmpty(path) || !_pathToId.TryGetValue(path, out int id)) return;
            _tree.SetSelectionByIdWithoutNotify(new[] { id });
        }

        public void SetHighlight(string path)
        {
            _highlightedPath = path;
            _tree.RefreshItems();
        }

        public void ExpandToPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            List<string> chain = new List<string>();
            while (!string.IsNullOrEmpty(parent) && parent.StartsWith(_rootPath))
            {
                chain.Add(parent);
                if (parent == _rootPath) break;
                parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
            }
            for (int i = chain.Count - 1; i >= 0; i--)
                if (_pathToId.TryGetValue(chain[i], out int id)) _tree.ExpandItem(id);
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
            if (string.IsNullOrEmpty(path) || path == _rootPath) return;
            _renamingPath = path;
            _tree.RefreshItems();
        }

        void OnRenameKeyDown(KeyDownEvent evt)
        {
            if (_renamingPath == null) return;

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                TextField field = evt.currentTarget as TextField;
                CommitRename(field != null ? field.value : null);
                evt.StopPropagation();
                evt.PreventDefault();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CancelRename();
                evt.StopPropagation();
                evt.PreventDefault();
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
            _pressed = true;
            _dragStarted = false;
        }

        void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed || _dragStarted) return;
            if ((evt.position - (Vector3)_pressPos).magnitude < DragThreshold) return;

            List<string> paths = GetSelectedPaths();
            string row = RowPath(evt.currentTarget as VisualElement);
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
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            evt.StopPropagation();
        }

        void OnRowDragPerform(DragPerformEvent evt)
        {
            string target = DropFolderFor(RowPath(evt.currentTarget as VisualElement));
            if (target == null) return;

            MoveAssetsInto(target);
            evt.StopPropagation();
        }

        void OnEmptyDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            // Must stop here: the built-in collection dragger throws on drag events
            // it has no controller for.
            evt.StopImmediatePropagation();
        }

        void OnEmptyDragPerform(DragPerformEvent evt)
        {
            MoveAssetsInto(_rootPath);
            evt.StopImmediatePropagation();
        }

        void OnEmptyContextClick(ContextClickEvent evt)
        {
            ShowCreateMenu(_rootPath);
            evt.StopPropagation();
        }

        void MoveAssetsInto(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;

            DragAndDrop.AcceptDrag();
            bool moved = false;
            foreach (string dragged in DragAndDrop.paths)
            {
                if (string.IsNullOrEmpty(dragged)) continue;
                string dest = $"{folder}/{Path.GetFileName(dragged)}";
                if (dest == dragged) continue;

                string error = AssetDatabase.MoveAsset(dragged, dest);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"BetterTabs: Move failed — {error}");
                else moved = true;
            }

            if (!moved) return;
            AssetDatabase.Refresh();
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

        // List variants: the right panel stores expand state per tab.
        public void SaveExpandedState(List<string> target)
        {
            if (target == null) return;
            target.Clear();
            foreach (KeyValuePair<string, int> kv in _pathToId)
                if (_tree.IsExpanded(kv.Value)) target.Add(kv.Key);
        }

        public void LoadExpandedState(List<string> expandedPaths)
        {
            if (expandedPaths == null) return;
            foreach (string path in expandedPaths)
                if (!string.IsNullOrEmpty(path) && _pathToId.TryGetValue(path, out int id))
                    _tree.ExpandItem(id);
        }

        public string SaveExpandedState()
        {
            List<string> expanded = new List<string>();
            foreach (KeyValuePair<string, int> kv in _pathToId)
                if (_tree.IsExpanded(kv.Value)) expanded.Add(kv.Key);
            return string.Join("|", expanded);
        }

        public void LoadExpandedState(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            foreach (string path in data.Split('|'))
                if (!string.IsNullOrEmpty(path) && _pathToId.TryGetValue(path, out int id))
                    _tree.ExpandItem(id);
        }
    }
}
