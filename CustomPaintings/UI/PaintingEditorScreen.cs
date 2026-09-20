using System;
using CustomContentCore;
using CustomContentCore.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;

namespace CustomPaintings.UI
{
    /// <summary>Edits one new painting, photo frame or replacement.</summary>
    internal sealed class PaintingEditorScreen : Screen
    {
        /*********
        ** Types
        *********/
        private sealed class LoadedImage
        {
            public Pixels Full = null!;
            public Pixels Work = null!;
            public Texture2D Display = null!;
        }

        private enum DragMode { None, Move, Resize }


        /*********
        ** Fields
        *********/
        private static readonly string[] WallSizes = { "1x1", "2x1", "1x2", "2x2", "3x2", "2x3", "3x3", "4x2", "4x3", "1x3", "3x1", "4x1", "5x3", "6x3" };

        private static readonly (int Minutes, string Label)[] SlideSpeeds =
        {
            (0, "New image each day"), (10, "Every 10 minutes"), (30, "Every 30 minutes"), (60, "Every hour"), (120, "Every 2 hours"), (180, "Every 3 hours"), (360, "Every 6 hours"),
            (-150, "Animate: fast"), (-300, "Animate: medium"), (-600, "Animate: slow")
        };

        private static readonly (float Chance, string Label)[] Chances =
        {
            (0.01f, "1% per catch"), (0.02f, "2% per catch"), (0.05f, "5% per catch"), (0.1f, "10% per catch"), (0.15f, "15% per catch"), (0.25f, "25% per catch"), (0.5f, "50% per catch"), (1f, "100% per catch")
        };

        private readonly PaintingStore Store;
        private readonly bool IsNew;
        private readonly CustomPainting? Painting;
        private readonly Replacement? Replacement;
        private readonly string? OriginalId;
        private readonly (int W, int H) ReplacementSize;
        private readonly string ReplacementName;
        /// <summary>Called after saving, with the saved name.</summary>
        private readonly Action<string> OnSaved;
        private readonly List<string> ImportedThisSession = new();

        private readonly List<Slide> Slides;
        private readonly Dictionary<string, LoadedImage?> Images = new(StringComparer.OrdinalIgnoreCase);
        private int SlideIndex;

        private Texture2D? PreviewTexture;
        private int PreviewScale = 1;
        private bool PreviewDirty = true;
        private long LastPreviewRender;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        // crop dragging
        private DragMode Drag;
        private Vector2 DragAnchor;
        private Vector2 DragOffset;
        private Rectangle CropBox;
        private Rectangle ImageDest;

        // widgets
        private readonly TextField NameField;
        private readonly TextField DescriptionField;
        private readonly TextField CaptionField;
        private readonly Cycler TypeCycler;
        private readonly Cycler SizeCycler;
        private readonly Cycler FrameCycler;
        private readonly Cycler ResolutionCycler;
        private readonly TextField PriceField;
        private readonly Checkbox CatalogueBox;
        private readonly Cycler SpeedCycler;
        private readonly ScrollList<Source> SourceList;
        private readonly Button AddShopButton;
        private readonly Button AddFishingButton;
        private readonly Cycler SourceShopCycler;
        private readonly Cycler SourceLocationCycler;
        private readonly Cycler SourceChanceCycler;
        private readonly TextField SourcePriceField;
        private readonly Checkbox SourceOnceBox;
        private readonly Button RemoveSourceButton;
        private readonly Button AddImageButton;
        private readonly Button ChangeImageButton;
        private readonly Button RemoveImageButton;
        private readonly Button MoveLeftButton;
        private readonly Button FitButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle PreviewBox;
        private Rectangle SlideStrip;
        private readonly List<(Rectangle Bounds, int Index)> SlideThumbs = new();
        private readonly List<(Rectangle Row, string Label)> FormLabels = new();


        /*********
        ** Accessors
        *********/
        private ImageSettings Settings => (ImageSettings?)this.Painting ?? this.Replacement!;
        private bool IsTable => this.Painting?.IsTable == true;
        private Source? SelectedSource => this.SourceList.Selected;


        /*********
        ** Public methods
        *********/
        /// <summary>Edit a new or existing painting.</summary>
        public PaintingEditorScreen(PaintingStore store, CustomPainting painting, bool isNew, Action<string> onSaved)
            : this(store, painting, null, isNew, onSaved, (0, 0), "") { }

        /// <summary>Edit a replacement for an existing painting.</summary>
        public PaintingEditorScreen(PaintingStore store, Replacement replacement, bool isNew, (int W, int H) size, string originalName, Action<string> onSaved)
            : this(store, null, replacement, isNew, onSaved, size, originalName) { }

