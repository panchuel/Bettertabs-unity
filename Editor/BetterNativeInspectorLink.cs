using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterTabs
{
    // Mirrors reference clicks made in Unity's OWN Inspector, not just in this tool's.
    // Those windows belong to the editor, so the hook is deliberately minimal: a
    // read-only observer on the window root that never consumes an event, attached
    // while BetterTabs is open and removed when it closes.
    internal static class BetterNativeInspectorLink
    {
        // A window root outlives the inspector contents it rebuilds, but not a domain
        // reload or a newly opened Properties window, so the scan repeats.
        const double RescanSeconds = 1.0;

        class Hook
        {
            public EditorWindow Window;
            public VisualElement Root;
            public EventCallback<PointerDownEvent> Callback;
        }

        static readonly List<Hook> Hooks = new List<Hook>();
        static Action<string> s_onAssetClicked;
        static double s_nextScan;

        public static void Enable(Action<string> onAssetClicked)
        {
            s_onAssetClicked = onAssetClicked;
            s_nextScan = 0d;
            Scan();
        }

        public static void Disable()
        {
            foreach (Hook hook in Hooks)
            {
                if (hook.Root != null && hook.Callback != null)
                    hook.Root.UnregisterCallback(hook.Callback, TrickleDown.TrickleDown);
            }
            Hooks.Clear();
            s_onAssetClicked = null;
        }

        // Cheap enough to call from the editor update loop; the scan itself is throttled.
        public static void Tick()
        {
            if (s_onAssetClicked == null) return;
            if (EditorApplication.timeSinceStartup < s_nextScan) return;
            s_nextScan = EditorApplication.timeSinceStartup + RescanSeconds;
            Scan();
        }

        static void Scan()
        {
            // Drop hooks whose window or root is gone, so a reload does not leave the
            // list holding destroyed objects.
            for (int i = Hooks.Count - 1; i >= 0; i--)
            {
                Hook hook = Hooks[i];
                if (hook.Window == null || hook.Root == null || hook.Root.panel == null)
                    Hooks.RemoveAt(i);
            }

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || !IsInspector(window)) continue;
                if (IndexOf(window) >= 0) continue;

                VisualElement root = window.rootVisualElement;
                if (root == null) continue;

                EventCallback<PointerDownEvent> callback =
                    BetterInspectorLinks.MirrorObjectFieldClicks(root, OnAssetClicked);
                if (callback == null) continue;

                Hooks.Add(new Hook { Window = window, Root = root, Callback = callback });
            }
        }

        // Matched by name rather than by type reference: both classes are internal to
        // UnityEditor, and PropertyEditor is also the base of the floating Properties
        // windows, which show the same object fields.
        static bool IsInspector(EditorWindow window)
        {
            string name = window.GetType().Name;
            return name == "InspectorWindow" || name == "PropertyEditor";
        }

        static int IndexOf(EditorWindow window)
        {
            for (int i = 0; i < Hooks.Count; i++)
                if (Hooks[i].Window == window) return i;
            return -1;
        }

        // Running inside someone else's window: a throw here would break their event
        // handling, so nothing is allowed to escape.
        static void OnAssetClicked(string path)
        {
            try
            {
                s_onAssetClicked?.Invoke(path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
