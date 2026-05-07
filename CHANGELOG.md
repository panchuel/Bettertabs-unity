# Changelog

## [1.0.4] — 2026-05-07

### Added

- **Tab bar overflow navigation** — `‹` and `›` arrow buttons appear at the edges when tabs exceed the bar width. Buttons disable automatically when the scroll is at its limit.
- **Auto-scroll to active tab** — switching tabs (click, Shift+Scroll, Ctrl+T, Ctrl+Shift+T) automatically scrolls the tab bar to keep the selected tab visible.
- **Smooth tab bar scroll animation** — tab bar scrolling lerps to the target position instead of jumping.
- **Settings window** — **Window › Folder Tabs › Settings**. Initial option: *Invert Scroll Direction* for Shift+Scroll and Ctrl+Shift+Scroll.
- **Window menu reorganized** — **Window › Folder Tabs** is now a submenu with *Open Folder Tabs* and *Settings*. Ctrl+T is registered via Unity's ShortcutManager and no longer appears as a menu entry.

### Fixed

- Tree view now shows folders before files at every depth level (previously files appeared first).
- Grid view: file names that exceed the cell width now truncate with `…` instead of being clipped abruptly.

---

## [1.0.2] — 2026-05-05

### Added

- **Ctrl+T** — Add tab from the currently selected folder or asset in the Project panel. Fires globally via the Unity menu system; no need to focus the Folder Tabs window first.
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
