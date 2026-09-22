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
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;

namespace CustomMining.UI
{
    /// <summary>Edits one rock: its picture, how long it takes to break, where in the mines it turns up, and what it gives.</summary>
    internal sealed class RockEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>The editor's pages.</summary>
        private enum Page { Rock, Where, Drops }

        private readonly MiningStore Store;
        private readonly CustomRock Rock;
        private readonly bool IsNew;
        private readonly Action<string> OnSaved;

        /// <summary>When editing new art for one of the game's own rocks, the change being made; null for your own.</summary>
        /// <remarks>The game's rock keeps where it turns up and what it gives, so only the art controls are shown.</remarks>
        private readonly GameRockChange? GameChange;

        private Page Current = Page.Rock;
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
        private readonly List<(Widget Owner, Rectangle Row, string Label)> Labels = new();

        private readonly TextField NameField;
        private readonly Cycler DetailCycler;
        private readonly TextField HitsField;
        private readonly TextField ExperienceField;

        private readonly (Checkbox Box, Dropdown Chance)[] AreaRows;

        private readonly ScrollList<RockDrop> DropList;
        private readonly Button AddDropButton;
        private readonly Button RemoveDropButton;
        private readonly TextField MinField;
        private readonly TextField MaxField;
        private readonly Dropdown DropChance;

        private readonly Button SaveButton;
        private readonly Button CancelButton;


