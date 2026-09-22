# 0.4.2 (not released yet)

## Crops: paint every image

**Paint** now sits next to the image picker in the crop editor and paints whatever it shows: the harvest icon, your own seed
image, or, for the automatic seed packet, the packet behind the harvest icon. The packet starts as the game's own; the
harvest icon still goes on its front, and **Game's packet** puts the original back. (#30)

## Paint: layers

The pixel editor has layers (bottom right): add an empty layer or a copy, move them up and down, hide them, show one at 75%,
50% or 25%, and merge one into the layer below. Every tool paints on the layer picked in the list; the eyedropper takes the
colour you see. Adding, deleting, moving and merging are undone like strokes. Saving lays the visible layers together into one
image, exactly as they look; hidden layers aren't saved. Layers last while the editor is open. (#9)

## Fixes

- A JPEG shared in a multiplayer game is stored as a real JPEG. Images travel as PNG data, and a received `photo.jpg` used to
  be written as PNG data under that name. (#20)
