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

namespace CustomFish.UI
{
    /// <summary>Edits one fish: its picture, how and where it's caught, the tank and pond, and gift tastes.</summary>
    internal sealed class FishEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        private static readonly string[] SeasonNames = { "spring", "summer", "fall", "winter" };

        /// <summary>The editor's pages.</summary>
        private enum Page { Fish, Catching, Where, Tank, Gifts }

        private readonly FishStore Store;
        private readonly CustomFishItem Fish;
        private readonly bool IsNew;

        /// <summary>Called after saving, with the saved name.</summary>
        private readonly Action<string> OnSaved;

        /// <summary>When editing new art for one of the game's own fish, the change being made; null for your own fish.</summary>
        /// <remarks>The game fish keeps where it bites and what it's worth, so only the art controls are shown.</remarks>
        private readonly GameFishChange? GameChange;

        private Page Current = Page.Fish;
        private readonly Dictionary<string, Pixels?> Images = new(StringComparer.OrdinalIgnoreCase);
        private Texture2D? PreviewIcon;
        private Texture2D? PreviewTank;
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
        private readonly TextField PriceField;
        private readonly TextField EnergyField;
        private readonly Cycler DetailCycler;

        private readonly Cycler MethodCycler;
        private readonly TextField DifficultyField;
        private readonly Cycler BehaviorCycler;
        private readonly TextField MinSizeField;
        private readonly TextField MaxSizeField;
        private readonly TextField ChanceField;
        private readonly TextField LevelField;
        private readonly Cycler WaterCycler;

        private readonly Checkbox[] SeasonBoxes;
        private readonly Cycler WeatherCycler;
        private readonly Cycler StartCycler;
        private readonly Cycler EndCycler;
        private readonly Checkbox[] PlaceBoxes;

        private readonly Checkbox AquariumBox;
        private readonly Cycler SwimCycler;
        private readonly Cycler TurnCycler;
        private readonly Checkbox FlipBox;
        private readonly Checkbox PondBox;
        private readonly Cycler RoeCycler;

        private readonly ScrollList<(string Name, string Label)> GiftList;

        private readonly Button SaveButton;
        private readonly Button CancelButton;


