# Test coverage

All six mods (Core, Paintings, Characters, Crops, Furniture, Fish) were tested by hand in the game. **There are no automated
tests.** Testing used a throwaway save, a temporary test-helper mod (console commands to load a save, place items, roll
fish and so on), and mouse clicks on the real UI, checked with screenshots.

Tested on one setup only: **Linux, Stardew Valley 1.6.15, SMAPI 4.5.2**, 1920×1080 window.

## Tested in the game

| Area | What was checked |
|---|---|
| Core editor | Start page, Escape/Close on the title screen and in-game, file browser (folders, shortcuts, scrolling, image preview), status messages wrapping |
| Export / import packs | Export, then import after deleting the content: files come back and a backup is made first. A crafted malicious zip (`..` paths aimed at a mod's DLL, manifest and config) was refused without changing those files. |
| Themes & backups | On the title screen: made an empty theme (every mod loaded nothing), switched back (the content came back byte for byte, empty image folders included), opened a 'Backup before joining' zip as a theme and used it (4 crops, 11 paintings, 2 farmer sheets loaded), switched back, and deleted a theme (a zip of it appeared in the backups and opens again). With a save loaded: the warning shows, the other theme's farmer sheets apply at once, the old theme's things show as Error Items, and switching back restores them. The start page in two columns with six sections at 1920x1080. Not yet tested: switching while hosting with players connected, a theme folder copied in by hand from another PC. |
| Export folder | If the folder can't be written, exports go to the Core mod folder with a warning (tested with a read-only folder) |
| Paintings | Create, crop (drag corners), frames, sizes, price, shops (price matches, including Pierre), fishing (5% chance measured over 2,000 rolls per spot; "only once" and location limits work), hang on a wall, full-screen viewer with text, delete |
| Animated paintings | A 2-frame and a 3-frame animation placed in a room; screenshots 100 ms apart show the frames advancing in real time. Not tested: many animated paintings at once (each one updates its texture every few frames). |
| Duplicate button | Copying makes an independent copy with a new name and ID; tested for paintings, furniture and wallpaper (crops use the same code, untested). |
| Photo frames / slideshows | Two images with captions, switching every 10 in-game minutes, standing on a table, viewer paging |
| Replaced / removed game paintings | Listed, marked "replaced", shown in the world |
| Villager portraits | Default image plus one emotion override, correct image per emotion in real dialogue, export original |
| Villager sprites | Export, choose an HD sheet, walking preview, outfit fallbacks, HD in the world, change while playing |
| HD farmer | All 10 sheet types load. The body is recolored per farmer: skin, eye, shoe, sleeve and dye colors match the normal game. Checked in the world, inventory, character creator, tool swings, eating and drinking, and after changing skin, hair, hat and shirt. A wrong-size sheet is refused. |
| Farmer editor | Sheets split over two pages (Farmer, Clothes & hats); stepping and typing an item number shows that hairstyle, beard, shirt or hat next to your HD version; counts match the game's own layout (56 hairstyles, 304 shirts, 132 hats, 32 accessories, 20 pants). 'Try on' shows a hat on the preview farmer in all four directions and the real farmer is unchanged afterwards; accessories use the same code and were only checked in the preview. |
| Crops | Create, harvest image, growth-days validation, Pierre sells the seeds at the set price, Get seeds, delete |
| Fish | A fish from an image, caught with a rod in the town and forest rivers: 124 of 400 rolls in town and 109 of 400 on the forest river gave it, none in the forest pond or the ocean (the places it wasn't given). Its data as the game reads it: the object (category fish, price, energy, roe colour tag), the fishing entry, the spawns, the tank entry and gift tastes (Willy loves it, Abigail hates it, Emily as the game decides). A fish pond takes it, with orange roe. A large fish tank with two of it and two carps given its art: all four drawn and swimming. The editor's pages at 1920x1080, including the crab pot version of the catching and where pages, and painting the picture. The game fish tab: new art on the carp (item and tank), the bream no longer caught in town, the lobster out of crab pots (its water type is set to one no water has). Not yet tested: catching one through the minigame by hand, a crab pot actually finding a custom fish overnight, a pond producing roe over days, cooking with it. |
| Wallpaper & floors | Made a wallpaper and a floor from images, hung/laid them in the farmhouse, left and came back (they stay), duplicated one, and checked they appear in the catalogue. Not yet tested: many sets at once, HD drawing at high zoom, multiplayer sharing of them. |
| Furniture | Create from a base, search, export template, HD sheet, price, Robin's shop, place, lamp on at night, change the art while playing, delete |
| Multiplayer | Two, three and four game copies on one PC over direct IP (LAN). Host shares and player accepts (both off by default, trust confirmation), HD farmers visible on both screens, editing locked while using host content, attack tests (see Core README, *Multiplayer security*). Sharing both ways: a player who joined shared their content and the host took it (6 files), and with four players the host shared to two players while the fourth, who shares its own, kept its own content. Content from several players at once was tested with two game copies using separate mod folders (`--mods-path`), each with its own painting: both sides showed their own plus the other's, marked 'from <player>'. |
| One content set per game | Two game copies with their own mod folders (`--mods-path`), the Host with two paintings and Player A with one of their own. Player A joined: their content was saved to a backup zip, their editor listed the Host's two paintings and not their own, and the start page said so. Player A opened one for editing: the Host's own list greyed out its buttons with "Pal is changing the paintings right now." Player A renamed it and saved: the change reached the Host ("Sending 1 changed file(s)" / "Pal changed your custom content"), the Host's own `paintings.json` was rewritten, the version it replaced was kept, the Host's list refreshed and the buttons came back. The Host then put the earlier version back from the *Earlier versions* screen, which kept a version of the restore itself. Player A left and their own painting came back ("Switched back to your own custom content"). Player A also added a painting of their own from an image on their PC: both the data file and the new image reached the Host ('Sending 2 changed file(s)'), the image landed in the Host's own `paintings/imported/` folder, and the Host's list showed it. Player A also added a painting of their own from an image on their PC: both the data file and the new image reached the Host, the image landed in the Host's own `paintings/imported/` folder, and the Host's list showed it. |
| Taking a change back, and locks running out | Host plus Player A, two game copies with their own mod folders. Player A opened a painting; the Host's *Multiplayer* screen listed it as "HostOne - Ann" and *Take it back* removed it, with both sides saying so. Player A saved anyway: the Host refused it ("Refused 1 change(s) from Ann"), Player A was told ("the host didn't keep your change"), the Host's file was untouched and Player A's copy went back to the Host's version. Then Player A opened another painting and their game was frozen for longer than the three-minute hold: the Host's list emptied by itself, and the Host could open that painting again. The save that would follow couldn't be driven, because a game frozen that long is dropped by Stardew's own timeout first - and being dropped releases the locks and gives Player A their own content back, which it did. |
| Fish in a shared game | Host with one fish and three changes to game fish, Player A joined and took the Host's set (fish, art and hidden fish all the same on both). Player A hiding a game fish was refused ("The host doesn't let players change the game's own items"), and so was opening the Host's fish ("The host only lets players change what they added themselves"). Player A duplicating the fish reached the Host as a new fish added by Player A, and Player A deleting it took it out of the Host's file. |
| Four players changing at once | Host plus Player A, B and C, four game copies with their own mod folders, the Host with three paintings. All three joined and took the Host's set. Player A opened the first painting and Player B the second at the same time: both were allowed, and each other's row showed "<name> is changing this". Player C was refused on the first ("Ann is changing 'HostOne' right now.") while the third stayed free. A and B saved different renames and both landed in the Host's own file, one item each, neither overwriting the other. Player C then duplicated a painting - adding needs no lock - and all four ended up with the same four paintings. |
| Sheet layouts | Every layout was checked against the game's own code, not guessed. Farmer bodies are read six frames to a row (`frame * 16 % 96` in `FarmerSprite.UpdateSourceRect`), whatever the sheet's width - the rest of each row is arms. Hairstyles are 16x96 (three directions), but every style in `hairstyles2` has its own left-facing sprite (`usesUniqueLeftSprite` in `Data/HairData`), so those are 16x128 with four directions. Hats 20x80 by the sheet's width; shirts 8x32 with 16 per row (the right half of the sheet is the dye mask); accessories 16x32 by the sheet's width; pants ten to a row in 192x688 blocks (`pants % 10 * 192`). Villager sprites and portraits follow the sheet's width (16x32 frames, 64x64 portraits), and wallpaper is 16 to a row at 16x48 with floors 8 to a row at 32x32, as `Wallpaper.cs` reads them. |
| Paint (in-game pixel editor) | Opened on the farmer's hairstyles sheet; starting from the game's art asks for 1x, 2x or 4x and copies it. Painted at 4x (strokes, colour from the palette, undo, save: accepted as a 4x sheet) and at 1x (accepted as a 1x sheet). Line, rectangle and ellipse (outlined and filled), replace-a-colour (69,984 pixels in one go, undone in one step), the replace brush, selection (drag, move, copy, paste, clear, flip, and 'turn' refusing a non-square piece), mirror drawing inside one sprite of a sheet, the brush (a 6-pixel round tip leaves a wide soft stroke where the pencil leaves a crisp line), changing a key in the Keys screen and seeing it saved in config.json, saving a painted sheet in one step (the paint screen's Save writes characters.json itself), the guides and part names for a hairstyles sheet, the cursor readout (pixel, sprite number, position in the sprite, facing), jumping to a sprite by number, the plain backgrounds, zoom to the cursor, both palette orders, and the colour picker (hue strip, shade square, R/G/B/A boxes, before-and-after preview) painting with a colour that wasn't in the image. Not yet tested: Shift-constrained shapes (the harness can't hold the key), fill on a very large sheet, the undo memory limit, painting furniture, villager sprites or a wallpaper tile (same code, not run). |
| Names | A 73-character name is saved and used in full; lists shorten what they show. A crop named `[77] Melon` reaches Pierre's shop as "77 Melon Seeds", so names can't smuggle the game's tokens into its data. |
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

## Checks that run without the game

`dotnet test` runs a small set of fast checks in `Tests/`, meant to catch the things that have quietly broken here before:

| Check | What it would have caught |
| --- | --- |
| Received paths and files | A path climbing out of the content folder, JSON with `$type` or comments, a program pretending to be a PNG, data hidden after an image |
| Items sent one at a time | A mod registered without `ContentEditing`, so it sends its whole data file and one player holds everything |
| Screens make what they declare | A button declared but never created - the editor then fails to open at all (happened twice with the support buttons) |
| Screens let go of what they hold | A screen that takes a lock and never releases it, leaving an item stuck until the lease runs out |
| Lists notice a change | A list that doesn't rebuild when another player's change arrives (this check found four screens that had lost it) |
| Themes | A switch that leaves the old theme's files behind, drops a mod's image folder, or copies anything but content (a DLL, the manifest) out of a theme; a pack that climbs out of its theme folder; theme names that can't be folders |
| Fish data | A fishing, crab pot or tank entry that doesn't match the game's own (checked against the game's pufferfish, lobster and crab), seasons written where the game doesn't read them, a tank sprite cut off when turned |

Run them before pushing:

```sh
dotnet test
```

They don't need Stardew Valley running, but they do need the game installed: the test project loads the game's `MonoGame.Framework.dll` for the colour type.
