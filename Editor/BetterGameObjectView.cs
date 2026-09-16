using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Prefab / scene-object tab: hierarchy on the left, stacked component
    // inspectors on the right. Each component gets an InspectorElement, which
    // renders custom UI Toolkit inspectors natively and legacy ones via IMGUI.
    internal class BetterGameObjectView : VisualElement
    {
        const float MinPaneW = 120f;

        readonly TwoPaneSplitView _split;
        readonly VisualElement _hierarchyPane;
        readonly VisualElement _inspectorPane;
        readonly TreeView _hierarchy;
        readonly ScrollView _inspector;
        readonly Label _message;

        readonly Dictionary<int, string> _idToPath = new Dictionary<int, string>();
        readonly Dictionary<string, int> _pathToId = new Dictionary<string, int>();
        int _nextId;

        Transform _root;
        string _selectionPath;
        string _inspectorKey;

        public event Action<string> SelectionChanged;
        public event Action ValueChanged;

        public BetterGameObjectView(float splitterX)
        {
            style.flexGrow = 1;

            _message = new Label();
            _message.AddToClassList("bt-inspector__empty");
            _message.style.display = DisplayStyle.None;
            Add(_message);

            _split = new TwoPaneSplitView(0, Mathf.Max(MinPaneW, splitterX),
                TwoPaneSplitViewOrientation.Horizontal);
            _split.style.flexGrow = 1;

            _hierarchy = new TreeView
            {
                fixedItemHeight = 18f,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _hierarchy.style.flexGrow = 1;
            _hierarchy.makeItem = MakeRow;
            _hierarchy.bindItem = BindRow;
            _hierarchy.selectionChanged += OnHierarchySelectionChanged;
            // The built-in collection dragger has no controller here and throws on
            // DragUpdated, which also breaks event handling for the splitter. Nothing
            // is draggable in this hierarchy, so swallow drag events before it sees them.
            _hierarchy.reorderable = false;
            _hierarchy.RegisterCallback<DragUpdatedEvent>(SwallowDrag, TrickleDown.TrickleDown);
            _hierarchy.RegisterCallback<DragPerformEvent>(SwallowDrag, TrickleDown.TrickleDown);
            // Each pane is a plain VisualElement wrapping the control. TreeView and
            // ScrollView carry flex-basis:0 in their own USS, which outranks the width
            // TwoPaneSplitView writes while dragging, so using them as panes directly
            // makes the divider appear dead and lets content spill over the other pane.
            _hierarchyPane = MakePane();
            _hierarchyPane.Add(_hierarchy);
            _split.Add(_hierarchyPane);

            _inspector = new ScrollView(ScrollViewMode.Vertical);
            _inspector.style.flexGrow = 1;

            _inspectorPane = MakePane();
            _inspectorPane.Add(_inspector);
            _split.Add(_inspectorPane);

            Add(_split);

        }

        static VisualElement MakePane()
        {
            VisualElement pane = new VisualElement();
            pane.style.minWidth = MinPaneW;
            // Clip, so a narrow pane does not paint over its neighbour.
            pane.style.overflow = Overflow.Hidden;
            return pane;
        }

        static void SwallowDrag(EventBase evt)
        {
            evt.StopImmediatePropagation();
        }

        // The split view drops its dimension while the view sits at display:none, so
        // the owner restores it on the way back in. Re-assigning the property is what
        // re-applies it; setting style.width directly does not survive layout.
        public void RestoreSplitter(float x)
        {
            if (x <= 0f || float.IsNaN(x)) return;
            float target = Mathf.Max(MinPaneW, x);
            schedule.Execute(() => _split.fixedPaneInitialDimension = target);
        }

        public float CurrentSplitterX()
        {
            float w = _hierarchyPane.resolvedStyle.width;
            return (w > 0f && !float.IsNaN(w)) ? w : MinPaneW;
        }

        // ── Target ────────────────────────────────────────────────────────────

        public void ShowMessage(string message)
        {
            _message.text = message;
            _message.style.display = DisplayStyle.Flex;
            _split.style.display = DisplayStyle.None;
            InvalidateInspectorCache();
            _inspector.Clear();
            _root = null;
        }

        public void SetTarget(GameObject root, string selectionPath)
        {
            if (root == null) { ShowMessage("Unavailable"); return; }

            _message.style.display = DisplayStyle.None;
            _split.style.display = DisplayStyle.Flex;

            bool sameRoot = _root == root.transform;
            _root = root.transform;
            _selectionPath = selectionPath;

            if (!sameRoot) RebuildHierarchy();

            // Mirror the stored selection without re-notifying the owner.
            Transform selected = ResolveSelection();
            string path = BetterHierarchyRenderer.GetPath(selected);
            if (_pathToId.TryGetValue(path, out int id))
                _hierarchy.SetSelectionByIdWithoutNotify(new[] { id });

            RefreshInspector(selected != null ? selected.gameObject : root);
        }

        Transform ResolveSelection()
        {
            if (_root == null) return null;
            if (string.IsNullOrEmpty(_selectionPath)) return _root;
            Transform found = BetterHierarchyRenderer.FindByPath(_root, _selectionPath);
            return found != null ? found : _root;
        }

        // Drops cached editors and forces a rebuild (used when a prefab is unloaded).
        public void Invalidate()
        {
            InvalidateInspectorCache();
            _inspector.Clear();
            _root = null;
        }

        // ── Hierarchy ─────────────────────────────────────────────────────────

        int IdFor(string path)
        {
            if (_pathToId.TryGetValue(path, out int existing)) return existing;
            int id = ++_nextId;
            _pathToId[path] = id;
            _idToPath[id] = path;
            return id;
        }

        void RebuildHierarchy()
        {
            _pathToId.Clear();
            _idToPath.Clear();
            _nextId = 0;

            List<TreeViewItemData<string>> roots = new List<TreeViewItemData<string>>
            {
                BuildNode(_root)
            };
            _hierarchy.SetRootItems(roots);
            _hierarchy.Rebuild();

            // Open the chain down to the stored selection.
            _hierarchy.ExpandItem(IdFor(BetterHierarchyRenderer.GetPath(_root)));
            if (!string.IsNullOrEmpty(_selectionPath))
            {
                string[] parts = _selectionPath.Split('/');
                string chain = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    chain = i == 0 ? parts[0] : chain + "/" + parts[i];
                    if (_pathToId.TryGetValue(chain, out int id)) _hierarchy.ExpandItem(id);
                }
            }
        }

        TreeViewItemData<string> BuildNode(Transform t)
        {
            string path = BetterHierarchyRenderer.GetPath(t);
            List<TreeViewItemData<string>> children = new List<TreeViewItemData<string>>();
            for (int i = 0; i < t.childCount; i++)
                children.Add(BuildNode(t.GetChild(i)));
            return new TreeViewItemData<string>(IdFor(path), path, children);
        }

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
            return row;
        }

        void BindRow(VisualElement row, int index)
        {
            string path = _hierarchy.GetItemDataForIndex<string>(index);
            Transform t = BetterHierarchyRenderer.FindByPath(_root, path);

            Label label = row.Q<Label>(className: "bt-row__label");
            Image icon = row.Q<Image>(className: "bt-row__icon");

            label.text = t != null ? t.name : path;
            icon.image = EditorGUIUtility.IconContent("GameObject Icon").image;
            // Inactive objects are dimmed, like the scene hierarchy does.
            row.style.opacity = (t != null && !t.gameObject.activeInHierarchy) ? 0.5f : 1f;
        }

        void OnHierarchySelectionChanged(IEnumerable<object> items)
        {
            string path = null;
            foreach (object o in items) path = o as string;
            if (path == null) return;

            _selectionPath = path;
            SelectionChanged?.Invoke(path);

            Transform t = BetterHierarchyRenderer.FindByPath(_root, path);
            RefreshInspector(t != null ? t.gameObject : null);
        }

        // ── Inspector ─────────────────────────────────────────────────────────

        void RefreshInspector(GameObject target)
        {
            string key = target != null ? target.GetInstanceID().ToString() : "";
            if (key == _inspectorKey) return;
            _inspectorKey = key;

            InvalidateInspectorCache();
            _inspector.Clear();
            if (target == null) return;

            AddEditor(target);
            foreach (Component component in target.GetComponents<Component>())
            {
                if (component == null) continue; // missing script
                AddEditor(component);
            }
        }

        void AddEditor(UnityEngine.Object target)
        {
            // Built from the object, not from an Editor we own: InspectorElement then
            // creates and disposes the editor itself. Destroying our own editor here
            // raced with the element disposing its SerializedObject, which threw from
            // the inspector OnDisable while the window was closing.
            InspectorElement element = new InspectorElement(target);

            // Reports edits so prefab tabs can write themselves back to disk.
            SerializedObject tracked = new SerializedObject(target);
            element.TrackSerializedObjectValue(tracked, _ => ValueChanged?.Invoke());

            _inspector.Add(element);
        }

        // Forces the next RefreshInspector call to rebuild instead of matching the cache.
        void InvalidateInspectorCache()
        {
            _inspectorKey = null;
        }
    }
}