        /*********
        ** Public methods
        *********/
        public FishEditorScreen(FishStore store, CustomFishItem fish, bool isNew, Action<string> onSaved)
        {
            this.Store = store;
            this.Fish = JsonConvert.DeserializeObject<CustomFishItem>(JsonConvert.SerializeObject(fish))!; // edit a copy so Cancel discards changes
            this.IsNew = isNew;
            this.OnSaved = onSaved;
            CustomFishItem f = this.Fish;
            if (f.Seasons.Count == 0)
                f.Seasons = SeasonNames.ToList(); // no seasons in the file means all year, so show it that way

            // picture
            this.Cropper = this.Add(new CropWidget { OnChanged = this.OnCropChanged, EmptyText = "Choose or paint a picture of the fish" });
            this.ChooseImageButton = this.Add(new Button("Choose image", this.BrowseImage, "Pick an image from your computer."));
            this.FitButton = this.Add(new Button("Fit", () => { this.Cropper.Fit(); this.ArtDirty = true; }, "Use the biggest square of the picture."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw the fish here in the game, starting from the picture it has now."));

            foreach (Page page in Enum.GetValues<Page>())
            {
                Page p = page;
                this.Tabs[page] = this.Add(new Button(page switch { Page.Fish => "Fish", Page.Catching => "Catching", Page.Where => "Where", Page.Tank => "Tank & pond", _ => "Gifts" }, () => this.ShowPage(p)));
                this.PageWidgets[page] = new List<Widget>();
            }

            // the fish itself
            this.NameField = this.On(Page.Fish, new TextField(f.Name, v => f.Name = v.Trim(), limit: 80));
            this.DescriptionField = this.On(Page.Fish, new TextField(f.Description, v => f.Description = v, limit: 200));
            this.PriceField = this.On(Page.Fish, new TextField(f.Price.ToString(), v => f.Price = int.TryParse(v, out int p) ? Math.Max(0, p) : 0, numbersOnly: true, limit: 7));
            this.EnergyField = this.On(Page.Fish, new TextField(f.Energy > 0 ? f.Energy.ToString() : "", v => f.Energy = int.TryParse(v, out int e) ? Math.Max(0, e) : 0, numbersOnly: true, limit: 4));
            this.DetailCycler = this.On(Page.Fish, new Cycler(new() { ("0", "Auto (HD)"), ("16", "Pixel art"), ("32", "Sharp"), ("64", "HD") }, f.Resolution <= 0 ? "0" : f.Resolution.ToString(), v => { f.Resolution = int.Parse(v); this.ArtDirty = true; },
                "How detailed it looks. Pixel art matches the game's style."));

            // catching
            this.MethodCycler = this.On(Page.Catching, new Cycler(new() { (FishData.Rod, "Fishing rod"), (FishData.CrabPot, "Crab pot") }, f.Method, v => { f.Method = v; this.SyncPage(); },
                "Caught with a rod and the fishing minigame, or found in crab pots."));
            this.DifficultyField = this.On(Page.Catching, new TextField(f.Difficulty.ToString(), v => f.Difficulty = int.TryParse(v, out int d) ? Math.Clamp(d, 0, 150) : 0, numbersOnly: true, limit: 3));
            this.BehaviorCycler = this.On(Page.Catching, new Cycler(FishData.Behaviors.Select(b => (b, char.ToUpper(b[0]) + b[1..])).ToList(), f.Behavior, v => f.Behavior = v,
                "How it moves in the minigame: mixed, dart (jumps about), smooth, sinker (heads down) or floater (heads up)."));
            this.MinSizeField = this.On(Page.Catching, new TextField(f.MinSize.ToString(), v => f.MinSize = int.TryParse(v, out int s) ? Math.Max(1, s) : 1, numbersOnly: true, limit: 3));
            this.MaxSizeField = this.On(Page.Catching, new TextField(f.MaxSize.ToString(), v => f.MaxSize = int.TryParse(v, out int s) ? Math.Max(1, s) : 1, numbersOnly: true, limit: 3));
            this.ChanceField = this.On(Page.Catching, new TextField(((int)Math.Round(f.BiteChance * 100)).ToString(), v => f.BiteChance = int.TryParse(v, out int c) ? Math.Clamp(c, 1, 100) / 100.0 : 0.4, numbersOnly: true, limit: 3));
            this.LevelField = this.On(Page.Catching, new TextField(f.MinFishingLevel.ToString(), v => f.MinFishingLevel = int.TryParse(v, out int l) ? Math.Clamp(l, 0, 10) : 0, numbersOnly: true, limit: 2));
            this.WaterCycler = this.On(Page.Catching, new Cycler(new() { ("freshwater", "Fresh water"), ("ocean", "Ocean") }, f.WaterType, v => f.WaterType = v, "Which crab pots find it: ones in rivers and lakes, or in the sea."));

            // where and when
            this.SeasonBoxes = SeasonNames.Select(season => this.On(Page.Where, new Checkbox(char.ToUpper(season[0]) + season[1..], f.Seasons.Contains(season, StringComparer.OrdinalIgnoreCase), on =>
            {
                f.Seasons.RemoveAll(s => s.Equals(season, StringComparison.OrdinalIgnoreCase));
                if (on)
                    f.Seasons.Add(season);
                f.Seasons = SeasonNames.Where(n => f.Seasons.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            }))).ToArray();
            this.WeatherCycler = this.On(Page.Where, new Cycler(new() { ("both", "Any weather"), ("sunny", "Sun only"), ("rainy", "Rain only") }, f.Weather, v => f.Weather = v));
            List<(string, string)> times = Enumerable.Range(6, 21).Select(h => ((h * 100).ToString(), TimeLabel(h * 100))).ToList();
            this.StartCycler = this.On(Page.Where, new Cycler(times.Take(times.Count - 1).ToList(), (f.StartTime / 100 * 100).ToString(), v => f.StartTime = int.Parse(v)));
            this.EndCycler = this.On(Page.Where, new Cycler(times.Skip(1).ToList(), (f.EndTime / 100 * 100).ToString(), v => f.EndTime = int.Parse(v)));
            this.PlaceBoxes = FishData.Places.Select(place => this.On(Page.Where, new Checkbox(place.Label, f.Locations.Contains(place.Key), on =>
            {
                f.Locations.Remove(place.Key);
                if (on)
                    f.Locations.Add(place.Key);
            }))).ToArray();

            // tank and pond
            this.AquariumBox = this.On(Page.Tank, new Checkbox("Can live in a fish tank", f.InAquarium, v => { f.InAquarium = v; this.SyncPage(); }, "Whether it can go in the fish tanks you build or buy."));
            this.SwimCycler = this.On(Page.Tank, new Cycler(new() { ("fish", "Swims"), ("float", "Floats"), ("eel", "Wriggles like an eel"), ("ground", "Sits on the bottom"), ("crawl", "Crawls on the bottom") }, f.SwimStyle, v => f.SwimStyle = v));
            this.TurnCycler = this.On(Page.Tank, new Cycler(new() { ("0", "Keep it as it is"), ("45", "Turn it right"), ("-45", "Turn it left") }, f.TankTurn.ToString(), v => { f.TankTurn = int.Parse(v); this.ArtDirty = true; },
                "Fish pictures are often drawn diagonally. Turn it so it swims level in the tank."));
            this.FlipBox = this.On(Page.Tank, new Checkbox("Mirror it", f.TankFlip, v => { f.TankFlip = v; this.ArtDirty = true; }, "Tank fish face right. Mirror it if it would swim backwards."));
            this.PondBox = this.On(Page.Tank, new Checkbox("Can live in a fish pond", f.InPond, v => { f.InPond = v; this.SyncPage(); }, "Whether it can go in a fish pond, where it multiplies and makes roe."));
            List<(string, string)> colours = ColorTags.Colors.Select(c => (c.Name, ColorTags.Label(c.Name))).ToList();
            colours.Insert(0, ("", "The fish's own colour"));
            this.RoeCycler = this.On(Page.Tank, new Cycler(colours, f.RoeColor, v => f.RoeColor = v, "The colour of the roe it makes in a fish pond."));

            // gifts
            this.GiftList = this.On(Page.Gifts, GiftTasteList.Create(f.GiftTastes));

            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.SyncImage();
            this.ShowPage(Page.Fish);
        }

        /// <summary>Edit new art for one of the game's own fish.</summary>
        /// <param name="store">The fish store.</param>
        /// <param name="change">The change to that fish (a new one if it has none yet).</param>
        /// <param name="onSaved">Called after saving, with the fish's name.</param>
        public FishEditorScreen(FishStore store, GameFishChange change, Action<string> onSaved)
            : this(store, store.AsFish(change), isNew: false, onSaved)
        {
            this.GameChange = change;
            this.ShowPage(Page.Fish); // now that it's a game fish, only its looks show
        }

        public override void Dispose()
        {
            this.Cropper.Dispose();
            this.PreviewIcon?.Dispose();
            this.PreviewTank?.Dispose();
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
            int tabW = (rw - 4 * 8) / 5;
            int tx = rx;
            foreach (Button tab in this.Tabs.Values)
            {
                tab.Bounds = new Rectangle(tx, top, tabW, 52);
                tx += tabW + 8;
            }
            int pageTop = top + 72;
            int labelW = 170, half = (rw - 16) / 2;
            int y;

            void Full(Page page, string label, Widget widget)
            {
                this.Labels.Add((widget, new Rectangle(rx, y, labelW, 48), label));
                widget.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 48);
                y += 56;
            }
            void Pair(Page page, string leftLabel, Widget left, string rightLabel, Widget right)
            {
                this.Labels.Add((left, new Rectangle(rx, y, labelW, 48), leftLabel));
                left.Bounds = new Rectangle(rx + labelW, y, half - labelW, 48);
                this.Labels.Add((right, new Rectangle(rx + half + 16, y, labelW, 48), rightLabel));
                right.Bounds = new Rectangle(rx + half + 16 + labelW, y, half - labelW, 48);
                y += 56;
            }

            y = pageTop;
            Full(Page.Fish, "Name", this.NameField);
            Full(Page.Fish, "Description", this.DescriptionField);
            Pair(Page.Fish, "Sells for", this.PriceField, "Energy", this.EnergyField);
            Full(Page.Fish, "Detail", this.DetailCycler);

            y = pageTop;
            Full(Page.Catching, "Caught with", this.MethodCycler);
            this.Labels.Add((this.WaterCycler, new Rectangle(rx, y, labelW, 48), "Water"));
            Pair(Page.Catching, "Difficulty", this.DifficultyField, "Moves", this.BehaviorCycler);
            Pair(Page.Catching, "Smallest (in)", this.MinSizeField, "Biggest (in)", this.MaxSizeField);
            Pair(Page.Catching, "Bites (%)", this.ChanceField, "Fishing level", this.LevelField);
            this.WaterCycler.Bounds = this.DifficultyField.Bounds with { Width = rw - labelW }; // a crab pot fish has water instead of the minigame's settings

            y = pageTop;
            this.Labels.Add((this.SeasonBoxes[0], new Rectangle(rx, y, labelW, 44), "Seasons"));
            int sw = (rw - labelW) / 4;
            for (int i = 0; i < this.SeasonBoxes.Length; i++)
                this.SeasonBoxes[i].Bounds = new Rectangle(rx + labelW + i * sw, y, sw, 44);
            y += 52;
            Full(Page.Where, "Weather", this.WeatherCycler);
            Pair(Page.Where, "From", this.StartCycler, "Until", this.EndCycler);
            this.Labels.Add((this.PlaceBoxes[0], new Rectangle(rx, y, labelW, 44), "Bites in"));
            y += 48;
            int rows = (this.PlaceBoxes.Length + 1) / 2;
            int rowH = Math.Clamp((bottom - y) / Math.Max(1, rows), 36, 48);
            for (int i = 0; i < this.PlaceBoxes.Length; i++)
                this.PlaceBoxes[i].Bounds = new Rectangle(rx + (i / rows) * (half + 16), y + (i % rows) * rowH, half, rowH - 4);

            y = pageTop;
            this.AquariumBox.Bounds = new Rectangle(rx, y, rw, 44);
            y += 52;
            Full(Page.Tank, "In the tank", this.SwimCycler);
            Full(Page.Tank, "Picture", this.TurnCycler);
            this.FlipBox.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 44);
            y += 68;
            this.PondBox.Bounds = new Rectangle(rx, y, rw, 44);
            y += 52;
            Full(Page.Tank, "Roe colour", this.RoeCycler);

            this.GiftList.Bounds = new Rectangle(rx, pageTop + 40, rw, bottom - pageTop - 40);

            // a game fish only gets new looks, so its few controls go on one page with no tabs
            if (this.GameChange != null)
            {
                this.Labels.RemoveAll(l => l.Owner == this.DetailCycler || l.Owner == this.TurnCycler);
                y = top;
                Full(Page.Fish, "Detail", this.DetailCycler);
                Full(Page.Fish, "In a tank", this.TurnCycler);
                this.FlipBox.Bounds = new Rectangle(rx + labelW, y, rw - labelW, 44);
            }

            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
            this.SyncPage();
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            string title = this.GameChange != null ? $"New art for the game's {this.Fish.Name}" : this.IsNew ? "New fish" : $"Edit '{this.Fish.Name}'";
            Gfx.Text(b, title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            foreach ((Widget owner, Rectangle row, string label) in this.Labels)
                if (owner.Visible)
                    Gfx.Text(b, label, new Vector2(row.X, row.Y + (row.Height - Gfx.LineHeight) / 2));

            if (this.Current == Page.Where && this.Fish.Method == FishData.CrabPot && this.GameChange == null)
                Gfx.Message(b, "Crab pots find it anywhere with the right water, at any time and in any season. Set the water on the Catching page.", this.GiftList.Bounds.Width, new Vector2(this.GiftList.Bounds.X, this.GiftList.Bounds.Y - 32), Color.DimGray);
            if (this.Current == Page.Gifts)
                Gfx.Text(b, "Click a villager to change how they feel about it as a gift.", new Vector2(this.GiftList.Bounds.X, this.GiftList.Bounds.Y - 38), Color.DimGray);

            this.DrawPreview(b);
            base.Draw(b, mouseX, mouseY);

            string help = this.GameChange != null
                ? "Where it bites and what it's worth stay the game's. The picture is used for the item and in fish tanks."
                : "Difficulty: the game's fish go from about 15 (easy) to 110 (legendary).";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void DrawPreview(SpriteBatch b)
        {
            Gfx.Inset(b, this.PreviewArea, new Color(40, 70, 110));
            this.RenderPreviewIfNeeded();
            Rectangle inner = new(this.PreviewArea.X + 16, this.PreviewArea.Y + 12, this.PreviewArea.Width - 32, this.PreviewArea.Height - 24);

            int icon = 96;
            if (this.PreviewIcon != null)
                b.Draw(this.PreviewIcon, new Rectangle(inner.X, inner.Y + 8, icon, icon), Color.White);
            Gfx.Text(b, "Item", new Vector2(inner.X, inner.Y + icon + 16), Color.White * 0.9f);

            int x = inner.X + icon + 48;
            if (this.PreviewTank != null && (this.GameChange != null ? this.Fish.Image != null : this.Fish.InAquarium))
            {
                int cell = this.PreviewTank.Width; // the sheet is one cell wide
                int size = Math.Min(inner.Height - 36, 120);
                b.Draw(this.PreviewTank, new Rectangle(x, inner.Y, size, size), new Rectangle(0, 0, cell, cell), Color.White);
                Gfx.Text(b, "In a tank", new Vector2(x, inner.Y + icon + 16), Color.White * 0.9f);
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

        /// <summary>Show the current page's fields, and only the ones that apply to this fish.</summary>
        private void SyncPage()
        {
            bool game = this.GameChange != null;
            bool crabPot = this.Fish.Method == FishData.CrabPot;
            foreach ((Page page, Button tab) in this.Tabs)
            {
                tab.Toggled = page == this.Current;
                tab.Visible = !game; // a game fish keeps everything but its looks, which fit on one page
            }
            foreach ((Page page, List<Widget> widgets) in this.PageWidgets)
                foreach (Widget widget in widgets)
                    widget.Visible = page == this.Current;

            // what doesn't apply to this fish
            if (game)
            {
                foreach (Widget widget in new Widget[] { this.NameField, this.DescriptionField, this.PriceField, this.EnergyField, this.AquariumBox, this.SwimCycler, this.PondBox, this.RoeCycler })
                    widget.Visible = false;
                // turning only applies to new art: without it, the fish keeps the game's own tank sprite
                this.TurnCycler.Visible = this.FlipBox.Visible = this.Fish.Image != null;
            }
            if (this.Current == Page.Catching)
            {
                this.DifficultyField.Visible = this.BehaviorCycler.Visible = this.LevelField.Visible = !crabPot;
                this.WaterCycler.Visible = crabPot;
            }
            else
                this.WaterCycler.Visible = false;
            if (this.Current == Page.Where && crabPot)
                foreach (Widget widget in this.PageWidgets[Page.Where])
                    widget.Visible = false;
            if (this.Current == Page.Tank && !game)
            {
                this.SwimCycler.Visible = this.TurnCycler.Visible = this.FlipBox.Visible = this.Fish.InAquarium;
                this.RoeCycler.Visible = this.Fish.InPond;
            }
        }

        /// <summary>A game time like 1300 as the game says it, like 1pm.</summary>
        private static string TimeLabel(int time)
        {
            int hour = time / 100 % 24;
            return hour switch { 0 => "midnight", 12 => "noon", < 12 => $"{hour}am", _ => $"{hour - 12}pm" } + (time >= 2400 ? " (night)" : "");
        }

        private void SyncImage()
        {
            ImageRef? image = this.Fish.Image;
            Pixels? pixels = image != null ? this.GetImage(image.File) : null;
            this.Cropper.SetImage(pixels, image != null && pixels != null ? ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) : null, 1);
            this.FitButton.Visible = pixels != null;
            this.ChooseImageButton.Label = image == null ? "Choose image" : "Change image";
            if (this.Tabs.Count > 0)
                this.SyncPage();
        }

        private void OnCropChanged(Rectangle crop)
        {
            if (this.Fish.Image is { } image)
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
            this.PreviewTank?.Dispose();
            this.PreviewIcon = null;
            this.PreviewTank = null;
            try
            {
                Pixels icon, tank;
                if (this.Fish.Image == null && this.GameChange != null && FishStore.GetVanillaIcon(this.GameChange.Target, 1) is { } vanilla)
                {
                    // no picture of its own yet: the game's, and how it would look in a tank
                    icon = new Pixels(ImageProcessor.Premultiply(vanilla.Data), vanilla.Width, vanilla.Height);
                    Pixels sheet = FishStore.MakeTankSheet(vanilla, 1, this.Fish.TankTurn, this.Fish.TankFlip);
                    tank = new Pixels(ImageProcessor.Premultiply(sheet.Data), sheet.Width, sheet.Height);
                }
                else
                {
                    FishStore.RenderedFish rendered = this.Store.Render(this.Fish, out string? warning);
                    icon = rendered.IconHd;
                    tank = rendered.TankHd;
                    if (warning != null)
                        this.ShowError(warning);
                }
                this.PreviewIcon = new Texture2D(Game1.graphics.GraphicsDevice, icon.Width, icon.Height);
                this.PreviewIcon.SetData(icon.Data);
                this.PreviewTank = new Texture2D(Game1.graphics.GraphicsDevice, tank.Width, tank.Height);
                this.PreviewTank.SetData(tank.Data);
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
                this.Fish.Image = new ImageRef { File = file };
                this.ArtDirty = true;
                this.SyncImage();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Draw the fish in the game, starting from its picture now: its own, the game's, or an empty one.</summary>
        private void Paint()
        {
            int scale = FishStore.GetScale(this.Fish.Resolution);
            int size = 16 * scale;
            Pixels start;
            if (this.Fish.Image != null)
            {
                Pixels hd = this.Store.Render(this.Fish, out _).IconHd;
                start = new Pixels(ImageProcessor.Unpremultiply(hd.Data), hd.Width, hd.Height);
            }
            else if (this.GameChange != null && FishStore.GetVanillaIcon(this.GameChange.Target, scale) is { } vanilla)
                start = vanilla;
            else
                start = new Pixels(new Color[size * size], size, size);

            this.Root.Push(new PaintScreen(start, $"Paint '{this.Fish.Name}'", pixels =>
            {
                try
                {
                    string file = CustomContent.SaveImage(this.Store.ImageFolder, this.Fish.Name, pixels);
                    this.Images.Remove(file);
                    this.Fish.Image = new ImageRef { File = file };
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
            CustomFishItem f = this.Fish;
            if (this.GameChange is { } change)
            {
                change.Image = f.Image;
                change.Resolution = f.Resolution;
                change.TankTurn = f.TankTurn;
                change.TankFlip = f.TankFlip;
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
                this.OnSaved(f.Name);
                return;
            }
            if (string.IsNullOrWhiteSpace(f.Name))
            {
                this.ShowError("Give the fish a name.");
                return;
            }
            if (f.Image == null)
            {
                this.ShowError("Choose or paint a picture of the fish.");
                return;
            }
            if (f.Method == FishData.Rod && f.Seasons.Count == 0)
            {
                this.ShowError("Pick at least one season it bites in, on the Where page.");
                return;
            }
            if (f.Method == FishData.Rod && f.Locations.Count == 0)
            {
                this.ShowError("Pick at least one place it bites, on the Where page.");
                return;
            }
            if (f.EndTime <= f.StartTime)
            {
                this.ShowError("It has to stop biting after it starts, on the Where page.");
                return;
            }

            FishFile file = this.Store.ReadFile();
            if (this.IsNew)
            {
                string baseId = CustomContent.ToId(f.Name, "Fish");
                if (baseId.Length == 0)
                    baseId = "Fish";
                string id = baseId;
                for (int i = 2; file.Fish.Any(existing => existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++)
                    id = $"{baseId}_{i}";
                f.Id = id;
                file.Fish.Add(f);
            }
            else
            {
                int index = file.Fish.FindIndex(existing => existing.Id == f.Id);
                if (index >= 0)
                    file.Fish[index] = f;
                else
                    file.Fish.Add(f);
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
            this.OnSaved(f.Name);
        }
    }
}
