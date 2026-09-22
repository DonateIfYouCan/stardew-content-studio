using System;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewValley;

namespace CustomFurniture.UI
{
    /// <summary>Makes a wallpaper or floor from one of your images: pick the image, crop it, and see it tiled on a wall and floor.</summary>
    internal sealed class WallpaperEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        private readonly FurnitureStore Store;
        private readonly CustomWallpaper Item;
        private readonly bool IsNew;
        private readonly Action<string> OnSaved;

        /// <summary>When editing new art for one of the game's own wallpapers or floors: which one, and the change being made; null for your own.</summary>
        private readonly GameWallpaper? GameItem;
        private readonly GameWallpaperChange? GameChange;

        private readonly CropWidget Cropper;
        private readonly TextField NameField;
        private readonly Cycler TypeCycler;
        private readonly Cycler DetailCycler;
        private readonly Button ChooseButton;
        private readonly Button FitButton;
        private readonly Button WholeButton;
        private readonly Checkbox LockShapeBox;
        private readonly Button PaintButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Pixels? Source;
        private Texture2D? Preview;
        private bool PreviewDirty = true;
        private string? Message;
        private Color MessageColor = Color.DarkRed;
        private Rectangle PreviewArea;

        /// <summary>The image the paint screen is open on, held so no other player changes it while it's being painted; null when the painter is closed.</summary>
        private string? PaintedImageLock;


