using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Remembers how wide the user made a TwoPaneSplitView's fixed pane.
    //
    // Reading the pane on demand is not enough: it measures zero while its view is
    // hidden, and it gets squeezed to the minimum whenever the layout runs short of
    // room — opening the project panel, narrowing the window, docking. Persisting
    // either of those overwrites a chosen width with one the user never picked, so
    // only a drag of the divider itself is recorded.
    internal class BetterSplitterTracker
    {
        // Unity's own class on the draggable handle between the two panes.
        const string DraglineClass = "unity-two-pane-split-view__dragline-anchor";

        readonly VisualElement _fixedPane;
        bool _userResizing;
        float _width;

        public BetterSplitterTracker(VisualElement split, VisualElement fixedPane, float initialWidth)
        {
            _fixedPane = fixedPane;
            _width = initialWidth;

            split.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            split.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            fixedPane.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public float Width => _width;

        // Used when the width is set from outside, e.g. restored from preferences.
        public void Set(float width)
        {
            if (width > 0f && !float.IsNaN(width)) _width = width;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            _userResizing = evt.button == 0 && IsDragline(evt.target as VisualElement);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_userResizing) return;
            Record();
            _userResizing = false;
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_userResizing) Record();
        }

        void Record()
        {
            float width = _fixedPane.resolvedStyle.width;
            if (width > 0f && !float.IsNaN(width)) _width = width;
        }

        static bool IsDragline(VisualElement el)
        {
            for (VisualElement e = el; e != null; e = e.parent)
            {
                if (e.ClassListContains(DraglineClass)) return true;
            }
            return false;
        }
    }
}
