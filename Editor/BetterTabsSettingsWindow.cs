using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    class BetterTabsSettingsWindow : EditorWindow
    {
        [MenuItem("Window/BetterTabs/Settings")]
        public static void Open()
        {
            var w = GetWindow<BetterTabsSettingsWindow>(true, "BetterTabs — Settings");
            w.minSize = new Vector2(320, 100);
            w.maxSize = new Vector2(320, 100);
            w.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Navigation", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool invert = EditorGUILayout.Toggle("Invert Scroll Direction", BetterTabsSettings.InvertScroll);
            if (EditorGUI.EndChangeCheck())
                BetterTabsSettings.InvertScroll = invert;

            EditorGUILayout.HelpBox(
                "When enabled, scroll up moves tabs right and scroll down moves tabs left.",
                MessageType.None);
        }
    }
}
