# Demo content

The "Emily plant" used in the 0.3.0 header and screenshots: a crop whose flower is Emily's head on a leafy stem.

- `emily-plant-growth-x4.png` — the growth sheet, 512x128 (8 frames of 16x32 at 4x): 2 seedling frames, 3 growing, then ripe, then two regrow frames that repeat the ripe one.
- `emily-plant-harvest-x4.png` — the harvest item, 64x64 (16x16 at 4x).

Both were made from the game's own Emily sprite, exported with *Export original sheet* in the Characters editor, so they only make sense next to a copy of the game.

To use them: put the two files in `CustomCrops/images`, add a crop in the editor, and point *Choose growth sheet* and *Choose image* at them. Four growth stages (`1,1,2,2`) matches the sheet; harvest with a scythe.
