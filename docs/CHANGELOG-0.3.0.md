# 0.3.0 — draft notes

Two big things since 0.2.1: you can draw pixel art **inside the game**, and a multiplayer game now runs on **one content set** that players can work on together.

## Paint in the game

A pixel editor on every image the mods use: paintings and photo frames, crops (including the growing plant), furniture, wallpaper and floors, villager portraits and sprites, and the farmer's own sheets.

- Pencil, brush (round, square or diamond tips, any size), eraser, eyedropper, fill, line, rectangle, ellipse, replace-a-colour, replace-while-you-drag, and a rectangular selection you can move, copy, paste, clear, flip or turn.
- Guides show what the game expects in the sheet you're editing, with the name of each part; a readout says which pixel and which sprite you're on, and you can jump to a sprite by number.
- Mirror drawing inside a sprite, zoom to the cursor, a grid for sprites and pixels, a plain background instead of the chequerboard, undo and redo.
- **Colour** previews a grey sheet (hair, for one) in any colour without changing it.
- A palette of the colours in the image, ordered by use or by shade, plus a colour picker with a shade square, hue strip and R/G/B/A boxes.
- Start from the game's own art at 1x, 2x or 4x — the game's art is never changed — or from an empty sheet.
- Keys for every tool, and you can change them.

## Multiplayer: one content set, worked on together

- In a host's game, everyone uses the **host's** content, so there's one answer to whose hair sheet is in use and placed items keep working. A player's own content is saved to a backup zip first and comes back when they leave.
- With *Let players change my content*, players can edit that set. Whatever you open is held until you close it, so two people can't write over each other — one painting, one crop, one villager's portraits, one farmer sheet at a time. Everyone sees who's changing what, and the host can take anything back.
- What a player saves is sent to the host, who checks it, keeps the version it replaces and passes the new one on. Only the host writes the host's files.
- **Earlier versions** puts back any data file as it was before it was last written over.
- Everything is off unless you turn it on, and it all lives on one *Multiplayer* screen.

## Also

- Crops: paint the growing plant (seedling, stages, ripe) instead of only importing a sheet.
- Wallpaper and floors: use a whole picture squeezed into a tile, or drag the crop box to any shape with *Lock shape* off.
- The start page has *Settings*, *Multiplayer* and *Earlier versions*; the editor says when the window is smaller than it's laid out for (1600x900).
- Every sheet layout was checked against the game's own code, including the second hairstyles sheet, which has four directions per style.
- `dotnet test` runs checks that don't need the game: what another player may send, what a received file becomes, and habits every screen has to keep.

## Fixes worth knowing

- Saved farmer sheets show on your character straight away.
- Saving a painted sheet is one step.
- Long names are kept in full, and `[77]`-style tokens can't be smuggled through a name.
- A change from another player keeps the folder its image lives in, so the item isn't dropped for a missing picture.