        /*********
        ** Public methods
        *********/
        public RockEditorScreen(MiningStore store, CustomRock rock, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.Rock = JsonConvert.DeserializeObject<CustomRock>(JsonConvert.SerializeObject(rock))!; // edit a copy so Cancel discards changes
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            CustomRock r = this.Rock;

            // picture
            this.Cropper = this.Add(new CropWidget { OnChanged = this.OnCropChanged, EmptyText = "Choose or paint a picture of the rock" });
            this.ChooseImageButton = this.Add(new Button("Choose image", this.BrowseImage, "Pick an image from your computer."));
            this.FitButton = this.Add(new Button("Fit", () => { this.Cropper.Fit(); this.ArtDirty = true; }, "Use the biggest square of the picture."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw the rock here in the game, starting from the picture it has now."));

            foreach (Page page in Enum.GetValues<Page>())
            {
                Page p = page;
                this.Tabs[page] = this.Add(new Button(page switch { Page.Rock => "Rock", Page.Where => "Where", _ => "What it gives" }, () => this.ShowPage(p)));
                this.PageWidgets[page] = new List<Widget>();
            }

            // the rock itself
            this.NameField = this.On(Page.Rock, new TextField(r.Name, v => r.Name = v.Trim(), limit: 80));
            this.DetailCycler = this.On(Page.Rock, new Cycler(new() { ("0", "Auto (HD)"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") }, r.Resolution <= 0 ? "0" : r.Resolution.ToString(), v => { r.Resolution = int.Parse(v); this.ArtDirty = true; },
                "How detailed it looks. Pixel art matches the game's style."));
            this.HitsField = this.On(Page.Rock, new TextField(r.Hits.ToString(), v => r.Hits = int.TryParse(v, out int h) ? RockData.CleanHits(h) : 1, numbersOnly: true, limit: 2));
            this.ExperienceField = this.On(Page.Rock, new TextField(r.Experience.ToString(), v => r.Experience = int.TryParse(v, out int x) ? RockData.CleanExperience(x) : 0, numbersOnly: true, limit: 3));

            // where in the mines
            this.AreaRows = RockData.Areas.Select(area =>
            {
                double current = r.Places.GetValueOrDefault(area.Key);
                Checkbox box = this.On(Page.Where, new Checkbox(area.Label, current > 0, on =>
                {
                    if (on)
                        r.Places[area.Key] = ReadChance(this.AreaChance(area.Key));
                    else
                        r.Places.Remove(area.Key);
                    this.SyncPage();
                }));
                Dropdown chance = this.On(Page.Where, new Dropdown(ChanceOptions(current), ChanceValue(current > 0 ? current : 0.1), v =>
                {
                    if (r.Places.ContainsKey(area.Key))
                        r.Places[area.Key] = ReadChance(v);
                }, "How many of the rocks down there are this one."));
                return (box, chance);
            }).ToArray();

            // what it gives
            this.DropList = this.On(Page.Drops, new ScrollList<RockDrop>(64, this.DrawDropRow)
            {
                OnSelect = (_, _) => this.SyncDrop(),
                EmptyText = "Nothing yet. Click 'Add' to put something in it."
            });
            this.AddDropButton = this.On(Page.Drops, new Button("Add", this.AddDrop, "Pick something the rock can give: one of yours, or one of the game's."));
            this.RemoveDropButton = this.On(Page.Drops, new Button("Remove", this.RemoveDrop));
            this.MinField = this.On(Page.Drops, new TextField("1", v => { if (this.DropList.Selected is { } drop) drop.Min = int.TryParse(v, out int n) ? Math.Max(1, n) : 1; }, numbersOnly: true, limit: 3));
            this.MaxField = this.On(Page.Drops, new TextField("1", v => { if (this.DropList.Selected is { } drop) drop.Max = int.TryParse(v, out int n) ? Math.Max(1, n) : 1; }, numbersOnly: true, limit: 3));
            this.DropChance = this.On(Page.Drops, new Dropdown(ChanceOptions(1), ChanceValue(1), v => { if (this.DropList.Selected is { } drop) drop.Chance = ReadChance(v); }, "How often the rock gives it at all."));

            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.SyncImage();
            this.RefreshDrops();
            this.ShowPage(Page.Rock);
        }

        /// <summary>Edit new art for one of the game's own rocks.</summary>
        /// <param name="store">The mineral store.</param>
        /// <param name="change">The change to that rock (a new one if it has none yet).</param>
        /// <param name="onSaved">Called after saving, with the rock's name.</param>
        public RockEditorScreen(MiningStore store, GameRockChange change, Action<string> onSaved)
            : this(store, AsRock(store, change), isNew: false, onSaved)
        {
            this.GameChange = change;
            this.ShowPage(Page.Rock); // now that it's a game rock, only its looks show
        }

        /// <summary>A change to one of the game's rocks as a rock to edit, so the same screen draws it.</summary>
        private static CustomRock AsRock(MiningStore store, GameRockChange change)
        {
            CustomMineral drawn = store.AsMineral(change);
            return new CustomRock { Id = change.Target, Name = drawn.Name, Image = change.Image, Resolution = change.Resolution };
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

            int lx = area.X + pad;
            int previewH = 170;
            this.Cropper.Bounds = new Rectangle(lx, top, leftW, bottom - top - 60 - previewH - 16);
            int by = this.Cropper.Bounds.Bottom + 10;
            this.ChooseImageButton.Bounds = new Rectangle(lx, by, 210, 48);
            this.FitButton.Bounds = new Rectangle(lx + 220, by, 100, 48);
            this.PaintButton.Bounds = new Rectangle(lx + 330, by, 120, 48);
            this.PreviewArea = new Rectangle(lx, by + 60, leftW, previewH);

            int rx = lx + leftW + 32;
            int rw = area.Right - pad - rx;
            int tabW = (rw - 2 * 8) / 3;
            int tx = rx;
            foreach (Button tab in this.Tabs.Values)
            {
                tab.Bounds = new Rectangle(tx, top, tabW, 52);
                tx += tabW + 8;
            }
            int pageTop = top + 72;
            int labelW = 190, half = (rw - 16) / 2;
            int y = pageTop;

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

            Full("Name", this.NameField);
            Full("Detail", this.DetailCycler);
            Pair("Hits to break", this.HitsField, "Mining XP", this.ExperienceField);

            y = pageTop;
            foreach ((Checkbox box, Dropdown chance) in this.AreaRows)
            {
                box.Bounds = new Rectangle(rx, y, half, 44);
                chance.Bounds = new Rectangle(rx + half + 16, y, 240, 44);
                y += 56;
            }

            // what it gives: the list, with the settings for the one picked under it
            int settingsH = 124;
            this.DropList.Bounds = new Rectangle(rx, pageTop, rw - 180, bottom - pageTop - settingsH);
            this.AddDropButton.Bounds = new Rectangle(rx + rw - 164, pageTop, 164, 52);
            this.RemoveDropButton.Bounds = new Rectangle(rx + rw - 164, pageTop + 64, 164, 52);
            y = this.DropList.Bounds.Bottom + 16;
            int fieldW = 90;
            this.Labels.Add((this.MinField, new Rectangle(rx, y, 120, 48), "Gives"));
            this.MinField.Bounds = new Rectangle(rx + 120, y, fieldW, 48);
            this.Labels.Add((this.MaxField, new Rectangle(rx + 120 + fieldW + 12, y, 40, 48), "to"));
            this.MaxField.Bounds = new Rectangle(rx + 120 + fieldW + 56, y, fieldW, 48);
            this.Labels.Add((this.DropChance, new Rectangle(rx + 120 + fieldW * 2 + 72, y, 100, 48), "How often"));
            this.DropChance.Bounds = new Rectangle(rx + 120 + fieldW * 2 + 176, y, 200, 48);

            // a game rock only gets new looks, so its one control goes on the page with no tabs
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
            string title = this.GameChange != null ? $"New art for the game's {this.Rock.Name}" : this.IsNew ? "New rock" : $"Edit '{this.Rock.Name}'";
            Gfx.Text(b, title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            foreach ((Widget owner, Rectangle row, string label) in this.Labels)
                if (owner.Visible)
                    Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));

            this.DrawPreview(b);
            base.Draw(b, mouseX, mouseY);

            string help = this.GameChange != null
                ? "Where it turns up and what it gives stay the game's. The picture is used wherever the rock is drawn."
                : this.Current switch
            {
                Page.Where => "A chance is how many of the rocks down there are this one. The rest of the level stays the game's.",
                Page.Drops => "This is on top of what any rock gives: stone, and the odd geode or gem the mines hand out anyway.",
                _ => "Hits are with a starter pickaxe; a better one breaks it faster, like the game's own rocks."
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
            Gfx.Text(b, "In the mine", new Vector2(inner.X, inner.Y + icon + 16), Color.White * 0.9f);

            if (this.GameChange != null)
                return;

            int x = inner.X + icon + 48;
            Gfx.Text(b, $"{RockData.CleanHits(this.Rock.Hits)} hit(s)", new Vector2(x, inner.Y + 8), Color.White * 0.9f);
            Gfx.Text(b, $"{RockData.CleanExperience(this.Rock.Experience)} mining XP", new Vector2(x, inner.Y + 8 + Gfx.LineHeight + 4), Color.White * 0.9f);
            int drops = RockData.DropsFor(this.Rock).Count();
            Gfx.Text(b, drops == 0 ? "Gives nothing of its own" : drops == 1 ? "Gives one thing" : $"Gives {drops} things", new Vector2(x, inner.Y + 8 + (Gfx.LineHeight + 4) * 2), Color.White * 0.75f);
        }

        private void DrawDropRow(SpriteBatch b, RockDrop drop, Rectangle bounds, bool selected, bool hover)
        {
            if (ItemRegistry.GetData(drop.Item) is ParsedItemData data)
            {
                int size = bounds.Height - 12;
                b.Draw(data.GetTexture(), new Rectangle(bounds.X + 6, bounds.Y + 6, size, size), data.GetSourceRect(), Color.White);
            }
            int textX = bounds.X + bounds.Height + 8;
            string name = ItemRegistry.GetData(drop.Item)?.DisplayName ?? drop.Item;
            int count = Math.Max(1, drop.Min);
            int most = Math.Max(count, drop.Max);
            string amount = count == most ? $"{count}" : $"{count}-{most}";
            Gfx.Text(b, Gfx.Fit($"{name} x{amount}", bounds.Right - textX - 140), new Vector2(textX, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2));
            string chance = MiningData.ChanceLabel(drop.Chance);
            Vector2 size2 = Gfx.Font.MeasureString(chance);
            Gfx.Text(b, chance, new Vector2(bounds.Right - size2.X - 12, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2), Color.DimGray);
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

        private void SyncPage()
        {
            bool game = this.GameChange != null;
            foreach ((Page page, Button tab) in this.Tabs)
            {
                tab.Toggled = page == this.Current;
                tab.Visible = !game; // a game rock keeps everything but its looks, which fit on one page
            }
            foreach ((Page page, List<Widget> widgets) in this.PageWidgets)
                foreach (Widget widget in widgets)
                    widget.Visible = page == this.Current;

            if (game)
            {
                foreach (Widget widget in new Widget[] { this.NameField, this.HitsField, this.ExperienceField })
                    widget.Visible = false;
                return;
            }

            if (this.Current == Page.Where)
            {
                foreach ((Checkbox box, Dropdown chance) in this.AreaRows)
                    chance.Visible = box.Checked;
            }
            if (this.Current == Page.Drops)
            {
                bool picked = this.DropList.Selected != null;
                this.RemoveDropButton.Visible = picked;
                this.MinField.Visible = this.MaxField.Visible = this.DropChance.Visible = picked;
            }
        }

        /// <summary>The chance picked for a part of the mines now, as a stored value.</summary>
        private string AreaChance(string area) => this.AreaRows[Array.FindIndex(RockData.Areas, a => a.Key == area)].Chance.Value;

        /// <summary>The chances to offer, with the one it already has added if it isn't one of them.</summary>
        private static List<(string Value, string Label)> ChanceOptions(double current)
        {
            List<double> chances = new() { 0.01, 0.02, 0.05, 0.1, 0.2, 0.35, 0.5, 0.75, 1 };
            if (current > 0 && !chances.Any(c => Math.Abs(c - current) < 0.0001))
                chances.Add(current);
            return chances.OrderBy(c => c).Select(c => (ChanceValue(c), MiningData.ChanceLabel(c))).ToList();
        }

        private static string ChanceValue(double chance) => chance.ToString("0.#####", CultureInfo.InvariantCulture);

        private static double ReadChance(string value) => MiningData.CleanChance(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) ? chance : 0.1);

        private void RefreshDrops()
        {
            int selected = this.DropList.SelectedIndex;
            this.DropList.Items = this.Rock.Drops.ToList();
            this.DropList.SelectedIndex = Math.Min(selected, this.DropList.Items.Count - 1);
            this.SyncDrop();
        }

        /// <summary>Show the settings of the drop that's picked.</summary>
        private void SyncDrop()
        {
            if (this.DropList.Selected is { } drop)
            {
                this.MinField.Text = Math.Max(1, drop.Min).ToString();
                this.MaxField.Text = Math.Max(Math.Max(1, drop.Min), drop.Max).ToString();
                this.DropChance.Options = ChanceOptions(drop.Chance);
                this.DropChance.Select(ChanceValue(MiningData.CleanChance(drop.Chance)));
            }
            this.SyncPage();
        }

        /// <summary>Pick something for the rock to give: one of your minerals, or one of the game's ores, minerals, gems or artefacts.</summary>
        private void AddDrop()
        {
            List<ItemPickerScreen.Choice> choices = new();
            foreach (CustomMineral mine in this.Store.File.Minerals)
                choices.Add(new ItemPickerScreen.Choice("(O)" + this.Store.GetItemId(mine.Id), mine.Name, "Mine"));
            foreach (MiningStore.GameMineral game in MiningStore.GetVanillaMinerals())
                choices.Add(new ItemPickerScreen.Choice("(O)" + game.Id, game.Name, "The game's"));
            foreach ((string id, string name) in GameResources())
                choices.Add(new ItemPickerScreen.Choice("(O)" + id, name, "Resource"));

            this.Root.Push(new ItemPickerScreen("What does it give?", choices, choice =>
            {
                this.Rock.Drops.Add(new RockDrop { Item = choice.ItemId, Min = 1, Max = 1, Chance = 1 });
                this.RefreshDrops();
                this.DropList.SelectedIndex = this.DropList.Items.Count - 1;
                this.SyncDrop();
            }));
        }

        /// <summary>The ores and other bits the mines hand out, which a rock most often gives.</summary>
        private static IEnumerable<(string Id, string Name)> GameResources()
        {
            string[] ids = { "378", "380", "384", "386", "390", "382", "62", "64", "66", "68", "70", "72", "80", "82", "84", "86", "749", "535", "536", "537" };
            foreach (string id in ids)
            {
                if (ItemRegistry.GetData("(O)" + id) is { } data)
                    yield return (id, data.DisplayName);
            }
        }

        private void RemoveDrop()
        {
            if (this.DropList.SelectedIndex < 0 || this.DropList.SelectedIndex >= this.Rock.Drops.Count)
                return;
            this.Rock.Drops.RemoveAt(this.DropList.SelectedIndex);
            this.RefreshDrops();
        }

        private void SyncImage()
        {
            ImageRef? image = this.Rock.Image;
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            this.Cropper.SetImage(pixels, image != null && pixels != null ? ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) : null, 1);
            this.FitButton.Visible = pixels != null;
            this.ChooseImageButton.Label = image == null ? "Choose image" : "Change image";
            if (this.Tabs.Count > 0)
                this.SyncPage();
        }

        private void OnCropChanged(Rectangle crop)
        {
            if (this.Rock.Image is { } image)
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
                if (this.Rock.Image == null && this.GameChange != null && OriginalContent.LoadItemSprite("(O)" + this.GameChange.Target, 1) is { } vanilla)
                    icon = new Pixels(ImageProcessor.Premultiply(vanilla.Data), vanilla.Width, vanilla.Height); // no picture of its own yet: the game's
                else
                {
                    MiningStore.RenderedMineral rendered = this.Store.Render(MiningStore.AsMineral(this.Rock), out string? warning);
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
                this.Rock.Image = new ImageRef { File = file };
                this.ArtDirty = true;
                this.SyncImage();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Draw the rock in the game, starting from its picture now.</summary>
        private void Paint()
        {
            int scale = MiningStore.GetScale(this.Rock.Resolution);
            int size = 16 * scale;
            Pixels start;
            if (this.Rock.Image != null)
            {
                Pixels hd = this.Store.Render(MiningStore.AsMineral(this.Rock), out _).IconHd;
                start = new Pixels(ImageProcessor.Unpremultiply(hd.Data), hd.Width, hd.Height);
            }
            else if (this.GameChange != null && OriginalContent.LoadItemSprite("(O)" + this.GameChange.Target, scale) is { } vanilla)
                start = vanilla;
            else
                start = new Pixels(new Color[size * size], size, size);

            this.Root.Push(new PaintScreen(start, $"Paint '{this.Rock.Name}'", pixels =>
            {
                try
                {
                    string file = CustomContent.SaveImage(this.Store.ImageFolder, this.Rock.Name, pixels);
                    this.Images.Remove(file);
                    this.Rock.Image = new ImageRef { File = file };
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
            CustomRock r = this.Rock;
            if (this.GameChange is { } change)
            {
                change.Image = r.Image;
                change.Resolution = r.Resolution;
                try
                {
                    this.Store.SaveGameRockChange(change);
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save: {ex.Message}");
                    return;
                }
                Game1.playSound("newArtifact");
                this.Root.Pop();
                this.OnSaved(r.Name);
                return;
            }
            if (string.IsNullOrWhiteSpace(r.Name))
            {
                this.ShowError("Give the rock a name.");
                return;
            }
            if (r.Image == null)
            {
                this.ShowError("Choose or paint a picture of the rock.");
                return;
            }
            if (!RockData.AreasFor(r).Any())
            {
                this.ShowError("Pick at least one part of the mines it turns up in, on the Where page.");
                return;
            }
            foreach (RockDrop drop in r.Drops)
                drop.Max = Math.Max(Math.Max(1, drop.Min), drop.Max);

            MiningFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(r.Name, "Rock");
                if (baseId.Length == 0)
                    baseId = "Rock";
                string id = baseId;
                for (int i = 2; file.Rocks.Any(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++)
                    id = $"{baseId}_{i}";
                r.Id = id;
                file.Rocks.Add(r);
            }
            else
            {
                int index = file.Rocks.FindIndex(existing => existing.Id == r.Id);
                if (index >= 0)
                    file.Rocks[index] = r;
                else
                    file.Rocks.Add(r);
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
            this.OnSaved(r.Name);
        }
    }
}
