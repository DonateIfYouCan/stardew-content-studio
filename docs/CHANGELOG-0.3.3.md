# 0.3.3

A round of work on the pixel editor.

## Only what the tool uses

The column beside the image shows the chosen tool's name and only the settings it uses: size and tip for the pencil,
fill shape for rectangles and ellipses, copy and paste for Select, and so on. Tools with nothing to set say what the
mouse buttons do with them.

## Two colours

The right mouse button paints with a second colour, shown behind the first beside the palette. It starts see-through,
so the right button erases until you pick one. **X** swaps them; either button on the palette sets that button's
colour. Taking the colour under the cursor with any tool is now **Alt + click**.

## Gradients

Fill, line, rectangle and ellipse can paint a gradient from one colour to the other:

- straight or round;
- **runs** with your drag, or left-right, top-bottom or corner to corner across what's painted;
- a round one starts **from** where you press or from the middle of the area;
- blended in 3 or 5 bands, dithered (only ever the two colours), or smooth.

A fill whose gradient runs a fixed way is one click; one that follows your drag shows the whole area in its gradient
while you drag.

## Pixel perfect

- Lines step evenly: no stub at the end.
- **Pixel perfect** (pencil and eraser, on by default) takes out the doubled corners a slow freehand line gets.
- Lines and shapes are previewed at the tip's real width, mirrored copies included.

## Also

- Pasted and moved pixels float until you're done with them, so dragging them no longer eats what's underneath. Pasting
  switches to Select so the paste can be dragged.
- Settings with more than a few options are dropdowns with their label inside ("Behind: checks"), so the top bar fits.
- Move view can drag the image anywhere, including the middle; Fit and Width open it centred.
- The colour preview only appears on grey sheets like hair ("Preview as: ginger"); the saved image stays grey.
- Tip size is `[-] [ n ] [+]` with any number you like; `[` and `]` change it.
- `dotnet test` runs 153 checks.
