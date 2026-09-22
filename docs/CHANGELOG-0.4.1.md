# 0.4.1

## Crops: gifts and colour

Crops get two things fish already had:

- **Gifts:** a Gifts button in the crop editor sets which villagers love, like, dislike or hate the harvest. Before, a custom
  crop fell back to the game's rule for its type, so nobody could love it.
- **Colour:** worked out from the harvest image, or chosen. The game uses it for dyeing, flower honey, and the colour of the
  wine, jelly, juice or pickles made from it. Custom crops had no colour before.

Both now live in Content Studio: Core, so fish and crops share them, and so can any mod that follows.

## Make your own copy of the game's items

Every Game tab has **Make my own copy**: crops, furniture, wallpaper and floors, and paintings, as fish already did. It opens
the editor on a new item of your own, with the game item's art saved as your own image to paint over and its settings copied:
a crop's growth, prices, where its seeds are sold, colour and gift tastes; a piece of furniture's kind, size and price; a
painting's size and price. Nothing is saved until you press Save, and the game's own item stays as it is. Copied fish now
also take the gift tastes villagers have for the game's fish.

## Also

- The fish editor's mines option says what works: floors 20 and 60. Floor 100 uses its own list and never gets custom fish.
- `dotnet test` runs 213 checks.
