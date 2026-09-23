using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Floating swatch grid used to tag a tab with a colour. It lives inside the window
    // root rather than in its own EditorWindow: an overlay needs no screen-coordinate
    // maths to sit under the tab, and it can never be left orphaned behind the editor.
    internal class BetterTabColorPicker : VisualElement
    {
        const float EdgeMargin = 4f;

        readonly VisualElement _root;
        readonly Action<int> _picked;
        readonly EventCallback<PointerDownEvent> _outsidePointerDown;

        BetterTabColorPicker(VisualElement root, int current, Action<int> picked)
        {
            _root = root;
            _picked = picked;

            AddToClassList("bt-swatches");
            focusable = true;

            for (int i = 0; i < BetterTabColors.Count; i++)
                Add(BuildSwatch(i, i == current));

            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                Close();
                evt.StopPropagation();
            });

            // TrickleDown so it is seen before the swatch handles its own click; a
            // press inside the popup is ignored here and closes it through the swatch.
            _outsidePointerDown = evt =>
            {
                if (evt.target is VisualElement el && (el == this || Contains(el))) return;
                Close();
            };
            _root.RegisterCallback(_outsidePointerDown, TrickleDown.TrickleDown);
        }

        // anchorWorld is the tab the popup belongs to; it opens under its lower-left
        // corner and slides back inside the window if it would overflow the right edge.
        public static void Show(VisualElement root, Rect anchorWorld, int current, Action<int> picked)
        {
            if (root == null) return;
            Close(root);

            var picker = new BetterTabColorPicker(root, BetterTabColors.Normalize(current), picked);
            root.Add(picker);

            // A tab that has not been laid out yet reports NaN bounds; falling back to
            // the top-left corner keeps the popup on screen instead of dropping it.
            Vector2 local = root.WorldToLocal(new Vector2(anchorWorld.xMin, anchorWorld.yMax));
            if (float.IsNaN(local.x) || float.IsNaN(local.y)) local = Vector2.zero;

            picker.style.left = local.x;
            picker.style.top = local.y + EdgeMargin;
            picker.RegisterCallback<GeometryChangedEvent>(picker.OnGeometryChanged);
            picker.schedule.Execute(() => picker.Focus());
        }

        public static void Close(VisualElement root)
        {
            root?.Q<BetterTabColorPicker>()?.Close();
        }

        void Close()
        {
            _root.UnregisterCallback(_outsidePointerDown, TrickleDown.TrickleDown);
            RemoveFromHierarchy();
        }

        // The popup size is only known once laid out, so it is pulled back inside the
        // window here. Writing the same value back would loop, hence the comparisons.
        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float maxLeft = Mathf.Max(EdgeMargin, _root.layout.width - layout.width - EdgeMargin);
            float left = Mathf.Clamp(resolvedStyle.left, EdgeMargin, maxLeft);
            if (!Mathf.Approximately(resolvedStyle.left, left))
                style.left = left;

            float maxTop = Mathf.Max(EdgeMargin, _root.layout.height - layout.height - EdgeMargin);
            float top = Mathf.Clamp(resolvedStyle.top, EdgeMargin, maxTop);
            if (!Mathf.Approximately(resolvedStyle.top, top))
                style.top = top;
        }

        VisualElement BuildSwatch(int colorIndex, bool selected)
        {
            VisualElement swatch = new VisualElement();
            swatch.AddToClassList("bt-swatch");
            swatch.tooltip = BetterTabColors.GetName(colorIndex);
            if (selected) swatch.AddToClassList("bt-swatch--selected");

            if (colorIndex == BetterTabColors.None)
            {
                swatch.AddToClassList("bt-swatch--none");
                Label clear = new Label("×");
                clear.AddToClassList("bt-swatch__clear");
                clear.pickingMode = PickingMode.Ignore;
                swatch.Add(clear);
            }
            else
            {
                swatch.style.backgroundColor = BetterTabColors.GetColor(colorIndex);
            }

            swatch.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                Close();
                _picked?.Invoke(colorIndex);
            });
            return swatch;
        }
    }
}