        private PaintingEditorScreen(PaintingStore store, CustomPainting? painting, Replacement? replacement, bool isNew, Action<string> onSaved, (int W, int H) replacementSize, string replacementName)
        {
            this.Store = store;
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            this.ReplacementSize = replacementSize;
            this.ReplacementName = replacementName;

            // work on copies so Cancel discards changes
            this.Painting = painting != null ? Clone(painting) : null;
            this.Replacement = replacement != null ? Clone(replacement) : null;
            this.OriginalId = painting?.Id;
            this.Slides = this.Settings.GetSlides().Select(Clone).ToList();
            this.Settings.Frame = this.Store.GetFrame(this.Settings.Frame).Name;

            // resolve 'auto' size now so the crop has a fixed shape
            if (this.Painting != null && !this.Painting.IsTable && !PaintingStore.TryParseSize(this.Painting.Size, out _))
            {
                LoadedImage? first = this.Slides.Count > 0 ? this.GetImage(this.Slides[0].File) : null;
                var size = this.Store.GetPaintingSize(this.Painting, first?.Full);
                this.Painting.Size = $"{size.W}x{size.H}";
            }
            else if (this.Painting?.IsTable == true)
                this.Painting.Size = PaintingStore.TryParseSize(this.Painting.Size, out var s) && s.H >= 2 ? "1x2" : "1x1";

            // form
            string name = this.Painting?.Name ?? this.Replacement?.Name ?? "";
            this.NameField = this.Add(new TextField(name, v => { if (this.Painting != null) this.Painting.Name = v; else this.Replacement!.Name = string.IsNullOrWhiteSpace(v) ? null : v; }, limit: 80));
            this.DescriptionField = this.Add(new TextField(this.Settings.Description ?? "", v => this.Settings.Description = string.IsNullOrWhiteSpace(v) ? null : v, limit: 500));
            this.CaptionField = this.Add(new TextField("", v => { if (this.CurrentSlide is { } slide) slide.Caption = string.IsNullOrWhiteSpace(v) ? null : v; }, limit: 500));

            this.TypeCycler = this.Add(new Cycler(
                new() { ("wall", "Wall"), ("table", "Table") },
                this.Painting?.Placement ?? "wall",
                this.OnTypeChanged,
                "Wall: a painting that hangs on a wall. Table: a small standing photo frame for tables and floors."
            ));
            this.SizeCycler = this.Add(new Cycler(this.GetSizeOptions(), this.Painting?.Size, v => { if (this.Painting != null) this.Painting.Size = v; this.OnShapeChanged(); }, "The size in tiles (width x height)."));
            this.FrameCycler = this.Add(new Cycler(this.Store.Frames.Select(f => (f.Name, f.DisplayName)).ToList(), this.Settings.Frame, v => { this.Settings.Frame = v; this.OnShapeChanged(); }, "Add your own frames as images in the mod's 'frames' folder."));

            this.ResolutionCycler = this.Add(new Cycler(
                new() { ("0", "Auto"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") },
                this.Settings.Resolution <= 0 ? "0" : this.Settings.Resolution >= 64 ? "64" : this.Settings.Resolution >= 32 ? "32" : "16",
                v => { this.Settings.Resolution = int.Parse(v); this.PreviewDirty = true; },
                "How detailed the painting looks when placed in the world. Auto matches your screen at the current zoom (sharpest). Pixel art matches the game's style (16 px per tile), Sharp is 2x and HD is 4x."
            ));

            string price = this.Painting != null ? this.Painting.Price.ToString() : this.Replacement!.Price?.ToString() ?? "";
            this.PriceField = this.Add(new TextField(price, v =>
            {
                int.TryParse(v, out int value);
                if (this.Painting != null)
                    this.Painting.Price = Math.Max(0, value);
                else
                    this.Replacement!.Price = string.IsNullOrWhiteSpace(v) ? null : Math.Max(0, value);
            }, numbersOnly: true, limit: 8));
            this.CatalogueBox = this.Add(new Checkbox("In catalogue", this.Painting?.InCatalogue ?? false, v => { if (this.Painting != null) { this.Painting.InCatalogue = v; this.Layout(this.Area); } },
                "Sold in the Furniture Catalogue, and can show up in random furniture slots at Robin's and the traveling cart."));
            this.SpeedCycler = this.Add(new Cycler(SlideSpeeds.Select(s => (s.Minutes.ToString(), s.Label)).ToList(), this.Settings.AnimationMs > 0 ? (-this.Settings.AnimationMs).ToString() : this.Settings.SlideMinutes.ToString(),
                v =>
                {
                    int value = int.Parse(v);
                    this.Settings.AnimationMs = value < 0 ? -value : 0; // negative values mean an animation in real time
                    this.Settings.SlideMinutes = value < 0 ? 0 : value;
                },
                "How often it switches to the next image. The 'Animate' options flip through them quickly, like a moving picture."));

            // sources
            List<Source> sources = this.Painting?.Sources ?? this.Replacement!.Sources;
            this.SourceList = this.Add(new ScrollList<Source>(40, this.DrawSource) { Items = sources, OnSelect = (_, _) => this.SyncSourceControls() });
            this.AddShopButton = this.Add(new Button("+ Shop", () => this.AddSource(new Source { Type = "Shop", Shop = "SeedShop" }), "Sell it in a shop."));
            this.AddFishingButton = this.Add(new Button("+ Fishing", () => this.AddSource(new Source { Type = "Fishing", Location = "GingerIsland", Chance = 0.05f }), "Make it a possible catch while fishing."));
            this.SourceShopCycler = this.Add(new Cycler(GetShopOptions(), null, v => { if (this.SelectedSource != null) this.SelectedSource.Shop = v; }));
            this.SourceLocationCycler = this.Add(new Cycler(Names.FishingLocations.Select(l => (l.Id, l.Label)).ToList(), null, v => { if (this.SelectedSource != null) this.SelectedSource.Location = v; }));
            this.SourceChanceCycler = this.Add(new Cycler(Chances.Select(c => (c.Chance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), c.Label)).ToList(), null, v =>
            {
                if (this.SelectedSource != null)
                    this.SelectedSource.Chance = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            }));
            this.SourcePriceField = this.Add(new TextField("", v =>
            {
                if (this.SelectedSource != null)
                    this.SelectedSource.Price = int.TryParse(v, out int p) ? Math.Max(0, p) : null;
            }, numbersOnly: true, limit: 8));
            this.SourceOnceBox = this.Add(new Checkbox("Only once", false, v => { if (this.SelectedSource != null) this.SelectedSource.Once = v; }, "Each player can only catch it once."));
            this.RemoveSourceButton = this.Add(new Button("Remove", this.RemoveSource));

            // slides
            this.AddImageButton = this.Add(new Button("Add image", () => this.BrowseImage(replace: false), "Add another image. Paintings with several images become a slideshow."));
            this.ChangeImageButton = this.Add(new Button("Change", () => this.BrowseImage(replace: true), "Replace the selected image."));
            this.RemoveImageButton = this.Add(new Button("Remove", this.RemoveSlide, "Remove the selected image."));
            this.MoveLeftButton = this.Add(new Button("Move left", this.MoveSlideLeft, "Show the selected image earlier in the slideshow."));
            this.FitButton = this.Add(new Button("Fit image", this.FitCrop, "Reset the crop to the largest area that fits."));

            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", this.Cancel));

            // make sure every slide has a crop matching the shape
            foreach (Slide slide in this.Slides)
                this.EnsureCrop(slide, refit: false);
            this.SyncSlideControls();
            this.SyncSourceControls();
        }

        public override void Dispose()
        {
            foreach (LoadedImage? image in this.Images.Values)
                image?.Display.Dispose();
            this.Images.Clear();
            this.PreviewTexture?.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int leftW = (int)(area.Width * 0.5);
            int rightX = area.X + pad + leftW + 24;
            int rightW = area.Right - pad - rightX;
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;

            // left: crop box, slide strip, caption
            int stripH = 96;
            int captionH = this.Slides.Count > 1 ? 56 : 0;
            this.CropBox = new Rectangle(area.X + pad, top + 52, leftW, bottom - top - 52 - stripH - 16 - captionH - 12);
            this.FitButton.Bounds = new Rectangle(area.X + pad + leftW - 200, top, 200, 44);
            this.SlideStrip = new Rectangle(area.X + pad, this.CropBox.Bottom + 12, leftW, stripH);
            int bx = this.SlideStrip.X;
            int by = this.SlideStrip.Bottom + 8;
            this.CaptionField.Bounds = new Rectangle(area.X + pad + 110, by, leftW - 110, 48);
            this.CaptionField.Visible = this.Slides.Count > 1;

            // slide buttons inside the strip's right side
            int buttonsW = 240;
            this.SlideStrip.Width = leftW - buttonsW - 8;
            this.AddImageButton.Bounds = new Rectangle(this.SlideStrip.Right + 8, this.SlideStrip.Y, buttonsW, 44);
            this.ChangeImageButton.Bounds = new Rectangle(this.SlideStrip.Right + 8, this.SlideStrip.Y + 50, (buttonsW - 6) / 2, 44);
            this.RemoveImageButton.Bounds = new Rectangle(this.ChangeImageButton.Bounds.Right + 6, this.SlideStrip.Y + 50, (buttonsW - 6) / 2, 44);
            this.MoveLeftButton.Bounds = new Rectangle(area.X + pad + leftW - 200 - 12 - 130, top, 130, 44);
            _ = bx;

            // right: preview + form (short fields share a row so everything fits)
            int rowH = 52;
            int gap = 16;
            int halfW = (rightW - gap) / 2;
            int fullLabelW = 110;
            int halfLabelW = 90;
            this.FormLabels.Clear();

            int formRows = 2 + (this.Painting != null ? 1 : 0) + 1 + 1 + (this.Slides.Count > 1 ? 1 : 0);
            int sourcesHeaderH = 52, minListH = 88, sourceEditorH = 108;
            int fixedH = formRows * rowH + sourcesHeaderH + minListH + sourceEditorH + 12;
            int previewH = Math.Clamp(bottom - top - fixedH, 100, 200);
            this.PreviewBox = new Rectangle(rightX, top, rightW, previewH);
            int y = this.PreviewBox.Bottom + 12;

            void Full(string label, Widget widget)
            {
                widget.Visible = true;
                int labelW = Math.Max(fullLabelW, (int)Gfx.Font.MeasureString(label).X + 16);
                this.FormLabels.Add((new Rectangle(rightX, y, labelW, 48), label));
                widget.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
                y += rowH;
            }

            void Half(string? label, Widget widget, bool right)
            {
                widget.Visible = true;
                int x = right ? rightX + halfW + gap : rightX;
                int labelW = label != null ? halfLabelW : 0;
                if (label != null)
                    this.FormLabels.Add((new Rectangle(x, y, labelW, 48), label));
                widget.Bounds = new Rectangle(x + labelW, y, halfW - labelW, 48);
            }

            Full(this.Replacement != null ? "New name" : "Name", this.NameField);
            Full("Text", this.DescriptionField);
            if (this.Painting != null)
            {
                Half("Type", this.TypeCycler, right: false);
                Half("Size", this.SizeCycler, right: true);
                y += rowH;
            }
            else
            {
                this.TypeCycler.Visible = false;
                this.SizeCycler.Visible = false;
            }
            Half("Frame", this.FrameCycler, right: false);
            Half("Detail", this.ResolutionCycler, right: true);
            y += rowH;
            Half(this.Replacement != null ? "Price" : "Price", this.PriceField, right: false);
            if (this.Painting != null)
            {
                this.CatalogueBox.Visible = true;
                this.CatalogueBox.Label = "In catalogue";
                this.CatalogueBox.Bounds = new Rectangle(rightX + halfW + gap, y + 2, halfW, 44);
            }
            else
                this.CatalogueBox.Visible = false;
            y += rowH;
            if (this.Slides.Count > 1)
                Full("Slideshow", this.SpeedCycler);
            else
                this.SpeedCycler.Visible = false;

            // sources
            this.FormLabels.Add((new Rectangle(rightX, y, fullLabelW + 40, 44), "Get it from"));
            this.AddShopButton.Bounds = new Rectangle(rightX + fullLabelW + 40, y, 140, 44);
            this.AddFishingButton.Bounds = new Rectangle(this.AddShopButton.Bounds.Right + 10, y, 160, 44);
            y += sourcesHeaderH;
            int listH = Math.Max(minListH, bottom - y - sourceEditorH);
            this.SourceList.Bounds = new Rectangle(rightX, y, rightW, listH);
            this.SourceList.EmptyText = this.Painting?.InCatalogue == true
                ? "Catalogue only. Add a shop or fishing spot?"
                : "Add a shop or fishing spot, or tick catalogue";
            y += listH + 8;

            // selected source editor (two rows)
            this.SourceShopCycler.Bounds = new Rectangle(rightX, y, halfW, 48);
            this.SourceLocationCycler.Bounds = new Rectangle(rightX, y, halfW, 48);
            this.SourcePriceField.Bounds = new Rectangle(rightX + halfW + gap + 70, y, halfW - 70, 48);
            this.SourceChanceCycler.Bounds = new Rectangle(rightX + halfW + gap, y, halfW, 48);
            this.SourceOnceBox.Bounds = new Rectangle(rightX, y + 54, halfW, 44);
            this.RemoveSourceButton.Bounds = new Rectangle(rightX + rightW - 150, y + 54, 150, 44);

            // bottom buttons
            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);

            this.SyncSourceControls();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            string title = this.Replacement != null
                ? $"Replace '{this.ReplacementName}'"
                : this.IsNew ? (this.IsTable ? "New photo frame" : "New painting") : $"Edit '{this.Painting!.Name ?? this.Painting.Id}'";
            Gfx.Text(b, Gfx.Fit(title, area.Width - 80, Gfx.TitleFont), new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            // crop area
            int hintW = (this.MoveLeftButton.Visible ? this.MoveLeftButton.Bounds.X : this.FitButton.Bounds.X) - this.CropBox.X - 12;
            Gfx.Text(b, Gfx.Fit("Drag to move, drag a corner to resize, scroll to zoom", hintW), new Vector2(this.CropBox.X, this.CropBox.Y - 44), Color.DimGray);
            this.DrawCropArea(b);
            this.DrawSlideStrip(b, mouseX, mouseY);
            if (this.CaptionField.Visible)
                Gfx.Text(b, "Caption", new Vector2(this.CropBox.X, this.CaptionField.Bounds.Y + 10));

            // preview
            this.DrawPreview(b);

            // form labels
            foreach ((Rectangle row, string label) in this.FormLabels)
                Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));
            if (this.SelectedSource?.IsShop == true)
                Gfx.Text(b, "Price", new Vector2(this.SourcePriceField.Bounds.X - 66, this.SourcePriceField.Bounds.Y + 10));

