# Content Studio: Core

Shared library mod for the Custom Content family of [SMAPI](https://smapi.io) mods for Stardew Valley 1.6.

It provides:
- **The in-game editor** (press `K`): one editor where each installed Custom Content mod adds a section.
- **Editor building blocks**: screens, buttons, cyclers, checkboxes, text fields, scroll lists, confirm dialogs.
- **File browser** for any folder on the computer (with drives/volumes on Windows/macOS/Linux).
- **Image tools**: PNG/JPEG decoding, cropping, framing (built-in and 9-slice custom frames), high-quality downscaling.
- **Export originals**: save the game's original art as PNG (optionally enlarged with sharp pixels) to edit in an external program and import again.
- **Content packs**: *Export pack* saves all custom content of every Custom Content mod (data + images) into one `.zip`;
  *Import pack* loads it on another PC (replacing that content, after automatically backing up the current content to `Exports/Backups`).
  Imports only write into each mod's registered content files/folders.
- **Multiplayer content sharing** (off by default, opt-in on *both* sides): a host with *Share my content when I host* sends its
  custom content to players who turned on *Accept content from hosts*. Received content goes to a per-host cache
  (`host-content/`) and is only used while in that host's game; the player's own content is never changed, and editing is
  locked while using a host's content. Only changed files are sent (SHA-256 checked), host edits are pushed live. Split-screen is skipped.
  See [Multiplayer security](#multiplayer-security).
- **Full-screen image viewer** with captions and slideshow navigation.
- **High-resolution furniture drawing**: mods register a renderer and their furniture is drawn from their own (sharper) texture.

**Status:** tested by hand on Linux only (Stardew Valley 1.6.15, SMAPI 4.5.2); Windows and macOS are untested. See [test coverage](../docs/TESTING.md) and [compatibility](../docs/COMPATIBILITY.md).

## Screenshots

![The editor start page (press K)](../docs/screenshots/core-start-page.png)
*The editor start page (press K).*

![Picking an image (the folder path is blurred)](../docs/screenshots/core-file-browser.png)
*Picking an image (the folder path is blurred).*

![Confirmation before accepting content from hosts](../docs/screenshots/core-accept-dialog.png)
*Confirmation before accepting content from hosts.*

All images in the screenshots are made for the demo.

## Paint

Editors that work with pixel art have a **Paint** button that opens the image in the game. The tools are down the left:
pencil, eraser, eyedropper, fill, line, rectangle, ellipse, replace a colour everywhere, replace it only where you drag,
and a rectangular selection you can move, copy, paste, clear, flip or turn. Shapes can be outlined or filled, Shift keeps
lines straight and boxes square, and mirror drawing repeats your strokes inside the sprite you're drawing in. There's
zoom (to the cursor), a grid for sprite and pixel boundaries, and undo/redo.

**Colour** shows the art in a colour without changing it, which is what you want for sheets the game colours itself: hair is
stored in grey so your character's hair colour can tint it, and this previews that. Pick one of the presets or any colour.

**Guides** show what the game expects in the sheet you're editing: where each sprite begins and ends, and the parts inside
it (which facing direction a band of pixels is, for example). The top right always says where the cursor is: the pixel, the
sprite number, the position within that sprite, and which part it's in. **Sprite** jumps to a sprite by the same number the
editors use and selects it. **Behind** switches what's drawn under see-through pixels: a checkerboard, or plain dark, light
or pink to judge the art against. Guides, the grid and the background can each be turned off. It edits a copy and saves as a new PNG
in the mod's images folder, so the art you started from is never overwritten.

Starting from the game's own art (a farmer sheet, a villager sprite, a piece of furniture) copies it first and asks what
size to draw at: **the game's size (1x)**, 2x, or 4x. 1x is one pixel per game pixel, which is what most pixel art wants;
the bigger sizes are for HD art. The colours below the canvas are the ones the image already uses, ordered by how much
it uses them or grouped by colour; **Choose colour** opens a picker for any other colour (drag in the shade square, pick a
hue, or type red, green, blue and see-through values), and the colours you pick that way stay in the row while you paint.

Keys - tools: `B` pencil, `E` eraser, `I` pick colour, `F` fill, `L` line, `R` rectangle, `O` oval, `A` replace all,
`D` replace drag, `S` select, `H` move view. Other: `C` copy, `V` paste, `Delete` clears the selection, `Z` undo, `Y` redo,
`+`/`-` zoom, `0` fills the width, `Shift` keeps lines and boxes straight, and holding `space` drags the image.

## Support

Thanks for using these mods! They're free, and every feature works without donating. If you'd like to, you can
[buy me a coffee](https://buymeacoffee.com/donateifyoucan). The editor's start page has a small optional *Support* section
with buttons that copy this link and the [GitHub page](https://github.com/DonateIfYouCan/stardew-content-studio) to the clipboard. There are no
pop-ups or reminders.

## For mod authors

1. Add a dependency in your `manifest.json`:
   ```json
   "Dependencies": [ { "UniqueID": "DonateIfYouCan.CustomContentCore", "MinimumVersion": "0.1.0" } ]
   ```
2. Reference the Core project/DLL without copying it (`Private="False"`).
3. In your `Entry` method:
   ```cs
   CustomContent.RegisterEditor(this.ModManifest, "Paintings", "Your own paintings", () => new MyMainScreen());
   CustomContent.RegisterFurnitureRenderer(MyStore.TryGetTexture);
   CustomContent.RegisterHdTexture(gameTexture, MyHdProvider); // draw any game texture from an HD version
   ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { "mydata.json", "images" }, MyStore.Reload); // packs + multiplayer sharing
   ```
   Load content from `ContentPacks.GetContentRoot(manifest, helper.DirectoryPath)` (it points to the host's content in multiplayer),
   call `CustomContent.EnsureEditable(manifest)` before saving and `CustomContent.NotifyContentChanged()` after, and read JSON with
   `CustomContent.ReadJsonFile<T>()` (lists replace defaults instead of being appended to).
   Also available: `ImageExport.Export(...)`, `OriginalContent.LoadTexture/LoadData(...)` (read unmodded game files), and the UI widgets (`CropWidget`, `FileBrowserScreen`, `ViewerMenu`, ...).

## Console commands

| Command | Description |
|---|---|
| `ccc_editor` | Open the editor start page. |
| `ccc_export` | Export all custom content into a pack (`.zip`) in the export folder. |
| `ccc_import <file>` | Import a pack (current content is backed up first). |

## Config

`config.json`:
- `EditorKey` (default `K`)
- `BrowserStartFolder` (default: your Pictures folder)
- `ExportFolder` (default: `Pictures/Stardew Custom Content`; also a shortcut in the file browser)
- `ExportScale` (default `4`: exports are enlarged 4x with sharp pixels; `1` = original size)
- `ShareContentAsHost` (default `false`) and `AcceptContentFromHost` (default `false`): multiplayer content sharing (also toggles on the editor's start page)

## Multiplayer security

The other side may run a modified mod or game and send anything, so each player's own checks are what protect them.

**Who is allowed to send.** SMAPI's sender ID can be forged: the host forwards farmhands' messages, and a modified SMAPI can
claim to be the host. So a player who accepts content makes a random 256-bit secret per game and sends it only to the host.
Messages addressed only to the host are never forwarded, so other players never see the secret. Offers and file data without
that secret are ignored. The host keeps the first secret per connection, so another player can't replace it.

**What is accepted before download:**
- only registered mods and their content paths;
- only `.json`, `.png`, `.jpg` or `.jpeg` files; never a zip, DLL, manifest or config;
- plain relative paths: letters, digits, space, `_ - . ( )`; no `..`, absolute paths, backslashes or device names like `CON`;
- at most 64 MB per file, 2 MB per JSON file, 2000 files and 200 MB per host;
- chunk counts, sizes and SHA-256 hashes are checked.

**Every received file is rebuilt from scratch before it's stored**, so only real content survives:
- Images must be PNG data; the host converts JPEGs before sending. They're decoded by Core's own managed PNG decoder, never
  the game's native one, with checksums verified, at most 8192 px per side, and exact-size decompression. Then
  they're re-encoded as a plain RGBA PNG. Metadata, appended data such as a hidden zip, and malformed structures are dropped.
- JSON must be a UTF-8 object, at most 32 levels deep, with text values under 20,000 characters. It's parsed and written out
  again without comments or `$...` properties like `$type`. The mods never enable Newtonsoft type handling.

**Where it goes.** Files are written only to `host-content/<host>/`, and only the 5 most recent hosts are kept. Images are
loaded only from inside the content folder, so a data file can't point to other files on the PC. Nothing received is ever
executed or extracted.

**On the host.** It shares only files in use: the data file and the images it references, never unused or deleted images,
links, config, manifests or DLLs. It ignores requests without the player's secret, requests for files it didn't offer, and
repeated requests while a send is still in progress.

## Building

```sh
dotnet build CustomContentCore
```
Deploys to the game's `Mods/CustomContentCore` folder.
