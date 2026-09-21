# 0.3.2

## The game's own crops and furniture

Paintings could already change the game's paintings; crops and furniture could only add new ones. Now both lists have a
tab for the game's own.

- **Game crops:** give a crop a new harvest icon and growing plant, painted in the game or from an image. How it grows,
  what it sells for and its seed packet stay the game's. Or take its seeds out of every shop.
- **Game furniture:** give a piece new art, including animation. Its type, size, name and price stay the game's. Or take
  it out of the Furniture Catalogue and every shop; copies already placed stay where they are.
- **Put the game's art back** at any time. Your images stay in the mod's folder.

In a host's game each game item is held on its own, so one player changing the game's lamp doesn't stop another hiding a
game table. Changing the game's own items needs the host's *Let players change my content* even the first time, since it
changes the game for everyone; whoever made a change can keep changing it after the host turns that off.

## Multiplayer fixes

- **Adding something with a new image while the host's *Let players change my content* was off never arrived.** The host
  allowed it, asked for the image, then threw the image away. It arrives now.
- **Rejoining a host sent back every image you already had** on your first save (13 files for a change that added one).
  The host refused them and told you it hadn't kept a change you never made; with the switch on, it wrote your copies
  over its own. Only what you changed is sent now.
- **What you added stays yours after the host restarts.** Who added what was only remembered until the host quit.
- **Closing an editor no longer leaves your own buttons greyed out** with "you are changing this", and a refusal now says
  which rule it was ("only what they added" or "not the game's own items").

## Also

- The note that the window is smaller than the editor is made for was drawn over the buttons it warned about. It's now a
  notice you click away, shown once.
- `dotnet test` runs 82 checks, including every combination of who may change what in a host's game.
