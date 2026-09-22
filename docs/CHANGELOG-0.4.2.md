# 0.4.2 (not released yet)

## Crops: paint every image

**Paint** now sits next to the image picker in the crop editor and paints whatever it shows: the harvest icon, your own seed
image, or, for the automatic seed packet, the packet behind the harvest icon. The packet starts as the game's own; the
harvest icon still goes on its front, and **Game's packet** puts the original back. (#30)

## Fixes

- A JPEG shared in a multiplayer game is stored as a real JPEG. Images travel as PNG data, and a received `photo.jpg` used to
  be written as PNG data under that name. (#20)
