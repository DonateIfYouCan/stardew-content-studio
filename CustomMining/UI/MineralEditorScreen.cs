using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewValley;

namespace CustomMining.UI
{
    /// <summary>Edits one mineral, gem or artefact: its picture, the geodes it comes out of, where it's dug up, the museum and gift tastes.</summary>
    internal sealed class MineralEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>The chances the editor offers, as a share of one.</summary>
        private static readonly double[] ChancePresets = { 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.35, 0.5, 1 };

        /// <summary>The editor's pages.</summary>
        private enum Page { Item, Geodes, Digging, Gifts }

        private readonly MiningStore Store;
        private readonly CustomMineral Item;
        private readonly bool IsNew;

        /// <summary>Called after saving, with the saved name.</summary>
        private readonly Action<string> OnSaved;

        /// <summary>When editing new art for one of the game's own, the change being made; null for your own.</summary>
        /// <remarks>The game item keeps where it's found and what it's worth, so only the art controls are shown.</remarks>
        private readonly GameMineralChange? GameChange;

        private Page Current = Page.Item;
        private readonly Dictionary<string, Pixels?> Images = new(StringComparer.OrdinalIgnoreCase);
        private Texture2D? PreviewIcon;
        private bool ArtDirty = true;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        // left
        private readonly CropWidget Cropper;
        private readonly Button ChooseImageButton;
        private readonly Button FitButton;
        private readonly Button PaintButton;
        private Rectangle PreviewArea;

        // pages
        private readonly Dictionary<Page, Button> Tabs = new();
        private readonly Dictionary<Page, List<Widget>> PageWidgets = new();
        /// <summary>The labels beside the fields, each shown while its field is.</summary>
        private readonly List<(Widget Owner, Rectangle Row, string Label)> Labels = new();

        private readonly TextField NameField;
        private readonly TextField DescriptionField;
        private readonly Cycler KindCycler;
        private readonly TextField PriceField;
        private readonly Cycler DetailCycler;
        private readonly Cycler ColourCycler;
        private readonly Checkbox MuseumBox;

        private readonly (Checkbox Box, Dropdown Chance)[] GeodeRows;
        private readonly (Checkbox Box, Dropdown Chance)[] DigRows;

        private readonly ScrollList<(string Name, string Label)> GiftList;

        private readonly Button SaveButton;
        private readonly Button CancelButton;


