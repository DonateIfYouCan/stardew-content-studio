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

namespace CustomCrops.UI
{
    /// <summary>Edits one custom crop: images, growth, prices and where the seeds are sold.</summary>
    internal sealed class CropEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        private static readonly string[] SeasonNames = { "spring", "summer", "fall", "winter" };

        private readonly CropStore Store;
        private readonly CustomCrop Crop;
        private readonly bool IsNew;
        /// <summary>Called after saving, with the saved name.</summary>
        private readonly Action<string> OnSaved;
        private readonly List<(string SeedId, string Name)> VanillaCrops;

        /// <summary>Which image the cropper edits: false = harvest icon, true = seed packet.</summary>
        private bool EditingSeed;

        private readonly Dictionary<string, Pixels?> Images = new(StringComparer.OrdinalIgnoreCase);
        private Texture2D? PreviewObjects;
        private Texture2D? PreviewGrowth;
        private int PreviewScale = 1;
        private bool ArtDirty = true;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        // left
        private readonly Cycler ImageCycler;
        private readonly CropWidget Cropper;
        private readonly Button ChooseImageButton;
        private readonly Button FitButton;
        private readonly Checkbox LockShapeBox;
        private readonly Button AutoPacketButton;
        private readonly Cycler LookCycler;
        private readonly Button ChooseSheetButton;
        private readonly Button ExportTemplateButton;
        private readonly Button PaintSheetButton;

        /// <summary>The growth sheet image the painter is open on, if any, as asked for with <see cref="CustomContent.TakeLock"/>.</summary>
        private string? HeldSheet;

        // right
        private readonly TextField NameField;
        private readonly TextField DescriptionField;
        private readonly Checkbox[] SeasonBoxes;
        private readonly TextField DaysField;
        private readonly TextField RegrowField;
        private readonly Cycler CategoryCycler;
        private readonly TextField EnergyField;
        private readonly TextField SellField;
        private readonly TextField SeedPriceField;
        private readonly TextField HarvestCountField;
        private readonly Checkbox TrellisBox;
        private readonly Checkbox ScytheBox;
        private readonly Checkbox PierreBox;
        private readonly Checkbox JojaBox;
        private readonly Checkbox TravelerBox;
        private readonly Cycler DetailCycler;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle PreviewArea;
        private readonly List<(Rectangle Row, string Label)> Labels = new();


