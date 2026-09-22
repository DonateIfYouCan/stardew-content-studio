# 0.4.2

## Crops: paint every image

**Paint** now sits next to the image picker in the crop editor and paints whatever it shows: the harvest icon, your own seed
image, or, for the automatic seed packet, the packet behind the harvest icon. The packet starts as the game's own; the
harvest icon still goes on its front, and **Game's packet** puts the original back. (#30)

## Paint: layers

The pixel editor has layers (bottom right): add an empty layer or a copy, move them up and down, hide them, show one at 75%,
50% or 25%, and merge one into the layer below. Every tool paints on the layer picked in the list; the eyedropper takes the
colour you see. Adding, deleting, moving and merging are undone like strokes. Saving lays the visible layers together into one
image, exactly as they look; hidden layers aren't saved. Layers last while the editor is open. (#9)

Layers and the close button below are checked by `dotnet test` but haven't been tried in the game yet; reports welcome.

## Fixes

- **Typing a K in a name closed the editor.** The editor key is ignored while you're typing in a text field; press Escape or
  click elsewhere to stop typing, and K works again.
- The editor has a close button (the game's red X) in its top-right corner, which does what the editor key does.
- A JPEG shared in a multiplayer game is stored as a real JPEG. Images travel as PNG data, and a received `photo.jpg` used to
  be written as PNG data under that name. (#20)

## Also

- `dotnet test` runs 228 checks.
