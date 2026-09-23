using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BetterTabs
{
    // Clicking an object reference in an inspector pings it in Unity's own Project
    // window, which says nothing inside this tool. The click is mirrored onto the left
    // panel instead, so the referenced asset lands where the user is already looking.
    internal static class BetterInspectorLinks
    {
        // Unity's own class for the picker button at the right of an ObjectField. Used
        // as a literal because the matching constant is not public on every version.
        const string SelectorClass = "unity-object-field__selector";

        // Only reaches UI Toolkit inspectors. A component drawn by a legacy IMGUI
        // editor paints its object fields inside an IMGUIContainer, where there is no
        // element to hit-test against and the click never surfaces as an event here.
        //
        // Returns the registered callback so a caller that attaches to a tree it does
        // not own — Unity's own Inspector — can detach again.
        public static EventCallback<PointerDownEvent> MirrorObjectFieldClicks(
            VisualElement host, Action<string> onAssetClicked)
        {
            if (host == null || onAssetClicked == null) return null;

            // TrickleDown and no StopPropagation: the field still pings, drags and
            // opens on double click exactly as before. This only observes.
            EventCallback<PointerDownEvent> callback = evt =>
            {
                if (evt.button != 0) return;

                VisualElement target = evt.target as VisualElement;
                ObjectField field = FindField(target, host);
                if (field == null) return;

                // The picker button opens the selector; only the reference itself counts.
                if (IsInsideSelector(target, field)) return;

                Object value = field.value;
                if (value == null) return;

                // A scene object has no asset path and cannot be shown in a folder tree.
                string path = AssetDatabase.GetAssetPath(value);
                if (string.IsNullOrEmpty(path)) return;

                onAssetClicked(path);
            };

            host.RegisterCallback(callback, TrickleDown.TrickleDown);
            return callback;
        }

        static ObjectField FindField(VisualElement from, VisualElement host)
        {
            for (VisualElement el = from; el != null && el != host; el = el.parent)
            {
                if (el is ObjectField field) return field;
            }
            return null;
        }

        static bool IsInsideSelector(VisualElement from, ObjectField field)
        {
            for (VisualElement el = from; el != null && el != field; el = el.parent)
            {
                if (el.ClassListContains(SelectorClass)) return true;
            }
            return false;
        }
    }
}
