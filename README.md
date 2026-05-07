# Folder Tabs

A dockable **Project panel replacement** for Unity that lets you pin folders as tabs and work without constantly navigating the full asset tree.

![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)
![Version](https://img.shields.io/badge/version-1.0.2-blue)
![Editor Only](https://img.shields.io/badge/scope-Editor%20only-orange)

---

## Why Folder Tabs?

The Unity Project panel is great for exploration — not for focus.  
When you're iterating on a feature, you don't need the entire asset tree: you need **the three folders you're actually working in**, instantly reachable.

Folder Tabs keeps those folders one click away, with the same interactions you already know from the native Project panel.

---

## Features

| | |
|---|---|
| **Pinned tabs** | Drag folders or assets in, reorder by dragging, close with `×` |
| **Tree view** | Expandable folders with child count, icons, and type labels |
| **Grid view** | Asset previews at 64×64, toggle in the toolbar |
| **Scoped search** | Search is contained to the active tab's folder |
| **Full context menu** | Open · Reveal · Rename · Duplicate · Delete · Copy Path · Properties |
| **Inline rename** | Click Rename or press F2 — edit in place, Enter confirms |
| **Create assets** | `+` button or right-click any folder to create inside it |
| **Inspector sync** | Clicking an asset shows it in the Inspector automatically |
| **Drag and drop** | Drag out to any Unity panel · Drop in to Move or Copy |
| **Auto-refresh** | Detects file system changes without manual refresh |
| **Browser shortcuts** | Ctrl+T / Ctrl+W / Ctrl+Shift+T + scroll navigation |
| **Session persistence** | Tabs, expanded paths, and search query survive editor restarts |

---

## Installation

1. Download `FolderTabs.unitypackage` from the Unity Asset Store or the [releases page](https://github.com/panchuel/foldertabs-unity/releases).
2. In Unity, go to **Assets › Import Package › Custom Package**.
3. Select the downloaded file and click **Import**.

No additional setup is required. The tool is ready to use after import.

### Via Git URL (optional)

1. Open **Window › Package Manager**.
2. Click **`+`** → **Add package from git URL**.
3. Paste:
   ```
   https://github.com/panchuel/foldertabs-unity.git#v1.0.2
   ```

---

## Quick Start

1. Open the window: **Window › Folder Tabs**.
2. Dock it anywhere — next to the Project panel, the Inspector, or as a floating window.
3. Drag any folder from the native Project panel into the Folder Tabs window.
4. The folder opens as a tab. Drag more folders to add more tabs.
5. Click a tab to switch, drag tabs to reorder them.

---

## Usage

### Adding tabs

- **Drag and drop** — drag any folder from the Project panel directly into the window.
- **Ctrl+T** — pins the folder currently selected in the Project panel as a new tab (works without focusing the Folder Tabs window first).
- **`+` button** — opens a folder picker dialog.

### Browsing assets

- **Tree view** — default layout. Folders expand inline. Child count is shown next to each folder name.
- **Grid view** — toggle with the grid icon in the toolbar. Shows asset previews at 64×64. Folders appear first, then files.
- **Scoped search** — type in the search bar to filter assets within the active tab's folder only.

### Managing tabs

| Action | How |
|---|---|
| Close a tab | Click `×` on the tab, or press `Ctrl+W` |
| Reorder tabs | Drag a tab left or right |
| Reopen last closed tab | `Ctrl+Shift+T` |
| Cycle through tabs | `Shift + Scroll` |
| Move active tab left / right | `Ctrl+Shift + Scroll` |

### Working with assets

Right-click any asset or folder to open the context menu:

- **Open** — opens the asset in its default editor.
- **Reveal in Explorer / Finder** — opens the file in the OS file browser.
- **Rename** — edits the name inline. Press `Enter` to confirm, `Escape` to cancel.
- **Duplicate** — creates a copy in the same folder.
- **Delete** — moves the asset to the Unity recycle bin (undoable via Edit › Undo).
- **Copy Path** — copies the full `Assets/...` path to the clipboard.
- **Select Dependencies** — selects all assets this file depends on in the native Project panel.
- **Properties** — opens the asset in the Inspector.

---

## Keyboard Shortcuts

### Tab management

| Shortcut | Action |
|---|---|
| `Ctrl+T` | Add tab from current Project selection (works globally) |
| `Ctrl+W` | Close the active tab |
| `Ctrl+Shift+T` | Reopen the last closed tab |

### Navigation

| Shortcut | Action |
|---|---|
| `Shift + Scroll` | Cycle through tabs |
| `Ctrl+Shift + Scroll` | Move the active tab left / right |

### Asset interaction

| Shortcut | Action |
|---|---|
| `Enter` | Open selected asset |
| `F2` | Rename selected asset |
| `Delete` | Delete selected asset |
| `Escape` | Cancel rename |

> **macOS:** Replace `Ctrl` with `Cmd` for all shortcuts.

---

## Platform Compatibility

| Platform | Status |
|---|---|
| Windows | Fully supported |
| macOS | Fully supported |
| Linux | Not officially tested |

This is an **Editor-only** tool. It has no effect on builds and adds no runtime overhead.

---

## Known Limitations

- **Editor-only** — not available at runtime.
- **Tab state is stored in EditorPrefs.** If EditorPrefs are cleared, all tabs will be lost. This does not affect project assets.
- **Ctrl+T may conflict** with other editor tools that register the same global shortcut. If it does not respond, check for conflicts in other installed packages.
- **Linux support is untested.** The tool should work but has not been verified on Linux editors.

---

## Troubleshooting

**Tabs disappeared after reopening Unity.**  
This happens if EditorPrefs were cleared or the Unity editor cache was reset. The folders themselves are untouched — simply re-add them by dragging from the Project panel.

**Ctrl+T does not open a new tab.**  
Another installed package may have registered the same shortcut. You can still add tabs by dragging folders or using the `+` button.

**Assets are not showing up in the tab.**  
Click the **Refresh** button in the toolbar, or right-click anywhere in the panel and select **Refresh**. This forces a rescan of the folder.

**Drag and drop is not working.**  
Make sure you are dragging from the **native Unity Project panel** (not from the OS file explorer). Dragging from the OS file manager into the window is not supported.

**The window appears empty after docking.**  
Resize the window slightly to trigger a repaint, or close and reopen it via **Window › Folder Tabs**.

---

## Requirements

- Unity **2021.3 or later**
- No third-party dependencies

---

## Support

Found a bug or have a feature request?  
Open an issue at [github.com/panchuel/foldertabs-unity/issues](https://github.com/panchuel/foldertabs-unity/issues).
