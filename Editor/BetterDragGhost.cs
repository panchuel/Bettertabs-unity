namespace BetterTabs
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    // Keeps a BetterDragGhostView next to the cursor during any editor drag and drop:
    // one started in BetterTabs, in the Hierarchy or Project window, or a file dragged
    // in from the OS. Every editor window lives in a UI Toolkit panel, IMGUI ones too
    // (their OnGUI runs in an IMGUIContainer inside it), so a TrickleDown callback on
    // each panel's root sees every drag event before the window handles it, and the
    // chip can be laid over whichever window the cursor is in.
    [InitializeOnLoad]
    internal static class BetterDragGhost
    {
        // Unity's fixed view-data key for an EditorWindow's own root inside its panel.
        // Unlike the element name it is never made unique, so it is safe to match.
        private const string WINDOW_ROOT_KEY = "rootVisualContainer";
        private const double HOOK_SCAN_INTERVAL = 2.0;
        // A drop outside every window sends no event. Once drag events stop for this
        // long and the cursor is over no window, the drag is taken as finished.
        private const double STALE_DRAG_SECONDS = 0.3;
        private const float CURSOR_OFFSET_X = 14f;
        private const float CURSOR_OFFSET_Y = 12f;
        private const float EDGE_MARGIN = 4f;
        private const int LEFT_BUTTON_MASK = 1;

        private static readonly List<VisualElement> hookedTrees = new List<VisualElement>();

        private static BetterDragGhostView view;
        private static VisualElement host;
        private static Object payloadFirstObject;
        private static string payloadFirstPath = string.Empty;
        private static int payloadCount;
        private static double nextScanTime;
        private static double lastDragEventTime;

        static BetterDragGhost()
        {
            EditorApplication.update += OnEditorUpdated;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static bool IsVisible()
        {
            return host != null;
        }

        private static void OnBeforeAssemblyReload()
        {
            Hide();
            for (int i = 0; i < hookedTrees.Count; i++)
            {
                Unhook(hookedTrees[i]);
            }
            hookedTrees.Clear();

            EditorApplication.update -= OnEditorUpdated;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private static void OnEditorUpdated()
        {
            double now = EditorApplication.timeSinceStartup;

            if (IsVisible())
            {
                // The docked tab changed or the window closed under the chip.
                bool hostGone = host.panel == null;
                bool droppedOutside = now - lastDragEventTime > STALE_DRAG_SECONDS
                    && EditorWindow.mouseOverWindow == null;
                if (hostGone || droppedOutside)
                {
                    Hide();
                }
            }

            if (now >= nextScanTime)
            {
                nextScanTime = now + HOOK_SCAN_INTERVAL;
                HookOpenWindows();
            }
        }

        // ── Panel hooks ───────────────────────────────────────────────────────

        private static void HookOpenWindows()
        {
            for (int i = hookedTrees.Count - 1; i >= 0; i--)
            {
                if (hookedTrees[i].panel == null)
                {
                    Unhook(hookedTrees[i]);
                    hookedTrees.RemoveAt(i);
                }
            }

            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] == null)
                {
                    continue;
                }

                IPanel panel = windows[i].rootVisualElement.panel;
                if (panel == null || hookedTrees.Contains(panel.visualTree))
                {
                    continue;
                }

                Hook(panel.visualTree);
                hookedTrees.Add(panel.visualTree);
            }
        }

        private static void Hook(VisualElement tree)
        {
            tree.RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
            tree.RegisterCallback<DragPerformEvent>(OnDragPerformed, TrickleDown.TrickleDown);
            tree.RegisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
            tree.RegisterCallback<PointerMoveEvent>(OnPointerMoved, TrickleDown.TrickleDown);
        }

        private static void Unhook(VisualElement tree)
        {
            tree.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
            tree.UnregisterCallback<DragPerformEvent>(OnDragPerformed, TrickleDown.TrickleDown);
            tree.UnregisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
            tree.UnregisterCallback<PointerMoveEvent>(OnPointerMoved, TrickleDown.TrickleDown);
        }

        // A docked area shares one panel between its tabs; only the visible tab's root
        // is attached to it, so the root is looked up per event rather than cached.
        private static VisualElement FindWindowRoot(VisualElement tree)
        {
            for (int i = tree.hierarchy.childCount - 1; i >= 0; i--)
            {
                VisualElement child = tree.hierarchy[i];
                if (child.viewDataKey == WINDOW_ROOT_KEY)
                {
                    return child;
                }
            }
            return null;
        }

        // ── Drag events ───────────────────────────────────────────────────────

        private static void OnDragUpdated(DragUpdatedEvent evt)
        {
            lastDragEventTime = EditorApplication.timeSinceStartup;

            if (!IsVisible())
            {
                if (!BetterTabsSettings.ShowDragPreview)
                {
                    return;
                }
                // A window opened since the last scan would otherwise miss this drag.
                HookOpenWindows();
            }

            VisualElement windowRoot = FindWindowRoot(evt.currentTarget as VisualElement);
            if (windowRoot == null || !TryRefreshPayload())
            {
                Hide();
                return;
            }

            if (view.parent != windowRoot)
            {
                windowRoot.Add(view);
            }
            else if (windowRoot.IndexOf(view) != windowRoot.childCount - 1)
            {
                view.BringToFront();
            }

            host = windowRoot;
            Place(windowRoot.WorldToLocal(evt.mousePosition));
        }

        private static void OnDragPerformed(DragPerformEvent evt)
        {
            Hide();
        }

        // Sent where the drag leaves or ends. Only the window holding the chip may hide
        // it: the next window's DragUpdated can arrive before this one.
        private static void OnDragExited(DragExitedEvent evt)
        {
            if (IsVisible() && host.panel != null && host.panel.visualTree == evt.currentTarget)
            {
                Hide();
            }
        }

        // Plain pointer moves only reach a window once no button is held, so the drag
        // is over even if its end was never reported.
        private static void OnPointerMoved(PointerMoveEvent evt)
        {
            if (IsVisible() && (evt.pressedButtons & LEFT_BUTTON_MASK) == 0)
            {
                Hide();
            }
        }

        // ── Chip ──────────────────────────────────────────────────────────────

        private static bool TryRefreshPayload()
        {
            Object[] objects = DragAndDrop.objectReferences;
            string[] paths = DragAndDrop.paths;
            if (objects == null)
            {
                objects = new Object[0];
            }
            if (paths == null)
            {
                paths = new string[0];
            }
            if (objects.Length == 0 && paths.Length == 0)
            {
                return false;
            }

            Object firstObject = objects.Length > 0 ? objects[0] : null;
            string firstPath = paths.Length > 0 ? paths[0] : string.Empty;
            int total = Mathf.Max(objects.Length, paths.Length);
            if (view != null && firstObject == payloadFirstObject && firstPath == payloadFirstPath && total == payloadCount)
            {
                return true;
            }

            if (view == null)
            {
                view = new BetterDragGhostView(BetterTabsWindow.LoadStyleSheet());
            }
            view.SetPayload(objects, paths);
            payloadFirstObject = firstObject;
            payloadFirstPath = firstPath;
            payloadCount = total;
            return true;
        }

        // Below-right of the cursor, flipped to the other side near the window's edge.
        // The size is last frame's layout, which is empty on the first event: the flip
        // then waits one event, which is not noticeable.
        private static void Place(Vector2 cursor)
        {
            float width = float.IsNaN(view.layout.width) ? 0f : view.layout.width;
            float height = float.IsNaN(view.layout.height) ? 0f : view.layout.height;

            float x = cursor.x + CURSOR_OFFSET_X;
            if (x + width > host.layout.width - EDGE_MARGIN)
            {
                x = cursor.x - CURSOR_OFFSET_X - width;
            }

            float y = cursor.y + CURSOR_OFFSET_Y;
            if (y + height > host.layout.height - EDGE_MARGIN)
            {
                y = cursor.y - CURSOR_OFFSET_Y - height;
            }

            view.style.left = x;
            view.style.top = y;
        }

        // Taken out of the window rather than hidden in place, so an IMGUI window is
        // left exactly as Unity built it between drags.
        private static void Hide()
        {
            if (view != null)
            {
                view.RemoveFromHierarchy();
            }
            host = null;
            payloadFirstObject = null;
            payloadFirstPath = string.Empty;
            payloadCount = 0;
        }
    }
}
