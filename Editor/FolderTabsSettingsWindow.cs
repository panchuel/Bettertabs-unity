using UnityEditor;
using UnityEngine;

namespace FolderTabs
{
    class FolderTabsSettingsWindow : EditorWindow
    {
        [MenuItem("Window/Folder Tabs/Settings")]
        public static void Open()
        {
            var w = GetWindow<FolderTabsSettingsWindow>(true, "Folder Tabs — Settings");
            w.minSize = new Vector2(320, 100);
            w.maxSize = new Vector2(320, 100);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Navigation", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool invert = EditorGUILayout.Toggle("Invert Scroll Direction", FolderTabsSettings.InvertScroll);
            if (EditorGUI.EndChangeCheck())
                FolderTabsSettings.InvertScroll = invert;

            EditorGUILayout.HelpBox(
                "When enabled, scroll up moves tabs right and scroll down moves tabs left.",
                MessageType.None);
        }
    }
}
