# Changelog

## [Unreleased]

### Changed

- **UI redesign** — one accent colour (`#5b9dd9`) now marks the active tab, the tree selection and the active view toggle, replacing the two unrelated highlight colours. Tabs are 32px with a rounded top and an accent rule, and fuse with the panel below them.
- **New toolbar** — a 42px strip under the tabs holds a clickable breadcrumb of the active tab's path, the search field (no longer hidden behind a magnifier), the grid/list segmented toggle and an overflow menu. The tab bar itself now holds only tabs and the `+` button.
- **Type-coloured icons** — folders, scenes, scripts, prefabs/GameObjects and data assets each get their own icon tint, in tabs, trees and lists. A colour assigned to a tab still wins over the type colour.
- **Close button on hover** — a tab's `×` only appears while the pointer is over that tab. Its space is reserved, so labels never shift.
- **Component cards** — the prefab/scene-object inspector shows one card per component headed by its real type name, replacing the repeated generic "Script" rows.
- **Hierarchy indentation guides** — a hairline per depth level in the hierarchy panel.
- **Asset inspector header** — icon badge tinted by type, asset name and full path, replacing the 22px title strip.
- **List view columns** — a NAME / TYPE header, 32px rows and alternating row tint. The per-row result count the header used to show is gone; the breadcrumb already says where you are.

### Added

- **Tab colours** — right-click a tab and choose **Color…** to open a swatch popup under it. The tab's icon is tinted with the chosen colour. The `×` swatch clears it. The choice is stored per tab and survives editor restarts.
- **Colour in the project panel** — a tagged folder's row is washed with its colour, fading out left to right, and every row beneath it takes a darker, fainter shade of the same one. Where tags nest, the deepest one wins.
- **Set Tab Color shortcut** — registered unbound under **Edit ▸ Shortcuts ▸ BetterTabs**; bind it to open the swatch popup on the active tab without the context menu.
- **Inspector references reveal in the project panel** — clicking an assigned object reference selects that asset in the left panel and scrolls to it, in BetterTabs' own inspectors **and in Unity's Inspector window**. Unity only pings its own Project window, which said nothing here. Works on inspectors drawn with UI Toolkit; a component with a custom IMGUI editor paints its fields inside an IMGUIContainer, where there is no element to hit-test.

### Fixed

- **Every asset kind shows the inspector Unity shows.** A source file Unity imports (`.fbx`, `.png`, `.cs`, `.shader`, audio, video…) is inspected through its **AssetImporter** — `ModelImporter` is what draws the Model/Rig/Animation/Materials tabs — while a native asset (`.mat`, `.asset`, `.unity`) is inspected through the asset itself. Only the second kind was ever built here, so a model tab came up empty. The importer's editor is now used whenever Unity has one, decided by asking for it rather than by a list of extensions, so a project's own ScriptedImporter works too.
- **Materials are no longer an empty strip.** `MaterialEditor` draws nothing at all while Unity considers the object's inspector foldout collapsed. Assets and components are marked expanded before their editor is built, as the native Inspector does.
- **Files can be dragged in from the OS file browser.** Dropping on a folder only ever called `AssetDatabase.MoveAsset`, which cannot touch a path outside the project, so an external file silently failed. External files are now copied in and imported the way Unity's own Project window does it, and the grid/list view accepts drops at all — it had no drag handling before.
- **Column widths survive a restart in prefab and scene-object tabs.** Closing Unity while any other tab was active read the width of the hidden hierarchy pane, which measures zero, and saved the minimum over the real value. Both splitters now track their last real width instead of asking a hidden panel.
- **Tab bar scrolling lerps again.** The animation from 1.0.5 was lost in the UI Toolkit migration; the active tab now glides into view instead of snapping, and so do the overflow arrows.

---

## [1.0.7] — 2026-05-13

### Added

- **How to Use window** — **Window › BetterTabs › How to Use** opens a scrollable reference panel covering all ways to add tabs (drag-drop, `+` button, Ctrl+T), close and restore them (Ctrl+W, Ctrl+Shift+T), navigate and reorder (Shift+Scroll, Ctrl+Shift+Scroll, overflow arrows), browse content (search, grid/list toggle, context menus), and pin non-folder assets.

---

## [1.0.6] — 2026-05-07

### Added

- **Rebranded to BetterTabs** — package name, namespace, menu paths, window title, and EditorPrefs keys updated throughout. Existing sessions migrate automatically on first open.
- **`+` button accepts any asset** — the add button now enables for any selected asset in the Project panel, not just folders. Non-folder assets open as pinned asset tabs.

### Fixed

- Non-folder asset tabs (scripts, materials, prefabs, etc.) now persist correctly across editor restarts. Previously only folder tabs were restored on load.

---

## [1.0.5] — 2026-05-07

### Added

- **Tab bar overflow navigation** — `‹` and `›` arrow buttons appear at the edges when tabs exceed the bar width. Buttons disable automatically when the scroll is at its limit.
- **Auto-scroll to active tab** — switching tabs (click, Shift+Scroll, Ctrl+T, Ctrl+Shift+T) automatically scrolls the tab bar to keep the selected tab visible.
- **Smooth tab bar scroll animation** — tab bar scrolling lerps to the target position instead of jumping.
- **Settings window** — **Window › BetterTabs › Settings**. Initial option: *Invert Scroll Direction* for Shift+Scroll and Ctrl+Shift+Scroll.
- **Window menu reorganized** — **Window › BetterTabs** is now a submenu with *Open BetterTabs* and *Settings*. Ctrl+T is registered via Unity's ShortcutManager and no longer appears as a menu entry.

### Fixed

- Tree view now shows folders before files at every depth level (previously files appeared first).
- Grid view: file names that exceed the cell width now truncate with `…` instead of being clipped abruptly.

---

## [1.0.2] — 2026-05-05

### Added

- **Ctrl+T** — Add tab from the currently selected folder or asset in the Project panel. Fires globally via the Unity menu system; no need to focus the BetterTabs window first.
- **Ctrl+W** — Close the active tab.
- **Ctrl+Shift+T** — Reopen the last closed tab. Skips tabs that are already open.
- **Shift+Scroll** — Cycle through tabs (wraps around).
- **Ctrl+Shift+Scroll** — Move the active tab left or right (wraps around).

---

## [1.0.1] — 2025-04-01

### Added

- Asset tabs — pin any non-folder asset (prefab, material, scene, etc.) as a tab with a preview, type label, and Open / Show in Project / Reveal in Explorer actions.
- Grid view now shows folders first, then files.
- Inspector sync on asset click.

### Fixed

- Grid view was hiding folders when the icon size toggle was active.

---

## [1.0.0] — 2025-03-15

### Added

- Initial release.
- Dockable editor window with draggable, reorderable folder tabs.
- Tree view with expand/collapse, child count badges, and type labels.
- Grid view with 64×64 asset previews.
- Scoped search with debounce and result highlighting.
- Full context menus: Open, Reveal in Explorer, Rename, Duplicate, Delete, Copy Path, Select Dependencies, Properties.
- Inline rename (F2 / double-click).
- Create asset submenu: Folder, C# Script, Scene, Material.
- Bidirectional drag-and-drop with the native Project panel.
- Auto-refresh via AssetPostprocessor.
- Per-tab state persistence (expanded paths, search query) across sessions.
- Custom window icon loaded from the package's `Editor/Icons/icon.png`.
