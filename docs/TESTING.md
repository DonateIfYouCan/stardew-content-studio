# Test coverage

All five mods (Core, Paintings, Characters, Crops, Furniture) were tested by hand in the game. **There are no automated
tests.** Testing used a throwaway save, a temporary test-helper mod (console commands to load a save, place items, roll
fish and so on), and mouse clicks on the real UI, checked with screenshots.

Tested on one setup only: **Linux, Stardew Valley 1.6.15, SMAPI 4.5.2**, 1920×1080 window.

## Tested in the game

| Area | What was checked |
|---|---|
| Core editor | Start page, Escape/Close on the title screen and in-game, file browser (folders, shortcuts, scrolling, image preview), status messages wrapping |
| Export / import packs | Export, then import after deleting the content: files come back and a backup is made first. A crafted malicious zip (`..` paths aimed at a mod's DLL, manifest and config) was refused without changing those files. |
| Export folder | If the folder can't be written, exports go to the Core mod folder with a warning (tested with a read-only folder) |
| Paintings | Create, crop (drag corners), frames, sizes, price, shops (price matches, including Pierre), fishing (5% chance measured over 2,000 rolls per spot; "only once" and location limits work), hang on a wall, full-screen viewer with text, delete |
| Animated paintings | 3-frame animation placed in a room; frames advance in real time. Not tested: many animated paintings at once (each one updates its texture every few frames). |
| Duplicate button | Copying a painting makes an independent copy with a new name and ID (tested for paintings; crops, furniture and wallpaper use the same code, untested). |
| Photo frames / slideshows | Two images with captions, switching every 10 in-game minutes, standing on a table, viewer paging |
| Replaced / removed game paintings | Listed, marked "replaced", shown in the world |
| Villager portraits | Default image plus one emotion override, correct image per emotion in real dialogue, export original |
| Villager sprites | Export, choose an HD sheet, walking preview, outfit fallbacks, HD in the world, change while playing |
| HD farmer | All 10 sheet types load. The body is recolored per farmer: skin, eye, shoe, sleeve and dye colors match the normal game. Checked in the world, inventory, character creator, tool swings, eating and drinking, and after changing skin, hair, hat and shirt. A wrong-size sheet is refused. |
| Farmer editor 'Try on' | Hats shown on the preview farmer in all four directions; the real farmer is unchanged after closing. Accessories use the same code, untested. |
| Crops | Create, harvest image, growth-days validation, Pierre sells the seeds at the set price, Get seeds, delete |
| Wallpaper & floors | Made a wallpaper and a floor from images, hung/laid them indoors (needs to be within reach, like the game), and checked they appear in the catalogue. Not yet tested: many sets at once, HD drawing at high zoom, multiplayer sharing of them. |
| Furniture | Create from a base, search, export template, HD sheet, price, Robin's shop, place, lamp on at night, change the art while playing, delete |
| Multiplayer | Two and three game copies on one PC over direct IP (LAN). Host shares and player accepts (both off by default, trust confirmation), HD farmers visible on both screens, editing locked while using host content, attack tests (see Core README, *Multiplayer security*) |
| Security helpers | Managed PNG decoder checked against Pillow on 15 PNG variants (pixel-identical) and 2,700 corrupted files (no crashes) |

## Not tested

- **Windows and macOS**: never run. The code was reviewed for OS-specific problems and the ones found were fixed, but a
  review isn't a test.
- **Multiplayer over Steam or GOG invites, or over the internet**: only LAN/direct IP with copies on one PC.
- **Split-screen**: content sync skips split-screen players; the mods themselves weren't tried in split-screen.
- **Controllers / gamepads**: the editor was only used with mouse and keyboard.
- **Other screen sizes and UI zoom levels**: only 1920×1080 at default zoom.
- **Other mods**: not tested together with other mods that change the same assets (e.g. Content Patcher packs editing
  portraits, farmer sheets or `Data/Furniture`). If another mod resizes a sheet, ours is skipped for that sheet with a warning.
- **Long-term play**: saving and loading over many in-game days, and older saves.
- **Performance** with many or very large images.
