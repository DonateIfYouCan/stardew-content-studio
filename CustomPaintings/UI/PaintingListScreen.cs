using System;
using CustomContentCore;
using CustomContentCore.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.TokenizableStrings;

namespace CustomPaintings.UI
{
    /// <summary>The editor's main page: lists your paintings and the game's paintings.</summary>
    internal sealed class PaintingListScreen : Screen
    {
        /*********
        ** Types
        *********/
        private sealed class Row
        {
            public string FurnitureId = "";
            public string Name = "";
            public string Details = "";
            public string? Badge;
            public CustomPainting? Painting;     // explicit entry in paintings.json
            public bool AutoAdded;
            public string? AutoFile;
            public Texture2D? Thumbnail;         // owned by this screen
            public ParsedItemData? GameData;     // for game paintings
        }


        /*********
        ** Fields
        *********/
        private readonly PaintingStore Store;
        private bool ShowGamePaintings;

        private readonly ScrollList<Row> List;
        private readonly Button MineTab;
        private readonly Button GameTab;
        private readonly TextField SearchField;
        private readonly Button NewPaintingButton;
        private readonly Button NewFrameButton;
        private readonly Button EditButton;
        private readonly Button DeleteButton;
        private readonly Button DuplicateButton;
        private readonly Button GiveButton;
        private readonly Button ReplaceButton;
        private readonly Button RestoreButton;
        private readonly Button HideButton;
        private readonly Button ExportButton;
        private readonly Button CloseButton;
        private readonly Checkbox AutoAddBox;

        private List<Row> AllRows = new();
        private string? Message;
        private Color MessageColor = Color.DarkGreen;


