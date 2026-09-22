# Content Studio: Mining & Museum

A [SMAPI](https://smapi.io) mod for Stardew Valley 1.6 that adds your own minerals, gems and artefacts with an in-game
editor: which geodes give them, where they're dug out of artefact spots, whether the museum takes them, and how villagers
feel about them as a gift. It can also give the game's own new art, or stop one being found.

**Requires [Content Studio: Core](../CustomContentCore)**, in this repo.

**Status:** tested by hand on Linux only (Stardew Valley 1.6.15, SMAPI 4.5.2); Windows and macOS are untested. See [test coverage](../docs/TESTING.md) and [compatibility](../docs/COMPATIBILITY.md).

## Installing

1. Install [SMAPI](https://smapi.io).
2. Put **Content Studio: Core** and this mod in the game's `Mods` folder (build them, see *Building*).
3. Start the game through SMAPI and press `K` to open the editor.

## Features

- Editor section **Minerals** (press `K`), with a page for each part:
  - **Mineral:** picture (any image, cropped to a square, or painted in the game), name, description, what it is
    (mineral, gem or artefact), price, detail (pixel art to HD), colour, and whether the museum takes it.
  - **Geodes:** which of the six geodes can give it — geode, frozen geode, magma geode, omni geode, artifact trove and
    golden coconut — and how often each one does.
  - **Dug up:** where artefact spots can turn it up, and how often, across the valley, the mines, the desert and Ginger
    Island. Only artefacts are dug up, as in the game.
  - **Gifts:** click a villager to set whether they love, like, dislike or hate it.
- **The game's own** (second tab): give one new art, used for the item and in the museum, or stop it being found in any
  geode or artefact spot. Ones you already have stay. Stopping something a bundle, quest or the museum needs means that
  can't be finished.
- **Make my own copy** starts one of yours from the game's: its picture, kind, price, colour, geodes, dig spots and gift
  tastes, all yours to change. The game's stays as it is.
- What you add is real to the game: minerals and artefacts can be donated to the museum and count towards its rewards,
  gems count as gems (so the gemologist profession pays out), and everything sells and ships like the game's own.

## Files

| Path | What it is |
|---|---|
| `minerals.json` | Your minerals and changes to the game's (written by the editor; reloads automatically when edited). |
| `images/` | Images picked or painted in the editor. |

## Console commands

| Command | Description |
|---|---|
| `cmine_editor [name]` | Open the editor, optionally straight into one. |
| `cmine_list` | List your minerals, gems and artefacts. |
| `cmine_give <name> [count]` | Put one in your inventory. |
| `cmine_reload` | Reload `minerals.json` and images. |

## Notes

- Don't delete something you've found: ones in your world, in chests or donated to the museum become Error Items.
- A gem is kept out of the museum by the game, so "Can be donated to the museum" lets one in on purpose.
- In multiplayer, every player needs the mod. With the Core's content sharing on, everyone uses the Host's set.

## Building

```sh
dotnet build CustomMining
```
