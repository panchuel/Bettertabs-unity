using UnityEditor;

namespace BetterTabs
{
    static class BetterTabsSettings
    {
        const string KeyInvertScroll = "BetterTabs.InvertScroll";

        public static bool InvertScroll
        {
            get => EditorPrefs.GetBool(KeyInvertScroll, false);
            set => EditorPrefs.SetBool(KeyInvertScroll, value);
        }
    }
}
