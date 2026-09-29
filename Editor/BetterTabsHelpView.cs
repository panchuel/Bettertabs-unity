using UnityEngine.UIElements;

namespace BetterTabs
{
    // The "How to Use" page, shown inside the BetterTabs window (the ? at the bottom
    // right) rather than as a window of its own. It opens by itself the first time
    // someone opens BetterTabs.
    internal class BetterTabsHelpView : VisualElement
    {
        public BetterTabsHelpView()
        {
            AddToClassList("bt-page");

            Section("Adding Tabs");
            Row("Drag a folder or asset",         "Drop it anywhere onto the BetterTabs window");
            Row("Drag files from Explorer",        "Drop them on a folder to import them into the project");
            Row("+ button",                        "Select a folder in the Project window, then click +");
            Row("Ctrl + T",                        "Add tab from the current Project selection");

            Section("Closing Tabs");
            Row("× button",                        "Hover a tab to reveal its ×, then click to close");
            Row("Ctrl + W",                        "Close the active tab");
            Row("Right-click tab → Close Others",  "Close every tab except the one you right-clicked");

            Section("Restoring Tabs");
            Row("Ctrl + Shift + T",                "Reopen the last closed tab (stackable)");

            Section("Navigating Tabs");
            Row("Left-click tab",                  "Switch to that tab");
            Row("Shift + Scroll Wheel",            "Cycle through tabs");
            Row("‹  ›  arrows",                    "Scroll the tab bar when tabs overflow");

            Section("Reordering Tabs");
            Row("Drag tab",                        "Click and drag a tab left or right to reorder");
            Row("Ctrl + Shift + Scroll Wheel",     "Move the active tab one position left or right");

            Section("Colouring Tabs");
            Row("Right-click tab → Color…",        "Opens a swatch popup and tints the tab icon");
            Row("Project panel",                   "The tagged folder's row is washed with the colour, its subtree darker");
            Row("× swatch",                        "Clears the colour and restores the default look");
            Row("Edit → Shortcuts",                "Bind 'BetterTabs/Set Tab Color' to open the popup on the active tab");

            Section("Browsing Content");
            Row("Breadcrumb",                      "Click any segment of the path to open that folder as a tab");
            Row("Search bar",                      "Searches the whole project; Unity filter syntax works (t:, l:)");
            Row("Escape in search bar",            "Clear the current search");
            Row("Grid / List toggle",              "Switch between grid and list view (folder tabs only)");
            Row("Panel button (far left)",         "Show or hide the project panel");
            Row("⋯ overflow menu",                 "Open Unity Search");
            Row("?  and  ⚙  (bottom right)",       "This guide and the BetterTabs settings");
            Row("Double-click folder",             "Open that folder as a new tab");
            Row("Double-click asset",              "Open it in its editor; a prefab enters Prefab Mode");
            Row("Expand an asset",                 "Shows its sub-assets (sprites, meshes, clips…), as the Project window does");
            Row("Right-click asset",               "Context menu: Open, Show in Project, Rename, Delete…");

            Section("Moving Assets");
            Row("Ctrl + C / Ctrl + X",             "Copy or cut the selected assets");
            Row("Ctrl + V",                        "Paste into the selected folder, or next to the selected file");
            Row("Ctrl + D",                        "Duplicate the selected assets");
            Row("Drag from the Hierarchy",         "Drop a scene object on a folder to create a prefab");

            Section("Prefab Tabs");
            Row("Save / Ctrl + S",                 "Write the prefab's changes (a • on the tab means unsaved)");
            Row("Leave or close the tab",          "Pending changes are saved automatically");

            Section("Asset Pinning");
            Row("Pin any asset",                   "Drag a non-folder asset onto the window to pin it");
            Row("Pinned asset view",               "Shows the asset's inspector and preview; import settings get Apply / Revert");
            Row("Click a reference",               "Selects that asset in the left panel - here or in Unity's Inspector");
        }

        void Section(string title)
        {
            // USS has no text-transform, so the label is upper-cased here.
            Label label = new Label(title.ToUpperInvariant());
            label.AddToClassList("bt-page__section");
            Add(label);
        }

        void Row(string shortcut, string description)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("bt-page__row");

            Label key = new Label(shortcut);
            key.AddToClassList("bt-page__key");
            row.Add(key);

            Label text = new Label(description);
            text.AddToClassList("bt-page__desc");
            row.Add(text);

            Add(row);
        }
    }
}