        /*********
        ** Public methods
        *********/
        public WallpaperEditorScreen(FurnitureStore store, CustomWallpaper item, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            this.Item = JsonConvert.DeserializeObject<CustomWallpaper>(JsonConvert.SerializeObject(item))!; // edit a copy so Cancel discards changes

            this.Cropper = this.Add(new CropWidget { OnChanged = crop => { this.Item.Crop = new[] { crop.X, crop.Y, crop.Width, crop.Height }; this.PreviewDirty = true; } });
            this.NameField = this.Add(new TextField(this.Item.Name, v => this.Item.Name = v, limit: 80));
            this.TypeCycler = this.Add(new Cycler(
                new() { ("wall", "Wallpaper"), ("floor", "Floor") },
                this.Item.IsFloor ? "floor" : "wall",
                v => { this.Item.IsFloor = v == "floor"; this.Cropper.SetAspect(this.Aspect); this.PreviewDirty = true; },
                "Wallpaper covers the walls of a room; floor covers the ground. The game tiles your image."));
            this.DetailCycler = this.Add(new Cycler(
                new() { ("0", "Auto (matches your zoom)"), ("1", "Pixel art (the game's size)"), ("2", "2x"), ("4", "4x"), ("8", "8x") },
                this.Item.Resolution.ToString(),
                v => { this.Item.Resolution = int.Parse(v); this.PreviewDirty = true; },
                "How detailed it's drawn in the world."));
            this.ChooseButton = this.Add(new Button("Choose image", this.Browse, "Pick an image from your computer; it's copied into the mod's images folder."));
            this.FitButton = this.Add(new Button("Biggest fit", this.FitBiggest, "Take the biggest piece of the image with the right shape for a tile, keeping it undistorted."));
            this.WholeButton = this.Add(new Button("Whole image", this.UseWholeImage, "Squeeze the whole image into one tile. Handy for a picture that isn't the shape of a tile."));
            this.LockShapeBox = this.Add(new Checkbox("Lock shape", true, on => { this.Cropper.FreeShape = !on; if (on) { this.Cropper.SetAspect(this.Aspect); this.PreviewDirty = true; } },
                "Keep the box the shape of a tile while you drag its corners. Unlock it to take any part of the picture and have it squeezed into the tile."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw on the image here in the game."));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.LoadImage();
        }

        /// <summary>Edit new art for one of the game's own wallpapers or floors.</summary>
        /// <param name="store">The furniture store.</param>
        /// <param name="item">The game wallpaper or floor.</param>
        /// <param name="change">The change to it (a new one if it has none yet).</param>
        /// <param name="onSaved">Called after saving, with its name.</param>
        public WallpaperEditorScreen(FurnitureStore store, GameWallpaper item, GameWallpaperChange change, Action<string> onSaved)
            : this(store, new CustomWallpaper { Id = item.Target, Name = item.Label, Image = change.Image, Crop = change.Crop, IsFloor = item.IsFloor, Resolution = change.Resolution }, isNew: false, onSaved)
        {
            this.GameItem = item;
            this.GameChange = change;
            // it's the game's own: what it is and what it's called stay the game's, only the art is yours
            this.NameField.Visible = false;
            this.TypeCycler.Visible = false;
        }

        /// <summary>Called when a screen opened from here closes again, such as the paint screen.</summary>
        public override void OnResume()
        {
            this.ReleasePaintedImage();
        }

        public override void Dispose()
        {
            this.ReleasePaintedImage();
            this.Cropper.Dispose();
            this.Preview?.Dispose();
        }

        /// <summary>Let go of the image the paint screen was open on, so another player in the game can paint it.</summary>
        private void ReleasePaintedImage()
        {
            if (this.PaintedImageLock is not { } thing)
                return;
            this.PaintedImageLock = null;
            CustomContent.ReleaseLock(this.Store.Manifest, thing);
        }


        /*********
        ** Layout & drawing
        *********/
        private double Aspect => this.Item.IsFloor ? 1 : (double)WallpaperSets.WallpaperWidth / WallpaperSets.WallpaperHeight;

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, top = area.Y + 84, bottom = area.Bottom - 96;
            int leftW = (int)(area.Width * 0.38);
            this.Cropper.Bounds = new Rectangle(area.X + pad, top + 36, leftW, bottom - top - 36 - 60);
            this.ChooseButton.Bounds = new Rectangle(area.X + pad, this.Cropper.Bounds.Bottom + 10, 240, 48);
            this.FitButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.Cropper.Bounds.Bottom + 10, 160, 48);
            this.WholeButton.Bounds = new Rectangle(this.FitButton.Bounds.Right + 10, this.Cropper.Bounds.Bottom + 10, 180, 48);
            this.PaintButton.Bounds = new Rectangle(this.WholeButton.Bounds.Right + 10, this.Cropper.Bounds.Bottom + 10, 120, 48);
            this.LockShapeBox.Bounds = new Rectangle(this.PaintButton.Bounds.Right + 12, this.Cropper.Bounds.Bottom + 12, 200, 44);

            int rightX = this.Cropper.Bounds.Right + 32, rightW = area.Right - pad - this.Cropper.Bounds.Right - 32;
            int labelW = 110, y = top;
            if (this.GameItem == null) // the game's own keeps its name and type, so those rows aren't there
            {
                this.NameField.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
                y += 60;
                this.TypeCycler.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
                y += 60;
            }
            this.DetailCycler.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
            y += 72;
            this.PreviewArea = new Rectangle(rightX, y + 36, rightW, bottom - (y + 36) - 12);

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            string title = this.GameItem != null ? $"New art for the game's {this.GameItem.Label}" : this.IsNew ? "New wallpaper or floor" : $"Edit '{this.Item.Name}'";
            Gfx.Text(b, title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            string shape = this.Item.IsFloor ? "square" : "tall strip";
            Gfx.Text(b, Gfx.Fit($"Your image - drag the box to move it, scroll to resize ({shape})", this.Cropper.Bounds.Width), new Vector2(this.Cropper.Bounds.X, this.Cropper.Bounds.Y - 36), Color.DimGray);
            if (this.GameItem == null)
            {
                Gfx.Text(b, "Name", new Vector2(this.NameField.Bounds.X - 110, this.NameField.Bounds.Y + 10));
                Gfx.Text(b, "Type", new Vector2(this.TypeCycler.Bounds.X - 110, this.TypeCycler.Bounds.Y + 10));
            }
            Gfx.Text(b, "Detail", new Vector2(this.DetailCycler.Bounds.X - 110, this.DetailCycler.Bounds.Y + 10));
            Gfx.Text(b, "Tiled preview", new Vector2(this.PreviewArea.X, this.PreviewArea.Y - 36), Color.DimGray);

            this.DrawPreview(b);
            base.Draw(b, mouseX, mouseY);

            string help = this.Item.IsFloor
                ? "Floors are one 32x32 tile, repeated across the ground."
                : "Wallpaper is one 16x48 strip, repeated along the walls.";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        /// <summary>Draw the tile repeated, as it would look in a room.</summary>
        private void DrawPreview(SpriteBatch b)
        {
            Gfx.Inset(b, this.PreviewArea, new Color(60, 50, 44));
            if (this.PreviewDirty)
                this.BuildPreview();
            if (this.Preview == null)
            {
                Gfx.TextCentered(b, "Choose an image to see it tiled here", this.PreviewArea, Color.White);
                return;
            }

            int tileW = this.Item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperWidth;
            int tileH = this.Item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperHeight;
            int scale = Math.Max(2, Math.Min((this.PreviewArea.Width - 24) / (tileW * 4), (this.PreviewArea.Height - 24) / (tileH * 2)));
            int cols = Math.Max(1, (this.PreviewArea.Width - 24) / (tileW * scale));
            int rows = Math.Max(1, (this.PreviewArea.Height - 24) / (tileH * scale));
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Rectangle dest = new(this.PreviewArea.X + 12 + col * tileW * scale, this.PreviewArea.Y + 12 + row * tileH * scale, tileW * scale, tileH * scale);
                    b.Draw(this.Preview, dest, null, Color.White);
                }
            }
        }

