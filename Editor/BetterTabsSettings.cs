using UnityEditor;

namespace BetterTabs
{
    static class BetterTabsSettings
    {
        const string KeyInvertScroll = "BetterTabs.InvertScroll";
        private const string KEY_SHOW_DRAG_PREVIEW = "BetterTabs.ShowDragPreview";
        // Per machine, not per project: the guide greets a person once, not every project.
        private const string KEY_HAS_SEEN_HELP = "BetterTabs.HasSeenHelp";

        public static bool InvertScroll
        {
            get => EditorPrefs.GetBool(KeyInvertScroll, false);
            set => EditorPrefs.SetBool(KeyInvertScroll, value);
        }

        public static bool ShowDragPreview
        {
            get { return EditorPrefs.GetBool(KEY_SHOW_DRAG_PREVIEW, true); }
            set { EditorPrefs.SetBool(KEY_SHOW_DRAG_PREVIEW, value); }
        }

        public static bool HasSeenHelp
        {
            get { return EditorPrefs.GetBool(KEY_HAS_SEEN_HELP, false); }
            set { EditorPrefs.SetBool(KEY_HAS_SEEN_HELP, value); }
        }
    }
}
