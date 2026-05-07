using UnityEditor;

namespace FolderTabs
{
    static class FolderTabsSettings
    {
        const string KeyInvertScroll = "FolderTabs.InvertScroll";

        public static bool InvertScroll
        {
            get => EditorPrefs.GetBool(KeyInvertScroll, false);
            set => EditorPrefs.SetBool(KeyInvertScroll, value);
        }
    }
}