        /// <summary>Take the biggest piece of the image that's the shape of a tile, undistorted.</summary>
        private void FitBiggest()
        {
            this.Cropper.FreeShape = false;
            this.LockShapeBox.Checked = true;
            this.Cropper.Fit();
            this.PreviewDirty = true;
        }

        /// <summary>Use the whole image as one tile, squeezed to fit, for a picture that isn't the shape of a tile.</summary>
        private void UseWholeImage()
        {
            if (this.Source == null)
                return;

            this.Cropper.FreeShape = true; // the whole picture, whatever shape it is, squeezed into the tile
            this.LockShapeBox.Checked = false;
            this.Item.Crop = new[] { 0, 0, this.Source.Width, this.Source.Height };
            this.Cropper.SetImage(this.Source, new Rectangle(0, 0, this.Source.Width, this.Source.Height), this.Aspect);
            this.PreviewDirty = true;
        }

        private void BuildPreview()
        {
            this.PreviewDirty = false;
            this.Preview?.Dispose();
            this.Preview = null;
            if (this.Source == null)
                return;
            if (this.Store.Wallpapers.Load(this.Item, _ => this.Source) is { } loaded)
                this.Preview = loaded.Hd.ToTexture();
        }


        /*********
        ** Private methods
        *********/
        private void LoadImage()
        {
            this.Source = this.Store.Decode(this.Item.Image);
            // a saved crop that isn't tile-shaped was made with 'Whole image', so keep it that way
            Rectangle? saved = this.Source != null ? ImageProcessor.ToCropRect(this.Item.Crop, this.Source.Width, this.Source.Height) : null;
            this.Cropper.FreeShape = saved is { } c && Math.Abs((double)c.Width / Math.Max(1, c.Height) - this.Aspect) >= 0.03;
            this.LockShapeBox.Checked = !this.Cropper.FreeShape;
            this.Cropper.SetImage(this.Source, saved, this.Aspect);
            this.Cropper.EmptyText = "Choose an image";
            this.PreviewDirty = true;
        }

