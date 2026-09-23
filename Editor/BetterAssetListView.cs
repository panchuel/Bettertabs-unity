using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Flat asset list used by the grid view and the search results. Both show the
    // same items with the same interactions and only differ in layout, so one
    // component covers them via a CSS class swap.
    internal class BetterAssetListView : VisualElement
    {
        internal enum Mode { List, Grid }

        const float DragThreshold = 4f;

        readonly VisualElement _header;
        readonly ScrollView _scroll;
        readonly VisualElement _items;

        readonly List<string> _paths = new List<string>();
        Mode _mode = Mode.List;
        string _selectedPath;

        // Drag-out state
        Vector2 _pressPos;
        bool _pressed;
        string _pressPath;

        public event Action<string> ItemSelected;
        public event Action<string> ItemActivated;
        public event Action<string> ItemDragged;
        public event Action<string> RenameRequested;
        public event Action RefreshRequested;

        // Supplies the rich-text match highlight when showing search results.
        public Func<string, string> HighlightProvider;

        public BetterAssetListView()
        {
            style.flexGrow = 1;

            // Column header, list mode only. The result count the search used to put
            // here now lives in the window status bar, where it does not steal a row.
            _header = new VisualElement();
            _header.AddToClassList("bt-list__header");
            _header.style.display = DisplayStyle.None;
            _header.Add(MakeColumn("NAME", "bt-list__col--name"));
            _header.Add(MakeColumn("TYPE", "bt-list__col--type"));
            Add(_header);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            Add(_scroll);

            _items = new VisualElement();
            _scroll.Add(_items);
        }

        static Label MakeColumn(string text, string modifier)
        {
            Label column = new Label(text);
            column.AddToClassList("bt-list__col");
            column.AddToClassList(modifier);
            return column;
        }

        // ── Data ──────────────────────────────────────────────────────────────

        public void SetItems(IReadOnlyList<string> paths, Mode mode)
        {
            _paths.Clear();
            if (paths != null) _paths.AddRange(paths);
            _mode = mode;

            _header.style.display = mode == Mode.List ? DisplayStyle.Flex : DisplayStyle.None;

            _items.EnableInClassList("bt-list__items--grid", mode == Mode.Grid);
            _items.EnableInClassList("bt-list__items--list", mode == Mode.List);

            Rebuild();
        }

        public void SetSelected(string path)
        {
            if (_selectedPath == path) return;
            _selectedPath = path;
            foreach (VisualElement item in _items.Children())
                item.EnableInClassList("bt-item--selected", (item.userData as string) == path);
        }

        void Rebuild()
        {
            _items.Clear();
            for (int i = 0; i < _paths.Count; i++)
                _items.Add(BuildItem(_paths[i], i));
        }

        VisualElement BuildItem(string path, int index)
        {
            bool isFolder = AssetDatabase.IsValidFolder(path);

            VisualElement item = new VisualElement();
            item.userData = path;
            item.AddToClassList("bt-item");
            item.AddToClassList(_mode == Mode.Grid ? "bt-item--grid" : "bt-item--row");
            if (path == _selectedPath) item.AddToClassList("bt-item--selected");
            if (_mode == Mode.List && index % 2 == 1) item.AddToClassList("bt-item--odd");
            if (BetterAssetTypeColors.IsDeprecated(path)) item.AddToClassList("bt-item--dim");
            item.tooltip = path;

            Image icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList(_mode == Mode.Grid ? "bt-item__preview" : "bt-item__icon");
            // A rendered preview carries its own colours; only the fallback type icon
            // is tinted, so a material thumbnail never comes out washed in gold.
            icon.image = IconFor(path, isFolder, out bool isTypeIcon);
            if (isTypeIcon) icon.tintColor = BetterAssetTypeColors.TintForPath(path);
            item.Add(icon);

            Label label = new Label();
            label.AddToClassList("bt-item__label");
            if (_mode == Mode.List && HighlightProvider != null)
            {
                label.enableRichText = true;
                label.text = HighlightProvider(path);
            }
            else
            {
                label.text = _mode == Mode.Grid
                    ? Path.GetFileName(path)
                    : Path.GetFileNameWithoutExtension(path);
            }
            item.Add(label);

            if (_mode == Mode.List)
            {
                Label type = new Label();
                type.AddToClassList("bt-row__type");
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                type.text = assetType != null ? assetType.Name : "";
                item.Add(type);
            }

            item.RegisterCallback<PointerDownEvent>(OnItemPointerDown);
            item.RegisterCallback<PointerMoveEvent>(OnItemPointerMove);
            item.RegisterCallback<PointerUpEvent>(OnItemPointerUp);
            item.RegisterCallback<ContextClickEvent>(OnItemContextClick);
            return item;
        }

        static Texture2D IconFor(string path, bool isFolder, out bool isTypeIcon)
        {
            isTypeIcon = true;
            if (isFolder) return EditorGUIUtility.FindTexture("Folder Icon");

            // Grid cells prefer the rendered preview; fall back to the type icon.
            Texture2D preview = AssetPreview.GetAssetPreview(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path));
            if (preview != null)
            {
                isTypeIcon = false;
                return preview;
            }
            return AssetDatabase.GetCachedIcon(path) as Texture2D;
        }

        // ── Interaction ───────────────────────────────────────────────────────

        void OnItemPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            string path = (evt.currentTarget as VisualElement)?.userData as string;
            if (path == null) return;

            if (evt.clickCount == 2)
            {
                ItemActivated?.Invoke(path);
                evt.StopPropagation();
                return;
            }

            SetSelected(path);
            ItemSelected?.Invoke(path);

            _pressPos = evt.position;
            _pressPath = path;
            _pressed = true;
            evt.StopPropagation();
        }

        void OnItemPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed || _pressPath == null) return;
            if ((evt.position - (Vector3)_pressPos).magnitude < DragThreshold) return;

            string path = _pressPath;
            _pressed = false;
            _pressPath = null;
            ItemDragged?.Invoke(path);
        }

        void OnItemPointerUp(PointerUpEvent evt)
        {
            _pressed = false;
            _pressPath = null;
        }

        void OnItemContextClick(ContextClickEvent evt)
        {
            string path = (evt.currentTarget as VisualElement)?.userData as string;
            if (path == null) return;

            GenericMenu menu = AssetDatabase.IsValidFolder(path)
                ? BetterTabsInteractionHandler.BuildFolderContextMenu(
                    path, OnRename, OnChanged, OnRefresh)
                : BetterTabsInteractionHandler.BuildContextMenu(
                    path, OnRename, OnChanged, OnRefresh);
            menu.ShowAsContext();
            evt.StopPropagation();
        }

        void OnRename(string path) => RenameRequested?.Invoke(path);
        void OnChanged(string path) => RefreshRequested?.Invoke();
        void OnRefresh() => RefreshRequested?.Invoke();
    }
}
