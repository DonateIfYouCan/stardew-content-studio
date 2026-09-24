# 0.5.0

A new mod in the suite, **Content Studio: Mining & Museum**, and a change to how chances are set everywhere.

## New: Mining & Museum

Two sections in the editor (press `K`).

**Minerals** — your own minerals, gems and artefacts:

- A picture (any image, cropped square, or painted in the game), name, description, what it is, price, detail and colour.
- **Geodes:** which of the six geodes give it, and how often. A geode keeps its own treasure about half the time, so 100%
  here is roughly one geode in two.
- **Dug up:** where artefact spots turn it up, across the valley, the mines, the desert and Ginger Island. Only artefacts
  are dug up, as in the game.
- **Gifts:** how each villager feels about it.
- **The museum** takes minerals, gems and artefacts alike, unless you say otherwise. The museum has 102 spots and the
  game's own 96 donatable items nearly fill it, so the Mineral page says how many are left.
- **The game's own** (second tab): new art, stop it being found, put the art back, or make your own copy to start from.

**Rocks** — rocks of your own, in place of the game's:

- A picture, how many hits it takes, the mining experience it gives, and what it gives when broken (any of your minerals,
  the game's minerals, gems and artefacts, or ores and other bits) — on top of what any rock gives.
- **Where:** the mines' three stretches, Skull Cavern, the Quarry Mine, the volcano on Ginger Island, and above ground on
  the farm, in the forest, on the mountain and in its quarry, the backwoods, the bus stop, the railroad, town, Secret
  Woods and Ginger Island, in the seasons you pick.
- **The game's own** (second tab): every rock and ore node the mines are filled with, to give new art, hold back, or copy.
  Holding one back puts a plain rock of that depth in its place, so a floor always has something to break.

## Everywhere: type the number

Chances used to be a list of set values, so 12.5% couldn't be said at all. They're now number fields: type any figure in
range, decimals included. Anything that isn't part of a number is dropped as you type, so a stray key can't close the
editor either.

- a mineral's geode and dig-spot chances, and a rock's chance per place and per drop
- a painting's chance of being caught while fishing (was "5% per catch", "10% per catch"...)
- **a paint layer's opacity**, which was 25/50/75/100 ([#36](https://github.com/DonateIfYouCan/stardew-content-studio/issues/36))

## Fixes

- **The museum could strand you.** Its donation screen won't close while you're holding something, so once the museum was
  full — which your own minerals make likely — picking up one more item left you pressing Escape at a screen that ignored
  you. It now closes in that case and hands the item back. A piece lifted off the museum floor still can't be carried out,
  as in the game.
- Geode drops were never reached: the game walks a geode's own long list first and stops at the first hit, so a drop added
  at the end never got a turn. Yours is asked first now.
- The game's rocks list showed rows with no picture: the mine generator rolls numbers the data doesn't fill in, so the list
  is read from the game's data instead, and the plain rocks are numbered over the ones really there.
- Hiding one of the game's ore nodes left some standing: the clumps of ore a level is dotted with come from a different
  place in the game's code than its rocks. Both are covered now.
- A typed chance reads back as typed (12.5% used to show as 13%).
- A checkbox cuts its label to the room it has, so a long one can't run under whatever sits beside it.

## Notes

- Everything in Mining works in a shared game: mine floors come out the same on every machine, breaking and drops sync,
  and a player using the host's content can edit it back to the host as in the other mods.
- Custom minerals can be handed to another player like any item — gifted face to face, dropped, or left in a chest. The
  game doesn't allow furniture (so paintings and wallpaper) to be gifted face to face.
