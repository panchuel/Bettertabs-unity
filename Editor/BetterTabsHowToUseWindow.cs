using UnityEditor;
using UnityEngine;

namespace BetterTabs
{
    class BetterTabsHowToUseWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Window/BetterTabs/How to Use")]
        public static void Open()
        {
            var w = GetWindow<BetterTabsHowToUseWindow>(true, "BetterTabs — How to Use");
            w.minSize = new Vector2(420, 480);
            w.Show();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(10);

            Section("Adding Tabs");
            Row("Drag a folder or asset",        "Drop it anywhere onto the BetterTabs window");
            Row("+ button",                       "Select a folder in the Project window, then click +");
            Row("Ctrl + T",                       "Add tab from the current Project selection");

            Space();
            Section("Closing Tabs");
            Row("× button",                       "Hover a tab to reveal its ×, then click to close");
            Row("Ctrl + W",                       "Close the active tab");
            Row("Right-click tab → Close Others", "Close every tab except the one you right-clicked");

            Space();
            Section("Restoring Tabs");
            Row("Ctrl + Shift + T",               "Reopen the last closed tab (stackable)");

            Space();
            Section("Navigating Tabs");
            Row("Left-click tab",                 "Switch to that tab");
            Row("Shift + Scroll Wheel",           "Cycle through tabs");
            Row("‹  ›  arrows",                   "Scroll the tab bar when tabs overflow");

            Space();
            Section("Reordering Tabs");
            Row("Drag tab",                       "Click and drag a tab left or right to reorder");
            Row("Ctrl + Shift + Scroll Wheel",    "Move the active tab one position left or right");

            Space();
            Section("Colouring Tabs");
            Row("Right-click tab -> Color...",    "Opens a swatch popup and tints the tab icon");
            Row("Project panel",                  "The tagged folder's row is washed with the colour, its subtree darker");
            Row("x swatch",                       "Clears the colour and restores the default look");
            Row("Edit -> Shortcuts",              "Bind 'BetterTabs/Set Tab Color' to open the popup on the active tab");

            Space();
            Section("Browsing Content");
            Row("Breadcrumb",                     "Click any segment of the path to open that folder as a tab");
            Row("Search bar",                     "Searches the whole project; Unity filter syntax works (t:, l:)");
            Row("Escape in search bar",           "Clear the current search");
            Row("Grid / List toggle",             "Switch between grid and list view (folder tabs only)");
            Row("Panel button (far left)",        "Show or hide the project panel");
            Row("⋯ overflow menu",                "Unity Search, Settings, How to Use");
            Row("Double-click folder",            "Open that folder as a new tab (grid view)");
            Row("Right-click asset",              "Context menu: Open, Show in Project, Rename, Delete…");

            Space();
            Section("Asset Pinning");
            Row("Pin any asset",                  "Drag a non-folder asset onto the window to pin it");
            Row("Pinned asset view",              "Shows a preview with Open / Show in Project / Reveal in Explorer buttons");
            Row("Click a reference",               "Selects that asset in the left panel - here or in Unity's Inspector");

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        static void Section(string title)
        {
            var sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = EditorStyles.boldLabel.fontSize + 2
            };
            EditorGUILayout.LabelField(title, sectionStyle);
            var r = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(r.x, r.yMax, r.width, 1), new Color(0.4f, 0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(4);
        }

        static void Row(string shortcut, string description)
        {
            int size = EditorStyles.miniLabel.fontSize + 2;

            var keyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = size
            };
            var descStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = size,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(shortcut, keyStyle, GUILayout.Width(200));
            EditorGUILayout.LabelField(description, descStyle);
            EditorGUILayout.EndHorizontal();
        }

        static void Space() => EditorGUILayout.Space(10);
    }
}
