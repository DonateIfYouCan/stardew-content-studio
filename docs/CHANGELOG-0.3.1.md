# 0.3.1

Two things players asked to be in charge of.

## Export at the game's own size

Exporting a sheet used to enlarge it four times, and the file said so: `Abigail sprite (x4).png`. That put HD art in
everyone's way, including the people who wanted the original to repaint pixel for pixel.

- Every export now **asks how big**: the game's size, 2x or 4x. The game's size comes first.
- The size you pick is remembered and offered first next time (it's also what the console commands use).
- A new `config.json` starts at the game's size; if you had it set to 4x, it stays 4x until you choose otherwise.
- The `(x4)` in the file name still only appears when you actually enlarged it.

## Turn off the hover tips

The line of help that follows the mouse is useful once and in the way afterwards.

- **Settings → Help on hover** turns it off. Everything else stays where it is; only the tip stops appearing.

## Also

- `dotnet test` runs 49 checks now: the four added here cover both defaults, that no export screen skips the question,
  and that the tip is still drawn behind the setting.