        /*********
        ** Public methods
        *********/
        public PaintingListScreen(PaintingStore store)
        {
            this.Store = store;

            this.List = this.Add(new ScrollList<Row>(88, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditSelected()
            });
            this.MineTab = this.Add(new Button("My paintings", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("Game paintings", () => this.SwitchTab(true)));
            this.SearchField = this.Add(new TextField("", _ => this.ApplyFilter(), limit: 60));

            this.NewPaintingButton = this.Add(new Button("+ New painting", () => this.CreateNew(table: false), "Pick an image from your computer and turn it into a painting."));
            this.NewFrameButton = this.Add(new Button("+ New photo frame", () => this.CreateNew(table: true), "A small standing photo frame for tables and floors."));
            this.EditButton = this.Add(new Button("Edit", this.EditSelected));
            this.DeleteButton = this.Add(new Button("Delete", this.DeleteSelected));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.GiveButton = this.Add(new Button("Put in inventory", this.GiveSelected, "Adds one to your inventory, for testing."));
            this.ReplaceButton = this.Add(new Button("Replace image", this.EditSelected, "Show your own image instead of this painting."));
            this.RestoreButton = this.Add(new Button("Restore original", this.RestoreSelected));
            this.HideButton = this.Add(new Button("Hide from shops", this.ToggleHidden, "Take it out of shops, the catalogue and fishing. Placed copies stay."));
            this.ExportButton = this.Add(new Button("Export original", this.ExportSelected, "Save the game's original art as a PNG, to edit in another program and use as a replacement."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.AutoAddBox = this.Add(new Checkbox("Auto-add images dropped into the paintings folder", this.Store.File.AutoAddImages, this.SetAutoAdd));

            this.Refresh();
        }

        /// <summary>Open the editor for one of this mod's paintings or a game painting, by name or ID.</summary>
        public bool OpenByName(string search)
        {
            foreach (bool game in new[] { false, true })
            {
                this.SwitchTab(game);
                int index = this.List.Items.FindIndex(r => PaintingStore.NormalizeName(r.Name) == PaintingStore.NormalizeName(search) || r.FurnitureId.EndsWith(search, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    this.List.SelectedIndex = index;
                    this.SyncButtons();
                    this.EditSelected();
                    return true;
                }
            }
            this.SwitchTab(false);
            return false;
        }

        public override void OnResume()
        {
            this.Refresh();
        }

        public override void Dispose()
        {
            this.DisposeThumbnails();
        }


        /*********
        ** Layout & drawing
        *********/
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int x = area.X + pad, y = area.Y + 84;
            int w = area.Width - pad * 2;
            int sideW = 300;

            this.MineTab.Bounds = new Rectangle(x, y, 240, 56);
            this.GameTab.Bounds = new Rectangle(x + 250, y, 240, 56);
            this.SearchField.Bounds = new Rectangle(x + w - sideW - 16 - 320, y + 4, 320, 48);
            y += 68;

            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);

            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewPaintingButton, this.NewFrameButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button.Visible ? 64 : 0;
            }
            by += 24;
            foreach (Button button in new[] { this.EditButton, this.ReplaceButton, this.ExportButton, this.GiveButton, this.DuplicateButton, this.RestoreButton, this.HideButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                if (button.Visible)
                    by += 64;
            }

            this.AutoAddBox.Bounds = new Rectangle(x, area.Bottom - 84, 700, 56);
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Paintings", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));

            this.MineTab.Toggled = !this.ShowGamePaintings;
            this.GameTab.Toggled = this.ShowGamePaintings;
            base.Draw(b, mouseX, mouseY);

            if (this.List.Selected == null && this.List.Items.Count > 0)
                Gfx.Text(b, Game1.parseText("Select a painting to edit it.", Game1.smallFont, 300), new Vector2(this.EditButton.Bounds.X, this.EditButton.Bounds.Y + 8), Color.DimGray);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.AutoAddBox.Bounds.Right - 40, new Vector2(this.AutoAddBox.Bounds.Right + 16, area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, Row row, Rectangle bounds, bool selected, bool hover)
        {
            Rectangle thumbArea = new(bounds.X + 8, bounds.Y + 6, 120, bounds.Height - 12);
            if (row.Thumbnail != null)
            {
                if (row.Thumbnail.Width > 64 || row.Thumbnail.Height > 64)
                    Gfx.Fitted(b, row.Thumbnail, null, thumbArea, pixelated: false);
                else
                    this.DrawPixelated(b, row.Thumbnail, null, thumbArea);
            }
            else if (row.GameData != null)
            {
                try
                {
                    Texture2D texture = row.GameData.GetTexture();
                    this.DrawPixelated(b, texture, row.GameData.GetSourceRect(), thumbArea);
                }
                catch
                {
                    // ignore broken textures
                }
            }

            int textX = thumbArea.Right + 16;
            Gfx.Text(b, Gfx.Fit(row.Name, bounds.Right - textX - 140), new Vector2(textX, bounds.Y + 12));
            Gfx.Text(b, Gfx.Fit(row.Details, bounds.Right - textX - 16), new Vector2(textX, bounds.Y + 46), Color.DimGray);
            if (row.Badge != null)
            {
                Vector2 size = Gfx.Font.MeasureString(row.Badge);
                Gfx.Text(b, row.Badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 12), new Color(160, 80, 20));
            }
        }

        private void DrawPixelated(SpriteBatch b, Texture2D texture, Rectangle? source, Rectangle area)
        {
            Rectangle src = source ?? texture.Bounds;
            int scale = Math.Max(1, Math.Min(area.Width / Math.Max(1, src.Width), area.Height / Math.Max(1, src.Height)));
            int w = src.Width * scale, h = src.Height * scale;
            if (w > area.Width || h > area.Height)
            {
                Gfx.Fitted(b, texture, src, area, pixelated: true);
                return;
            }
            b.Draw(texture, new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h), src, Color.White);
        }


        /*********
        ** Data
        *********/
        private void Refresh()
        {
            string? selectedId = this.List.Selected?.FurnitureId;
            this.DisposeThumbnails();
            this.AllRows = this.ShowGamePaintings ? this.BuildGameRows() : this.BuildMyRows();
            this.ApplyFilter();
            int index = this.List.Items.FindIndex(r => r.FurnitureId == selectedId);
            this.List.SelectedIndex = index;
            if (index >= 0)
                this.List.EnsureVisible(index);
            this.AutoAddBox.Checked = this.Store.File.AutoAddImages;
            this.SyncButtons();
        }

        private List<Row> BuildMyRows()
        {
            PaintingsFile file = this.Store.File;
            List<Row> rows = new();
            foreach (ResolvedEntry entry in this.Store.Added)
            {
                CustomPainting? painting = file.Paintings.FirstOrDefault(p => this.Store.GetItemId(p.Id) == entry.FurnitureId);
                string kind = entry.Table ? (entry.Height >= 2 ? "Photo frame (tall)" : "Photo frame") : $"{entry.Width}x{entry.Height} painting";
                if (entry.Slides.Count > 1)
                    kind += entry.AnimationMs > 0 ? $", animated ({entry.Slides.Count} frames)" : $", slideshow of {entry.Slides.Count}";
                Row row = new()
                {
                    FurnitureId = entry.FurnitureId,
                    Name = entry.Name ?? entry.FurnitureId,
                    Details = $"{kind} · {entry.Price}g · {DescribeSources(entry)}",
                    Painting = painting,
                    AutoAdded = painting == null,
                    AutoFile = painting == null ? entry.Slides.FirstOrDefault()?.Path : null,
                    Badge = painting == null ? "auto-added" : null
                };
                row.Thumbnail = this.CreateThumbnail(entry);
                rows.Add(row);
            }
            return rows;
        }

        private List<Row> BuildGameRows()
        {
            List<Row> rows = new();
            IDictionary<string, string> furniture = DataLoader.Furniture(Game1.content);
            foreach ((string id, string raw) in furniture)
            {
                string[] fields = raw.Split('/');
                if (fields.Length < 8 || fields[1] != "painting" || id.StartsWith(this.Store.GetItemId("")))
                    continue;

                ParsedItemData? data = ItemRegistry.GetData("(F)" + id);
                ResolvedEntry? replaced = this.Store.Replaced.FirstOrDefault(r => r.FurnitureId == id);
                bool hidden = this.Store.Removed.Contains(id);
                (int w, int h) = PaintingStore.GetFurnitureSize(raw);

                List<string> badges = new();
                if (replaced != null)
                    badges.Add("replaced");
                if (hidden)
                    badges.Add("hidden");

                Row row = new()
                {
                    FurnitureId = id,
                    Name = data?.DisplayName ?? TokenParser.ParseText(fields[7]),
                    Details = $"ID {id} · {w}x{h} · {fields[5]}g" + (replaced != null ? $" · {DescribeSources(replaced)}" : ""),
                    Badge = badges.Count > 0 ? string.Join(", ", badges) : null,
                    GameData = replaced?.Slides.Count > 0 ? null : data,
                    Thumbnail = replaced?.Slides.Count > 0 ? this.CreateThumbnail(replaced) : null
                };
                rows.Add(row);
            }
            return rows;
        }

        private Texture2D? CreateThumbnail(ResolvedEntry entry)
        {
            if (entry.Slides.Count == 0)
                return null;
            ResolvedSlide slide = entry.Slides[Math.Clamp(entry.CurrentSlide, 0, entry.Slides.Count - 1)];
            Pixels sprite = slide.HiRes
                ?? (slide.Source != null ? ImageProcessor.Render(slide.Source, entry.Width, entry.Height, entry.Table, slide.SourceCrop, entry.Scaling, entry.Frame, 4) : null)
                ?? slide.Sprite;
            Texture2D texture = new(Game1.graphics.GraphicsDevice, sprite.Width, sprite.Height);
            texture.SetData(sprite.Data);
            return texture;
        }

        private static string DescribeSources(ResolvedEntry entry)
        {
            List<string> parts = new();
            if (entry.InCatalogue)
                parts.Add("catalogue");
            foreach (Source source in entry.Sources)
            {
                if (source.IsShop)
                    parts.Add(Names.Shop(source.Shop));
                else if (source.IsFishing)
                    parts.Add($"fishing at {Names.FishingLocation(source.Location)}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "no sources yet";
        }

        private void ApplyFilter()
        {
            string search = this.SearchField.Text.Trim();
            this.List.Items = search.Length == 0
                ? this.AllRows
                : this.AllRows.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || r.FurnitureId.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            this.List.SelectedIndex = -1;
            this.List.Scroll = 0;
            this.SyncButtons();
        }

        private void DisposeThumbnails()
        {
            foreach (Row row in this.AllRows)
                row.Thumbnail?.Dispose();
        }

        private void SyncButtons()
        {
            Row? row = this.List.Selected;
            bool mine = !this.ShowGamePaintings;

            this.NewPaintingButton.Visible = mine;
            this.NewFrameButton.Visible = mine;
            this.AutoAddBox.Visible = mine;

            this.EditButton.Visible = mine && row != null;
            this.DeleteButton.Visible = mine && row != null;
            this.DuplicateButton.Visible = mine && row != null && !row.AutoAdded;
            this.GiveButton.Visible = row != null && ModEntry.Config.EditorCanGive;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Adds one to your inventory, for testing." : "Load a save first.";

            bool replaced = row != null && this.Store.Replaced.Any(r => r.FurnitureId == row.FurnitureId);
            bool hidden = row != null && this.Store.Removed.Contains(row.FurnitureId);
            this.ReplaceButton.Visible = !mine && row != null;
            this.ReplaceButton.Label = replaced ? "Edit replacement" : "Replace image";
            this.RestoreButton.Visible = !mine && replaced;
            this.HideButton.Visible = !mine && row != null;
            this.ExportButton.Visible = !mine && row != null;
            this.HideButton.Label = hidden ? "Show in shops again" : "Hide from shops";

            if (this.Root != null)
                this.OnLayout(this.Area);
        }


        /*********
        ** Actions
        *********/
        private void SwitchTab(bool game)
        {
            if (this.ShowGamePaintings == game)
                return;
            this.ShowGamePaintings = game;
            this.SearchField.Text = "";
            this.Message = null;
            this.List.SelectedIndex = -1;
            this.Refresh();
        }

        private void SetAutoAdd(bool value)
        {
            PaintingsFile file = this.Store.ReadFile();
            file.AutoAddImages = value;
            this.SaveFile(file, value ? "Images in the paintings folder are added automatically." : "Only paintings made here (or listed in paintings.json) are added.");
        }

        private void CreateNew(bool table)
        {
            this.Root.Push(new FileBrowserScreen(null, path =>
            {
                string file;
                try
                {
                    file = this.Store.ImportImage(path);
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't copy the image: {ex.Message}", error: true);
                    return;
                }

                string name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
                if (name.Length > 40)
                    name = name[..40];
                CustomPainting painting = new()
                {
                    Id = "",
                    Name = name.Length > 0 ? name : "Untitled",
                    Image = file,
                    Placement = table ? "table" : "wall",
                    Size = "auto",
                    Price = table ? 500 : 1000,
                    InCatalogue = true
                };
                this.Root.Push(new PaintingEditorScreen(this.Store, painting, isNew: true, saved => this.ShowMessage($"Saved '{saved}'.")));
            }, this.Store.BrowserPlaces));
        }

        private void EditSelected()
        {
            Row? row = this.List.Selected;
            if (row == null)
                return;

            if (this.ShowGamePaintings)
            {
                IDictionary<string, string> furniture = DataLoader.Furniture(Game1.content);
                if (!furniture.TryGetValue(row.FurnitureId, out string? raw))
                    return;
                Replacement replacement = this.Store.File.Replace.FirstOrDefault(r => PaintingStore.ResolveFurnitureId(r.Target, furniture) == row.FurnitureId)
                    ?? new Replacement { Target = row.FurnitureId };
                bool isNew = !this.Store.File.Replace.Contains(replacement);

                if (isNew || replacement.GetSlides().Count == 0)
                {
                    // pick an image first
                    this.Root.Push(new FileBrowserScreen(null, path =>
                    {
                        try
                        {
                            replacement.Image = this.Store.ImportImage(path);
                        }
                        catch (Exception ex)
                        {
                            this.ShowMessage($"Couldn't copy the image: {ex.Message}", error: true);
                            return;
                        }
                        this.OpenReplacementEditor(replacement, isNew, raw, row.Name);
                    }, this.Store.BrowserPlaces));
                }
                else
                    this.OpenReplacementEditor(replacement, isNew, raw, row.Name);
                return;
            }

            CustomPainting painting = row.Painting ?? new CustomPainting
            {
                Id = Path.GetFileNameWithoutExtension(row.AutoFile ?? row.Name),
                Name = row.Name,
                Image = row.AutoFile != null ? Path.GetFileName(row.AutoFile) : null,
                Size = this.Store.File.AutoDefaults.Size,
                Price = this.Store.File.AutoDefaults.Price,
                Scaling = this.Store.File.AutoDefaults.Scaling,
                Frame = this.Store.File.AutoDefaults.Frame,
                InCatalogue = this.Store.File.AutoDefaults.InCatalogue,
                Sources = this.Store.File.AutoDefaults.Sources.ToList()
            };
            this.Root.Push(new PaintingEditorScreen(this.Store, painting, isNew: false, saved => this.ShowMessage($"Saved '{saved}'.")));
        }

        private void OpenReplacementEditor(Replacement replacement, bool isNew, string raw, string name)
        {
            (int w, int h) = PaintingStore.GetFurnitureSize(raw);
            this.Root.Push(new PaintingEditorScreen(this.Store, replacement, isNew, (w, h), name, _ => this.ShowMessage($"Replaced '{name}'.")));
        }

        /// <summary>Copy the selected painting, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } row)
                return;
            PaintingsFile file = this.Store.ReadFile();
            CustomPainting? painting = file.Paintings.FirstOrDefault(p => this.Store.GetItemId(p.Id) == row.FurnitureId);
            if (painting == null)
                return;

            CustomPainting copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomPainting>(Newtonsoft.Json.JsonConvert.SerializeObject(painting))!;
            copy.Name = $"{painting.Name ?? "Painting"} copy";
            string baseId = CustomContent.ToId(copy.Name, "Painting");
            string id = baseId;
            for (int i = 2; file.Paintings.Exists(p => p.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Paintings.Add(copy);
            try
            {
                this.Store.Save(file);
                this.ShowMessage($"Copied to '{copy.Name}'.");
                this.Refresh();
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't copy: {ex.Message}", error: true);
            }
        }

        private void DeleteSelected()
        {
            Row? row = this.List.Selected;
            if (row == null)
                return;

            string warning = "Copies already placed in your world will turn into Error Items.";
            string message = row.AutoAdded
                ? $"Delete '{row.Name}'?\n\nIts image will be moved to paintings/{PaintingStore.RemovedFolderName} so it isn't added again.\n{warning}"
                : $"Delete '{row.Name}'?\n\n{warning}";

            this.Root.Push(new ConfirmScreen(message, "Delete", () =>
            {
                try
                {
                    if (row.AutoAdded)
                    {
                        if (row.AutoFile != null)
                            this.Store.MoveToRemoved(row.AutoFile);
                        this.Store.Reload();
                    }
                    else
                    {
                        PaintingsFile file = this.Store.ReadFile();
                        CustomPainting? painting = file.Paintings.FirstOrDefault(p => this.Store.GetItemId(p.Id) == row.FurnitureId);
                        if (painting != null)
                        {
                            file.Paintings.Remove(painting);
                            HashSet<string> stillUsed = file.Paintings.Cast<ImageSettings>().Concat(file.Replace).SelectMany(p => p.GetSlides()).Select(s => s.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            this.Store.Save(file);
                            foreach (Slide slide in painting.GetSlides().Where(s => !stillUsed.Contains(s.File)))
                                this.Store.DeleteImportedImage(slide.File);
                        }
                    }
                    this.ShowMessage($"Deleted '{row.Name}'.");
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't delete: {ex.Message}", error: true);
                }
            }));
        }

        /// <summary>Export the selected game painting's original art (ignoring any replacement).</summary>
        private void ExportSelected()
        {
            if (this.List.Selected is not { } row)
                return;
            try
            {
                string path = ExportOriginal(row.FurnitureId, row.Name);
                this.ShowMessage($"Exported to {path}");
                Game1.playSound("coin");
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't export: {ex.Message}", error: true);
            }
        }

        /// <summary>Export a game painting's original art (ignoring any replacement), returning the file path.</summary>
        public static string ExportOriginal(string furnitureId, string name)
        {
            Dictionary<string, string>? original = OriginalContent.LoadData<Dictionary<string, string>>("Data/Furniture");
            if (original == null || !original.TryGetValue(furnitureId, out string? raw))
                throw new InvalidOperationException("couldn't find the original data for this painting");

            string[] fields = raw.Split('/');
            string textureName = fields.Length > 9 && !string.IsNullOrWhiteSpace(fields[9]) ? fields[9] : "TileSheets/furniture";
            int spriteIndex = fields.Length > 8 && int.TryParse(fields[8], out int parsed) ? parsed : int.TryParse(furnitureId, out int id) ? id : 0;
            (int w, int h) = PaintingStore.GetFurnitureSize(raw);

            using Texture2D? texture = OriginalContent.LoadTexture(textureName)
                ?? throw new InvalidOperationException($"couldn't read the original texture '{textureName}'");

            // same as the game's default furniture source rectangle
            Rectangle source = new(spriteIndex * 16 % texture.Width, spriteIndex * 16 / texture.Width * 16, w * 16, h * 16);
            return ImageExport.Export(texture, source, $"Painting - {name}", ImageExport.DefaultScale);
        }

        private void GiveSelected()
        {
            Row? row = this.List.Selected;
            if (row == null || !Context.IsWorldReady)
                return;
            Item item = ItemRegistry.Create("(F)" + row.FurnitureId);
            if (!Game1.player.addItemToInventoryBool(item))
                Game1.createItemDebris(item, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added '{item.DisplayName}' to your inventory.");
        }

        private void RestoreSelected()
        {
            Row? row = this.List.Selected;
            if (row == null)
                return;
            PaintingsFile file = this.Store.ReadFile();
            IDictionary<string, string> furniture = DataLoader.Furniture(Game1.content);
            file.Replace.RemoveAll(r => PaintingStore.ResolveFurnitureId(r.Target, furniture) == row.FurnitureId);
            this.SaveFile(file, $"'{row.Name}' is back to the original.");
        }

        private void ToggleHidden()
        {
            Row? row = this.List.Selected;
            if (row == null)
                return;
            PaintingsFile file = this.Store.ReadFile();
            IDictionary<string, string> furniture = DataLoader.Furniture(Game1.content);
            bool hidden = this.Store.Removed.Contains(row.FurnitureId);
            if (hidden)
                file.Remove.RemoveAll(r => PaintingStore.ResolveFurnitureId(r, furniture) == row.FurnitureId);
            else
                file.Remove.Add(row.FurnitureId);
            this.SaveFile(file, hidden ? $"'{row.Name}' can be bought again." : $"'{row.Name}' is hidden from shops, the catalogue and fishing.");
        }

        private void SaveFile(PaintingsFile file, string message)
        {
            try
            {
                this.Store.Save(file);
                this.ShowMessage(message);
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't save: {ex.Message}", error: true);
            }
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            this.Refresh();
        }
    }
}
