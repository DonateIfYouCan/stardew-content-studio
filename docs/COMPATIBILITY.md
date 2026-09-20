# Compatibility

| | Status |
|---|---|
| Stardew Valley 1.6.15 | Tested |
| Other 1.6.x versions | Not tested; may work |
| Stardew Valley 1.5 or older | Not supported |
| SMAPI 4.5.2 | Tested |
| Other SMAPI 4.x versions | Not tested |
| Linux | Tested |
| Windows | Not tested. Written to work and reviewed (see *Cross-platform review* below), never run. |
| macOS | Not tested. Written to work and reviewed (see *Cross-platform review* below), never run. |
| Android / consoles | Not supported (SMAPI mods of this kind don't run there) |
| Multiplayer | Tested over LAN/direct IP only. Steam/GOG invites and internet play not tested. |
| Split-screen | Not tested |
| Controller | Not tested; the editor expects a mouse and keyboard |

## Requirements

- All mods need **Content Studio: Core**.
- Players in multiplayer only see your custom content if they have the same mods installed and turned on
  *Accept content from hosts*. Without the mods, custom items show as Error Items.

## Other mods

- Not tested with other mods. Mods that change the same things can conflict:
  - villager portraits or sprites (e.g. portrait mods, Content Patcher packs);
  - farmer sheets (hair, shirts, pants, hats, accessories, bodies);
  - `Data/Furniture`, `Data/Crops`, `Data/Objects`, `Data/Shops`, `Data/Locations`.
- Our edits are applied late. If another mod changes the size of a sheet we replace, ours is skipped for that sheet and a
  warning is logged, so the other mod's version shows.
- Hairstyles, hats and other items that other mods add with their own textures are not drawn in HD.

## Limits

| | Limit |
|---|---|
| Item name | 80 characters. The whole name is saved and used in the game; lists shorten what they *show* with "…". |
| Characters in a name | Square brackets are removed and slashes and line breaks become spaces before the name goes into the game's data, because the game reads `[...]` as a token and uses `/` to separate fields. Everything else (including `%`, `&`, accents and emoji) is kept. |
| ID made from a name | First 40 letters/digits, plus a number if that ID is taken. IDs end up in save files, so they don't change when you rename an item. |
| Imported image file name | First 60 characters (letters, digits, `_`, `-`), so the whole path stays inside Windows' 260-character limit. |
| HD sheet size | Up to 8x the original, and at most 8192 pixels on a side, which every graphics card that runs the game can load. Bigger sheets are refused with a message. |
| Slides in a painting | Not limited by the mod; each slide is one image in memory. |

## Cross-platform review

Windows and macOS were never run; this is what was checked in the code, not tested in the game:

- Paths are built with `Path.Combine` and compared case-insensitively; no `/` or `\` is hard-coded. Asset names (like
  `Mods/<id>/Wallpapers`) use `/`, which is what SMAPI expects on every OS.
- The game's own files are read through SMAPI's content path, which points inside `Contents/Resources` on macOS.
- Imported content packs reject `..`, absolute paths, and names Windows reserves (`CON`, `PRN`, `AUX`, `NUL`, `COM1`…).
- File names that differ only in capital letters are a problem on Windows and macOS, where the file system doesn't tell
  them apart; sharing content in multiplayer warns about them instead of overwriting.
- Exports and backups go to your Pictures folder; if it can't be written (Windows' Controlled Folder Access, for example)
  they go to the Core mod's `exports` folder with a warning.

## Known limitations

- Deleting a custom painting, crop or furniture turns copies already in a save into Error Items (the editor warns first).
- HD farmer: body parts that are recolored per farmer (skin, eyes, shoes, sleeves) must keep the original sheet's colors
  in your HD version; shading around them is fine.
- Windows: if Controlled Folder Access blocks the Pictures folder, exports and backups go to the `exports` folder inside
  the Core mod folder (a warning in the SMAPI console says so). Set `ExportFolder` in the Core's `config.json` to choose
  another folder.
- Multiplayer: a host's file names that differ only in capital letters (like `a.png` and `A.png`) can't both be shared;
  the host shares one and logs a warning.