        /*********
        ** Public methods
        *********/
        public MineralEditorScreen(MiningStore store, CustomMineral item, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.Item = JsonConvert.DeserializeObject<CustomMineral>(JsonConvert.SerializeObject(item))!; // edit a copy so Cancel discards changes
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            CustomMineral m = this.Item;

            // picture
            this.Cropper = this.Add(new CropWidget { OnChanged = this.OnCropChanged, EmptyText = "Choose or paint a picture of it" });
            this.ChooseImageButton = this.Add(new Button("Choose image", this.BrowseImage, "Pick an image from your computer."));
            this.FitButton = this.Add(new Button("Fit", () => { this.Cropper.Fit(); this.ArtDirty = true; }, "Use the biggest square of the picture."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw it here in the game, starting from the picture it has now."));

            foreach (Page page in Enum.GetValues<Page>())
            {
                Page p = page;
                this.Tabs[page] = this.Add(new Button(page switch { Page.Item => "Mineral", Page.Geodes => "Geodes", Page.Digging => "Dug up", _ => "Gifts" }, () => this.ShowPage(p)));
                this.PageWidgets[page] = new List<Widget>();
            }

            // the item itself
            this.NameField = this.On(Page.Item, new TextField(m.Name, v => m.Name = v.Trim(), limit: 80));
            this.DescriptionField = this.On(Page.Item, new TextField(m.Description, v => m.Description = v, limit: 200));
            this.KindCycler = this.On(Page.Item, new Cycler(MiningData.Kinds.Select(k => (k.Kind, k.Label)).ToList(), m.Kind, v => { m.Kind = v; this.SyncPage(); },
                "A mineral or gem comes out of geodes; only an artefact is dug out of artefact spots. Gems are worth more when you have the right profession."));
            this.PriceField = this.On(Page.Item, new TextField(m.Price.ToString(), v => m.Price = int.TryParse(v, out int p) ? Math.Max(0, p) : 0, numbersOnly: true, limit: 7));
            this.DetailCycler = this.On(Page.Item, new Cycler(new() { ("0", "Auto (HD)"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") }, m.Resolution <= 0 ? "0" : m.Resolution.ToString(), v => { m.Resolution = int.Parse(v); this.ArtDirty = true; },
                "How detailed it looks. Pixel art matches the game's style."));
            List<(string, string)> colours = ColorTags.Colors.Select(c => (c.Name, ColorTags.Label(c.Name))).ToList();
            colours.Insert(0, ("", "From the picture"));
            this.ColourCycler = this.On(Page.Item, new Cycler(colours, m.Color, v => m.Color = v, "Its colour, which the game uses for colour-matching bundles and dyeing."));
            this.MuseumBox = this.On(Page.Item, new Checkbox("Can be donated to the museum", m.InMuseum, v => m.InMuseum = v, "Whether Gunther takes it for the museum, where it counts towards the museum's rewards."));

            // geodes
            this.GeodeRows = MiningData.Geodes.Select(geode =>
            {
                double current = m.Geodes.GetValueOrDefault(geode.Id);
                Checkbox box = this.On(Page.Geodes, new Checkbox(geode.Label, current > 0, on =>
                {
                    if (on)
                        m.Geodes[geode.Id] = ReadChance(this.GeodeChance(geode.Id));
                    else
                        m.Geodes.Remove(geode.Id);
                    this.SyncPage();
                }));
                Dropdown chance = this.On(Page.Geodes, new Dropdown(ChanceOptions(current), ChanceValue(current > 0 ? current : 0.05), v =>
                {
                    if (m.Geodes.ContainsKey(geode.Id))
                        m.Geodes[geode.Id] = ReadChance(v);
                }, "How often a geode of that kind gives this instead of one of its usual finds. A geode keeps its own treasure about half the time, so even 100% here is roughly one geode in two."));
                return (box, chance);
            }).ToArray();

            // dug up
            this.DigRows = MiningData.DigPlaces.Select(place =>
            {
                double current = m.DigSpots.GetValueOrDefault(place.Key);
                Checkbox box = this.On(Page.Digging, new Checkbox(place.Label, current > 0, on =>
                {
                    if (on)
                        m.DigSpots[place.Key] = ReadChance(this.DigChance(place.Key));
                    else
                        m.DigSpots.Remove(place.Key);
                    this.SyncPage();
                }));
                Dropdown chance = this.On(Page.Digging, new Dropdown(ChanceOptions(current), ChanceValue(current > 0 ? current : 0.05), v =>
                {
                    if (m.DigSpots.ContainsKey(place.Key))
                        m.DigSpots[place.Key] = ReadChance(v);
                }, "How often an artefact spot dug there gives this."));
                return (box, chance);
            }).ToArray();

            // gifts
            this.GiftList = this.On(Page.Gifts, GiftTasteList.Create(m.GiftTastes));

            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.SyncImage();
            this.ShowPage(Page.Item);
        }

        /// <summary>Edit new art for one of the game's own.</summary>
        /// <param name="store">The mineral store.</param>
        /// <param name="change">The change to that item (a new one if it has none yet).</param>
        /// <param name="onSaved">Called after saving, with the item's name.</param>
        public MineralEditorScreen(MiningStore store, GameMineralChange change, Action<string> onSaved)
            : this(store, store.AsMineral(change), isNew: false, onSaved)
        {
            this.GameChange = change;
            this.ShowPage(Page.Item); // now that it's a game item, only its looks show
        }

        public override void Dispose()
        {
            this.Cropper.Dispose();
            this.PreviewIcon?.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int leftW = (int)(area.Width * 0.34);
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;
            this.Labels.Clear();

            // left: picture and preview
            int lx = area.X + pad;
            int previewH = 170;
            this.Cropper.Bounds = new Rectangle(lx, top, leftW, bottom - top - 60 - previewH - 16);
            int by = this.Cropper.Bounds.Bottom + 10;
            this.ChooseImageButton.Bounds = new Rectangle(lx, by, 210, 48);
            this.FitButton.Bounds = new Rectangle(lx + 220, by, 100, 48);
            this.PaintButton.Bounds = new Rectangle(lx + 330, by, 120, 48);
            this.PreviewArea = new Rectangle(lx, by + 60, leftW, previewH);

            // right: page tabs, then the page
            int rx = lx + leftW + 32;
            int rw = area.Right - pad - rx;
            int tabW = (rw - 3 * 8) / 4;
            int tx = rx;
            foreach (Button tab in this.Tabs.Values)
            {
                tab.Bounds = new Rectangle(tx, top, tabW, 52);
                tx += tabW + 8;
            }
            int pageTop = top + 72;
            int labelW = 170, half = (rw - 16) / 2;
            int y;

            void Full(string label, Widget widget)
            {
                this.Labels.Add((widget, new Rectangle(rx, y, labelW, 48), label));
                widget.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 48);
                y += 56;
            }
            void Pair(string leftLabel, Widget left, string rightLabel, Widget right)
            {
                this.Labels.Add((left, new Rectangle(rx, y, labelW, 48), leftLabel));
                left.Bounds = new Rectangle(rx + labelW, y, half - labelW, 48);
                this.Labels.Add((right, new Rectangle(rx + half + 16, y, labelW, 48), rightLabel));
                right.Bounds = new Rectangle(rx + half + 16 + labelW, y, half - labelW, 48);
                y += 56;
            }

            y = pageTop;
            Full("Name", this.NameField);
            Full("Description", this.DescriptionField);
            Pair("It is a", this.KindCycler, "Sells for", this.PriceField);
            Pair("Detail", this.DetailCycler, "Colour", this.ColourCycler);
            this.MuseumBox.Bounds = new Rectangle(rx, y, rw, 44);

            // the geodes it comes out of: one row each, the chance beside it
            y = pageTop;
            foreach ((Checkbox box, Dropdown chance) in this.GeodeRows)
            {
                box.Bounds = new Rectangle(rx, y, half, 44);
                chance.Bounds = new Rectangle(rx + half + 16, y, 240, 44);
                y += 52;
            }

            // where it's dug up: two columns, since there are more places than fit down one
            int rows = (this.DigRows.Length + 1) / 2;
            int colW = (rw - 24) / 2;
            int digTop = pageTop;
            int rowH = Math.Clamp((bottom - digTop) / Math.Max(1, rows), 40, 52);
            for (int i = 0; i < this.DigRows.Length; i++)
            {
                int cx = rx + (i / rows) * (colW + 24);
                int cy = digTop + (i % rows) * rowH;
                this.DigRows[i].Box.Bounds = new Rectangle(cx, cy, colW - 200, rowH - 6);
                this.DigRows[i].Chance.Bounds = new Rectangle(cx + colW - 192, cy, 192, rowH - 6);
            }

            this.GiftList.Bounds = new Rectangle(rx, pageTop + 40, rw, bottom - pageTop - 40);

            // a game item only gets new looks, so its one control goes on the page with no tabs
            if (this.GameChange != null)
            {
                this.Labels.RemoveAll(l => l.Owner == this.DetailCycler);
                y = top;
                Full("Detail", this.DetailCycler);
            }

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
            this.SyncPage();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            string title = this.GameChange != null ? $"New art for the game's {this.Item.Name}" : this.IsNew ? "New mineral" : $"Edit '{this.Item.Name}'";
            Gfx.Text(b, title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            foreach ((Widget owner, Rectangle row, string label) in this.Labels)
                if (owner.Visible)
                    Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));

            if (this.Current == Page.Digging && this.GameChange == null && !MiningData.CanBeDugUp(this.Item.Kind))
                Gfx.Message(b, "Only artefacts are dug out of artefact spots. Make it an artefact on the Mineral page to bury it.", this.GiftList.Bounds.Width, new Vector2(this.GiftList.Bounds.X, this.GiftList.Bounds.Y - 32), Color.DimGray);
            if (this.Current == Page.Gifts)
                Gfx.Text(b, "Click a villager to change how they feel about it as a gift.", new Vector2(this.GiftList.Bounds.X, this.GiftList.Bounds.Y - 38), Color.DimGray);

            this.DrawPreview(b);
            base.Draw(b, mouseX, mouseY);

            string help = this.GameChange != null
                ? "Where it's found and what it's worth stay the game's. The picture is used for the item, in the museum and everywhere else."
                : this.Current switch
                {
                    Page.Geodes => "Clint opens geodes for you. A chance is how often a geode of that kind gives this one.",
                    Page.Digging => "Artefact spots are the wiggling worms you dig up with a hoe.",
                    _ => "Minerals, gems and artefacts can all go in the museum, unless you say otherwise on the Mineral page."
                };
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void DrawPreview(SpriteBatch b)
        {
            Gfx.Inset(b, this.PreviewArea, new Color(60, 50, 40));
            this.RenderPreviewIfNeeded();
            Rectangle inner = new(this.PreviewArea.X + 16, this.PreviewArea.Y + 12, this.PreviewArea.Width - 32, this.PreviewArea.Height - 24);

            int icon = 96;
            if (this.PreviewIcon != null)
                b.Draw(this.PreviewIcon, new Rectangle(inner.X, inner.Y + 8, icon, icon), Color.White);
            Gfx.Text(b, "Item", new Vector2(inner.X, inner.Y + icon + 16), Color.White * 0.9f);

            if (this.GameChange == null)
            {
                int x = inner.X + icon + 48;
                string kind = MiningData.Kinds.FirstOrDefault(k => k.Kind == this.Item.Kind).Label ?? "Mineral";
                Gfx.Text(b, kind, new Vector2(x, inner.Y + 8), Color.White * 0.9f);
                Gfx.Text(b, $"{Math.Max(0, this.Item.Price)}g", new Vector2(x, inner.Y + 8 + Gfx.LineHeight + 4), Color.White * 0.9f);
                Gfx.Text(b, this.Item.InMuseum ? "In the museum" : "Not for the museum", new Vector2(x, inner.Y + 8 + (Gfx.LineHeight + 4) * 2), Color.White * 0.75f);
            }
        }


        /*********
        ** Private methods
        *********/
        private T On<T>(Page page, T widget) where T : Widget
        {
            this.PageWidgets[page].Add(widget);
            return this.Add(widget);
        }

        private void ShowPage(Page page)
        {
            this.Current = page;
            this.SyncPage();
        }

        /// <summary>Show the current page's fields, and only the ones that apply to this item.</summary>
        private void SyncPage()
        {
            bool game = this.GameChange != null;
            foreach ((Page page, Button tab) in this.Tabs)
            {
                tab.Toggled = page == this.Current;
                tab.Visible = !game; // a game item keeps everything but its looks, which fit on one page
            }
            foreach ((Page page, List<Widget> widgets) in this.PageWidgets)
                foreach (Widget widget in widgets)
                    widget.Visible = page == this.Current;

            if (game)
            {
                foreach (Widget widget in new Widget[] { this.NameField, this.DescriptionField, this.KindCycler, this.PriceField, this.ColourCycler, this.MuseumBox })
                    widget.Visible = false;
                return;
            }

            // a chance only shows for something it's actually found in
            if (this.Current == Page.Geodes)
            {
                foreach ((Checkbox box, Dropdown chance) in this.GeodeRows)
                    chance.Visible = box.Checked;
            }
            if (this.Current == Page.Digging)
            {
                bool dug = MiningData.CanBeDugUp(this.Item.Kind);
                foreach ((Checkbox box, Dropdown chance) in this.DigRows)
                {
                    box.Visible = dug;
                    chance.Visible = dug && box.Checked;
                }
            }
        }

        /// <summary>The chance picked for a geode now, as a stored value.</summary>
        private string GeodeChance(string geodeId) => this.GeodeRows[Array.FindIndex(MiningData.Geodes, g => g.Id == geodeId)].Chance.Value;

        /// <summary>The chance picked for a place now, as a stored value.</summary>
        private string DigChance(string place) => this.DigRows[Array.FindIndex(MiningData.DigPlaces, p => p.Key == place)].Chance.Value;

        /// <summary>The chances to offer, with the one it already has added if it isn't one of them.</summary>
        private static List<(string Value, string Label)> ChanceOptions(double current)
        {
            List<double> chances = ChancePresets.ToList();
            if (current > 0 && !chances.Any(c => Math.Abs(c - current) < 0.0001))
                chances.Add(current); // a copy of the game's keeps its own odds
            return chances.OrderBy(c => c).Select(c => (ChanceValue(c), MiningData.ChanceLabel(c))).ToList();
        }

        private static string ChanceValue(double chance) => chance.ToString("0.#####", CultureInfo.InvariantCulture);

        private static double ReadChance(string value) => MiningData.CleanChance(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) ? chance : 0.05);

        private void SyncImage()
        {
            ImageRef? image = this.Item.Image;
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            this.Cropper.SetImage(pixels, image != null && pixels != null ? ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) : null, 1);
            this.FitButton.Visible = pixels != null;
            this.ChooseImageButton.Label = image == null ? "Choose image" : "Change image";
            if (this.Tabs.Count > 0)
                this.SyncPage();
        }

        private void OnCropChanged(Rectangle crop)
        {
            if (this.Item.Image is { } image)
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
            this.PreviewIcon?.Dispose();
            this.PreviewIcon = null;
            try
            {
                Pixels icon;
                if (this.Item.Image == null && this.GameChange != null && OriginalContent.LoadItemSprite("(O)" + this.GameChange.Target, 1) is { } vanilla)
                    icon = new Pixels(ImageProcessor.Premultiply(vanilla.Data), vanilla.Width, vanilla.Height); // no picture of its own yet: the game's
                else
                {
                    MiningStore.RenderedMineral rendered = this.Store.Render(this.Item, out string? warning);
                    icon = rendered.IconHd;
                    if (warning != null)
                        this.ShowError(warning);
                }
                this.PreviewIcon = new Texture2D(Game1.graphics.GraphicsDevice, icon.Width, icon.Height);
                this.PreviewIcon.SetData(icon.Data);
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't draw the preview: {ex.Message}");
            }
        }

        private void BrowseImage()
        {
            this.Root.Push(new FileBrowserScreen(null, path =>
            {
                string? file = this.Import(path);
                if (file == null)
                    return;
                this.Item.Image = new ImageRef { File = file };
                this.ArtDirty = true;
                this.SyncImage();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Draw it in the game, starting from its picture now: its own, the game's, or an empty one.</summary>
        private void Paint()
        {
            int scale = MiningStore.GetScale(this.Item.Resolution);
            int size = 16 * scale;
            Pixels start;
            if (this.Item.Image != null)
            {
                Pixels hd = this.Store.Render(this.Item, out _).IconHd;
                start = new Pixels(ImageProcessor.Unpremultiply(hd.Data), hd.Width, hd.Height);
            }
            else if (this.GameChange != null && OriginalContent.LoadItemSprite("(O)" + this.GameChange.Target, scale) is { } vanilla)
                start = vanilla;
            else
                start = new Pixels(new Color[size * size], size, size);

            this.Root.Push(new PaintScreen(start, $"Paint '{this.Item.Name}'", pixels =>
            {
                try
                {
                    string file = CustomContent.SaveImage(this.Store.ImageFolder, this.Item.Name, pixels);
                    this.Images.Remove(file);
                    this.Item.Image = new ImageRef { File = file };
                    this.ArtDirty = true;
                    this.Message = null;
                    this.SyncImage();
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the picture: {ex.Message}");
                }
            }));
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
            CustomMineral m = this.Item;
            if (this.GameChange is { } change)
            {
                change.Image = m.Image;
                change.Resolution = m.Resolution;
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
                this.OnSaved(m.Name);
                return;
            }
            if (string.IsNullOrWhiteSpace(m.Name))
            {
                this.ShowError("Give it a name.");
                return;
            }
            if (m.Image == null)
            {
                this.ShowError("Choose or paint a picture of it.");
                return;
            }
            if (!MiningData.CanBeDugUp(m.Kind))
                m.DigSpots.Clear(); // only artefacts are dug up, so don't keep odds the game would ignore
            if (MiningData.GeodesFor(m).Count() == 0 && MiningData.DigSpotsFor(m).Count() == 0)
            {
                this.ShowError(MiningData.CanBeDugUp(m.Kind)
                    ? "Pick a geode it comes out of, or a place it's dug up."
                    : "Pick at least one geode it comes out of, on the Geodes page.");
                return;
            }

            MiningFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(m.Name, "Mineral");
                if (baseId.Length == 0)
                    baseId = "Mineral";
                string id = baseId;
                for (int i = 2; file.Minerals.Any(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++)
                    id = $"{baseId}_{i}";
                m.Id = id;
                file.Minerals.Add(m);
            }
            else
            {
                int index = file.Minerals.FindIndex(existing => existing.Id == m.Id);
                if (index >= 0)
                    file.Minerals[index] = m;
                else
                    file.Minerals.Add(m);
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
            this.OnSaved(m.Name);
        }
    }
}
