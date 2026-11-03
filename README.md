# Favorites Bar for Unity

A free, open-source favorites bar for the Unity Editor. Hold **Alt** over the Project window and your favorite assets, folders and scene objects appear right there. Release Alt and the bar disappears again, so it takes up no screen space.

## Install

**Window > Package Manager > + > Install package from git URL…**

```
https://github.com/Raptor2012/unity-favorites-bar.git
```

Pin a version by adding a tag: `https://github.com/Raptor2012/unity-favorites-bar.git#v1.1.0`.

Requires Unity 2021.3 or newer. It's an editor-only package and adds nothing to builds.

## Use

| Do this | What happens |
| --- | --- |
| Hold **Alt** with the mouse over the Project window | The bar opens on top of the Project window |
| Release **Alt** | The bar closes (unless locked with the lock button) |
| **Window > Favorites Bar** | A dockable bar that stays open |
| Drag assets, folders or scene objects onto the bar | They're added where you drop them |
| **+** button in the toolbar | Adds the current selection |
| Click | Selects and pings the item; a folder opens in the Project window |
| Double-click / **Enter** | Opens the asset, or frames the scene object in the Scene view |
| Drag an item | Reorders it, or drop it into the scene or an inspector field. Hover a page tab while dragging to move it to that page |
| Right-click an item | Open, Select, Move To page, Remove |
| **Delete** / **Backspace** | Removes the highlighted item |
| **Up / Down** | Moves the highlight |
| New-page button | Adds a page |
| Double-click a page tab, or right-click > Rename | Renames the page |
| Scroll wheel, **1-9** or **Left / Right** | Switches pages |
| **Esc** | Closes the bar |

Scene objects are remembered by their `GlobalObjectId`. Clicking one whose scene is closed offers to save and opens that scene. Objects in a scene that has never been saved can't be added yet; save the scene first.

## Settings

**Edit > Preferences > Favorites Bar**: turn the hold-to-show bar on or off, and choose the shortcut (Alt, Alt + Shift or Ctrl + Alt).

## Where favorites are stored

`UserSettings/FavoritesBar.asset` in your project. They're per user and per project, and Unity's default `.gitignore` already leaves `UserSettings/` out of version control.

## Licence

[MIT](LICENSE.md) © 2026 Sanket Das
