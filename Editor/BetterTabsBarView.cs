using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // UI Toolkit tab bar. Owns presentation and pointer interaction (select, close,
    // reorder by drag, overflow scrolling). The window supplies the data and reacts
    // to the events; it never touches these visual elements directly.
    internal class BetterTabsBarView : VisualElement
    {
        const float ScrollStep = 80f;
        const float DragThreshold = 4f;

        readonly ScrollView _scroll;
        readonly VisualElement _row;
        readonly Button _leftArrow;
        readonly Button _rightArrow;
        readonly Button _addBtn;
        readonly Button _pingBtn;
        readonly Button _panelBtn;

        readonly List<BetterTabEntry> _tabs = new List<BetterTabEntry>();
        int _selectedIndex = -1;

        // Drag-reorder state. The DOM is left untouched while dragging: tabs are
        // placed by translate over a logical order, so layout never shifts mid-drag.
        VisualElement _dragEl;
        List<VisualElement> _dragTabs;
        float[] _dragWidths;
        float[] _dragBaseX;
        int _dragFrom = -1;
        int _dragTo = -1;
        float _pressX;
        float _grabOffsetX;
        bool _dragging;

        public event Action<int> TabSelected;
        public event Action<int> TabClosed;
        public event Action<int> TabContextMenu;
        public event Action<int, int> TabMoved;
        public event Action AddClicked;
        public event Action PingClicked;
        public event Action PanelToggleClicked;

        public BetterTabsBarView()
        {
            AddToClassList("bt-tabbar");

            _leftArrow = MakeButton("‹", "bt-tabbar__arrow", () => ScrollBy(-ScrollStep));
            Add(_leftArrow);

            _scroll = new ScrollView(ScrollViewMode.Horizontal);
            _scroll.AddToClassList("bt-tabbar__scroll");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(_scroll);

            _row = new VisualElement();
            _row.AddToClassList("bt-tabbar__row");
            _row.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            _row.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            _scroll.Add(_row);

            _rightArrow = MakeButton("›", "bt-tabbar__arrow", () => ScrollBy(ScrollStep));
            Add(_rightArrow);

            _addBtn = MakeButton("+", null, () => AddClicked?.Invoke());
            _addBtn.tooltip = "Add tab from current selection";
            Add(_addBtn);

            _pingBtn = MakeButton(null, null, () => PingClicked?.Invoke());
            _pingBtn.tooltip = "Ping in Hierarchy / Project";
            Add(_pingBtn);

            _panelBtn = MakeButton(null, null, () => PanelToggleClicked?.Invoke());
            Add(_panelBtn);

            RegisterCallback<GeometryChangedEvent>(_ => UpdateArrows());
            _scroll.horizontalScroller.valueChanged += _ => UpdateArrows();
        }

        Button MakeButton(string text, string extraClass, Action onClick)
        {
            Button b = new Button(onClick);
            b.AddToClassList("bt-tabbar__btn");
            if (!string.IsNullOrEmpty(extraClass)) b.AddToClassList(extraClass);
            if (text != null) b.text = text;
            return b;
        }

        // ── Data ──────────────────────────────────────────────────────────────

        public void SetTabs(List<BetterTabEntry> tabs, int selectedIndex)
        {
            _tabs.Clear();
            if (tabs != null) _tabs.AddRange(tabs);
            _selectedIndex = selectedIndex;
            Rebuild();
        }

        public void SetButtonState(bool canAdd, bool showPing, bool panelOpen)
        {
            _addBtn.SetEnabled(canAdd);

            _pingBtn.style.display = showPing ? DisplayStyle.Flex : DisplayStyle.None;
            if (showPing && _pingBtn.style.backgroundImage.value.texture == null)
            {
                Texture2D pingIcon = EditorGUIUtility.IconContent("d_SearchJump Icon").image as Texture2D;
                if (pingIcon != null) _pingBtn.style.backgroundImage = Background.FromTexture2D(pingIcon);
                else _pingBtn.text = "⊙";
            }

            if (_panelBtn.style.backgroundImage.value.texture == null)
            {
                Texture2D panelIcon = EditorGUIUtility.FindTexture("d_Project");
                if (panelIcon != null) _panelBtn.style.backgroundImage = Background.FromTexture2D(panelIcon);
                else _panelBtn.text = panelOpen ? "◁" : "▷";
            }
            _panelBtn.tooltip = panelOpen ? "Hide Project Panel" : "Show Project Panel";
        }

        // Highlights the bar while an asset drag hovers the window.
        public void SetDropHint(bool active)
        {
            EnableInClassList("bt-tabbar--drophint", active);
        }

        public void ScrollToSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _row.childCount) return;
            schedule.Execute(() => _scroll.ScrollTo(_row[_selectedIndex]));
        }

        void Rebuild()
        {
            _row.Clear();
            for (int i = 0; i < _tabs.Count; i++)
                _row.Add(BuildTab(_tabs[i], i));
            UpdateArrows();
        }

        VisualElement BuildTab(BetterTabEntry tab, int index)
        {
            VisualElement el = new VisualElement();
            el.AddToClassList("bt-tab");
            if (index == _selectedIndex) el.AddToClassList("bt-tab--active");
            el.tooltip = tab.path;

            Texture2D icon = BetterTabsWindow.GetTabIconFor(tab);
            if (icon != null)
            {
                Image img = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("bt-tab__icon");
                el.Add(img);
            }

            Label label = new Label(tab.name);
            label.AddToClassList("bt-tab__label");
            el.Add(label);

            Button close = new Button(() => TabClosed?.Invoke(IndexOfElement(el))) { text = "×" };
            close.AddToClassList("bt-tab__close");
            el.Add(close);

            el.RegisterCallback<PointerDownEvent>(evt => OnTabPointerDown(evt, el));
            el.RegisterCallback<ContextClickEvent>(evt =>
            {
                TabContextMenu?.Invoke(IndexOfElement(el));
                evt.StopPropagation();
            });
            return el;
        }

        int IndexOfElement(VisualElement el) => _row.IndexOf(el);

        // ── Drag to reorder ───────────────────────────────────────────────────

        void OnTabPointerDown(PointerDownEvent evt, VisualElement el)
        {
            if (evt.button != 0) return;
            // Let the close button consume its own click.
            if (evt.target is Button) return;

            _dragEl = el;
            _pressX = evt.position.x;
            _grabOffsetX = _row.WorldToLocal(evt.position).x - el.layout.x;
            _dragging = false;

            // Capture on the row: the tab itself may be restyled during the drag.
            _row.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void BeginDrag()
        {
            _dragging = true;
            _dragTabs = new List<VisualElement>(_row.Children());

            int count = _dragTabs.Count;
            _dragWidths = new float[count];
            _dragBaseX = new float[count];
            for (int i = 0; i < count; i++)
            {
                _dragWidths[i] = _dragTabs[i].layout.width;
                _dragBaseX[i] = _dragTabs[i].layout.x;
            }

            _dragFrom = _dragTabs.IndexOf(_dragEl);
            _dragTo = _dragFrom;

            _dragEl.AddToClassList("bt-tab--dragging");
            // The dragged tab must track the cursor with no easing.
            _dragEl.AddToClassList("bt-tab--notransition");
        }

        // Logical order right now: every tab except the dragged one, with the dragged
        // one re-inserted at _dragTo.
        List<int> BuildLogicalOrder()
        {
            List<int> order = new List<int>(_dragTabs.Count);
            for (int i = 0; i < _dragTabs.Count; i++)
                if (i != _dragFrom) order.Add(i);
            order.Insert(Mathf.Clamp(_dragTo, 0, order.Count), _dragFrom);
            return order;
        }

        void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (_dragEl == null || !_row.HasPointerCapture(evt.pointerId)) return;

            if (!_dragging)
            {
                if (Mathf.Abs(evt.position.x - _pressX) < DragThreshold) return;
                BeginDrag();
            }

            int count = _dragTabs.Count;
            float dragW = _dragWidths[_dragFrom];

            float wanted = _row.WorldToLocal(evt.position).x - _grabOffsetX;
            float totalW = 0f;
            for (int i = 0; i < count; i++) totalW += _dragWidths[i];
            wanted = Mathf.Clamp(wanted, 0f, Mathf.Max(0f, totalW - dragW));

            List<int> order = BuildLogicalOrder();
            float[] slotX = SlotPositions(order);

            // Swap against the neighbour centre using the dragged EDGES, so it reacts
            // as soon as it meaningfully overlaps instead of needing to pass the centre.
            int slot = order.IndexOf(_dragFrom);
            if (slot > 0)
            {
                int left = order[slot - 1];
                if (wanted < slotX[left] + _dragWidths[left] * 0.5f)
                {
                    _dragTo = slot - 1;
                    order = BuildLogicalOrder();
                    slotX = SlotPositions(order);
                }
            }
            if (slot < count - 1)
            {
                int right = order[slot + 1];
                if (wanted + dragW > slotX[right] + _dragWidths[right] * 0.5f)
                {
                    _dragTo = slot + 1;
                    order = BuildLogicalOrder();
                    slotX = SlotPositions(order);
                }
            }

            // Dragged tab follows the cursor; the others slide (USS transition) to the
            // slot they would occupy, leaving the gap it will drop into.
            _dragEl.style.translate = new Translate(wanted - _dragBaseX[_dragFrom], 0f);
            for (int i = 0; i < count; i++)
            {
                if (i == _dragFrom) continue;
                _dragTabs[i].style.translate = new Translate(slotX[i] - _dragBaseX[i], 0f);
            }
            evt.StopPropagation();
        }

        float[] SlotPositions(List<int> order)
        {
            float[] slotX = new float[_dragTabs.Count];
            float x = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                int idx = order[i];
                slotX[idx] = x;
                x += _dragWidths[idx];
            }
            return slotX;
        }

        void OnRowPointerUp(PointerUpEvent evt)
        {
            if (_dragEl == null) return;
            _row.ReleasePointer(evt.pointerId);

            if (!_dragging)
            {
                TabSelected?.Invoke(IndexOfElement(_dragEl));
                ClearDragState();
                evt.StopPropagation();
                return;
            }

            int from = _dragFrom;
            int to = Mathf.Clamp(_dragTo, 0, _tabs.Count - 1);

            if (to != from)
            {
                BetterTabEntry moved = _tabs[from];
                _tabs.RemoveAt(from);
                _tabs.Insert(to, moved);

                if (_selectedIndex == from) _selectedIndex = to;
                else if (from < _selectedIndex && to >= _selectedIndex) _selectedIndex--;
                else if (from > _selectedIndex && to <= _selectedIndex) _selectedIndex++;

                TabMoved?.Invoke(from, to);
            }

            ClearDragState();
            // Rebuild from the model: fresh elements carry no leftover translate, so the
            // tabs simply appear settled instead of animating into place again.
            Rebuild();
            evt.StopPropagation();
        }

        void ClearDragState()
        {
            _dragEl = null;
            _dragTabs = null;
            _dragWidths = null;
            _dragBaseX = null;
            _dragFrom = -1;
            _dragTo = -1;
            _dragging = false;
        }

        // ── Overflow arrows ───────────────────────────────────────────────────

        void ScrollBy(float delta)
        {
            _scroll.horizontalScroller.value += delta;
            UpdateArrows();
        }

        void UpdateArrows()
        {
            float content = _row.layout.width;
            float viewport = _scroll.contentViewport.layout.width;
            bool overflow = !float.IsNaN(content) && !float.IsNaN(viewport) && content > viewport + 1f;

            _leftArrow.style.display = overflow ? DisplayStyle.Flex : DisplayStyle.None;
            _rightArrow.style.display = overflow ? DisplayStyle.Flex : DisplayStyle.None;
            if (!overflow) return;

            float value = _scroll.horizontalScroller.value;
            _leftArrow.SetEnabled(value > 0.5f);
            _rightArrow.SetEnabled(value < _scroll.horizontalScroller.highValue - 0.5f);
        }
    }
}
