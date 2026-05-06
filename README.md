# 📁 Folder Tabs

A dockable **Project panel replacement** for Unity that lets you pin folders as
tabs and work without constantly navigating the full asset tree.

![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)
![Version](https://img.shields.io/badge/version-1.0.2-blue)

---

## Why Folder Tabs?

The Unity Project panel is great for exploration — not for focus.  
When you're iterating on a feature, you don't need the entire asset tree:
you need **the three folders you're actually working in**, instantly reachable.

Folder Tabs keeps those folders one click away, with the same interactions
you already know from the native Project panel.

---

## Installation

### Via Git URL (recommended)

1. Open **Window › Package Manager**
2. Click **`+`** → **Add package from git URL**
3. Paste:
   ```
   https://github.com/panchuel/foldertabs-unity.git
   ```

### Via .unitypackage

Download `FolderTabs-v1.0.2.unitypackage` from the
[latest release](https://github.com/panchuel/foldertabs-unity/releases)
and double-click to import.

---

## Usage

1. Open the window: **Window › Folder Tabs**
2. Drag any folder from the Project panel into the window
3. The folder opens as a tab — drag more to add more

---

## Features

| | |
|---|---|
| 📌 **Pinned tabs** | Drag folders or assets in, reorder by dragging, close with `×` |
| 🌲 **Tree view** | Expandable folders with child count, icons, and type labels |
| ⊞ **Grid view** | Asset previews at 64×64, toggle in the toolbar |
| 🔍 **Scoped search** | Search is contained to the active tab's folder |
| 🖱️ **Full context menu** | Open · Reveal · Rename · Duplicate · Delete · Copy Path · Properties |
| ✏️ **Inline rename** | Click Rename or press F2 — edit in place, Enter confirms |
| ➕ **Create assets** | `+` button or right-click any folder to create inside it |
| 🔗 **Inspector sync** | Clicking an asset shows it in the Inspector automatically |
| ↕️ **Drag and drop** | Drag out to any Unity panel · Drop in to Move or Copy |
| 🔄 **Auto-refresh** | Detects file system changes without manual refresh |
| ⌨️ **Browser shortcuts** | Ctrl+T / Ctrl+W / Ctrl+Shift+T + scroll navigation |

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

---

## Requirements

- Unity **2021.3 or later**
- No third-party dependencies
