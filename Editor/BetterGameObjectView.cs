using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
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
        GameObject _inspectorTarget;
        bool _hasInspectorTarget;

        // Remembers the width the user dragged the divider to. Survives the view
        // being hidden and ignores the squeezes the layout applies on its own.
        BetterSplitterTracker _splitter;

        public event Action<string> SelectionChanged;
        public event Action ValueChanged;

        // An object reference was clicked; the owner reveals it in the left panel.
        public event Action<string> AssetReferenceClicked;

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
                fixedItemHeight = 24f,
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _hierarchy.style.flexGrow = 1;
            _hierarchy.AddToClassList("bt-tree");
            // The guides are the point of this panel's redesign: without a hairline
            // per depth level there is no way to see what is a child of what.
            _hierarchy.AddToClassList("bt-tree--guides");
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
            _hierarchyPane.AddToClassList("bt-panel");
            _hierarchyPane.Add(_hierarchy);
            _split.Add(_hierarchyPane);

            _inspector = new ScrollView(ScrollViewMode.Vertical);
            _inspector.style.flexGrow = 1;

            _inspectorPane = MakePane();
            _inspectorPane.Add(_inspector);
            _split.Add(_inspectorPane);

            Add(_split);

            _splitter = new BetterSplitterTracker(_split, _hierarchyPane,
                Mathf.Max(MinPaneW, splitterX));

            BetterInspectorLinks.MirrorObjectFieldClicks(this,
                path => AssetReferenceClicked?.Invoke(path));
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
            _splitter.Set(target);
            schedule.Execute(() => _split.fixedPaneInitialDimension = target);
        }

        // The tracked width, never the live one: a hidden pane measures 0, and the
        // owner persists this on shutdown regardless of which tab happens to be open.
        // Reading the pane there used to save the collapsed width over a good one.
        public float CurrentSplitterX() => _splitter.Width;

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

            if (!sameRoot)
            {
                RebuildHierarchy();
                // Switching between two prefab or scene-object tabs never passes
                // through the pane-mode change that used to re-apply this, so each
                // one came up with whatever width the split view happened to keep.
                RestoreSplitter(_splitter.Width);
            }

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
            icon.tintColor = BetterAssetTypeColors.GameObjectTint;
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
            // Keyed on the reference rather than an id: Object.GetInstanceID is an error
            // from Unity 6.6 on, and its replacement does not exist on the versions this
            // package still declares support for. The reference also asks the question
            // directly — is this the same object the cards were built for?
            if (_hasInspectorTarget && ReferenceEquals(_inspectorTarget, target)
                && !IsDestroyed(target))
                return;

            _hasInspectorTarget = true;
            _inspectorTarget = target;

            _inspector.Clear();
            if (target == null) return;

            _inspector.Add(BuildObjectHeader(target));
            _inspector.Add(MakeEditor(target));

            Label section = new Label("COMPONENTS");
            section.AddToClassList("bt-section");
            _inspector.Add(section);

            foreach (Component component in target.GetComponents<Component>())
            {
                if (component == null) continue; // missing script
                _inspector.Add(BuildComponentCard(component));
            }
        }

        static VisualElement BuildObjectHeader(GameObject target)
        {
            VisualElement header = new VisualElement();
            header.AddToClassList("bt-inspector__header");

            VisualElement badge = new VisualElement();
            badge.AddToClassList("bt-inspector__badge");
            Color tint = BetterAssetTypeColors.GameObjectTint;
            badge.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.16f);

            Image icon = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                image = EditorGUIUtility.IconContent("GameObject Icon").image,
                tintColor = tint
            };
            icon.AddToClassList("bt-inspector__icon");
            badge.Add(icon);
            header.Add(badge);

            VisualElement titles = new VisualElement();
            titles.AddToClassList("bt-inspector__titles");

            Label title = new Label(target.name);
            title.AddToClassList("bt-inspector__title");
            titles.Add(title);

            Transform parent = target.transform.parent;
            Label subtitle = new Label(parent != null
                ? parent.name + " \u203a " + target.name
                : target.name);
            subtitle.AddToClassList("bt-inspector__subtitle");
            titles.Add(subtitle);

            header.Add(titles);
            return header;
        }

        // One card per component, headed by the component's real type name. The stock
        // inspector repeats a generic "Script" row instead, which says nothing about
        // which of four MonoBehaviours a given block belongs to.
        VisualElement BuildComponentCard(Component component)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("bt-comp");

            VisualElement header = new VisualElement();
            header.AddToClassList("bt-comp__header");

            Image icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("bt-comp__icon");
            icon.image = EditorGUIUtility.ObjectContent(component, component.GetType()).image;
            header.Add(icon);

            Label name = new Label(component.GetType().Name);
            name.AddToClassList("bt-comp__name");
            header.Add(name);

            Button menu = new Button { text = "\u22ee" };
            menu.AddToClassList("bt-comp__menu");
            menu.tooltip = "Component menu";
            menu.clicked += () => ShowComponentMenu(component, menu.worldBound);
            header.Add(menu);
            card.Add(header);

            VisualElement body = new VisualElement();
            body.AddToClassList("bt-comp__body");
            body.Add(MakeEditor(component));
            card.Add(body);
            return card;
        }

        // The operations Unity's own component context menu offers, built from the
        // public API: the editor's real menu is not exposed to a custom window.
        void ShowComponentMenu(Component component, Rect anchor)
        {
            GenericMenu menu = new GenericMenu();

            if (component is Transform)
                menu.AddDisabledItem(new GUIContent("Remove Component"));
            else
                menu.AddItem(new GUIContent("Remove Component"), false, () =>
                {
                    Undo.DestroyObjectImmediate(component);
                    RebuildInspector();
                });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Move Up"), false, () =>
            {
                ComponentUtility.MoveComponentUp(component);
                RebuildInspector();
            });
            menu.AddItem(new GUIContent("Move Down"), false, () =>
            {
                ComponentUtility.MoveComponentDown(component);
                RebuildInspector();
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Component"), false,
                () => ComponentUtility.CopyComponent(component));
            menu.AddItem(new GUIContent("Paste Component Values"), false, () =>
            {
                ComponentUtility.PasteComponentValues(component);
                ValueChanged?.Invoke();
                RebuildInspector();
            });

            MonoBehaviour behaviour = component as MonoBehaviour;
            if (behaviour != null)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Edit Script"), false,
                    () => AssetDatabase.OpenAsset(MonoScript.FromMonoBehaviour(behaviour)));
            }

            menu.DropDown(anchor);
        }

        // Adding or removing a component changes the card list, which the key-based
        // cache would otherwise consider unchanged.
        void RebuildInspector()
        {
            GameObject target = _inspectorTarget;
            InvalidateInspectorCache();
            ValueChanged?.Invoke();
            RefreshInspector(target);
        }

        VisualElement MakeEditor(UnityEngine.Object target)
        {
            // Some editors (MaterialEditor among them) draw nothing while Unity thinks
            // the object's inspector foldout is collapsed. Each card carries its own
            // header here, so the native foldout state must not leave one empty.
            InternalEditorUtility.SetIsInspectorExpanded(target, true);

            // Built from the object, not from an Editor we own: InspectorElement then
            // creates and disposes the editor itself. Destroying our own editor here
            // raced with the element disposing its SerializedObject, which threw from
            // the inspector OnDisable while the window was closing.
            InspectorElement element = new InspectorElement(target);

            // Reports edits so prefab tabs can write themselves back to disk.
            SerializedObject tracked = new SerializedObject(target);
            element.TrackSerializedObjectValue(tracked, _ => ValueChanged?.Invoke());

            // The generated "Script" row is left alone on purpose. Hiding it made the
            // InspectorElement regenerate its fields, which showed the row again, which
            // hid it again: an endless relayout that read as the last card flickering.
            // The card header already names the component, which was the actual problem.
            return element;
        }

        // Forces the next RefreshInspector call to rebuild instead of matching the cache.
        void InvalidateInspectorCache()
        {
            _hasInspectorTarget = false;
        }

        // A destroyed object keeps a live reference that compares equal to null through
        // Unity's operator, so matching the reference alone would leave cards pointing
        // at nothing after a prefab is unloaded.
        static bool IsDestroyed(GameObject target)
        {
            return !ReferenceEquals(target, null) && target == null;
        }
    }
}