        /*********
        ** Public methods
        *********/
        public CropEditorScreen(CropStore store, CustomCrop crop, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.Crop = JsonConvert.DeserializeObject<CustomCrop>(JsonConvert.SerializeObject(crop))!; // edit a copy so Cancel discards changes
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            this.VanillaCrops = CropStore.GetVanillaCrops();
            CustomCrop c = this.Crop;

            // images
            this.ImageCycler = this.Add(new Cycler(new() { ("harvest", "Harvest item"), ("seed", "Seed packet") }, "harvest", v => { this.EditingSeed = v == "seed"; this.SyncImage(); }));
            this.Cropper = this.Add(new CropWidget { OnChanged = this.OnCropChanged });
            this.ChooseImageButton = this.Add(new Button("Choose image", this.BrowseImage, "Pick an image from your computer."));
            this.FitButton = this.Add(new Button("Fit image", this.FitAndLock));
            this.LockShapeBox = this.Add(new Checkbox("Lock shape", true, on => { this.Cropper.FreeShape = !on; if (on) this.Cropper.SetAspect(1); this.ArtDirty = true; },
                "Keep the box square while you drag its corners. Unlock it to take any part of the picture and have it squeezed into the icon."));
            this.AutoPacketButton = this.Add(new Button("Use auto packet", () => { c.SeedImage = null; this.ArtDirty = true; this.SyncImage(); }, "Make the seed packet from the harvest icon."));

            List<(string, string)> looks = this.VanillaCrops.Select(v => (v.SeedId, $"Looks like: {v.Name}")).ToList();
            looks.Insert(0, ("", "Own growth sheet"));
            this.LookCycler = this.Add(new Cycler(looks, !string.IsNullOrEmpty(c.GrowthSheet) ? "" : c.LooksLike, v =>
            {
                if (v.Length > 0)
                {
                    c.LooksLike = v;
                    c.GrowthSheet = null;
                }
                this.ArtDirty = true;
                this.SyncButtons();
            }, "How the plant looks while growing: copy a game crop, or use your own growth sheet (8 frames of 16x32)."));
            this.ChooseSheetButton = this.Add(new Button("Choose growth sheet", this.BrowseSheet, "Your growth sheet: 8 frames of 16x32 in a row (seed, stages, grown), or a whole-number multiple like 512x128."));
            this.ExportTemplateButton = this.Add(new Button("Export template", this.ExportTemplate, "Save the selected game crop's growth sheet as a PNG to paint over."));
            this.PaintSheetButton = this.Add(new Button("Paint growth", this.PaintSheet, "Draw the growing plant here in the game: its seedling, each stage and the ripe plant."));

            // fields
            this.NameField = this.Add(new TextField(c.Name, v => c.Name = v.Trim(), limit: 80));
            this.DescriptionField = this.Add(new TextField(c.Description, v => c.Description = v, limit: 200));
            this.SeasonBoxes = SeasonNames.Select(season => this.Add(new Checkbox(char.ToUpper(season[0]) + season[1..], c.Seasons.Contains(season, StringComparer.OrdinalIgnoreCase), on =>
            {
                c.Seasons.RemoveAll(s => s.Equals(season, StringComparison.OrdinalIgnoreCase));
                if (on)
                    c.Seasons.Add(season);
                c.Seasons = SeasonNames.Where(n => c.Seasons.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            }))).ToArray();
            this.DaysField = this.Add(new TextField(string.Join(",", c.DaysInPhase), v => { if (TryParseDays(v, out List<int> days)) c.DaysInPhase = days; }, limit: 20));
            this.RegrowField = this.Add(new TextField(c.RegrowDays > 0 ? c.RegrowDays.ToString() : "", v => c.RegrowDays = int.TryParse(v, out int d) && d > 0 ? d : -1, numbersOnly: true, limit: 3));
            this.CategoryCycler = this.Add(new Cycler(new() { ("vegetable", "Vegetable"), ("fruit", "Fruit"), ("flower", "Flower"), ("other", "Other") }, c.Category, v => c.Category = v));
            this.EnergyField = this.Add(new TextField(c.Energy > 0 ? c.Energy.ToString() : "", v => c.Energy = int.TryParse(v, out int e) ? Math.Max(0, e) : 0, numbersOnly: true, limit: 4));
            this.SellField = this.Add(new TextField(c.SellPrice.ToString(), v => c.SellPrice = int.TryParse(v, out int p) ? Math.Max(0, p) : 0, numbersOnly: true, limit: 7));
            this.SeedPriceField = this.Add(new TextField(c.SeedPrice.ToString(), v => c.SeedPrice = int.TryParse(v, out int p) ? Math.Max(1, p) : 1, numbersOnly: true, limit: 7));
            this.HarvestCountField = this.Add(new TextField(c.HarvestMax.ToString(), v => c.HarvestMin = c.HarvestMax = int.TryParse(v, out int n) ? Math.Clamp(n, 1, 99) : 1, numbersOnly: true, limit: 2));
            this.TrellisBox = this.Add(new Checkbox("Trellis", c.Trellis, v => c.Trellis = v, "Grows on a trellis: you can't walk through it (like hops and grapes)."));
            this.ScytheBox = this.Add(new Checkbox("Scythe", c.Scythe, v => c.Scythe = v, "Harvested with a scythe instead of by hand."));
            this.PierreBox = this.Add(new Checkbox("Pierre", c.SoldAtPierre, v => c.SoldAtPierre = v, "Pierre sells the seeds in the crop's seasons."));
            this.JojaBox = this.Add(new Checkbox("JojaMart", c.SoldAtJoja, v => c.SoldAtJoja = v, "JojaMart sells the seeds in the crop's seasons."));
            this.TravelerBox = this.Add(new Checkbox("Traveling cart", c.SoldAtTraveler, v => c.SoldAtTraveler = v, "The traveling cart always has the seeds."));
            this.DetailCycler = this.Add(new Cycler(new() { ("0", "Auto (HD)"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") }, c.Resolution <= 0 ? "0" : c.Resolution.ToString(), v => { c.Resolution = int.Parse(v); this.ArtDirty = true; },
                "How detailed the icons and plant look. Pixel art matches the game's style."));

            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.SyncImage();
        }

        public override void OnResume()
        {
            this.ReleaseSheet(); // the painter is closed again, so another player can take their turn at that sheet
        }

        public override void Dispose()
        {
            this.ReleaseSheet();
            this.Cropper.Dispose();
            this.PreviewObjects?.Dispose();
            this.PreviewGrowth?.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int leftW = (int)(area.Width * 0.36);
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;
            this.Labels.Clear();

            // left: image cropper and growth look
            int lx = area.X + pad;
            this.Labels.Add((new Rectangle(lx, top, 110, 48), "Image"));
            this.ImageCycler.Bounds = new Rectangle(lx + 110, top, leftW - 110, 48);
            int growthBlock = 48 + 12 + 48;
            this.Cropper.Bounds = new Rectangle(lx, top + 60, leftW, bottom - (top + 60) - 60 - growthBlock - 24);
            int by = this.Cropper.Bounds.Bottom + 10;
            this.ChooseImageButton.Bounds = new Rectangle(lx, by, 210, 48);
            this.FitButton.Bounds = new Rectangle(lx + 220, by, 150, 48);
            this.LockShapeBox.Bounds = new Rectangle(lx + 380, by + 2, 180, 44);
            this.AutoPacketButton.Bounds = new Rectangle(lx + 220, by, 220, 48);
            by += 72;
            this.LookCycler.Bounds = new Rectangle(lx, by, leftW, 48);
            by += 60;
            this.ChooseSheetButton.Bounds = new Rectangle(lx, by, 270, 48);
            this.ExportTemplateButton.Bounds = new Rectangle(lx + 280, by, Math.Max(120, leftW - 280 - 150), 48);
            this.PaintSheetButton.Bounds = new Rectangle(this.ExportTemplateButton.Bounds.Right + 10, by, 140, 48);

            // right: preview and fields
            int rx = lx + leftW + 32;
            int rw = area.Right - pad - rx;
            this.PreviewArea = new Rectangle(rx, top, rw, 150);
            int y = this.PreviewArea.Bottom + 16;
            int labelW = 150, half = (rw - 16) / 2;

            void Full(string label, Widget widget)
            {
                this.Labels.Add((new Rectangle(rx, y, labelW, 48), label));
                widget.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 48);
                y += 56;
            }
            void Pair(string leftLabel, Widget left, string rightLabel, Widget right, int rightLabelW = 150)
            {
                this.Labels.Add((new Rectangle(rx, y, labelW, 48), leftLabel));
                left.Bounds = new Rectangle(rx + labelW, y, half - labelW, 48);
                this.Labels.Add((new Rectangle(rx + half + 16, y, rightLabelW, 48), rightLabel));
                right.Bounds = new Rectangle(rx + half + 16 + rightLabelW, y, half - rightLabelW, 48);
                y += 56;
            }
            void Boxes(string label, params Checkbox[] boxes)
            {
                this.Labels.Add((new Rectangle(rx, y, labelW, 44), label));
                int bw = (rw - labelW) / boxes.Length;
                for (int i = 0; i < boxes.Length; i++)
                    boxes[i].Bounds = new Rectangle(rx + labelW + i * bw, y, bw, 44);
                y += 52;
            }

            Full("Name", this.NameField);
            Full("Description", this.DescriptionField);
            Boxes("Seasons", this.SeasonBoxes);
            Pair("Days", this.DaysField, "Regrows", this.RegrowField);
            Pair("Type", this.CategoryCycler, "Energy", this.EnergyField);
            Pair("Sells for", this.SellField, "Seed price", this.SeedPriceField);
            Pair("Harvest", this.HarvestCountField, "", this.TrellisBox, 0);
            this.ScytheBox.Bounds = new Rectangle(this.TrellisBox.Bounds.X + 160, this.TrellisBox.Bounds.Y, 160, 44);
            this.TrellisBox.Bounds = new Rectangle(this.TrellisBox.Bounds.X, this.TrellisBox.Bounds.Y, 150, 44);
            Boxes("Seeds sold", this.PierreBox, this.JojaBox, this.TravelerBox);
            Full("Detail", this.DetailCycler);

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
            this.SyncButtons();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, this.IsNew ? "New crop" : $"Edit '{this.Crop.Name}'", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            // generated seed packet (instead of the cropper)
            if (this.EditingSeed && this.Crop.SeedImage == null)
            {
                Gfx.Inset(b, this.Cropper.Bounds, new Color(50, 42, 36));
                this.RenderPreviewIfNeeded();
                if (this.PreviewObjects != null)
                {
                    int size = Math.Min(this.Cropper.Bounds.Width, this.Cropper.Bounds.Height) - 120;
                    b.Draw(this.PreviewObjects, new Rectangle(this.Cropper.Bounds.Center.X - size / 2, this.Cropper.Bounds.Y + 30, size, size), new Rectangle(0, 0, this.PreviewObjects.Width / 2, this.PreviewObjects.Height), Color.White);
                }
                Gfx.TextCentered(b, "Made from the harvest icon", new Rectangle(this.Cropper.Bounds.X, this.Cropper.Bounds.Bottom - 70, this.Cropper.Bounds.Width, 40), Color.LightGray);
            }

            foreach ((Rectangle row, string label) in this.Labels)
                Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));
            if (!string.IsNullOrEmpty(this.Crop.GrowthSheet))
                Gfx.Text(b, Gfx.Fit($"Sheet: {this.Crop.GrowthSheet}", this.LookCycler.Bounds.Width), new Vector2(this.LookCycler.Bounds.X, this.LookCycler.Bounds.Y - 30), Color.DimGray);

            this.DrawPreview(b);
            base.Draw(b, mouseX, mouseY);

            string help = "Days: one number per growth stage, like 1,2,2,3. Regrows: days until it produces again (leave empty to harvest once).";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        /// <summary>Use the biggest piece of the image with the right shape, and put the shape lock back on.</summary>
        private void FitAndLock()
        {
            this.Cropper.FreeShape = false;
            this.LockShapeBox.Checked = true;
            this.Cropper.Fit();
            this.ArtDirty = true;
        }

        private void DrawPreview(SpriteBatch b)
        {
            Gfx.Inset(b, this.PreviewArea, new Color(150, 110, 70));
            this.RenderPreviewIfNeeded();
            Rectangle inner = new(this.PreviewArea.X + 16, this.PreviewArea.Y + 12, this.PreviewArea.Width - 32, this.PreviewArea.Height - 24);

            // seed + harvest icons
            int icon = 64;
            int x = inner.X;
            if (this.PreviewObjects != null)
            {
                int half = this.PreviewObjects.Width / 2;
                b.Draw(this.PreviewObjects, new Rectangle(x, inner.Y + 20, icon, icon), new Rectangle(0, 0, half, this.PreviewObjects.Height), Color.White);
                b.Draw(this.PreviewObjects, new Rectangle(x + icon + 12, inner.Y + 20, icon, icon), new Rectangle(half, 0, half, this.PreviewObjects.Height), Color.White);
            }
            Gfx.Text(b, "Seeds  Harvest", new Vector2(x, inner.Y + icon + 28), Color.White * 0.9f);
            x += Math.Max(icon * 2 + 40, (int)Gfx.Font.MeasureString("Seeds  Harvest").X + 32);

            // growth frames: seed, stages..., grown
            if (this.PreviewGrowth != null)
            {
                int frames = 8;
                int frameW = Math.Min(48, (inner.Right - x) / frames - 6);
                int frameH = frameW * 2;
                int fw = this.PreviewGrowth.Width / 16, fh = this.PreviewGrowth.Height;
                for (int i = 0; i < frames; i++)
                    b.Draw(this.PreviewGrowth, new Rectangle(x + i * (frameW + 6), inner.Bottom - frameH - 4, frameW, frameH), new Rectangle(i * fw, 0, fw, fh), Color.White);
            }
            Gfx.Text(b, "Growing", new Vector2(x, inner.Y), Color.White * 0.9f);
        }


        /*********
        ** Private methods
        *********/
        /// <summary>The most growth stages a crop can have.</summary>
        private const int MaxStages = 5;

        /// <summary>Parse the growth days, like <c>1,2,2,3</c>: 1 to 5 whole numbers from 1 to 28.</summary>
        private static bool TryParseDays(string value, out List<int> days)
        {
            days = new List<int>();
            foreach (string part in value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, out int day) || day < 1 || day > 28)
                    return false;
                days.Add(day);
            }
            return days.Count is > 0 and <= MaxStages;
        }

        private ImageRef? EditedImage => this.EditingSeed ? this.Crop.SeedImage : this.Crop.HarvestImage;

        private void SyncImage()
        {
            ImageRef? image = this.EditedImage;
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            this.Cropper.Visible = !(this.EditingSeed && this.Crop.SeedImage == null);
            this.Cropper.EmptyText = this.EditingSeed ? "Choose a seed packet image" : "Choose an image for the harvest";
            this.Cropper.SetImage(pixels, image != null && pixels != null ? ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) : null, 1);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            this.FitButton.Visible = this.Cropper.Visible && this.Cropper.Image != null && !(this.EditingSeed && this.Crop.SeedImage != null);
            this.AutoPacketButton.Visible = this.EditingSeed && this.Crop.SeedImage != null;
            this.ChooseImageButton.Label = this.EditedImage == null ? "Choose image" : "Change image";
            this.ExportTemplateButton.Enabled = string.IsNullOrEmpty(this.Crop.GrowthSheet) && this.LookCycler.Value.Length > 0;
            this.ChooseSheetButton.Label = string.IsNullOrEmpty(this.Crop.GrowthSheet) ? "Choose growth sheet" : "Change growth sheet";
        }

        private void OnCropChanged(Rectangle crop)
        {
            if (this.EditedImage is { } image)
                image.Crop = new[] { crop.X, crop.Y, crop.Width, crop.Height };
            if (!this.Cropper.IsDragging)
                this.ArtDirty = true;
        }

        public override void LeftHeld(int x, int y) => this.Cropper.LeftHeld(x, y);

        public override void ReleaseLeft(int x, int y)
        {
            if (this.Cropper.IsDragging)
            {
                this.Cropper.ReleaseLeft();
                this.ArtDirty = true;
            }
        }

        private Pixels? GetImage(string file)
        {
            if (!this.Images.TryGetValue(file, out Pixels? image))
            {
                image = this.Store.Decode(file);
                this.Images[file] = image;
            }
            return image;
        }

        private void RenderPreviewIfNeeded()
        {
            if (!this.ArtDirty)
                return;
            this.ArtDirty = false;
            this.PreviewObjects?.Dispose();
            this.PreviewGrowth?.Dispose();
            this.PreviewObjects = null;
            this.PreviewGrowth = null;
            try
            {
                CropStore.RenderedCrop rendered = this.Store.Render(this.Crop, out string? warning);
                this.PreviewScale = rendered.Scale;
                this.PreviewObjects = new Texture2D(Game1.graphics.GraphicsDevice, rendered.ObjectsHd.Width, rendered.ObjectsHd.Height);
                this.PreviewObjects.SetData(rendered.ObjectsHd.Data);
                this.PreviewGrowth = new Texture2D(Game1.graphics.GraphicsDevice, rendered.CropHd.Width, rendered.CropHd.Height);
                this.PreviewGrowth.SetData(rendered.CropHd.Data);
                if (warning != null)
                    this.ShowError(warning);
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't draw the preview: {ex.Message}");
            }
        }

        private void BrowseImage()
        {
            bool seed = this.EditingSeed;
            this.Root.Push(new FileBrowserScreen(null, path =>
            {
                string? file = this.Import(path);
                if (file == null)
                    return;
                ImageRef image = new() { File = file };
                if (seed)
                    this.Crop.SeedImage = image;
                else
                    this.Crop.HarvestImage = image;
                this.ArtDirty = true;
                this.SyncImage();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Let go of the growth sheet the painter was open on, so another player can change it.</summary>
        private void ReleaseSheet()
        {
            if (this.HeldSheet is not { } thing)
                return;
            this.HeldSheet = null;
            CustomContent.ReleaseLock(this.Store.Manifest, thing);
        }

        /// <summary>Draw the growing plant here in the game: the sheet it already has, the game crop it copies, or an empty one.</summary>
        private void PaintSheet()
        {
            // painting the sheet it already has: hold that image while the painter is open, like the other editors do
            if (!string.IsNullOrEmpty(this.Crop.GrowthSheet) && this.GetImage(this.Crop.GrowthSheet) is { } existing)
            {
                string thing = $"file:{CropStore.ImageFolderName}/{this.Crop.GrowthSheet}";
                CustomContent.TakeLock(this.Store.Manifest, thing, $"the growth sheet for '{this.Crop.Name}'", (granted, why) =>
                {
                    if (!granted)
                    {
                        this.ShowError(why);
                        return;
                    }
                    this.HeldSheet = thing;
                    CropStore.TryGetGrowthFactor(existing.Width, existing.Height, out int factor, out _);
                    this.OpenPaintSheet(existing, Math.Max(1, factor));
                });
                return;
            }

            // nothing of their own yet: start from the game crop this one copies, or from an empty sheet
            string seedId = this.LookCycler.Value;
            string what = seedId.Length > 0
                ? $"Paint a copy of the {this.VanillaCrops.FirstOrDefault(v => v.SeedId == seedId).Name ?? "game"} crop's growth sheet. The game's own art is never changed."
                : "Paint the growing plant from scratch: 8 frames in a row - the seedling, each stage, and the ripe plant.";
            this.Root.Push(new ChoiceScreen(
                $"{what}\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{CropStore.GrowthWidth}x{CropStore.GrowthHeight}: one pixel is one game pixel.", () => this.StartPaint(seedId, 1)),
                ("Twice the size (2x)", $"{CropStore.GrowthWidth * 2}x{CropStore.GrowthHeight * 2}: room for finer detail.", () => this.StartPaint(seedId, 2)),
                ("Four times the size (4x)", $"{CropStore.GrowthWidth * 4}x{CropStore.GrowthHeight * 4}: the usual size for HD art.", () => this.StartPaint(seedId, 4))));
        }

        /// <summary>Open the painter on a copy of a game crop's sheet, or on an empty one.</summary>
        private void StartPaint(string seedId, int scale)
        {
            Pixels start = (seedId.Length > 0 ? CropStore.GetVanillaGrowth(seedId, scale) : null) ?? CropStore.BlankGrowth(scale);
            this.OpenPaintSheet(start, scale);
        }

        /// <summary>Open the paint screen on a growth sheet and use what comes back.</summary>
        /// <param name="image">The pixels to start from.</param>
        /// <param name="scale">How many times bigger than the game's own sheet those pixels are, for the guides.</param>
        private void OpenPaintSheet(Pixels image, int scale)
        {
            string[] frames = { "seedling", "stage 1", "stage 2", "stage 3", "stage 4", "ripe", "spare", "spare" };
            this.Root.Push(new PaintScreen(image, $"Paint the growth of '{this.Crop.Name}'", pixels =>
            {
                try
                {
                    this.Crop.GrowthSheet = CustomContent.SaveImage(this.Store.ImageFolder, $"{this.Crop.Name} growth", pixels);
                    this.LookCycler.Index = 0; // a sheet of their own replaces the game crop it copied
                    this.Images.Remove(this.Crop.GrowthSheet);
                    this.ArtDirty = true; // the cycler was already on 'own sheet', so nothing else would redraw it
                    this.Message = null;
                    this.SyncButtons();
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the sheet: {ex.Message}");
                }
            }, cellWidth: 16 * scale, cellHeight: 32 * scale, partWidth: 16 * scale, partHeight: 32 * scale, partLabels: frames));
        }

        private void BrowseSheet()
        {
            string? start = Directory.Exists(ImageExport.ExportFolder) ? ImageExport.ExportFolder : null;
            this.Root.Push(new FileBrowserScreen(start, path =>
            {
                string? file = this.Import(path);
                if (file == null)
                    return;
                Pixels? pixels = this.GetImage(file);
                if (pixels == null)
                {
                    this.ShowError($"Couldn't read '{file}'.");
                    return;
                }
                if (!CropStore.TryGetGrowthFactor(pixels.Width, pixels.Height, out _, out string? error))
                {
                    this.ShowError(error);
                    return;
                }
                this.Crop.GrowthSheet = file;
                this.LookCycler.Index = 0;
                this.ArtDirty = true;
                this.SyncButtons();
            }, this.Store.BrowserPlaces));
        }

        private void ExportTemplate()
        {
            string seedId = this.LookCycler.Value;
            string name = this.VanillaCrops.FirstOrDefault(v => v.SeedId == seedId).Name ?? seedId;
            try
            {
                ImageExport.AskScale(this, scale =>
                {
                    string path = CropStore.ExportVanillaGrowth(seedId, name, scale);
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

        private string? Import(string path)
        {
            try
            {
                return this.Store.ImportImage(path);
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't copy the image: {ex.Message}");
                return null;
            }
        }

        private void ShowError(string message)
        {
            this.Message = message;
            this.MessageColor = Color.DarkRed;
            Game1.playSound("cancel");
        }

        private void Save()
        {
            CustomCrop c = this.Crop;
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                this.ShowError("Give the crop a name.");
                return;
            }
            if (c.Seasons.Count == 0)
            {
                this.ShowError("Pick at least one season.");
                return;
            }
            if (c.HarvestImage == null)
            {
                this.ShowError("Choose an image for the harvest.");
                return;
            }
            if (!TryParseDays(this.DaysField.Text, out _))
            {
                this.ShowError($"Days must be 1 to {MaxStages} whole numbers from 1 to 28, separated by commas, like 1,2,2,3.");
                return;
            }

            CropsFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(c.Name, "Crop");
                if (baseId.Length == 0)
                    baseId = "Crop";
                string id = baseId;
                for (int i = 2; file.Crops.Any(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++)
                    id = $"{baseId}_{i}";
                c.Id = id;
                file.Crops.Add(c);
            }
            else
            {
                int index = file.Crops.FindIndex(existing => existing.Id == c.Id);
                if (index >= 0)
                    file.Crops[index] = c;
                else
                    file.Crops.Add(c);
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
            this.OnSaved(this.Crop.Name);
        }
    }
}
