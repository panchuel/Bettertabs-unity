using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // The 42px strip under the tab bar: breadcrumb on the left, then the controls
    // that apply to whatever the active tab is showing. These used to be crammed
    // into the tab bar itself, which left no room for the path.
    internal class BetterTabsToolbarView : VisualElement
    {
        const string SeparatorGlyph = "›";

        readonly Button _panelBtn;
        readonly VisualElement _crumbs;
        readonly TextField _searchField;
        readonly VisualElement _viewToggle;
        readonly Button _gridBtn;
        readonly Button _listBtn;
        readonly Button _focusBtn;
        readonly Button _saveBtn;
        readonly Button _overflowBtn;

        // Crumb index -> the path that crumb walks to.
        readonly List<string> _crumbPaths = new List<string>();

        public event Action<string> SearchChanged;
        public event Action<bool> ViewModeChanged;      // true = grid
        public event Action<string> CrumbClicked;
        public event Action FocusClicked;
        public event Action SaveClicked;
        public event Action PanelToggleClicked;
        public event Action UnitySearchClicked;

        public BetterTabsToolbarView()
        {
            AddToClassList("bt-toolbar");

            // Sidebar toggles live at the leading edge in every tool that has one,
            // and it is used often enough that burying it in the overflow would hurt.
            _panelBtn = MakeIconButton("bt-toolbar__btn", "d_Project", "◁",
                () => PanelToggleClicked?.Invoke());
            _panelBtn.AddToClassList("bt-toolbar__btn--lead");
            Add(_panelBtn);

            _crumbs = new VisualElement();
            _crumbs.AddToClassList("bt-crumbs");
            Add(_crumbs);

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("bt-toolbar__spacer");
            Add(spacer);

            _focusBtn = new Button(() => FocusClicked?.Invoke()) { text = "Focus in Scene" };
            _focusBtn.AddToClassList("bt-toolbar__action");
            _focusBtn.tooltip = "Ping in Hierarchy / Project";
            Add(_focusBtn);

            _saveBtn = new Button(() => SaveClicked?.Invoke()) { text = "Save" };
            _saveBtn.AddToClassList("bt-toolbar__action");
            _saveBtn.tooltip = "Save the prefab (Ctrl+S). Also saved when you leave or close the tab.";
            Add(_saveBtn);

            _searchField = new TextField();
            _searchField.AddToClassList("bt-search");
            _searchField.textEdition.placeholder = "Search all assets…";
            _searchField.RegisterValueChangedCallback(e => SearchChanged?.Invoke(e.newValue));
            _searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) ClearSearch();
            });
            Add(_searchField);

            _viewToggle = new VisualElement();
            _viewToggle.AddToClassList("bt-seg");
            _gridBtn = MakeIconButton("bt-seg__btn", "d_GridLayoutGroup Icon", "⊞",
                () => ViewModeChanged?.Invoke(true));
            _gridBtn.tooltip = "Grid view";
            _viewToggle.Add(_gridBtn);

            VisualElement sep = new VisualElement();
            sep.AddToClassList("bt-seg__sep");
            _viewToggle.Add(sep);

            _listBtn = MakeIconButton("bt-seg__btn", "d_UnityEditor.ConsoleWindow", "≡",
                () => ViewModeChanged?.Invoke(false));
            _listBtn.tooltip = "List view";
            _viewToggle.Add(_listBtn);
            Add(_viewToggle);

            _overflowBtn = MakeIconButton("bt-toolbar__btn", "d__Menu", "⋯", ShowOverflowMenu);
            _overflowBtn.tooltip = "More options";
            Add(_overflowBtn);
        }

        static Button MakeIconButton(string className, string iconName, string fallbackGlyph,
            Action onClick)
        {
            Button button = new Button(onClick);
            button.AddToClassList(className);

            Texture2D icon = EditorGUIUtility.FindTexture(iconName);
            if (icon != null) button.style.backgroundImage = Background.FromTexture2D(icon);
            else button.text = fallbackGlyph;
            return button;
        }

        void ShowOverflowMenu()
        {
            GenericMenu menu = new GenericMenu();
            // Settings and How to Use live at the bottom right of the window now.
            menu.AddItem(new GUIContent("Open Unity Search"), false, () => UnitySearchClicked?.Invoke());
            menu.DropDown(_overflowBtn.worldBound);
        }

        // ── Breadcrumb ────────────────────────────────────────────────────────

        // Each segment is a button that walks back up the path; the last one is the
        // current location and stays inert.
        public void SetBreadcrumb(IReadOnlyList<string> labels, IReadOnlyList<string> paths)
        {
            _crumbs.Clear();
            _crumbPaths.Clear();
            if (labels == null || labels.Count == 0) return;

            for (int i = 0; i < labels.Count; i++)
            {
                if (i > 0)
                {
                    Label separator = new Label(SeparatorGlyph);
                    separator.AddToClassList("bt-crumb__sep");
                    _crumbs.Add(separator);
                }

                bool last = i == labels.Count - 1;
                string target = (paths != null && i < paths.Count) ? paths[i] : null;
                _crumbPaths.Add(target);

                Button crumb = new Button(() =>
                {
                    if (!string.IsNullOrEmpty(target)) CrumbClicked?.Invoke(target);
                }) { text = labels[i] };
                crumb.AddToClassList("bt-crumb");
                if (last) crumb.AddToClassList("bt-crumb--last");
                crumb.SetEnabled(!last && !string.IsNullOrEmpty(target));
                _crumbs.Add(crumb);
            }
        }

        // ── State ─────────────────────────────────────────────────────────────

        public void SetState(bool showSearch, bool showView, bool gridView, bool showFocus,
            bool panelOpen, bool showSave, bool canSave)
        {
            _searchField.style.display = showSearch ? DisplayStyle.Flex : DisplayStyle.None;
            _viewToggle.style.display = showView ? DisplayStyle.Flex : DisplayStyle.None;
            _focusBtn.style.display = showFocus ? DisplayStyle.Flex : DisplayStyle.None;
            _saveBtn.style.display = showSave ? DisplayStyle.Flex : DisplayStyle.None;
            _saveBtn.SetEnabled(canSave);

            _gridBtn.EnableInClassList("bt-seg__btn--active", gridView);
            _listBtn.EnableInClassList("bt-seg__btn--active", !gridView);

            _panelBtn.tooltip = panelOpen ? "Hide Project Panel" : "Show Project Panel";
        }

        public void FocusSearch()
        {
            _searchField.schedule.Execute(() => _searchField.Q("unity-text-input")?.Focus());
        }

        public void ClearSearch()
        {
            if (string.IsNullOrEmpty(_searchField.value)) return;
            _searchField.SetValueWithoutNotify("");
            SearchChanged?.Invoke("");
        }
    }
}
