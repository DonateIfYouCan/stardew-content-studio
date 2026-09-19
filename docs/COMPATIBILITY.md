# Compatibility

| | Status |
|---|---|
| Stardew Valley 1.6.15 | Tested |
| Other 1.6.x versions | Not tested; may work |
| Stardew Valley 1.5 or older | Not supported |
| SMAPI 4.5.2 | Tested |
| Other SMAPI 4.x versions | Not tested |
| Linux | Tested |
| Windows | Not tested. Written to work (no Linux-only code, Windows path rules handled), reviewed, not run. |
| macOS | Not tested. Written to work (uses SMAPI's content path for the Mac app layout), reviewed, not run. |
| Android / consoles | Not supported (SMAPI mods of this kind don't run there) |
| Multiplayer | Tested over LAN/direct IP only. Steam/GOG invites and internet play not tested. |
| Split-screen | Not tested |
| Controller | Not tested; the editor expects a mouse and keyboard |

## Requirements

- All mods need **Custom Content Core**.
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

## Known limitations

- Deleting a custom painting, crop or furniture turns copies already in a save into Error Items (the editor warns first).
- HD farmer: body parts that are recolored per farmer (skin, eyes, shoes, sleeves) must keep the original sheet's colors
  in your HD version; shading around them is fine.
- Windows: if Controlled Folder Access blocks the Pictures folder, exports and backups go to the `exports` folder inside
  the Core mod folder (a warning in the SMAPI console says so). Set `ExportFolder` in the Core's `config.json` to choose
  another folder.
- Multiplayer: a host's file names that differ only in capital letters (like `a.png` and `A.png`) can't both be shared;
  the host shares one and logs a warning.
