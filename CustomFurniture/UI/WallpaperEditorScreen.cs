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

        private readonly CropWidget Cropper;
        private readonly TextField NameField;
        private readonly Cycler TypeCycler;
        private readonly Cycler DetailCycler;
        private readonly Button ChooseButton;
        private readonly Button FitButton;
        private readonly Button PaintButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Pixels? Source;
        private Texture2D? Preview;
        private bool PreviewDirty = true;
        private string? Message;
        private Color MessageColor = Color.DarkRed;
        private Rectangle PreviewArea;


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
            this.FitButton = this.Add(new Button("Fit image", () => { this.Cropper.Fit(); this.PreviewDirty = true; }));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw on the image here in the game."));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.LoadImage();
        }

        public override void Dispose()
        {
            this.Cropper.Dispose();
            this.Preview?.Dispose();
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
            this.FitButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.Cropper.Bounds.Bottom + 10, 150, 48);
            this.PaintButton.Bounds = new Rectangle(this.FitButton.Bounds.Right + 10, this.Cropper.Bounds.Bottom + 10, 120, 48);

            int rightX = this.Cropper.Bounds.Right + 32, rightW = area.Right - pad - this.Cropper.Bounds.Right - 32;
            int labelW = 110, y = top;
            this.NameField.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
            y += 60;
            this.TypeCycler.Bounds = new Rectangle(rightX + labelW, y, rightW - labelW, 48);
            y += 60;
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
            Gfx.Text(b, this.IsNew ? "New wallpaper or floor" : $"Edit '{this.Item.Name}'", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, this.Item.IsFloor ? "Your image (square)" : "Your image (tall strip)", new Vector2(this.Cropper.Bounds.X, this.Cropper.Bounds.Y - 36), Color.DimGray);
            Gfx.Text(b, "Name", new Vector2(this.NameField.Bounds.X - 110, this.NameField.Bounds.Y + 10));
            Gfx.Text(b, "Type", new Vector2(this.TypeCycler.Bounds.X - 110, this.TypeCycler.Bounds.Y + 10));
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
            this.Cropper.SetImage(this.Source, this.Source != null ? ImageProcessor.ToCropRect(this.Item.Crop, this.Source.Width, this.Source.Height) : null, this.Aspect);
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

        /// <summary>Open the paint screen on the chosen image, or on a blank tile if there's nothing chosen yet.</summary>
        private void Paint()
        {
            Pixels image = this.Source ?? new Pixels(new Color[64 * (this.Item.IsFloor ? 64 : 192)], 64, this.Item.IsFloor ? 64 : 192);
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
