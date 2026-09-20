using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>
    /// Draws on an image inside the game: pencil, eraser, colour picker and fill, with zoom, undo and a grid that shows where
    /// the sprites in a sheet begin. It works on a copy, so nothing changes until you save; saving hands the pixels back to
    /// whichever editor opened it.
    /// </summary>
    public sealed class PaintScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>The tools you can draw with.</summary>
        private enum Tool { Pencil, Eraser, Picker, Fill, Line, Rectangle, ReplaceAll, ReplaceBrush, Select }

        /// <summary>Something that changed the image and can be undone.</summary>
        private interface IStroke
        {
            /// <summary>The part of the image it touched, for redrawing.</summary>
            Rectangle Area { get; }

            /// <summary>Roughly how much memory it holds.</summary>
            long Bytes { get; }

            /// <summary>Put the image back (<paramref name="undo"/>) or forward again.</summary>
            void Apply(Color[] canvas, int width, bool undo);
        }

        /// <summary>A stroke stored as the rectangle it covered, before and after.</summary>
        private sealed record AreaStroke(Rectangle Area, Color[] Before, Color[] After) : IStroke
        {
            public long Bytes => (long)this.Area.Width * this.Area.Height * 8;

            public void Apply(Color[] canvas, int width, bool undo)
            {
                Paste(canvas, undo ? this.Before : this.After, this.Area, width);
            }
        }

        /// <summary>One colour swapped for another across the image, stored as the pixels it hit.</summary>
        private sealed record ColourStroke(Rectangle Area, int[] Pixels, Color Before, Color After) : IStroke
        {
            public long Bytes => (long)this.Pixels.Length * 4;

            public void Apply(Color[] canvas, int width, bool undo)
            {
                Color colour = undo ? this.Before : this.After;
                foreach (int index in this.Pixels)
                    canvas[index] = colour;
            }
        }

        /// <summary>How many strokes can be undone. Each one only keeps the pixels it touched.</summary>
        private const int MaxUndo = 40;

        private readonly int Width;
        private readonly int Height;

        /// <summary>The pixels being edited (straight alpha).</summary>
        private readonly Color[] Canvas;

        /// <summary>The size of one sprite in the image, for the grid; 0 for no grid.</summary>
        private readonly int CellWidth;
        private readonly int CellHeight;

        private readonly string Title;
        private readonly Action<Pixels> OnSave;

        private readonly List<IStroke> Undone = new();
        private readonly List<IStroke> Done = new();

        private Texture2D Texture = null!;
        private Rectangle CanvasArea;

        /// <summary>How many screen pixels one image pixel takes.</summary>
        private int Zoom = 1;

        /// <summary>The image pixel shown at the top left of the canvas area.</summary>
        private Point View;

        private Tool Current = Tool.Pencil;
        private Color Colour = Color.Black;
        private int BrushSize = 1;
        private bool ShowGrid = true;

        /// <summary>How many of the image's colours the palette offers.</summary>
        private const int PaletteSize = 18;

        /// <summary>The colours offered below the canvas, most used first.</summary>
        private readonly Color[] ByUse;

        /// <summary>The same colours sorted by hue, for when you're looking for a shade.</summary>
        private readonly Color[] ByHue;

        /// <summary>The palette as it's shown now.</summary>
        private Color[] Palette;

        /// <summary>Colours picked by hand, newest first, so they stay within reach while painting.</summary>
        private readonly List<Color> Recent = new();

        /// <summary>Where the colour swatches start, which moves when the selection buttons appear.</summary>
        private int SwatchesX;

        /// <summary>While drawing: where the last painted pixel was, the colour each touched pixel had before, and the area covered.</summary>
        private Point? LastPixel;
        private Dictionary<int, Color>? StrokeOriginals;
        private Rectangle StrokeArea;

        /// <summary>While dragging a line or rectangle: where it started and where the cursor is now.</summary>
        private Point? ShapeStart;
        private Point ShapeEnd;

        /// <summary>The colour the replace brush is swapping out, taken where the drag started.</summary>
        private Color ReplaceTarget;

        /// <summary>The selected rectangle, if any, and what's being done with it.</summary>
        private Rectangle? Selection;
        private bool DraggingSelection;
        private bool MovingSelection;
        private Point MoveGrabbedAt;
        private Rectangle MoveStartedAs;

        /// <summary>The pixels lifted for a move (the hole left behind is filled with see-through).</summary>
        private Color[]? Floating;

        /// <summary>What was copied, ready to paste.</summary>
        private Color[]? Clipboard;
        private Point ClipboardSize;

        /// <summary>Whether the view has been placed for the image yet.</summary>
        private bool Placed;

        /// <summary>Roughly how much memory the undo history may use, so painting on a huge sheet can't fill it up.</summary>
        private const long MaxUndoBytes = 48L * 1024 * 1024;

        private readonly Cycler ToolCycler;
        private readonly Cycler SizeCycler;
        private readonly Button UndoButton;
        private readonly Button RedoButton;
        private readonly Button ZoomInButton;
        private readonly Button ZoomOutButton;
        private readonly Button FitButton;
        private readonly Button WidthButton;
        private readonly Checkbox GridBox;
        private readonly Cycler PaletteCycler;
        private readonly Button ColourButton;
        private readonly Button CopyButton;
        private readonly Button PasteButton;
        private readonly Button ClearButton;
        private readonly Button FlipButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private string? Message;


        /*********
        ** Public methods
        *********/
        /// <param name="image">The image to edit (it isn't changed; a copy is edited).</param>
        /// <param name="title">The heading, e.g. "Paint 'Hairstyles'".</param>
        /// <param name="onSave">Called with the new pixels when you save.</param>
        /// <param name="cellWidth">The width of one sprite in the image, for the grid (0 for none).</param>
        /// <param name="cellHeight">The height of one sprite in the image, for the grid (0 for none).</param>
        public PaintScreen(Pixels image, string title, Action<Pixels> onSave, int cellWidth = 0, int cellHeight = 0)
        {
            this.Width = image.Width;
            this.Height = image.Height;
            this.Canvas = (Color[])image.Data.Clone();
            this.CellWidth = cellWidth;
            this.CellHeight = cellHeight;
            this.Title = title;
            this.OnSave = onSave;
            this.ByUse = GetPalette(this.Canvas);
            this.ByHue = ByColour(this.ByUse);
            this.Palette = this.ByUse;
            this.Colour = this.Palette.FirstOrDefault(c => c.A > 0, Color.Black);
            this.Texture = image.ToTexture();

            this.ToolCycler = this.Add(new Cycler(
                new()
                {
                    ("pencil", "Pencil"), ("eraser", "Eraser"), ("picker", "Eyedropper"), ("fill", "Fill"),
                    ("line", "Line"), ("rectangle", "Rectangle"), ("replaceall", "Replace colour"), ("replacebrush", "Replace brush"), ("select", "Select")
                },
                "pencil",
                v => this.Current = v switch
                {
                    "eraser" => Tool.Eraser,
                    "picker" => Tool.Picker,
                    "fill" => Tool.Fill,
                    "line" => Tool.Line,
                    "rectangle" => Tool.Rectangle,
                    "replaceall" => Tool.ReplaceAll,
                    "replacebrush" => Tool.ReplaceBrush,
                    "select" => Tool.Select,
                    _ => Tool.Pencil
                },
                "Pencil draws and the eraser makes pixels see-through. Fill replaces one connected area; 'replace colour' changes that colour everywhere in the image; the 'replace brush' only changes it where you drag. Select marks a rectangle you can move, copy, flip or clear. Right-click always picks the colour under the cursor."));
            this.SizeCycler = this.Add(new Cycler(
                new() { ("1", "1 pixel"), ("2", "2x2"), ("3", "3x3"), ("4", "4x4") },
                "1",
                v => this.BrushSize = int.Parse(v),
                "How many pixels the pencil and eraser cover."));
            this.UndoButton = this.Add(new Button("Undo", this.Undo, "Take back the last stroke."));
            this.RedoButton = this.Add(new Button("Redo", this.Redo));
            this.ZoomOutButton = this.Add(new Button("-", () => this.SetZoom(this.Zoom - 1, this.CanvasArea.Center), "Zoom out."));
            this.ZoomInButton = this.Add(new Button("+", () => this.SetZoom(this.Zoom + 1, this.CanvasArea.Center), "Zoom in. The mouse wheel works too."));
            this.FitButton = this.Add(new Button("Fit", this.Fit, "Show the whole image."));
            this.WidthButton = this.Add(new Button("Width", this.FitWidth, "Fill the width with the image, which is how it opens."));
            this.GridBox = this.Add(new Checkbox("Grid", true, v => this.ShowGrid = v, "Show where each sprite in the sheet begins."));
            this.PaletteCycler = this.Add(new Cycler(
                new() { ("used", "Most used"), ("hue", "By colour") },
                "used",
                v => this.Palette = v == "hue" ? this.ByHue : this.ByUse,
                "The colours taken from this image: in the order the image uses them most, or grouped by colour."));
            this.CopyButton = this.Add(new Button("Copy", this.CopySelection, "Copy what's selected."));
            this.PasteButton = this.Add(new Button("Paste", this.PasteClipboard, "Put what you copied in the top left of the selection (or of the view)."));
            this.ClearButton = this.Add(new Button("Clear", this.ClearSelection, "Make everything in the selection see-through."));
            this.FlipButton = this.Add(new Button("Flip", this.FlipSelection, "Mirror the selection left to right."));
            this.ColourButton = this.Add(new Button("Choose colour", this.ChooseColour, "Pick any colour, or type its red, green and blue values. The eyedropper takes a colour out of the image instead."));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", this.Cancel));
            this.SyncButtons();
        }

        public override void Dispose()
        {
            this.Texture.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;

            this.ToolCycler.Bounds = new Rectangle(area.X + pad, top, 260, 48);
            this.SizeCycler.Bounds = new Rectangle(this.ToolCycler.Bounds.Right + 12, top, 200, 48);
            this.UndoButton.Bounds = new Rectangle(this.SizeCycler.Bounds.Right + 24, top, 120, 48);
            this.RedoButton.Bounds = new Rectangle(this.UndoButton.Bounds.Right + 8, top, 120, 48);
            this.ZoomInButton.Bounds = new Rectangle(area.Right - pad - 48, top, 48, 48);
            this.ZoomOutButton.Bounds = new Rectangle(this.ZoomInButton.Bounds.X - 8 - 48, top, 48, 48);
            this.FitButton.Bounds = new Rectangle(this.ZoomOutButton.Bounds.X - 8 - 90, top, 90, 48);
            this.WidthButton.Bounds = new Rectangle(this.FitButton.Bounds.X - 8 - 110, top, 110, 48);
            this.GridBox.Bounds = new Rectangle(this.WidthButton.Bounds.X - 12 - 110, top + 2, 110, 44);

            int paletteH = 56;
            this.CanvasArea = new Rectangle(area.X + pad, top + 60, area.Width - pad * 2, bottom - (top + 60) - paletteH - 12);

            // the selection buttons share the palette row, so the top row doesn't overflow
            this.ColourButton.Bounds = new Rectangle(this.CanvasArea.X + 52, this.CanvasArea.Bottom + 8, 200, 44);
            int sx = this.ColourButton.Bounds.Right + 12;
            foreach (Button button in new[] { this.CopyButton, this.PasteButton, this.ClearButton, this.FlipButton })
            {
                button.Bounds = new Rectangle(sx, this.CanvasArea.Bottom + 8, 100, 44);
                if (button.Visible)
                    sx += 104;
            }
            this.SwatchesX = sx + 12;
            this.PaletteCycler.Bounds = new Rectangle(area.Right - pad - 300, this.CanvasArea.Bottom + 8, 300, 44);
            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);

            if (!this.Placed)
            {
                this.Placed = true;
                this.FitWidth();
            }
            this.ClampView();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, this.Title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            this.DrawCanvas(b, mouseX, mouseY);
            this.DrawPalette(b, mouseX, mouseY);
            base.Draw(b, mouseX, mouseY);

            string help = this.Message ?? $"{this.Width}x{this.Height}, {this.Zoom}x zoom. Drag to draw, right-click to pick a colour, wheel to zoom, arrow keys to move. B pencil, E eraser, I pick, F fill, L line, R rectangle, S select, Z undo.";
            Gfx.Message(b, help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? Color.DarkGreen : Color.DimGray);
        }

        /// <summary>Draw the image, the transparency checkerboard behind it and the sprite grid over it.</summary>
        private void DrawCanvas(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Inset(b, this.CanvasArea, new Color(60, 56, 52));
            Rectangle inner = this.Inner;

            // show whole pixels only: if the area doesn't divide by the zoom, the last part-pixel is left off rather than squashed
            int columns = Math.Min(inner.Width / this.Zoom, this.Width - this.View.X);
            int rows = Math.Min(inner.Height / this.Zoom, this.Height - this.View.Y);
            if (columns <= 0 || rows <= 0)
                return;
            int shownW = columns * this.Zoom, shownH = rows * this.Zoom;
            Rectangle dest = new(inner.X, inner.Y, shownW, shownH);
            Rectangle source = new(this.View.X, this.View.Y, columns, rows);

            // checkerboard, so see-through pixels are obvious. Its squares are a fixed size on screen, not one image
            // pixel each, so it can't be mistaken for the art.
            const int square = 8;
            for (int y = dest.Y; y < dest.Bottom; y += square)
            {
                for (int x = dest.X; x < dest.Right; x += square)
                {
                    bool dark = ((x - dest.X) / square + (y - dest.Y) / square) % 2 == 0;
                    Gfx.Rect(b, new Rectangle(x, y, Math.Min(square, dest.Right - x), Math.Min(square, dest.Bottom - y)), dark ? new Color(118, 118, 118) : new Color(148, 148, 148));
                }
            }

            b.Draw(this.Texture, dest, source, Color.White);

            // sprite grid
            if (this.ShowGrid && this.CellWidth > 0 && this.CellHeight > 0 && this.Zoom >= 2)
            {
                Color line = new(0, 0, 0, 90);
                for (int x = this.CellWidth - this.View.X % this.CellWidth; x * this.Zoom < shownW; x += this.CellWidth)
                    Gfx.Rect(b, new Rectangle(dest.X + x * this.Zoom, dest.Y, 1, shownH), line);
                for (int y = this.CellHeight - this.View.Y % this.CellHeight; y * this.Zoom < shownH; y += this.CellHeight)
                    Gfx.Rect(b, new Rectangle(dest.X, dest.Y + y * this.Zoom, shownW, 1), line);
            }

            // a faint grid on every pixel once they're big enough to aim at
            if (this.Zoom >= 8)
            {
                Color line = new(0, 0, 0, 40);
                for (int x = this.Zoom; x < shownW; x += this.Zoom)
                    Gfx.Rect(b, new Rectangle(dest.X + x, dest.Y, 1, shownH), line);
                for (int y = this.Zoom; y < shownH; y += this.Zoom)
                    Gfx.Rect(b, new Rectangle(dest.X, dest.Y + y, shownW, 1), line);
            }

            // where the line or rectangle being dragged will land
            if (this.ShapeStart is { } shapeStart)
            {
                foreach (Point dot in this.ShapePixels(shapeStart, this.ShapeEnd))
                {
                    int sx = dest.X + (dot.X - this.View.X) * this.Zoom, sy = dest.Y + (dot.Y - this.View.Y) * this.Zoom;
                    if (sx >= dest.X && sy >= dest.Y && sx < dest.Right && sy < dest.Bottom)
                        Gfx.Rect(b, new Rectangle(sx, sy, this.Zoom, this.Zoom), this.Colour);
                }
            }

            // the selection, and the pixels being dragged with it
            if (this.Selection is { } selected)
            {
                if (this.Floating is { } floating)
                {
                    for (int y = 0; y < selected.Height; y++)
                    {
                        for (int x = 0; x < selected.Width; x++)
                        {
                            Color colour = floating[y * selected.Width + x];
                            if (colour.A == 0)
                                continue;
                            int sx = dest.X + (selected.X + x - this.View.X) * this.Zoom, sy = dest.Y + (selected.Y + y - this.View.Y) * this.Zoom;
                            if (sx >= dest.X && sy >= dest.Y && sx < dest.Right && sy < dest.Bottom)
                                Gfx.Rect(b, new Rectangle(sx, sy, this.Zoom, this.Zoom), colour);
                        }
                    }
                }
                Rectangle box = new(dest.X + (selected.X - this.View.X) * this.Zoom, dest.Y + (selected.Y - this.View.Y) * this.Zoom, selected.Width * this.Zoom, selected.Height * this.Zoom);
                Gfx.Outline(b, box, Color.White, 2);
                Gfx.Outline(b, new Rectangle(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), Color.Black, 2);
            }

            // outline the pixel under the cursor
            if (this.ToPixel(mouseX, mouseY) is { } pixel && this.Zoom >= 3)
            {
                Rectangle box = new(dest.X + (pixel.X - this.View.X) * this.Zoom, dest.Y + (pixel.Y - this.View.Y) * this.Zoom, this.Zoom * this.BrushSize, this.Zoom * this.BrushSize);
                Gfx.Outline(b, box, Color.White, 1);
            }
        }

        /// <summary>Draw the colours from the image, with the current one marked.</summary>
        private void DrawPalette(SpriteBatch b, int mouseX, int mouseY)
        {
            int size = 40, gap = 6;
            int x = this.CanvasArea.X, y = this.CanvasArea.Bottom + 10;
            Gfx.Rect(b, new Rectangle(x, y, size, size), this.Colour);
            Gfx.Outline(b, new Rectangle(x, y, size, size), Color.Black, 2);
            x = this.SwatchesX;
            foreach (Color colour in this.Swatches)
            {
                Rectangle box = new(x, y, size, size);
                Gfx.Rect(b, box, colour);
                Gfx.Outline(b, box, colour == this.Colour ? Color.White : new Color(60, 56, 52), colour == this.Colour ? 3 : 1);
                x += size + gap;
                if (x + size > this.PaletteCycler.Bounds.X - 16)
                    break;
            }
        }


        /*********
        ** Input
        *********/
        public override void LeftClick(int x, int y)
        {
            if (this.CanvasArea.Contains(x, y))
            {
                this.StartStroke(x, y);
                return;
            }
            if (this.ClickPalette(x, y))
                return;
            base.LeftClick(x, y);
        }

        public override void LeftHeld(int x, int y)
        {
            if (this.DraggingSelection && this.ShapeStart is { } from)
            {
                if (this.ToPixel(x, y) is { } to)
                    this.Selection = Rectangle.Intersect(
                        new Rectangle(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X) + 1, Math.Abs(to.Y - from.Y) + 1),
                        new Rectangle(0, 0, this.Width, this.Height));
                return;
            }
            if (this.MovingSelection && this.Selection is { } moving)
            {
                if (this.ToPixel(x, y) is { } at)
                {
                    Rectangle moved = this.MoveStartedAs;
                    moved.X = Math.Clamp(this.MoveStartedAs.X + at.X - this.MoveGrabbedAt.X, -moved.Width + 1, this.Width - 1);
                    moved.Y = Math.Clamp(this.MoveStartedAs.Y + at.Y - this.MoveGrabbedAt.Y, -moved.Height + 1, this.Height - 1);
                    this.Selection = moved;
                    _ = moving;
                }
                return;
            }
            if (this.ShapeStart != null)
            {
                if (this.ToPixel(x, y) is { } pixel)
                    this.ShapeEnd = pixel;
                return;
            }
            if (this.StrokeOriginals != null)
                this.PaintAt(x, y);
        }

        public override void ReleaseLeft(int x, int y)
        {
            if (this.DraggingSelection)
            {
                this.DraggingSelection = false;
                this.ShapeStart = null;
                if (this.Selection is { Width: <= 1, Height: <= 1 })
                    this.Selection = null; // a plain click clears the selection
                this.SyncButtons();
                return;
            }
            if (this.MovingSelection)
            {
                this.MovingSelection = false;
                this.DropSelection();
                return;
            }
            if (this.ShapeStart is { } start)
            {
                this.StrokeOriginals = new Dictionary<int, Color>();
                this.StrokeArea = Rectangle.Empty;
                foreach (Point pixel in this.ShapePixels(start, this.ShapeEnd))
                    this.PaintDot(pixel.X, pixel.Y, this.Colour);
                this.Refresh(this.StrokeArea);
                this.ShapeStart = null;
            }
            this.CommitStroke();
        }

        /// <summary>Handle a right-click: always take the colour under the cursor, whichever tool is chosen.</summary>
        public override void RightClick(int x, int y)
        {
            if (this.ToPixel(x, y) is not { } pixel)
                return;
            this.Colour = this.Canvas[pixel.Y * this.Width + pixel.X];
            Game1.playSound("smallSelect");
        }

        public override void Scroll(int x, int y, int direction)
        {
            if (this.CanvasArea.Contains(x, y))
            {
                this.SetZoom(this.Zoom + (direction > 0 ? 1 : -1), new Point(x, y));
                return;
            }
            base.Scroll(x, y, direction);
        }

        public override bool KeyPress(Keys key)
        {
            switch (key)
            {
                case Keys.B: this.ToolCycler.Index = 0; this.Current = Tool.Pencil; return true;
                case Keys.E: this.ToolCycler.Index = 1; this.Current = Tool.Eraser; return true;
                case Keys.I: this.ToolCycler.Index = 2; this.Current = Tool.Picker; return true;
                case Keys.F: this.ToolCycler.Index = 3; this.Current = Tool.Fill; return true;
                case Keys.L: this.ToolCycler.Index = 4; this.Current = Tool.Line; return true;
                case Keys.R: this.ToolCycler.Index = 5; this.Current = Tool.Rectangle; return true;
                case Keys.S: this.ToolCycler.Index = 8; this.Current = Tool.Select; return true;
                case Keys.C: this.CopySelection(); return true;
                case Keys.V: this.PasteClipboard(); return true;
                case Keys.Delete: this.ClearSelection(); return true;
                case Keys.Z: this.Undo(); return true;
                case Keys.Y: this.Redo(); return true;
                case Keys.Left: this.View.X -= 8; this.ClampView(); return true;
                case Keys.Right: this.View.X += 8; this.ClampView(); return true;
                case Keys.Up: this.View.Y -= 8; this.ClampView(); return true;
                case Keys.Down: this.View.Y += 8; this.ClampView(); return true;
                default: return false;
            }
        }

        /// <summary>Handle a click on one of the palette colours.</summary>
        private bool ClickPalette(int x, int y)
        {
            int size = 40, gap = 6;
            int px = this.SwatchesX, py = this.CanvasArea.Bottom + 10;
            if (y < py || y > py + size)
                return false;
            foreach (Color colour in this.Swatches)
            {
                if (new Rectangle(px, py, size, size).Contains(x, y))
                {
                    this.Colour = colour;
                    Game1.playSound("smallSelect");
                    return true;
                }
                px += size + gap;
                if (px + size > this.PaletteCycler.Bounds.X - 16)
                    break;
            }
            return false;
        }


        /*********
        ** Drawing on the image
        *********/
        /// <summary>The part of the canvas area the image is drawn in.</summary>
        private Rectangle Inner => new(this.CanvasArea.X + 8, this.CanvasArea.Y + 8, Math.Max(1, this.CanvasArea.Width - 16), Math.Max(1, this.CanvasArea.Height - 16));

        /// <summary>The image pixel under a screen point, if the cursor is over the image.</summary>
        private Point? ToPixel(int x, int y)
        {
            Rectangle inner = this.Inner;
            if (!inner.Contains(x, y))
                return null;
            int px = this.View.X + (x - inner.X) / this.Zoom;
            int py = this.View.Y + (y - inner.Y) / this.Zoom;
            return px >= 0 && py >= 0 && px < this.Width && py < this.Height ? new Point(px, py) : null;
        }

        private void StartStroke(int x, int y)
        {
            if (this.ToPixel(x, y) is not { } pixel)
                return;

            if (this.Current == Tool.Picker)
            {
                this.Colour = this.Canvas[pixel.Y * this.Width + pixel.X];
                Game1.playSound("smallSelect");
                return;
            }

            if (this.Current == Tool.Select)
            {
                if (this.Selection is { } current && current.Contains(pixel))
                {
                    this.MovingSelection = true;
                    this.MoveGrabbedAt = pixel;
                    this.MoveStartedAs = current;
                    this.LiftSelection();
                }
                else
                {
                    this.DraggingSelection = true;
                    this.ShapeStart = pixel;
                    this.ShapeEnd = pixel;
                    this.Selection = new Rectangle(pixel.X, pixel.Y, 1, 1);
                }
                return;
            }

            // a line or rectangle is only drawn when you let go, so you can see where it will land first
            if (this.Current is Tool.Line or Tool.Rectangle)
            {
                this.ShapeStart = pixel;
                this.ShapeEnd = pixel;
                return;
            }

            this.StrokeOriginals = new Dictionary<int, Color>();
            this.StrokeArea = Rectangle.Empty;
            this.LastPixel = null;

            if (this.Current == Tool.Fill)
            {
                this.Fill(pixel, this.Colour);
                this.Refresh(this.StrokeArea);
                this.CommitStroke();
                return;
            }
            if (this.Current == Tool.ReplaceAll)
            {
                this.ReplaceEverywhere(this.Canvas[pixel.Y * this.Width + pixel.X]);
                return;
            }
            if (this.Current == Tool.ReplaceBrush)
                this.ReplaceTarget = this.Canvas[pixel.Y * this.Width + pixel.X];
            this.PaintAt(x, y);
        }

        private void PaintAt(int x, int y)
        {
            if (this.ToPixel(x, y) is not { } pixel)
                return;
            Color colour = this.Current == Tool.Eraser ? Color.Transparent : this.Colour;

            // join the dots, so a fast drag doesn't leave gaps
            Point from = this.LastPixel ?? pixel;
            int steps = Math.Max(Math.Abs(pixel.X - from.X), Math.Abs(pixel.Y - from.Y));
            for (int i = 0; i <= steps; i++)
            {
                int px = steps == 0 ? pixel.X : from.X + (pixel.X - from.X) * i / steps;
                int py = steps == 0 ? pixel.Y : from.Y + (pixel.Y - from.Y) * i / steps;
                this.PaintDot(px, py, colour);
            }
            Rectangle painted = Rectangle.Union(
                new Rectangle(from.X, from.Y, this.BrushSize, this.BrushSize),
                new Rectangle(pixel.X, pixel.Y, this.BrushSize, this.BrushSize));
            this.LastPixel = pixel;
            this.Refresh(Rectangle.Intersect(painted, new Rectangle(0, 0, this.Width, this.Height)));
        }

        private void PaintDot(int x, int y, Color colour)
        {
            for (int dy = 0; dy < this.BrushSize; dy++)
            {
                for (int dx = 0; dx < this.BrushSize; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= this.Width || py >= this.Height)
                        continue;
                    if (this.Current == Tool.ReplaceBrush && this.Canvas[py * this.Width + px] != this.ReplaceTarget)
                        continue;
                    this.Remember(py * this.Width + px);
                    this.Canvas[py * this.Width + px] = colour;
                    this.Grow(px, py);
                }
            }
        }

        /// <summary>Take the selected pixels out of the image, leaving see-through behind, so they can be dragged around.</summary>
        private void LiftSelection()
        {
            if (this.Selection is not { } area || this.Floating != null)
                return;
            this.StrokeOriginals = new Dictionary<int, Color>();
            this.StrokeArea = Rectangle.Empty;
            this.Floating = Cut(this.Canvas, area, this.Width);
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    int index = (area.Y + y) * this.Width + area.X + x;
                    this.Remember(index);
                    this.Canvas[index] = Color.Transparent;
                    this.Grow(area.X + x, area.Y + y);
                }
            }
            this.Refresh(area);
        }

        /// <summary>Put the pixels being dragged down where the selection now is.</summary>
        private void DropSelection()
        {
            if (this.Floating is not { } pixels || this.Selection is not { } area)
                return;
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    int px = area.X + x, py = area.Y + y;
                    if (px < 0 || py < 0 || px >= this.Width || py >= this.Height)
                        continue;
                    Color colour = pixels[y * area.Width + x];
                    if (colour.A == 0)
                        continue;
                    int index = py * this.Width + px;
                    this.Remember(index);
                    this.Canvas[index] = colour;
                    this.Grow(px, py);
                }
            }
            this.Floating = null;
            this.Refresh(Rectangle.Intersect(area, new Rectangle(0, 0, this.Width, this.Height)));
            this.CommitStroke();
        }

        /// <summary>Copy the selected pixels.</summary>
        private void CopySelection()
        {
            if (this.Selection is not { } area)
                return;
            this.Clipboard = Cut(this.Canvas, area, this.Width);
            this.ClipboardSize = new Point(area.Width, area.Height);
            this.Message = $"Copied {area.Width}x{area.Height} pixels.";
            this.SyncButtons();
        }

        /// <summary>Paste what was copied at the selection's top left, or at the top left of the view.</summary>
        private void PasteClipboard()
        {
            if (this.Clipboard is not { } pixels)
                return;
            Point at = this.Selection is { } area ? new Point(area.X, area.Y) : this.View;
            this.StrokeOriginals = new Dictionary<int, Color>();
            this.StrokeArea = Rectangle.Empty;
            for (int y = 0; y < this.ClipboardSize.Y; y++)
            {
                for (int x = 0; x < this.ClipboardSize.X; x++)
                {
                    int px = at.X + x, py = at.Y + y;
                    if (px < 0 || py < 0 || px >= this.Width || py >= this.Height)
                        continue;
                    int index = py * this.Width + px;
                    this.Remember(index);
                    this.Canvas[index] = pixels[y * this.ClipboardSize.X + x];
                    this.Grow(px, py);
                }
            }
            this.Refresh(this.StrokeArea);
            this.CommitStroke();
            this.Selection = new Rectangle(at.X, at.Y, this.ClipboardSize.X, this.ClipboardSize.Y);
            this.Message = "Pasted.";
        }

        /// <summary>Make everything in the selection see-through.</summary>
        private void ClearSelection()
        {
            if (this.Selection is not { } area)
                return;
            this.StrokeOriginals = new Dictionary<int, Color>();
            this.StrokeArea = Rectangle.Empty;
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    int index = (area.Y + y) * this.Width + area.X + x;
                    this.Remember(index);
                    this.Canvas[index] = Color.Transparent;
                    this.Grow(area.X + x, area.Y + y);
                }
            }
            this.Refresh(area);
            this.CommitStroke();
        }

        /// <summary>Mirror the selection left to right.</summary>
        private void FlipSelection()
        {
            if (this.Selection is not { } area)
                return;
            Color[] copy = Cut(this.Canvas, area, this.Width);
            this.StrokeOriginals = new Dictionary<int, Color>();
            this.StrokeArea = Rectangle.Empty;
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    int index = (area.Y + y) * this.Width + area.X + x;
                    this.Remember(index);
                    this.Canvas[index] = copy[y * area.Width + (area.Width - 1 - x)];
                    this.Grow(area.X + x, area.Y + y);
                }
            }
            this.Refresh(area);
            this.CommitStroke();
        }

        /// <summary>The pixels a line or rectangle covers, from where the drag started to where it is now.</summary>
        private IEnumerable<Point> ShapePixels(Point start, Point end)
        {
            if (this.Current == Tool.Rectangle)
            {
                int left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
                int top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
                for (int x = left; x <= right; x++)
                {
                    yield return new Point(x, top);
                    yield return new Point(x, bottom);
                }
                for (int y = top; y <= bottom; y++)
                {
                    yield return new Point(left, y);
                    yield return new Point(right, y);
                }
                yield break;
            }

            int steps = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            for (int i = 0; i <= steps; i++)
            {
                yield return steps == 0
                    ? start
                    : new Point(start.X + (end.X - start.X) * i / steps, start.Y + (end.Y - start.Y) * i / steps);
            }
        }

        /// <summary>Swap one colour for another everywhere in the image.</summary>
        private void ReplaceEverywhere(Color target)
        {
            if (target == this.Colour)
                return;
            List<int> changed = new();
            Rectangle area = Rectangle.Empty;
            for (int i = 0; i < this.Canvas.Length; i++)
            {
                if (this.Canvas[i] != target)
                    continue;
                this.Canvas[i] = this.Colour;
                changed.Add(i);
                int x = i % this.Width, y = i / this.Width;
                area = area.IsEmpty ? new Rectangle(x, y, 1, 1) : Rectangle.Union(area, new Rectangle(x, y, 1, 1));
            }
            this.StrokeOriginals = null;
            if (changed.Count == 0)
            {
                this.Message = "No pixels of that colour.";
                return;
            }
            this.Done.Add(new ColourStroke(area, changed.ToArray(), target, this.Colour));
            this.TrimHistory();
            this.Undone.Clear();
            this.Refresh(area);
            this.Message = $"Replaced {changed.Count:n0} {(target.A == 0 ? "see-through " : "")}pixel{(changed.Count == 1 ? "" : "s")}. Undo puts them back.";
            this.SyncButtons();
        }

        /// <summary>Replace the connected area of one colour, like a paint bucket.</summary>
        private void Fill(Point start, Color colour)
        {
            Color target = this.Canvas[start.Y * this.Width + start.X];
            if (target == colour)
                return;

            Stack<Point> todo = new();
            todo.Push(start);
            while (todo.Count > 0)
            {
                Point p = todo.Pop();
                if (p.X < 0 || p.Y < 0 || p.X >= this.Width || p.Y >= this.Height)
                    continue;
                int index = p.Y * this.Width + p.X;
                if (this.Canvas[index] != target)
                    continue;
                this.Remember(index);
                this.Canvas[index] = colour;
                this.Grow(p.X, p.Y);
                todo.Push(new Point(p.X + 1, p.Y));
                todo.Push(new Point(p.X - 1, p.Y));
                todo.Push(new Point(p.X, p.Y + 1));
                todo.Push(new Point(p.X, p.Y - 1));
            }
        }

        /// <summary>Keep a pixel's colour from before this stroke, once, so the stroke can be undone.</summary>
        private void Remember(int index)
        {
            this.StrokeOriginals?.TryAdd(index, this.Canvas[index]);
        }

        /// <summary>Note that a pixel changed, so only that part of the image is stored and re-uploaded.</summary>
        private void Grow(int x, int y)
        {
            this.StrokeArea = this.StrokeArea.IsEmpty
                ? new Rectangle(x, y, 1, 1)
                : Rectangle.Union(this.StrokeArea, new Rectangle(x, y, 1, 1));
        }

        private void CommitStroke()
        {
            if (this.StrokeOriginals == null)
                return;
            if (!this.StrokeArea.IsEmpty)
            {
                // the 'before' picture is the area as it is now, with the touched pixels put back
                Color[] before = Cut(this.Canvas, this.StrokeArea, this.Width);
                foreach ((int index, Color colour) in this.StrokeOriginals)
                {
                    int x = index % this.Width - this.StrokeArea.X, y = index / this.Width - this.StrokeArea.Y;
                    if (x >= 0 && y >= 0 && x < this.StrokeArea.Width && y < this.StrokeArea.Height)
                        before[y * this.StrokeArea.Width + x] = colour;
                }
                this.Done.Add(new AreaStroke(this.StrokeArea, before, Cut(this.Canvas, this.StrokeArea, this.Width)));
                this.TrimHistory();
                this.Undone.Clear();
            }
            this.StrokeOriginals = null;
            this.LastPixel = null;
            this.StrokeArea = Rectangle.Empty;
            this.Message = null;
            this.SyncButtons();
        }

        /// <summary>Drop the oldest strokes when the history gets too big (one fill on a large sheet can be many megabytes).</summary>
        private void TrimHistory()
        {
            long bytes = this.Done.Sum(s => s.Bytes);
            while (this.Done.Count > 1 && (this.Done.Count > MaxUndo || bytes > MaxUndoBytes))
            {
                bytes -= this.Done[0].Bytes;
                this.Done.RemoveAt(0);
            }
        }

        private void Undo()
        {
            if (this.Done.Count == 0)
                return;
            IStroke stroke = this.Done[^1];
            this.Done.RemoveAt(this.Done.Count - 1);
            this.Undone.Add(stroke);
            stroke.Apply(this.Canvas, this.Width, undo: true);
            this.Refresh(stroke.Area);
            this.SyncButtons();
        }

        private void Redo()
        {
            if (this.Undone.Count == 0)
                return;
            IStroke stroke = this.Undone[^1];
            this.Undone.RemoveAt(this.Undone.Count - 1);
            this.Done.Add(stroke);
            stroke.Apply(this.Canvas, this.Width, undo: false);
            this.Refresh(stroke.Area);
            this.SyncButtons();
        }

        /// <summary>Copy the changed pixels into the texture the canvas draws.</summary>
        private void Refresh(Rectangle area)
        {
            if (area.IsEmpty)
                return;
            Color[] part = new Color[area.Width * area.Height];
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < area.Width; x++)
                {
                    Color c = this.Canvas[(area.Y + y) * this.Width + area.X + x];
                    part[y * area.Width + x] = Color.FromNonPremultiplied(c.R, c.G, c.B, c.A);
                }
            }
            this.Texture.SetData(0, area, part, 0, part.Length);
        }


        /*********
        ** View
        *********/
        private void SetZoom(int zoom, Point around)
        {
            int old = this.Zoom;
            this.Zoom = Math.Clamp(zoom, 1, 24);
            if (this.Zoom == old)
                return;

            // keep the pixel under the cursor where it is
            Rectangle inner = this.Inner;
            double px = this.View.X + (around.X - inner.X) / (double)old;
            double py = this.View.Y + (around.Y - inner.Y) / (double)old;
            this.View = new Point((int)Math.Round(px - (around.X - inner.X) / (double)this.Zoom), (int)Math.Round(py - (around.Y - inner.Y) / (double)this.Zoom));
            this.ClampView();
        }

        /// <summary>Zoom so the image fills the width, which is the most useful view for pixel art.</summary>
        private void FitWidth()
        {
            Rectangle inner = this.Inner;
            this.Zoom = Math.Clamp(inner.Width / Math.Max(1, this.Width), 1, 24);
            this.View = Point.Zero;
            this.ClampView();
        }

        private void Fit()
        {
            Rectangle inner = this.Inner;
            this.Zoom = Math.Max(1, Math.Min(inner.Width / Math.Max(1, this.Width), inner.Height / Math.Max(1, this.Height)));
            this.View = Point.Zero;
        }

        private void ClampView()
        {
            Rectangle inner = this.Inner;
            int maxX = Math.Max(0, this.Width - inner.Width / Math.Max(1, this.Zoom));
            int maxY = Math.Max(0, this.Height - inner.Height / Math.Max(1, this.Zoom));
            this.View = new Point(Math.Clamp(this.View.X, 0, maxX), Math.Clamp(this.View.Y, 0, maxY));
        }


        /*********
        ** Private methods
        *********/
        /// <summary>The colours in the row under the canvas: the ones you picked by hand first, then the image's own.</summary>
        private IEnumerable<Color> Swatches => this.Recent.Concat(this.Palette);

        /// <summary>Open the colour picker and keep what comes back within reach.</summary>
        private void ChooseColour()
        {
            this.Root.Push(new ColourPickerScreen(this.Colour, colour =>
            {
                this.Colour = colour;
                this.Recent.Remove(colour);
                this.Recent.Insert(0, colour);
                if (this.Recent.Count > 6)
                    this.Recent.RemoveAt(this.Recent.Count - 1);
            }));
        }

        private void SyncButtons()
        {
            this.UndoButton.Enabled = this.Done.Count > 0;
            this.RedoButton.Visible = this.Undone.Count > 0;
            this.CopyButton.Visible = this.Selection != null;
            this.ClearButton.Visible = this.Selection != null;
            this.FlipButton.Visible = this.Selection != null;
            this.PasteButton.Visible = this.Clipboard != null;
            if (this.Area.Width > 0)
                this.Layout(this.Area); // the palette row shifts as those buttons come and go
        }

        private void Save()
        {
            this.OnSave(new Pixels((Color[])this.Canvas.Clone(), this.Width, this.Height));
            Game1.playSound("newArtifact");
            this.Root.Pop();
        }

        private void Cancel()
        {
            if (this.Done.Count == 0)
            {
                this.Root.Pop();
                return;
            }
            this.Root.Push(new ConfirmScreen("Throw away your changes to this image?", "Throw away", () => this.Root.Pop()));
        }

        /// <summary>
        /// The colours already in the image, most used first, so you can paint with the art's own palette. Big sheets are
        /// sampled rather than counted pixel by pixel: with millions of pixels every colour worth showing turns up in the
        /// sample anyway, and it keeps opening the screen instant.
        /// </summary>
        private static Color[] GetPalette(Color[] pixels)
        {
            const int maxSamples = 400_000;
            int step = Math.Max(1, pixels.Length / maxSamples);
            Dictionary<Color, int> counts = new();
            for (int i = 0; i < pixels.Length; i += step)
            {
                Color colour = pixels[i];
                if (colour.A == 0)
                    continue;
                counts.TryGetValue(colour, out int count);
                counts[colour] = count + 1;
            }
            List<Color> palette = counts.OrderByDescending(p => p.Value).Take(PaletteSize).Select(p => p.Key).ToList();
            foreach (Color extra in new[] { Color.Black, Color.White })
            {
                if (!palette.Contains(extra))
                    palette.Add(extra);
            }
            return palette.ToArray();
        }

        /// <summary>Sort the palette by colour instead of by how much it's used, which makes shades easier to find.</summary>
        private static Color[] ByColour(IEnumerable<Color> palette)
        {
            return palette
                .OrderBy(c =>
                {
                    float max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f, min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255f;
                    return max - min < 0.04f ? -1f : Hue(c); // greys first, then colours by hue
                })
                .ThenBy(c => (c.R + c.G + c.B) / 3)
                .ToArray();
        }

        /// <summary>The hue of a colour, 0-360.</summary>
        private static float Hue(Color c)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d <= 0)
                return 0;
            float hue = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            hue *= 60;
            return hue < 0 ? hue + 360 : hue;
        }

        /// <summary>Copy one area out of an image.</summary>
        private static Color[] Cut(Color[] pixels, Rectangle area, int width)
        {
            Color[] result = new Color[area.Width * area.Height];
            for (int y = 0; y < area.Height; y++)
                Array.Copy(pixels, (area.Y + y) * width + area.X, result, y * area.Width, area.Width);
            return result;
        }

        /// <summary>Copy one area back into an image.</summary>
        private static void Paste(Color[] pixels, Color[] part, Rectangle area, int width)
        {
            for (int y = 0; y < area.Height; y++)
                Array.Copy(part, y * area.Width, pixels, (area.Y + y) * width + area.X, area.Width);
        }
    }
}
