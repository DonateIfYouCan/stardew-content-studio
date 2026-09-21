using System;
using System.Collections.Generic;
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
    /// <summary>Edits one piece of custom furniture.</summary>
    internal sealed class FurnitureEditorScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly CustomFurnitureItem Item;
        private readonly bool IsNew;
        /// <summary>Called after saving, with the saved name.</summary>
        private readonly Action<string> OnSaved;

        /// <summary>When editing new art for one of the game's own pieces, the change being made; null for your own furniture.</summary>
        /// <remarks>The game's piece keeps its type, size, name and price, so only the art controls are shown.</remarks>
        private readonly GameFurnitureChange? GameChange;

        private FurnitureTemplate? Template;
        private Pixels? Sheet;
        private Texture2D? SheetTexture;
        private Texture2D? TemplateTexture;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        /// <summary>The sheet image the paint screen is open on, held so no other player changes it while it's being painted; null when the painter is closed.</summary>
        private string? PaintedImageLock;

        private readonly Button BaseButton;
        private readonly Button ExportButton;
        private readonly Button ChooseButton;
        private readonly Button PaintButton;
        private readonly TextField FramesField;
        private readonly TextField SpeedField;
        private readonly TextField NameField;
        private readonly TextField PriceField;
        private readonly Checkbox CatalogueBox;
        private readonly Checkbox RobinBox;
        private readonly Checkbox TravelerBox;
        private readonly Cycler DetailCycler;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle PreviewArea;
        private readonly List<(Rectangle Row, string Label)> Labels = new();

        public FurnitureEditorScreen(FurnitureStore store, CustomFurnitureItem item, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.Item = JsonConvert.DeserializeObject<CustomFurnitureItem>(JsonConvert.SerializeObject(item))!; // edit a copy so Cancel discards changes
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            CustomFurnitureItem f = this.Item;

            this.BaseButton = this.Add(new Button("Choose base furniture", () => this.Root.Push(new TemplatePickerScreen(store, this.SetTemplate)), "Pick the game furniture yours is based on."));
            this.ExportButton = this.Add(new Button("Export template", this.ExportTemplate, "Save the base furniture's sprite (all frames) as a PNG to paint over."));
            this.ChooseButton = this.Add(new Button("Choose your sheet", this.BrowseSheet, "Your sprite: the same layout as the template, at any whole-number multiple of its size."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw the sprite here in the game. Starting from the base furniture copies it; the game's art is never changed."));
            this.FramesField = this.Add(new TextField(f.AnimationFrames.ToString(), v => { f.AnimationFrames = int.TryParse(v, out int n) ? Math.Clamp(n, 1, 64) : 1; this.ValidateSheet(); }, numbersOnly: true, limit: 2));
            this.SpeedField = this.Add(new TextField(f.FrameMilliseconds.ToString(), v => f.FrameMilliseconds = int.TryParse(v, out int n) ? Math.Clamp(n, 16, 5000) : 150, numbersOnly: true, limit: 4));
            this.NameField = this.Add(new TextField(f.Name, v => f.Name = v.Trim(), limit: 80));
            this.PriceField = this.Add(new TextField(f.Price.ToString(), v => f.Price = int.TryParse(v, out int p) ? Math.Max(0, p) : 0, numbersOnly: true, limit: 7));
            this.CatalogueBox = this.Add(new Checkbox("Furniture Catalogue", f.InCatalogue, v => f.InCatalogue = v));
            this.RobinBox = this.Add(new Checkbox("Robin", f.SoldAtRobin, v => f.SoldAtRobin = v));
            this.TravelerBox = this.Add(new Checkbox("Traveling cart", f.SoldAtTraveler, v => f.SoldAtTraveler = v));
            this.DetailCycler = this.Add(new Cycler(new() { ("0", "Auto (your sheet's detail)"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") }, f.Resolution.ToString(), v => f.Resolution = int.Parse(v)));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            if (store.GetTemplate(f.BasedOn) is { } template)
                this.SetTemplate(template);
            if (!string.IsNullOrEmpty(f.Sheet))
                this.LoadSheet(f.Sheet);
            this.SyncButtons();
        }

        /// <summary>Edit new art for one of the game's own pieces of furniture.</summary>
        /// <param name="store">The furniture store.</param>
        /// <param name="change">The change to that piece (a new one if it has none yet).</param>
        /// <param name="onSaved">Called after saving, with the piece's name.</param>
        public FurnitureEditorScreen(FurnitureStore store, GameFurnitureChange change, Action<string> onSaved)
            : this(store, new CustomFurnitureItem
            {
                Id = change.Target,
                Name = store.GetTemplate(change.Target)?.Name ?? change.Target,
                BasedOn = change.Target,
                Sheet = change.Sheet,
                AnimationFrames = change.AnimationFrames,
                FrameMilliseconds = change.FrameMilliseconds,
                Resolution = change.Resolution
            }, isNew: false, onSaved)
        {
            this.GameChange = change;
            // the game's piece keeps what makes it that piece; only the art is yours
            foreach (Widget widget in new Widget[] { this.BaseButton, this.NameField, this.PriceField, this.CatalogueBox, this.RobinBox, this.TravelerBox })
                widget.Visible = false;
        }

        /// <summary>Called when a screen opened from here closes again, such as the paint screen.</summary>
        public override void OnResume()
        {
            this.ReleasePaintedImage();
        }

        public override void Dispose()
        {
            this.ReleasePaintedImage();
            this.SheetTexture?.Dispose();
            this.TemplateTexture?.Dispose();
        }

        /// <summary>Let go of the sheet image the paint screen was open on, so another player in the game can paint it.</summary>
        private void ReleasePaintedImage()
        {
            if (this.PaintedImageLock is not { } thing)
                return;
            this.PaintedImageLock = null;
            CustomContent.ReleaseLock(this.Store.Manifest, thing);
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, top = area.Y + 84;
            int leftW = (int)(area.Width * 0.5);
            this.Labels.Clear();
            this.PreviewArea = new Rectangle(area.X + pad, top + 64, leftW, area.Bottom - 96 - (top + 64) - 64);
            this.BaseButton.Bounds = new Rectangle(area.X + pad, top, 300, 52);
            this.ExportButton.Bounds = new Rectangle(area.X + pad, this.PreviewArea.Bottom + 12, 230, 48);
            this.ChooseButton.Bounds = new Rectangle(area.X + pad + 240, this.PreviewArea.Bottom + 12, 250, 48);
            this.PaintButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.PreviewArea.Bottom + 12, 120, 48);

            int rx = area.X + pad + leftW + 32, rw = area.Right - pad - rx, y = top, labelW = 170;
            void Row(string label, Widget widget, int height = 48)
            {
                this.Labels.Add((new Rectangle(rx, y, labelW, height), label));
                widget.Bounds = new Rectangle(rx + labelW, y, rw - labelW, height);
                y += height + 10;
            }
            if (this.GameChange == null)
            {
                Row("Name", this.NameField);
                Row("Price", this.PriceField);
                this.Labels.Add((new Rectangle(rx, y, labelW, 44), "Sold in"));
                this.CatalogueBox.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 44);
                y += 52;
                this.RobinBox.Bounds = new Rectangle(rx + labelW, y, 160, 44);
                this.TravelerBox.Bounds = new Rectangle(rx + labelW + 170, y, rw - labelW - 170, 44);
                y += 60;
            }
            Row("Detail", this.DetailCycler);
            bool animate = this.Template?.CanAnimate == true;
            this.FramesField.Visible = animate;
            this.SpeedField.Visible = animate;
            if (animate)
            {
                y += 10;
                Row("Animation frames", this.FramesField);
                Row("Speed (ms)", this.SpeedField);
            }

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            string title = this.GameChange != null ? $"New art for the game's {this.Item.Name}" : this.IsNew ? "New furniture" : $"Edit '{this.Item.Name}'";
            Gfx.Text(b, title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            FurnitureTemplate? t = this.Template;
            if (t != null && this.GameChange != null)
                Gfx.Text(b, Gfx.Fit($"{t.Kind}, {t.TilesWide}x{t.TilesHigh} tiles: its type, size, name and price stay the game's.", this.PreviewArea.Width), new Vector2(area.X + 36, this.BaseButton.Bounds.Y + 12), Color.DimGray);
            else if (t != null)
                Gfx.Text(b, Gfx.Fit($"Based on: {t.Name} ({t.Kind}, {t.TilesWide}x{t.TilesHigh} tiles)", area.X + 32 + (int)(area.Width * 0.5) - this.BaseButton.Bounds.Right - 16), new Vector2(this.BaseButton.Bounds.Right + 16, this.BaseButton.Bounds.Y + 12));

            // preview: each frame (original on top, yours below), plus the animation
            Gfx.Inset(b, this.PreviewArea, new Color(196, 160, 112));
            if (t != null && this.TemplateTexture != null)
            {
                int frames = t.Frames;
                int cellW = (this.PreviewArea.Width - 40) / Math.Max(2, frames + (t.CanAnimate ? 1 : 0));
                int rowH = (this.PreviewArea.Height - 80) / 2;
                float scale = Math.Min((float)(cellW - 16) / t.Source.Width, (float)(rowH - 30) / t.Source.Height);
                if (scale >= 1)
                    scale = (float)Math.Floor(scale);
                int w = (int)(t.Source.Width * scale), h = (int)(t.Source.Height * scale);
                for (int i = 0; i < frames; i++)
                {
                    int x = this.PreviewArea.X + 20 + i * cellW;
                    Gfx.Text(b, t.FrameLabels[i].Length > 0 ? t.FrameLabels[i] : "Game", new Vector2(x, this.PreviewArea.Y + 12), Color.White);
                    b.Draw(this.TemplateTexture, new Rectangle(x, this.PreviewArea.Y + 44, w, h), new Rectangle(i * t.Source.Width, 0, t.Source.Width, t.Source.Height), Color.White);
                    if (this.SheetTexture != null)
                    {
                        int f = this.SheetTexture.Width / Math.Max(1, t.Source.Width * frames * (t.CanAnimate ? Math.Max(1, this.Item.AnimationFrames) : 1));
                        b.Draw(this.SheetTexture, new Rectangle(x, this.PreviewArea.Y + 44 + rowH, w, h), new Rectangle(i * t.Source.Width * f, 0, t.Source.Width * f, t.Source.Height * f), Color.White);
                    }
                }
                if (t.CanAnimate && this.SheetTexture != null && this.Item.AnimationFrames > 1)
                {
                    int f = this.SheetTexture.Width / Math.Max(1, t.Source.Width * this.Item.AnimationFrames);
                    int frame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / Math.Max(16, this.Item.FrameMilliseconds) % this.Item.AnimationFrames);
                    int x = this.PreviewArea.X + 20 + frames * cellW;
                    Gfx.Text(b, "Animated", new Vector2(x, this.PreviewArea.Y + 12), Color.White);
                    b.Draw(this.SheetTexture, new Rectangle(x, this.PreviewArea.Y + 44 + rowH, w, h), new Rectangle(frame * t.Source.Width * f, 0, t.Source.Width * f, t.Source.Height * f), Color.White);
                }
                if (this.SheetTexture == null)
                    Gfx.Text(b, "Your sheet will show here.", new Vector2(this.PreviewArea.X + 20, this.PreviewArea.Y + 44 + rowH + 20), Color.White);
            }
            else
                Gfx.TextCentered(b, "Choose the base furniture first.", this.PreviewArea, Color.White);

            foreach ((Rectangle row, string label) in this.Labels)
                Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));
            base.Draw(b, mouseX, mouseY);

            string help = t == null ? "" : t.Frames == 2 ? $"Your sheet needs {t.Frames} frames side by side: {string.Join(", then ", t.FrameLabels)}." : "For an animation, put the frames side by side and set the number of frames.";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void SetTemplate(FurnitureTemplate template)
        {
            this.Template = template;
            this.Item.BasedOn = template.Id;
            if (!template.CanAnimate)
                this.Item.AnimationFrames = 1;
            if (string.IsNullOrWhiteSpace(this.Item.Name) || this.Item.Name == "New furniture")
                this.Item.Name = $"My {template.Name}";
            this.NameField.Text = this.Item.Name;
            this.TemplateTexture?.Dispose();
            this.TemplateTexture = FurnitureStore.LoadTemplateFrames(template)?.ToTexture();
            this.Message = null; // e.g. "choose the base furniture first"
            this.ValidateSheet();
            this.SyncButtons();
            this.Layout(this.Area);
        }

        private void SyncButtons()
        {
            this.BaseButton.Label = this.Template == null ? "Choose base furniture" : "Change base";
            this.ExportButton.Enabled = this.Template != null;
            this.ChooseButton.Enabled = this.Template != null;
            this.ChooseButton.Label = this.Sheet == null ? "Choose your sheet" : "Change sheet";
        }

        private void LoadSheet(string file)
        {
            this.Sheet = this.Store.Decode(file);
            this.SheetTexture?.Dispose();
            this.SheetTexture = this.Sheet?.ToTexture();
            this.ValidateSheet();
        }

        /// <summary>Show an error if the sheet doesn't fit the template.</summary>
        private bool ValidateSheet()
        {
            if (this.Template == null || this.Sheet == null)
                return false;
            int frames = this.Template.CanAnimate ? this.Item.AnimationFrames : 1;
            if (!FurnitureStore.TryGetSheetFactor(this.Template, frames, this.Sheet.Width, this.Sheet.Height, out _, out string? error))
            {
                this.ShowError(error);
                return false;
            }
            this.Message = null;
            return true;
        }

        private void ExportTemplate()
        {
            if (this.Template == null)
                return;
            try
            {
                ImageExport.AskScale(this, scale =>
                {
                    string path = FurnitureStore.ExportTemplate(this.Template, scale);
                    this.Message = $"Exported to {path}";
                    this.MessageColor = Color.DarkGreen;
                    Game1.playSound("coin");
                    return path;
                });
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't export: {ex.Message}");
            }
        }

        private void BrowseSheet()
        {
            string? start = Directory.Exists(ImageExport.ExportFolder) ? ImageExport.ExportFolder : null;
            this.Root.Push(new FileBrowserScreen(start, path =>
            {
                try
                {
                    this.Item.Sheet = this.Store.ImportImage(path);
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't copy the image: {ex.Message}");
                    return;
                }
                this.LoadSheet(this.Item.Sheet);
                this.SyncButtons();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Open the paint screen: on your sheet if you chose one, else on a copy of the base furniture's frames at the size you pick.</summary>
        private void Paint()
        {
            if (this.Sheet is { } mine)
            {
                // painting over an image file already in the mod folder, so hold that file until the painter closes;
                // the sizes below start from a copy of the game's art instead, which is nobody's file yet.
                string thing = FurnitureStore.GetImageLockThing(this.Item.Sheet!);
                CustomContent.TakeLock(this.Store.Manifest, thing, $"the image for '{this.Item.Name}'", (granted, holder) =>
                {
                    if (!granted)
                    {
                        this.ShowError($"{holder} is changing that image right now.");
                        return;
                    }
                    this.PaintedImageLock = thing;
                    this.OpenPaint(mine);
                });
                return;
            }
            if (this.Template == null)
            {
                this.ShowError("Choose the base furniture first.");
                return;
            }
            if (FurnitureStore.LoadTemplateFrames(this.Template) is not { } frames)
            {
                this.ShowError("Couldn't read the base furniture's sprite.");
                return;
            }
            this.Root.Push(new ChoiceScreen(
                $"Paint a copy of {this.Template.Name}'s sprite ({frames.Width}x{frames.Height}, all frames side by side). The game's own art is never changed.\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{frames.Width}x{frames.Height}: one pixel is one game pixel.", () => this.OpenPaint(frames)),
                ("Twice the size (2x)", $"{frames.Width * 2}x{frames.Height * 2}: room for finer detail.", () => this.OpenPaint(ImageProcessor.Enlarge(frames, 2))),
                ("Four times the size (4x)", $"{frames.Width * 4}x{frames.Height * 4}: the usual size for HD art.", () => this.OpenPaint(ImageProcessor.Enlarge(frames, 4)))));
        }

        /// <summary>Open the paint screen and use whatever comes back as this furniture's sheet.</summary>
        private void OpenPaint(Pixels image)
        {
            int factor = this.Template != null ? Math.Max(1, image.Height / Math.Max(1, this.Template.Source.Height)) : 1;
            this.Root.Push(new PaintScreen(image, $"Paint {this.Item.Name}", pixels =>
            {
                try
                {
                    this.Item.Sheet = CustomContent.SaveImage(this.Store.ImageFolder, $"{this.Item.Name} painted", pixels);
                    this.LoadSheet(this.Item.Sheet);
                    this.SyncButtons();
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the image: {ex.Message}");
                }
            }, this.Template != null ? this.Template.Source.Width * factor : 0, this.Template != null ? this.Template.Source.Height * factor : 0));
        }

        private void ShowError(string message)
        {
            this.Message = message;
            this.MessageColor = Color.DarkRed;
        }

        private void Save()
        {
            CustomFurnitureItem f = this.Item;
            if (this.Template == null)
            {
                this.ShowError("Choose the base furniture first.");
                return;
            }
            if (this.Sheet == null)
            {
                this.ShowError("Choose your sheet (export the template to get the right size and layout).");
                return;
            }
            if (!this.ValidateSheet())
                return;
            if (this.GameChange is { } change)
            {
                change.Sheet = f.Sheet;
                change.AnimationFrames = f.AnimationFrames;
                change.FrameMilliseconds = f.FrameMilliseconds;
                change.Resolution = f.Resolution;
                try
                {
                    this.Store.SaveGameChange(change);
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
            if (string.IsNullOrWhiteSpace(f.Name))
            {
                this.ShowError("Give it a name.");
                return;
            }

            FurnitureFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(f.Name, "Furniture");
                if (baseId.Length == 0)
                    baseId = "Furniture";
                string id = baseId;
                for (int i = 2; file.Furniture.Any(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++)
                    id = $"{baseId}_{i}";
                f.Id = id;
                file.Furniture.Add(f);
            }
            else
            {
                int index = file.Furniture.FindIndex(e => e.Id == f.Id);
                if (index >= 0)
                    file.Furniture[index] = f;
                else
                    file.Furniture.Add(f);
            }

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