            base.Draw(b, mouseX, mouseY);

            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.MessageColor);
        }

        private void DrawCropArea(SpriteBatch b)
        {
            Gfx.Inset(b, this.CropBox, new Color(50, 42, 36));
            Slide? slide = this.CurrentSlide;
            LoadedImage? image = slide != null ? this.GetImage(slide.File) : null;
            if (slide == null || image == null)
            {
                Gfx.TextCentered(b, slide == null ? "Add an image to start" : "Can't read this image", this.CropBox, Color.LightGray);
                this.ImageDest = Rectangle.Empty;
                return;
            }

            Rectangle inner = new(this.CropBox.X + 16, this.CropBox.Y + 16, this.CropBox.Width - 32, this.CropBox.Height - 32);
            this.ImageDest = Gfx.Fitted(b, image.Display, null, inner, pixelated: false);

            // darken outside the crop
            Rectangle crop = this.ToScreen(this.GetCrop(slide, image));
            Color shade = Color.Black * 0.55f;
            Rectangle d = this.ImageDest;
            Gfx.Rect(b, new Rectangle(d.X, d.Y, d.Width, Math.Max(0, crop.Y - d.Y)), shade);
            Gfx.Rect(b, new Rectangle(d.X, crop.Bottom, d.Width, Math.Max(0, d.Bottom - crop.Bottom)), shade);
            Gfx.Rect(b, new Rectangle(d.X, crop.Y, Math.Max(0, crop.X - d.X), crop.Height), shade);
            Gfx.Rect(b, new Rectangle(crop.Right, crop.Y, Math.Max(0, d.Right - crop.Right), crop.Height), shade);
            Gfx.Outline(b, crop, Color.White, 2);
            foreach (Vector2 corner in Corners(crop))
                Gfx.Rect(b, new Rectangle((int)corner.X - 7, (int)corner.Y - 7, 14, 14), new Color(255, 210, 90));

            Gfx.Text(b, $"{image.Full.Width} x {image.Full.Height}", new Vector2(this.CropBox.X + 16, this.CropBox.Bottom - 40), Color.LightGray);
        }

        private void DrawSlideStrip(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Inset(b, this.SlideStrip, new Color(255, 250, 235));
            this.SlideThumbs.Clear();
            int size = this.SlideStrip.Height - 20;
            int x = this.SlideStrip.X + 10;
            for (int i = 0; i < this.Slides.Count; i++)
            {
                Rectangle thumb = new(x, this.SlideStrip.Y + 10, size, size);
                if (thumb.Right > this.SlideStrip.Right - 10)
                {
                    Gfx.Text(b, $"+{this.Slides.Count - i}", new Vector2(x, thumb.Y + size / 2 - 14));
                    break;
                }
                this.SlideThumbs.Add((thumb, i));
                Gfx.Rect(b, thumb, new Color(60, 50, 40));
                LoadedImage? image = this.GetImage(this.Slides[i].File);
                if (image != null)
                    Gfx.Fitted(b, image.Display, null, new Rectangle(thumb.X + 4, thumb.Y + 4, thumb.Width - 8, thumb.Height - 8), pixelated: false);
                if (i == this.SlideIndex)
                    Gfx.Outline(b, thumb, new Color(255, 170, 40), 4);
                else if (thumb.Contains(mouseX, mouseY))
                    Gfx.Outline(b, thumb, Color.White, 2);
                x += size + 10;
            }
        }

        private void DrawPreview(SpriteBatch b)
        {
            Rectangle box = this.PreviewBox;
            Gfx.Inset(b, box);

            // wall or table backdrop
            Rectangle inner = new(box.X + 8, box.Y + 8, box.Width - 16, box.Height - 16);
            if (this.IsTable)
            {
                Gfx.Rect(b, new Rectangle(inner.X, inner.Y, inner.Width, inner.Height * 2 / 3), new Color(206, 170, 120));
                Gfx.Rect(b, new Rectangle(inner.X, inner.Y + inner.Height * 2 / 3, inner.Width, inner.Height - inner.Height * 2 / 3), new Color(150, 96, 52));
            }
            else
            {
                Gfx.Rect(b, inner, new Color(196, 160, 112));
                for (int x = inner.X + 16; x < inner.Right; x += 32)
                    Gfx.Rect(b, new Rectangle(x, inner.Y, 4, inner.Height), new Color(186, 150, 104));
            }

            this.RenderPreviewIfNeeded();
            if (this.PreviewTexture != null)
            {
                // draw at the in-game size (4 screen pixels per game pixel) if it fits, else shrink to fit
                float inGame = 4f / this.PreviewScale;
                float scale = Math.Min(inGame, Math.Min((inner.Height - 16f) / this.PreviewTexture.Height, (inner.Width - 16f) / this.PreviewTexture.Width));
                if (this.PreviewScale == 1 && scale >= 1)
                    scale = (float)Math.Floor(scale); // keep pixel art crisp
                int w = (int)(this.PreviewTexture.Width * scale), h = (int)(this.PreviewTexture.Height * scale);
                int py = this.IsTable ? inner.Y + inner.Height * 2 / 3 - h + 12 : inner.Y + (inner.Height - h) / 2;
                b.Draw(this.PreviewTexture, new Rectangle(inner.X + (inner.Width - w) / 2, py, w, h), Color.White);
            }
            Gfx.Text(b, "In-game look", new Vector2(inner.X + 8, inner.Y + 4), Color.White * 0.9f);
        }

        private void DrawSource(SpriteBatch b, Source source, Rectangle row, bool selected, bool hover)
        {
            string text;
            if (source.IsShop)
            {
                string shop = Names.Shops.FirstOrDefault(s => s.Id == source.Shop).Label ?? source.Shop ?? "?";
                text = $"Shop: {shop}" + (source.Price != null ? $", {source.Price}g" : "");
            }
            else if (source.IsFishing)
            {
                string location = Names.FishingLocations.FirstOrDefault(l => l.Id == source.Location).Label ?? source.Location ?? "?";
                text = $"Fishing: {location}, {source.Chance * 100:0.#}%" + (source.Once ? ", once" : "");
            }
            else
                text = $"{source.Type}";
            Gfx.Text(b, Gfx.Fit(text, row.Width - 16), new Vector2(row.X + 8, row.Y + (row.Height - Gfx.LineHeight) / 2));
        }


        /*********
        ** Input
        *********/
        public override void LeftClick(int x, int y)
        {
            // slide thumbnails
            foreach ((Rectangle bounds, int index) in this.SlideThumbs)
            {
                if (bounds.Contains(x, y))
                {
                    this.SlideIndex = index;
                    this.SyncSlideControls();
                    Game1.playSound("smallSelect");
                    return;
                }
            }

            // crop area
            if ((this.ImageDest.Contains(x, y) || this.IsNearCropHandle(x, y)) && this.StartDrag(x, y))
                return;

            base.LeftClick(x, y);
        }

        public override void LeftHeld(int x, int y)
        {
            if (this.Drag == DragMode.None || this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image)
                return;

            Vector2 mouse = this.ToImage(x, y);
            Rectangle crop = this.GetCrop(slide, image);
            double aspect = this.GetAspect();

            if (this.Drag == DragMode.Move)
            {
                int nx = (int)Math.Round(mouse.X - this.DragOffset.X);
                int ny = (int)Math.Round(mouse.Y - this.DragOffset.Y);
                crop.X = Math.Clamp(nx, 0, image.Full.Width - crop.Width);
                crop.Y = Math.Clamp(ny, 0, image.Full.Height - crop.Height);
            }
            else
            {
                // resize from the anchor corner, keeping the aspect ratio
                float dx = mouse.X - this.DragAnchor.X, dy = mouse.Y - this.DragAnchor.Y;
                int dirX = dx < 0 ? -1 : 1, dirY = dy < 0 ? -1 : 1;
                double maxW = dirX > 0 ? image.Full.Width - this.DragAnchor.X : this.DragAnchor.X;
                double maxH = dirY > 0 ? image.Full.Height - this.DragAnchor.Y : this.DragAnchor.Y;
                double w = Math.Max(Math.Abs(dx), Math.Abs(dy) * aspect);
                w = Math.Min(w, Math.Min(maxW, maxH * aspect));
                w = Math.Max(w, Math.Min(8 * aspect, maxW));
                double h = w / aspect;
                crop = new Rectangle(
                    (int)Math.Round(dirX > 0 ? this.DragAnchor.X : this.DragAnchor.X - w),
                    (int)Math.Round(dirY > 0 ? this.DragAnchor.Y : this.DragAnchor.Y - h),
                    Math.Max(1, (int)Math.Round(w)),
                    Math.Max(1, (int)Math.Round(h))
                );
            }

            this.SetCrop(slide, crop, image);
        }

        public override void ReleaseLeft(int x, int y)
        {
            if (this.Drag != DragMode.None)
            {
                this.Drag = DragMode.None;
                this.PreviewDirty = true;
            }
        }

        public override void Scroll(int x, int y, int direction)
        {
            if (this.CropBox.Contains(x, y) && this.CurrentSlide is { } slide && this.GetImage(slide.File) is { } image)
            {
                Rectangle crop = this.GetCrop(slide, image);
                double factor = direction > 0 ? 0.9 : 1.1;
                double aspect = this.GetAspect();
                double w = Math.Clamp(crop.Width * factor, 8, Math.Min(image.Full.Width, image.Full.Height * aspect));
                double h = w / aspect;
                Vector2 center = new(crop.X + crop.Width / 2f, crop.Y + crop.Height / 2f);
                this.SetCrop(slide, new Rectangle((int)Math.Round(center.X - w / 2), (int)Math.Round(center.Y - h / 2), (int)Math.Round(w), (int)Math.Round(h)), image);
                return;
            }
            base.Scroll(x, y, direction);
        }

        public override bool KeyPress(Keys key)
        {
            if (key == Keys.Escape)
            {
                this.Cancel();
                return true;
            }
            return false;
        }


        /*********
        ** Crop logic
        *********/
        private Slide? CurrentSlide => this.SlideIndex >= 0 && this.SlideIndex < this.Slides.Count ? this.Slides[this.SlideIndex] : null;

        private (int W, int H) GetTiles()
        {
            if (this.Replacement != null)
                return this.ReplacementSize;
            if (this.Painting!.IsTable)
                return PaintingStore.TryParseSize(this.Painting.Size, out var t) && t.H >= 2 ? (1, 2) : (1, 1);
            return PaintingStore.TryParseSize(this.Painting.Size, out var size) ? size : (2, 2);
        }

        private double GetAspect()
        {
            (int w, int h) = this.GetTiles();
            return ImageProcessor.GetImageAspect(w, h, this.IsTable, this.Store.GetFrame(this.Settings.Frame));
        }

        private Rectangle GetCrop(Slide slide, LoadedImage image)
        {
            return ImageProcessor.ToCropRect(slide.Crop, image.Full.Width, image.Full.Height)
                ?? ImageProcessor.DefaultCrop(image.Full.Width, image.Full.Height, this.GetAspect());
        }

        private void SetCrop(Slide slide, Rectangle crop, LoadedImage image)
        {
            crop.Width = Math.Clamp(crop.Width, 1, image.Full.Width);
            crop.Height = Math.Clamp(crop.Height, 1, image.Full.Height);
            crop.X = Math.Clamp(crop.X, 0, image.Full.Width - crop.Width);
            crop.Y = Math.Clamp(crop.Y, 0, image.Full.Height - crop.Height);
            slide.Crop = new[] { crop.X, crop.Y, crop.Width, crop.Height };
            this.PreviewDirty = true;
        }

        /// <summary>Make sure a slide has a crop with the current aspect ratio.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="refit">Whether to reshape an existing crop (keeping its center and area) instead of only filling in missing ones.</param>
        private void EnsureCrop(Slide slide, bool refit)
        {
            LoadedImage? image = this.GetImage(slide.File);
            if (image == null)
                return;
            double aspect = this.GetAspect();
            Rectangle? existing = ImageProcessor.ToCropRect(slide.Crop, image.Full.Width, image.Full.Height);
            if (existing == null)
            {
                this.SetCrop(slide, ImageProcessor.DefaultCrop(image.Full.Width, image.Full.Height, aspect), image);
                return;
            }
            if (!refit && Math.Abs((double)existing.Value.Width / existing.Value.Height - aspect) < 0.02)
                return;

            Rectangle old = existing.Value;
            double area = (double)old.Width * old.Height;
            double w = Math.Sqrt(area * aspect);
            double h = w / aspect;
            double scale = Math.Min(1, Math.Min(image.Full.Width / w, image.Full.Height / h));
            w *= scale;
            h *= scale;
            Vector2 center = new(old.X + old.Width / 2f, old.Y + old.Height / 2f);
            this.SetCrop(slide, new Rectangle((int)Math.Round(center.X - w / 2), (int)Math.Round(center.Y - h / 2), Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h))), image);
        }

        private void FitCrop()
        {
            if (this.CurrentSlide is { } slide && this.GetImage(slide.File) is { } image)
                this.SetCrop(slide, ImageProcessor.DefaultCrop(image.Full.Width, image.Full.Height, this.GetAspect()), image);
        }

        private bool StartDrag(int x, int y)
        {
            if (this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image)
                return false;
            Rectangle crop = this.GetCrop(slide, image);
            Rectangle screenCrop = this.ToScreen(crop);

            // corner: resize from the opposite corner
            Vector2[] corners = Corners(screenCrop);
            Vector2[] imageCorners = { new(crop.Right, crop.Bottom), new(crop.X, crop.Bottom), new(crop.Right, crop.Y), new(crop.X, crop.Y) };
            for (int i = 0; i < 4; i++)
            {
                if (Vector2.Distance(corners[i], new Vector2(x, y)) <= 18)
                {
                    this.Drag = DragMode.Resize;
                    this.DragAnchor = imageCorners[i];
                    return true;
                }
            }

            if (!this.ImageDest.Contains(x, y))
                return false;

            // inside: move; outside the crop: jump there first
            Vector2 mouse = this.ToImage(x, y);
            if (!screenCrop.Contains(x, y))
            {
                crop.X = (int)(mouse.X - crop.Width / 2f);
                crop.Y = (int)(mouse.Y - crop.Height / 2f);
                this.SetCrop(slide, crop, image);
                crop = this.GetCrop(slide, image);
            }
            this.Drag = DragMode.Move;
            this.DragOffset = new Vector2(mouse.X - crop.X, mouse.Y - crop.Y);
            return true;
        }

        private bool IsNearCropHandle(int x, int y)
        {
            if (this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image)
                return false;
            return Corners(this.ToScreen(this.GetCrop(slide, image))).Any(c => Vector2.Distance(c, new Vector2(x, y)) <= 18);
        }

        /// <summary>The four corners, in the order top-left, top-right, bottom-left, bottom-right.</summary>
        private static Vector2[] Corners(Rectangle r)
        {
            return new[] { new Vector2(r.X, r.Y), new Vector2(r.Right, r.Y), new Vector2(r.X, r.Bottom), new Vector2(r.Right, r.Bottom) };
        }

        private Rectangle ToScreen(Rectangle crop)
        {
            if (this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image || this.ImageDest.Width == 0)
                return Rectangle.Empty;
            float s = (float)this.ImageDest.Width / image.Full.Width;
            return new Rectangle(this.ImageDest.X + (int)(crop.X * s), this.ImageDest.Y + (int)(crop.Y * s), (int)(crop.Width * s), (int)(crop.Height * s));
        }

        private Vector2 ToImage(int x, int y)
        {
            if (this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image || this.ImageDest.Width == 0)
                return Vector2.Zero;
            float s = (float)image.Full.Width / this.ImageDest.Width;
            return new Vector2((x - this.ImageDest.X) * s, (y - this.ImageDest.Y) * s);
        }


        /*********
        ** Preview
        *********/
        private void RenderPreviewIfNeeded()
        {
            if (!this.PreviewDirty)
                return;
            long now = Environment.TickCount64;
            if (this.Drag != DragMode.None && now - this.LastPreviewRender < 80)
                return;

            this.PreviewDirty = this.Drag != DragMode.None; // keep refreshing while dragging
            this.LastPreviewRender = now;
            this.PreviewTexture?.Dispose();
            this.PreviewTexture = null;

            if (this.CurrentSlide is not { } slide || this.GetImage(slide.File) is not { } image)
                return;

            Rectangle crop = this.GetCrop(slide, image);
            double s = (double)image.Work.Width / image.Full.Width;
            Rectangle workCrop = new((int)(crop.X * s), (int)(crop.Y * s), Math.Max(1, (int)(crop.Width * s)), Math.Max(1, (int)(crop.Height * s)));
            (int w, int h) = this.GetTiles();
            this.PreviewScale = ImageProcessor.GetScale(this.Settings.Resolution);
            Pixels sprite = ImageProcessor.Render(image.Work, w, h, this.IsTable, workCrop, "crop", this.Store.GetFrame(this.Settings.Frame), this.PreviewScale);
            this.PreviewTexture = new Texture2D(Game1.graphics.GraphicsDevice, sprite.Width, sprite.Height);
            this.PreviewTexture.SetData(sprite.Data);
        }

        private LoadedImage? GetImage(string file)
        {
            if (this.Images.TryGetValue(file, out LoadedImage? cached))
                return cached;

            LoadedImage? loaded = null;
            string? path = this.Store.ResolveImage(file);
            if (path != null)
            {
                try
                {
                    Pixels full = ImageProcessor.Decode(path);
                    loaded = new LoadedImage
                    {
                        Full = full,
                        Work = full.Downscale(640),
                        Display = full.Downscale(1024).ToTexture()
                    };
                }
                catch (Exception ex)
                {
                    ModEntry.Log($"Editor couldn't read '{file}': {ex.Message}");
                }
            }
            this.Images[file] = loaded;
            return loaded;
        }


        /*********
        ** Actions
        *********/
        private void OnTypeChanged(string placement)
        {
            if (this.Painting == null)
                return;
            this.Painting.Placement = placement;
            if (this.Painting.IsTable)
            {
                // pick portrait or landscape from the current image
                LoadedImage? image = this.CurrentSlide != null ? this.GetImage(this.CurrentSlide.File) : null;
                this.Painting.Size = image != null && image.Full.Height > image.Full.Width ? "1x2" : "1x1";
            }
            else
                this.Painting.Size = "2x2";
            this.SizeCycler.Options = this.GetSizeOptions();
            this.SizeCycler.Index = Math.Max(0, this.SizeCycler.Options.FindIndex(o => o.Value == this.Painting.Size));
            this.OnShapeChanged();
        }

        private void OnShapeChanged()
        {
            foreach (Slide slide in this.Slides)
                this.EnsureCrop(slide, refit: true);
            this.PreviewDirty = true;
        }

        private List<(string Value, string Label)> GetSizeOptions()
        {
            if (this.Painting?.IsTable == true)
                return new() { ("1x1", "Small"), ("1x2", "Tall") };
            return WallSizes.Select(s => (s, s.Replace("x", " x ") + " tiles")).ToList();
        }

        private static List<(string Value, string Label)> GetShopOptions()
        {
            List<(string, string)> options = new();
            HashSet<string> shops;
            try
            {
                shops = DataLoader.Shops(Game1.content).Keys.ToHashSet();
            }
            catch
            {
                shops = new HashSet<string>();
            }
            foreach ((string id, string label) in Names.Shops)
                if (shops.Count == 0 || shops.Contains(id))
                    options.Add((id, label));
            foreach (string id in shops.OrderBy(s => s))
                if (!Names.Shops.Any(s => s.Id == id))
                    options.Add((id, id));
            return options;
        }

        private void BrowseImage(bool replace)
        {
            this.Root.Push(new FileBrowserScreen(null, path =>
            {
                string file;
                try
                {
                    file = this.Store.ImportImage(path);
                    this.ImportedThisSession.Add(file);
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't copy the image: {ex.Message}");
                    return;
                }

                if (replace && this.CurrentSlide is { } slide)
                {
                    slide.File = file;
                    slide.Crop = null;
                    this.EnsureCrop(slide, refit: false);
                }
                else
                {
                    Slide added = new() { File = file };
                    this.Slides.Add(added);
                    this.SlideIndex = this.Slides.Count - 1;
                    this.EnsureCrop(added, refit: false);
                }
                this.SyncSlideControls();
                this.Layout(this.Area);
                this.PreviewDirty = true;
            }, this.Store.BrowserPlaces));
        }

        private void RemoveSlide()
        {
            if (this.CurrentSlide == null)
                return;
            if (this.Slides.Count == 1)
            {
                this.ShowMessage("A painting needs at least one image. Use 'Change' to pick a different one.");
                return;
            }
            this.Slides.RemoveAt(this.SlideIndex);
            this.SlideIndex = Math.Clamp(this.SlideIndex, 0, this.Slides.Count - 1);
            this.SyncSlideControls();
            this.Layout(this.Area);
            this.PreviewDirty = true;
        }

        private void MoveSlideLeft()
        {
            if (this.SlideIndex <= 0 || this.SlideIndex >= this.Slides.Count)
                return;
            (this.Slides[this.SlideIndex - 1], this.Slides[this.SlideIndex]) = (this.Slides[this.SlideIndex], this.Slides[this.SlideIndex - 1]);
            this.SlideIndex--;
            this.SyncSlideControls();
        }

        private void SyncSlideControls()
        {
            this.CaptionField.Text = this.CurrentSlide?.Caption ?? "";
            this.ChangeImageButton.Enabled = this.CurrentSlide != null;
            this.RemoveImageButton.Enabled = this.Slides.Count > 1;
            this.MoveLeftButton.Visible = this.Slides.Count > 1;
            this.MoveLeftButton.Enabled = this.SlideIndex > 0;
            this.FitButton.Enabled = this.CurrentSlide != null;
            this.PreviewDirty = true;
        }

        private void AddSource(Source source)
        {
            this.SourceList.Items.Add(source);
            this.SourceList.SelectedIndex = this.SourceList.Items.Count - 1;
            this.SourceList.EnsureVisible(this.SourceList.SelectedIndex);
            this.SyncSourceControls();
        }

        private void RemoveSource()
        {
            if (this.SelectedSource == null)
                return;
            this.SourceList.Items.RemoveAt(this.SourceList.SelectedIndex);
            this.SourceList.SelectedIndex = -1;
            this.SyncSourceControls();
        }

        private void SyncSourceControls()
        {
            Source? source = this.SelectedSource;
            bool shop = source?.IsShop == true;
            bool fishing = source?.IsFishing == true;

            this.SourceShopCycler.Visible = shop;
            this.SourcePriceField.Visible = shop;
            this.SourceLocationCycler.Visible = fishing;
            this.SourceChanceCycler.Visible = fishing;
            this.SourceOnceBox.Visible = fishing;
            this.RemoveSourceButton.Visible = source != null;

            if (shop)
            {
                this.SourceShopCycler.Index = Math.Max(0, this.SourceShopCycler.Options.FindIndex(o => o.Value == source!.Shop));
                this.SourcePriceField.Text = source!.Price?.ToString() ?? "";
            }
            if (fishing)
            {
                int location = this.SourceLocationCycler.Options.FindIndex(o => string.Equals(o.Value, source!.Location, StringComparison.OrdinalIgnoreCase));
                if (location < 0 && !string.IsNullOrWhiteSpace(source!.Location))
                {
                    this.SourceLocationCycler.Options.Add((source.Location, source.Location));
                    location = this.SourceLocationCycler.Options.Count - 1;
                }
                this.SourceLocationCycler.Index = Math.Max(0, location);

                int chance = 0;
                float best = float.MaxValue;
                for (int i = 0; i < Chances.Length; i++)
                {
                    float diff = Math.Abs(Chances[i].Chance - source!.Chance);
                    if (diff < best)
                        (best, chance) = (diff, i);
                }
                this.SourceChanceCycler.Index = chance;
                this.SourceOnceBox.Checked = source!.Once;
            }
        }

        private void ShowMessage(string message, bool error = true)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            if (error)
                Game1.playSound("cancel");
        }

        private void Save()
        {
            if (this.Slides.Count == 0 || this.Slides.All(s => this.GetImage(s.File) == null))
            {
                this.ShowMessage("Add at least one image first.");
                return;
            }

            this.Settings.SetSlides(this.Slides);
            if (this.Slides.Count <= 1)
                this.Settings.SlideMinutes = 0;

            PaintingsFile file = this.Store.ReadFile();
            if (this.Painting != null)
            {
                if (string.IsNullOrWhiteSpace(this.Painting.Name))
                    this.Painting.Name = "Untitled";

                if (this.IsNew)
                {
                    string baseId = PaintingStore.SanitizeId(this.Painting.Name!.Replace(' ', '_'));
                    if (baseId.Trim('_').Length == 0)
                        baseId = "Painting";
                    string id = baseId;
                    for (int i = 2; file.Paintings.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)); i++)
                        id = $"{baseId}_{i}";
                    this.Painting.Id = id;
                    file.Paintings.Add(this.Painting);
                }
                else
                {
                    int index = file.Paintings.FindIndex(p => string.Equals(p.Id, this.OriginalId, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                        file.Paintings[index] = this.Painting;
                    else
                        file.Paintings.Add(this.Painting); // e.g. an auto-added image being customized
                }
            }
            else
            {
                Replacement replacement = this.Replacement!;
                int index = file.Replace.FindIndex(r => string.Equals(r.Target, replacement.Target, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    file.Replace[index] = replacement;
                else
                    file.Replace.Add(replacement);
            }

            try
            {
                this.Store.Save(file);
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't save: {ex.Message}");
                return;
            }

            Game1.playSound("newArtifact");
            this.ImportedThisSession.Clear();
            this.Root.Pop();
            this.OnSaved(this.Painting?.Name ?? "");
        }

        private void Cancel()
        {
            // clean up images copied in for this edit but never saved
            if (this.ImportedThisSession.Count > 0)
            {
                PaintingsFile file = this.Store.ReadFile();
                HashSet<string> used = file.Paintings.Cast<ImageSettings>().Concat(file.Replace).SelectMany(p => p.GetSlides()).Select(s => s.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string imported in this.ImportedThisSession.Distinct())
                {
                    if (!used.Contains(imported) && imported.StartsWith(PaintingStore.ImportFolderName + "/"))
                        this.Store.DeleteImportedImage(imported);
                }
            }
            this.Root.Pop();
        }

        private static T Clone<T>(T value)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
        }
    }
}
