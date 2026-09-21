using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace CustomCharacters.UI
{
    /// <summary>Edits one villager's portraits: a default image for every emotion, plus optional per-emotion overrides.</summary>
    internal sealed class PortraitEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>The slot index for the default image.</summary>
        private const int DefaultSlot = -1;

        private static readonly string[] EmotionNames = { "Neutral", "Happy", "Sad", "Unique", "Love", "Angry" };

        private readonly CharacterStore Store;
        private readonly PortraitSet Set;
        private readonly string DisplayName;
        private readonly Action OnSaved;

        /// <summary>The game's original portraits, for comparison.</summary>
        private readonly Texture2D? Original;
        private readonly int SlotCount;

        /// <summary>Decoded images by file.</summary>
        private readonly Dictionary<string, Pixels?> Images = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered previews by slot (128px for the grid, HD for the big preview).</summary>
        private readonly Dictionary<(int Slot, int Size), Texture2D> Previews = new();

        private int Selected = DefaultSlot;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        private readonly CropWidget Cropper;
        private readonly Button ChooseButton;
        private readonly Button FitButton;
        private readonly Checkbox LockShapeBox;
        private readonly Button OwnImageButton;
        private readonly Button OwnCropButton;
        private readonly Button RemoveOverrideButton;
        private readonly Button ExportButton;
        private readonly Button PaintButton;

        /// <summary>The portrait image the painter is open on, if any, as asked for with <see cref="CustomContent.TakeLock"/>.</summary>
        private string? HeldImage;
        private readonly Cycler DetailCycler;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private readonly List<(Rectangle Bounds, int Slot)> Tiles = new();
        private Rectangle GridArea;
        private Rectangle PreviewArea;


        /*********
        ** Public methods
        *********/
        public PortraitEditorScreen(CharacterStore store, PortraitSet set, string displayName, Action onSaved)
        {
            this.Store = store;
            this.Set = JsonConvert.DeserializeObject<PortraitSet>(JsonConvert.SerializeObject(set))!; // edit a copy so Cancel discards changes
            this.DisplayName = displayName;
            this.OnSaved = onSaved;

            this.Original = CharacterStore.LoadOriginalPortraits(set.Npc);
            this.SlotCount = this.Original != null ? Math.Max(1, (this.Original.Width / 64) * (this.Original.Height / 64)) : 6;

            this.Cropper = this.Add(new CropWidget { OnChanged = this.OnCropChanged });
            this.ChooseButton = this.Add(new Button("Choose image", () => this.BrowseImage(), "Pick an image from your computer."));
            this.FitButton = this.Add(new Button("Fit image", this.FitAndLock, "Use the largest square that fits."));
            this.LockShapeBox = this.Add(new Checkbox("Lock shape", true, on => { this.Cropper.FreeShape = !on; if (on) this.Cropper.SetAspect(1); },
                "Keep the box square while you drag its corners. Unlock it to take any part of the picture and have it squeezed into the portrait."));
            this.OwnImageButton = this.Add(new Button("Use a different image", () => this.BrowseImage(), "Give this emotion its own image."));
            this.OwnCropButton = this.Add(new Button("Same image, own crop", this.CreateCropOverride, "Use the default image, but frame this emotion differently."));
            this.RemoveOverrideButton = this.Add(new Button("Use default image", this.RemoveOverride, "Remove this emotion's own image."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw this portrait here in the game: the image it uses, or the game's own portrait copied at the size you pick."));
            this.ExportButton = this.Add(new Button("Export original", this.ExportOriginal, "Save the game's original portrait as a PNG, to edit in another program and import again."));
            this.DetailCycler = this.Add(new Cycler(
                new() { ("0", "Auto"), ("64", "Pixel art"), ("128", "Sharp"), ("256", "HD") },
                this.Set.Resolution <= 0 ? "0" : this.Set.Resolution >= 256 ? "256" : this.Set.Resolution >= 128 ? "128" : "64",
                v => { this.Set.Resolution = int.Parse(v); this.ClearPreviews(); },
                "How detailed portraits look (dialogue, shops, events). Auto matches your screen; Pixel art matches the game's style."
            ));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.SelectSlot(DefaultSlot);
        }

        public override void OnResume()
        {
            this.ReleaseImage(); // the painter is closed again, so another player can take their turn at that image
        }

        public override void Dispose()
        {
            this.ReleaseImage();
            this.Cropper.Dispose();
            this.Original?.Dispose();
            this.ClearPreviews();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int leftW = (int)(area.Width * 0.46);
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;

            // left: cropper + buttons
            this.Cropper.Bounds = new Rectangle(area.X + pad, top, leftW, bottom - top - 60);
            int by = this.Cropper.Bounds.Bottom + 12;
            this.ChooseButton.Bounds = new Rectangle(area.X + pad, by, 230, 48);
            this.FitButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, by, 160, 48);
            this.LockShapeBox.Bounds = new Rectangle(this.PaintButton.Bounds.Right + 12, by + 2, 180, 44);
            this.OwnImageButton.Bounds = new Rectangle(area.X + pad, by, 290, 48);
            this.OwnCropButton.Bounds = new Rectangle(this.OwnImageButton.Bounds.Right + 10, by, 290, 48);
            this.RemoveOverrideButton.Bounds = new Rectangle(area.X + pad + leftW - 250, by, 250, 48);
            this.PaintButton.Bounds = new Rectangle(this.FitButton.Bounds.Right + 10, by, 130, 48);

            // right: emotion grid, preview, detail
            int rightX = area.X + pad + leftW + 24;
            int rightW = area.Right - pad - rightX;
            this.GridArea = new Rectangle(rightX, top + 36, rightW, 0);
            this.Tiles.Clear();
            int tile = 104, gap = 10;
            int columns = Math.Max(1, (rightW + gap) / (tile + gap));
            List<int> slots = new() { DefaultSlot };
            slots.AddRange(Enumerable.Range(0, this.SlotCount));
            for (int i = 0; i < slots.Count; i++)
            {
                int col = i % columns, row = i / columns;
                this.Tiles.Add((new Rectangle(rightX + col * (tile + gap), this.GridArea.Y + row * (tile + 34 + gap), tile, tile), slots[i]));
            }
            int gridBottom = this.Tiles.Max(t => t.Bounds.Bottom) + 30;
            this.GridArea.Height = gridBottom - this.GridArea.Y;

            this.DetailCycler.Bounds = new Rectangle(rightX + 110, gridBottom + 12, Math.Min(360, rightW - 110 - 260 - 16), 48); // leave room for the export button
            int previewTop = this.DetailCycler.Bounds.Bottom + 16;
            int previewSize = Math.Clamp(Math.Min(bottom - previewTop - 40, rightW), 96, 256);
            this.PreviewArea = new Rectangle(rightX, previewTop, previewSize, previewSize);
            this.ExportButton.Bounds = new Rectangle(area.Right - pad - 260, this.DetailCycler.Bounds.Y, 260, 48);

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
            this.SyncButtons();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, $"Portraits: {this.DisplayName}", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            // left: cropper or "uses default" panel
            if (!this.Cropper.Visible)
            {
                Rectangle panel = this.Cropper.Bounds;
                Gfx.Inset(b, panel, new Color(50, 42, 36));
                Texture2D? preview = this.GetPreview(this.Selected, 256);
                if (preview != null)
                    Gfx.Fitted(b, preview, null, new Rectangle(panel.X + 40, panel.Y + 40, panel.Width - 80, panel.Height - 140), pixelated: false);
                Gfx.TextCentered(b, $"{this.SlotName(this.Selected)} uses the default image.", new Rectangle(panel.X, panel.Bottom - 90, panel.Width, 40), Color.LightGray);
            }

            // grid
            Gfx.Text(b, "Emotions (click one to edit it)", new Vector2(this.GridArea.X, this.GridArea.Y - 36), Color.DimGray);
            foreach ((Rectangle bounds, int slot) in this.Tiles)
            {
                Gfx.Rect(b, bounds, new Color(60, 50, 40));
                Texture2D? preview = this.GetPreview(slot, 128);
                if (preview != null)
                    b.Draw(preview, new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), Color.White);

                // original portrait in the corner, for comparison
                if (slot >= 0 && this.Original != null)
                {
                    Rectangle source = Game1.getSourceRectForStandardTileSheet(this.Original, slot, 64, 64);
                    Rectangle corner = new(bounds.Right - 36, bounds.Bottom - 36, 34, 34);
                    Gfx.Rect(b, new Rectangle(corner.X - 2, corner.Y - 2, corner.Width + 4, corner.Height + 4), Color.White);
                    b.Draw(this.Original, corner, source, Color.White);
                }

                if (slot >= 0 && this.Set.Overrides.ContainsKey(slot))
                    Gfx.Rect(b, new Rectangle(bounds.X + 4, bounds.Y + 4, 14, 14), new Color(255, 170, 40));
                if (slot == this.Selected)
                    Gfx.Outline(b, bounds, new Color(255, 170, 40), 4);
                else if (bounds.Contains(mouseX, mouseY))
                    Gfx.Outline(b, bounds, Color.White, 2);
                Gfx.TextCentered(b, Gfx.Fit(this.SlotName(slot), bounds.Width + 8), new Rectangle(bounds.X - 4, bounds.Bottom + 2, bounds.Width + 8, 30));
            }

            // big preview, as in dialogue
            Gfx.Text(b, "Detail", new Vector2(this.GridArea.X, this.DetailCycler.Bounds.Y + 10));
            Gfx.Inset(b, new Rectangle(this.PreviewArea.X - 8, this.PreviewArea.Y - 8, this.PreviewArea.Width + 16, this.PreviewArea.Height + 16), new Color(247, 211, 145));
            Texture2D? big = this.GetPreview(this.Selected < 0 ? 0 : this.Selected, -1);
            if (big != null)
                b.Draw(big, this.PreviewArea, Color.White);
            Gfx.Text(b, Game1.parseText("In dialogue.\nThe orange dot marks emotions with their own image; the small corner picture is the original.", Game1.smallFont, Math.Max(120, this.Area.Right - 60 - this.PreviewArea.Right - 24)),
                new Vector2(this.PreviewArea.Right + 24, this.PreviewArea.Y), Color.DimGray);

            base.Draw(b, mouseX, mouseY);

            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.MessageColor);
        }


        /*********
        ** Input
        *********/
        public override void LeftClick(int x, int y)
        {
            foreach ((Rectangle bounds, int slot) in this.Tiles)
            {
                if (bounds.Contains(x, y))
                {
                    Game1.playSound("smallSelect");
                    this.SelectSlot(slot);
                    return;
                }
            }
            base.LeftClick(x, y);
        }

        public override void LeftHeld(int x, int y)
        {
            this.Cropper.LeftHeld(x, y);
        }

        public override void ReleaseLeft(int x, int y)
        {
            if (this.Cropper.IsDragging)
            {
                this.Cropper.ReleaseLeft();
                this.ClearPreviews();
            }
        }


        /*********
        ** Private methods
        *********/
        private string SlotName(int slot)
        {
            if (slot == DefaultSlot)
                return "Default";
            return slot < EmotionNames.Length ? EmotionNames[slot] : $"Extra {slot - EmotionNames.Length + 1}";
        }

        /// <summary>The image reference being edited for the selected slot (null if the slot just uses the default).</summary>
        private ImageRef? EditedImage => this.Selected == DefaultSlot ? this.Set.Default : this.Set.Overrides.GetValueOrDefault(this.Selected);

        /// <summary>Use the biggest piece of the image with the right shape, and put the shape lock back on.</summary>
        private void FitAndLock()
        {
            this.Cropper.FreeShape = false;
            this.LockShapeBox.Checked = true;
            this.Cropper.Fit();
            
        }

        private void SelectSlot(int slot)
        {
            this.Selected = slot;
            this.Message = null;
            ImageRef? image = this.EditedImage;
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            this.Cropper.Visible = slot == DefaultSlot || image != null;
            this.Cropper.EmptyText = slot == DefaultSlot ? "Choose an image used for every emotion" : "Choose an image";
            this.Cropper.SetImage(pixels, image != null ? ImageProcessor.ToCropRect(image.Crop, pixels?.Width ?? 1, pixels?.Height ?? 1) : null, 1);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            bool isDefault = this.Selected == DefaultSlot;
            bool hasOverride = !isDefault && this.Set.Overrides.ContainsKey(this.Selected);
            this.ChooseButton.Visible = isDefault || hasOverride;
            this.ChooseButton.Label = isDefault ? (this.Set.Default == null ? "Choose image" : "Change image") : "Change image";
            this.FitButton.Visible = this.Cropper.Visible && this.Cropper.Image != null;
            this.OwnImageButton.Visible = !isDefault && !hasOverride;
            this.OwnCropButton.Visible = !isDefault && !hasOverride && this.Set.Default != null;
            this.RemoveOverrideButton.Visible = hasOverride;
            if (this.FitButton.Visible && this.ChooseButton.Visible)
                this.FitButton.Bounds.X = this.ChooseButton.Bounds.Right + 10;
        }

        private void OnCropChanged(Rectangle crop)
        {
            if (this.EditedImage is { } image)
                image.Crop = new[] { crop.X, crop.Y, crop.Width, crop.Height };
            if (!this.Cropper.IsDragging)
                this.ClearPreviews();
        }

        private void BrowseImage()
        {
            int slot = this.Selected;
            this.Root.Push(new FileBrowserScreen(null, path =>
            {
                string file;
                try
                {
                    file = this.Store.ImportImage(path);
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't copy the image: {ex.Message}");
                    return;
                }

                ImageRef image = new() { File = file };
                if (slot == DefaultSlot)
                    this.Set.Default = image;
                else
                    this.Set.Overrides[slot] = image;
                this.ClearPreviews();
                this.SelectSlot(slot);
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Draw this portrait here in the game: the image it uses, or a copy of the game's own at the size picked.</summary>
        private void Paint()
        {
            int slot = this.Selected;
            ImageRef? image = slot == DefaultSlot ? this.Set.Default : this.Set.GetImage(slot);

            // painting an image they already have: hold that file while the painter is open
            if (image != null && this.GetImage(image.File) is { } mine)
            {
                string thing = $"file:{CharacterStore.ImageFolderName}/{image.File}";
                CustomContent.TakeLock(this.Store.Manifest, thing, $"{this.Set.Npc}'s portrait", (granted, why) =>
                {
                    if (!granted)
                    {
                        this.ShowError(why);
                        return;
                    }
                    this.HeldImage = thing;
                    this.OpenPaint(slot, mine);
                });
                return;
            }

            if (this.Original == null)
            {
                this.ShowError("Couldn't read the original portraits.");
                return;
            }

            // starting from the game's own portrait: it's copied, never changed
            Rectangle area = Game1.getSourceRectForStandardTileSheet(this.Original, Math.Max(0, slot), 64, 64);
            Pixels original = ImageProcessor.FromTexture(this.Original, area);
            this.Root.Push(new ChoiceScreen(
                $"Paint a copy of {this.Set.Npc}'s portrait ({original.Width}x{original.Height}). The game's own art is never changed.\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{original.Width}x{original.Height}: one pixel is one game pixel.", () => this.OpenPaint(slot, original)),
                ("Twice the size (2x)", $"{original.Width * 2}x{original.Height * 2}: room for finer detail.", () => this.OpenPaint(slot, ImageProcessor.Enlarge(original, 2))),
                ("Four times the size (4x)", $"{original.Width * 4}x{original.Height * 4}: the usual size for HD art.", () => this.OpenPaint(slot, ImageProcessor.Enlarge(original, 4)))));
        }

        /// <summary>Open the paint screen on a portrait and use what comes back for this emotion.</summary>
        private void OpenPaint(int slot, Pixels image)
        {
            this.Root.Push(new PaintScreen(image, $"Paint {this.Set.Npc}'s portrait", pixels =>
            {
                try
                {
                    string file = CustomContent.SaveImage(this.Store.ImageFolder, $"{this.Set.Npc} portrait painted", pixels);
                    ImageRef painted = new() { File = file };
                    if (slot == DefaultSlot)
                        this.Set.Default = painted;
                    else
                        this.Set.Overrides[slot] = painted;
                    this.ClearPreviews();
                    this.SelectSlot(slot);
                    this.Save(); // one step: what's painted is kept, without a second save on the way out
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the image: {ex.Message}");
                }
            }));
        }

        /// <summary>Let go of the portrait image the painter was open on.</summary>
        private void ReleaseImage()
        {
            if (this.HeldImage is not { } thing)
                return;
            this.HeldImage = null;
            CustomContent.ReleaseLock(this.Store.Manifest, thing);
        }

        /// <summary>Export the selected emotion's original portrait (the neutral one for the default slot).</summary>
        private void ExportOriginal()
        {
            if (this.Original == null)
            {
                this.ShowError("Couldn't read the original portraits.");
                return;
            }
            int index = Math.Max(0, this.Selected);
            Rectangle source = Game1.getSourceRectForStandardTileSheet(this.Original, index, 64, 64);
            try
            {
                ImageExport.AskScale(this, scale =>
                {
                    string path = ImageExport.Export(this.Original, source, $"{this.Set.Npc} portrait - {this.SlotName(index)}", scale);
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

        private void CreateCropOverride()
        {
            if (this.Set.Default == null || this.Selected == DefaultSlot)
                return;
            this.Set.Overrides[this.Selected] = new ImageRef { File = this.Set.Default.File, Crop = this.Set.Default.Crop?.ToArray() };
            this.ClearPreviews();
            this.SelectSlot(this.Selected);
        }

        private void RemoveOverride()
        {
            this.Set.Overrides.Remove(this.Selected);
            this.ClearPreviews();
            this.SelectSlot(this.Selected);
        }

        private Pixels? GetImage(string file)
        {
            if (!this.Images.TryGetValue(file, out Pixels? image))
            {
                image = this.Store.DecodeForEditor(file);
                this.Images[file] = image;
            }
            return image;
        }

        /// <summary>Get a rendered preview of a slot.</summary>
        /// <param name="slot">The slot (default slot shows the default image).</param>
        /// <param name="size">The size in pixels, or -1 for the size used in dialogue at the chosen detail level.</param>
        private Texture2D? GetPreview(int slot, int size)
        {
            if (size < 0)
            {
                int resolution = this.Set.Resolution;
                int scale = resolution <= 0 ? ImageProcessor.GetAutoUiScale() : Math.Clamp(resolution / 64, 1, ImageProcessor.MaxAutoScale);
                size = 64 * scale;
            }
            if (this.Previews.TryGetValue((slot, size), out Texture2D? cached))
                return cached;

            ImageRef? image = slot == DefaultSlot ? this.Set.Default : this.Set.GetImage(slot);
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            if (image == null || pixels == null)
                return null;
            Rectangle crop = ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) ?? ImageProcessor.DefaultCrop(pixels.Width, pixels.Height, 1);
            Texture2D texture = ImageProcessor.Resize(pixels, crop, size, size).ToTexture();
            this.Previews[(slot, size)] = texture;
            return texture;
        }

        private void ClearPreviews()
        {
            foreach (Texture2D texture in this.Previews.Values)
                texture.Dispose();
            this.Previews.Clear();
        }

        private void ShowError(string message)
        {
            this.Message = message;
            this.MessageColor = Color.DarkRed;
            Game1.playSound("cancel");
        }

        private void Save()
        {
            if (this.Set.Default == null)
            {
                this.ShowError("Choose a default image first. It's used for every emotion without its own image.");
                this.SelectSlot(DefaultSlot);
                return;
            }

            CharactersFile file = this.Store.ReadFile();
            file.Portraits.RemoveAll(p => string.Equals(p.Npc, this.Set.Npc, StringComparison.OrdinalIgnoreCase));
            file.Portraits.Add(this.Set);
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
            this.OnSaved();
        }
    }
}
