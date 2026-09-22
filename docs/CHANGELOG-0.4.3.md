# 0.4.3

## Fixes

- **Typing a K in a name closed the editor** (in 0.4.1 and earlier). The editor key is ignored while you're typing in a text
  field; press Escape or click elsewhere to stop typing, and K works again. The editor also has a close button, the game's
  red X, in its top-right corner. (Both came in 0.4.2; this release is the one to take for it.)
- **Layers are a tool now** (key N) instead of a panel squeezed under the other settings. Picking it gives the whole column
  beside the image to the layers: the list takes the room there is, with New, Copy, Up, Down, Merge, Delete, Hide and how
  much it shows underneath, ending where the image area ends. Clicking the image with it picks the layer painted there, and
  other tools say which layer they paint on when there are several.
- In a short window the tool buttons get lower, so all of them fit.

## Also

- `dotnet test` runs 229 checks.