        private void Browse()
        {
            string? start = Directory.Exists(ImageExport.ExportFolder) ? ImageExport.ExportFolder : null;
            this.Root.Push(new FileBrowserScreen(start, path =>
            {
                try
                {
                    this.Item.Image = this.Store.ImportImage(path);
                    this.Item.Crop = null;
                    this.LoadImage();
                    this.Message = null;
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't use that image: {ex.Message}");
                }
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Open the paint screen: on your image if you chose one, else on a blank tile at the size you pick.</summary>
        private void Paint()
        {
            if (this.Source is { } chosen)
            {
                // painting over an image file already in the mod folder, so hold that file until the painter closes;
                // the blank tiles below are nobody's file yet, so there's nothing to hold.
                string thing = FurnitureStore.GetImageLockThing(this.Item.Image!);
                CustomContent.TakeLock(this.Store.Manifest, thing, $"the image for '{this.Item.Name}'", (granted, why) =>
                {
                    if (!granted)
                    {
                        this.ShowError(why);
                        return;
                    }
                    this.PaintedImageLock = thing;
                    this.OpenPaint(chosen);
                });
                return;
            }

            int w = this.Item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperWidth;
            int h = this.Item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperHeight;
            if (this.GameItem != null && GameWallpapers.LoadOriginal(this.GameItem) is { } original)
            {
                this.Root.Push(new ChoiceScreen(
                    $"Paint a copy of the game's {this.GameItem.Label} ({w}x{h}). The game's own art is never changed.\n\nWhat size do you want to draw at?",
                    ("The game's size (1x)", $"{w}x{h}: one pixel is one game pixel.", () => this.OpenPaint(original)),
                    ("Twice the size (2x)", $"{w * 2}x{h * 2}: room for finer detail.", () => this.OpenPaint(ImageProcessor.Enlarge(original, 2))),
                    ("Four times the size (4x)", $"{w * 4}x{h * 4}: the usual size for HD art.", () => this.OpenPaint(ImageProcessor.Enlarge(original, 4)))));
                return;
            }
            this.Root.Push(new ChoiceScreen(
                $"Paint a new {(this.Item.IsFloor ? "floor tile" : "wallpaper strip")} from scratch. The game draws it at {w}x{h}.\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{w}x{h}: one pixel is one game pixel.", () => this.OpenPaint(Blank(w, h))),
                ("Twice the size (2x)", $"{w * 2}x{h * 2}: room for finer detail.", () => this.OpenPaint(Blank(w * 2, h * 2))),
                ("Four times the size (4x)", $"{w * 4}x{h * 4}: the usual size for HD art.", () => this.OpenPaint(Blank(w * 4, h * 4)))));
        }

        private static Pixels Blank(int width, int height)
        {
            return new Pixels(new Color[width * height], width, height);
        }

        /// <summary>Open the paint screen and use whatever comes back as this wallpaper's image.</summary>
        private void OpenPaint(Pixels image)
        {
            this.Root.Push(new PaintScreen(image, this.Item.IsFloor ? "Paint the floor tile" : "Paint the wallpaper", pixels =>
            {
                try
                {
                    this.Item.Image = CustomContent.SaveImage(this.Store.ImageFolder, $"{this.Item.Name} painted", pixels);
                    this.Item.Crop = null;
                    this.LoadImage();
                    this.Message = null;
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the image: {ex.Message}");
                }
            }));
        }

        private void ShowError(string message)
        {
            this.Message = message;
            this.MessageColor = Color.DarkRed;
            Game1.playSound("cancel");
        }

        private void Save()
        {
            if (this.GameChange is { } change)
            {
                if (string.IsNullOrWhiteSpace(this.Item.Image))
                {
                    this.ShowError("Choose an image, or paint one.");
                    return;
                }
                change.Image = this.Item.Image;
                change.Crop = this.Item.Crop;
                change.Resolution = this.Item.Resolution;
                try
                {
                    this.Store.SaveGameWallpaperChange(change);
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save: {ex.Message}");
                    return;
                }
                Game1.playSound("newArtifact");
                this.Root.Pop();
                this.OnSaved(this.Item.Name);
                return;
            }
            if (string.IsNullOrWhiteSpace(this.Item.Name))
            {
                this.ShowError("Give it a name.");
                return;
            }
            if (string.IsNullOrWhiteSpace(this.Item.Image))
            {
                this.ShowError("Choose an image.");
                return;
            }

            FurnitureFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(this.Item.Name, "Wallpaper");
                if (baseId.Length == 0)
                    baseId = "Wallpaper";
                string id = baseId;
                for (int i = 2; file.Wallpapers.Exists(w => w.Id == id); i++)
                    id = $"{baseId}_{i}";
                this.Item.Id = id;
            }
            file.Wallpapers.RemoveAll(w => w.Id == this.Item.Id);
            file.Wallpapers.Add(this.Item);
            try
            {
                this.Store.Save(file);
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't save: {ex.Message}");
                return;
            }
            Game1.playSound("newArtifact");
            this.Root.Pop();
            this.OnSaved(this.Item.Name);
        }
    }
}
