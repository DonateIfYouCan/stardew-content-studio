using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomCharacters.UI
{
    /// <summary>Edits the farmer's HD sheets (body, hair, clothes, hats, accessories): export the originals, choose HD versions, browse the sheet's items and preview your farmer.</summary>
    internal sealed class FarmerEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        private readonly CharacterStore Store;
        private readonly Action OnSaved;

        /// <summary>Which sheets this page shows: the ones you pick when making your character, or the ones you wear.</summary>
        private readonly string Group;

        /// <summary>The HD sheet per layer ID being edited (a copy, so Cancel discards changes).</summary>
        private readonly Dictionary<string, string> Files;

        /// <summary>The game's original sheets by layer ID.</summary>
        private readonly Dictionary<string, Texture2D?> Originals = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Decoded HD sheets by file.</summary>
        private readonly Dictionary<string, Texture2D?> HdSheets = new(StringComparer.OrdinalIgnoreCase);

        private FarmerLayer Layer;
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        private readonly Cycler LayerCycler;
        private readonly Button PrevItemButton;
        private readonly Button NextItemButton;
        private readonly TextField ItemField;
        private readonly Checkbox TryOnBox;
        private readonly Button ExportButton;
        private readonly Button ChooseButton;
        private readonly Button PaintButton;
        private readonly Button RemoveButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle SheetArea;
        private Rectangle ZoomArea;
        private Rectangle FarmerArea;

        /// <summary>Which item of the sheet is shown in the close-up (a hairstyle, hat, beard, ...).</summary>
        private int ItemIndex;

        /// <summary>How many items the current sheet holds (set while drawing).</summary>
        private int ItemCount = 1;


        /*********
        ** Public methods
        *********/
        /// <param name="store">The mod's content.</param>
        /// <param name="group">Which sheets to show: <see cref="FarmerHd.FarmerGroup"/> or <see cref="FarmerHd.ClothesGroup"/>.</param>
        /// <param name="onSaved">Called after saving.</param>
        public FarmerEditorScreen(CharacterStore store, string group, Action onSaved)
        {
            this.Store = store;
            this.Group = group;
            this.OnSaved = onSaved;
            this.Files = new Dictionary<string, string>(store.File.Farmer, StringComparer.OrdinalIgnoreCase);

            FarmerLayer[] layers = FarmerHd.GetLayers(group);
            this.Layer = layers[0];
            this.LayerCycler = this.Add(new Cycler(layers.Select(l => (l.Id, l.Label)).ToList(), this.Layer.Id, v => { this.Layer = FarmerHd.GetLayer(v)!; this.Message = null; this.SetItem(0); this.SyncButtons(); },
                "The farmer is drawn in layers. Each can have an HD sheet; the others stay as they are."));
            this.PrevItemButton = this.Add(new Button("-", () => this.SetItem(this.ItemIndex - 1), "Show the previous one."));
            this.ItemField = this.Add(new TextField("0", this.OnItemTyped, numbersOnly: true, limit: 5));
            this.NextItemButton = this.Add(new Button("+", () => this.SetItem(this.ItemIndex + 1), "Show the next one."));
            this.TryOnBox = this.Add(new Checkbox("Try it on", false, _ => { }, "Show this one on the preview farmer. Your real farmer isn't changed."));
            this.ExportButton = this.Add(new Button("Export original sheet", this.ExportOriginal, "Save the game's sheet (enlarged, with sharp pixels) to paint over in another program."));
            this.ChooseButton = this.Add(new Button("Choose HD sheet", this.Browse, "Pick your HD version. It must be the original size times a whole number."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw on the sheet here in the game. Without an HD sheet yet, it starts from the game's art enlarged 4x."));
            this.RemoveButton = this.Add(new Button("Remove", this.RemoveSheet));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.SyncButtons();
        }

        public override void Dispose()
        {
            foreach (Texture2D? texture in this.Originals.Values.Concat(this.HdSheets.Values))
                texture?.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        private string Title => this.Group == FarmerHd.ClothesGroup ? "Clothes & hats (HD)" : "Farmer (HD)";

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;
            this.LayerCycler.Bounds = new Rectangle(area.X + pad + 110, top, 380, 48);

            int sheetW = (int)(area.Width * 0.3);
            int farmerW = 220;
            int zx = area.X + pad + sheetW + 24;
            int zw = area.Right - pad - farmerW - 24 - zx;

            // the row that picks which item of the sheet to look at
            top += 60;
            this.PrevItemButton.Bounds = new Rectangle(zx + 76, top, 48, 44);
            this.ItemField.Bounds = new Rectangle(zx + 130, top, 86, 44);
            this.NextItemButton.Bounds = new Rectangle(zx + 222, top, 48, 44);
            this.TryOnBox.Bounds = new Rectangle(zx + 286, top, Math.Max(110, zw - 286), 44);
            top += 56;

            this.SheetArea = new Rectangle(area.X + pad, top + 36, sheetW, bottom - top - 36 - 64);
            this.ExportButton.Bounds = new Rectangle(area.X + pad, this.SheetArea.Bottom + 12, Math.Min(300, sheetW), 48);
            this.FarmerArea = new Rectangle(area.Right - pad - farmerW, top + 36, farmerW, bottom - top - 36 - 64);
            this.ZoomArea = new Rectangle(zx, top + 36, zw, bottom - top - 36 - 64);
            this.ChooseButton.Bounds = new Rectangle(this.ZoomArea.X, this.ZoomArea.Bottom + 12, 250, 48);
            this.PaintButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.ZoomArea.Bottom + 12, 120, 48);
            this.RemoveButton.Bounds = new Rectangle(this.PaintButton.Bounds.Right + 10, this.ZoomArea.Bottom + 12, 150, 48);
            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, this.Title, new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Sheet", new Vector2(area.X + 32, this.LayerCycler.Bounds.Y + 10));

            Texture2D? original = this.GetOriginal(this.Layer);
            (Texture2D? hd, int factor, string status) = this.GetSheet(this.Layer);
            Gfx.Text(b, Gfx.Fit(status, area.Right - 60 - this.LayerCycler.Bounds.Right - 24), new Vector2(this.LayerCycler.Bounds.Right + 24, this.LayerCycler.Bounds.Y + 10), Color.DimGray);
            Gfx.Text(b, this.Layer.ItemWord, new Vector2(this.ZoomArea.X, this.PrevItemButton.Bounds.Y + 10));

            // sheet overview
            Gfx.Text(b, hd != null ? $"Your sheet ({factor}x)" : "Original sheet", new Vector2(this.SheetArea.X, this.SheetArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.SheetArea, new Color(200, 190, 170));
            if ((hd ?? original) is { } sheet)
                Gfx.Fitted(b, sheet, null, new Rectangle(this.SheetArea.X + 12, this.SheetArea.Y + 12, this.SheetArea.Width - 24, this.SheetArea.Height - 24), pixelated: hd == null);

            // close-up of one item, original vs yours
            Gfx.Text(b, "Close-up", new Vector2(this.ZoomArea.X, this.ZoomArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.ZoomArea, new Color(120, 170, 90));
            if (original != null)
            {
                (Rectangle source, int count) = this.Layer.GetItem(this.ItemIndex, original.Width, original.Height);
                this.ItemCount = count;
                int cellW = hd != null ? (this.ZoomArea.Width - 48) / 2 : this.ZoomArea.Width - 32;
                int scale = Math.Max(1, Math.Min(cellW / Math.Max(1, source.Width), (this.ZoomArea.Height - 80) / Math.Max(1, source.Height)));
                Rectangle dest = new(this.ZoomArea.X + 16, this.ZoomArea.Y + 16, source.Width * scale, source.Height * scale);
                b.Draw(original, dest, source, Color.White);
                if (hd != null)
                {
                    (Rectangle hdSource, _) = this.Layer.GetItem(this.ItemIndex, hd.Width, hd.Height, factor);
                    b.Draw(hd, new Rectangle(dest.Right + 16, dest.Y, dest.Width, dest.Height), hdSource, Color.White);
                }
                string caption = hd != null
                    ? $"{this.ItemIndex} of {count} in the sheet. Left: original, right: yours."
                    : $"{this.ItemIndex} of {count} in the sheet. Choose an HD sheet to compare.";
                Gfx.Text(b, Gfx.Fit(caption, this.ZoomArea.Width - 32), new Vector2(this.ZoomArea.X + 16, this.ZoomArea.Bottom - 44), Color.White);
            }

            // your farmer as currently saved (with their own colors)
            Gfx.Text(b, "Your farmer", new Vector2(this.FarmerArea.X, this.FarmerArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.FarmerArea, new Color(200, 190, 170));
            if (Context.IsWorldReady)
            {
                // all four directions (the game only lines up the farmer's layers at its normal size, 64x128)
                this.DrawFarmer(b, new Vector2(this.FarmerArea.Center.X - 72, this.FarmerArea.Y + 16));
                Gfx.Text(b, Gfx.Fit(this.IsTryingOn ? "Trying it on" : "As saved", this.FarmerArea.Width - 24), new Vector2(this.FarmerArea.X + 12, this.FarmerArea.Bottom - 44), Color.DimGray);
            }
            else
                Gfx.Text(b, Game1.parseText("Load a save to see your farmer.", Gfx.Font, this.FarmerArea.Width - 24), new Vector2(this.FarmerArea.X + 12, this.FarmerArea.Y + 16), Color.DimGray);

            base.Draw(b, mouseX, mouseY);

            string help = original != null
                ? (this.Layer.IsBody
                    ? "Tip: keep skin, eyes, shoes and sleeves in the original's colors (shading is fine); they're recolored to match each farmer."
                    : $"Tip: export the original, paint over it at {original.Width * 4}x{original.Height * 4} (or any whole-number size), then choose it here.")
                : "";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Whether the previewed farmer wears the item being looked at (only hats and accessories can be put on by number).</summary>
        private bool IsTryingOn => this.TryOnBox.Checked && this.TryOnBox.Visible;

        /// <summary>Draw the player facing down, right, up and left, in a 2x2 grid.</summary>
        private void DrawFarmer(SpriteBatch b, Vector2 topLeft)
        {
            Farmer player = Game1.player;
            int facing = player.FacingDirection;
            StardewValley.Objects.Hat? hat = player.hat.Value;
            int accessory = player.accessory.Value;
            try
            {
                if (this.IsTryingOn && this.Layer.Id == "hats")
                    player.hat.Value = ItemRegistry.Create<StardewValley.Objects.Hat>($"(H){this.ItemIndex}", allowNull: true);
                else if (this.IsTryingOn && this.Layer.Id == "accessories")
                    player.accessory.Value = this.ItemIndex;
                FarmerRenderer.isDrawingForUI = true;
                (int Direction, int Frame, bool Flip)[] poses = { (2, 0, false), (1, 6, false), (0, 12, false), (3, 6, true) };
                for (int i = 0; i < poses.Length; i++)
                {
                    (int direction, int frame, bool flip) = poses[i];
                    player.FacingDirection = direction; // the renderer picks hair/hat/shirt sprites by the direction
                    Vector2 position = topLeft + new Vector2(i % 2 * 80, i / 2 * 144);
                    Rectangle source = new(frame * 16 % 96, frame * 16 / 96 * 32, 16, 32); // 6 frames per row (the arms are beside them)
                    player.FarmerRenderer.draw(b, new FarmerSprite.AnimationFrame(frame, 0, false, flip), frame, source, position, Vector2.Zero, 0.8f, Color.White, 0f, 1f, player);
                }
            }
            finally
            {
                player.FacingDirection = facing;
                player.hat.Value = hat;
                player.accessory.Value = accessory;
                FarmerRenderer.isDrawingForUI = false;
            }
        }

        /// <summary>Show another item of the sheet.</summary>
        private void SetItem(int index)
        {
            this.ItemIndex = Math.Clamp(index, 0, Math.Max(0, this.ItemCount - 1));
            if (this.ItemField.Text != this.ItemIndex.ToString())
                this.ItemField.Text = this.ItemIndex.ToString();
        }

        /// <summary>Handle a number typed in the item box.</summary>
        private void OnItemTyped(string text)
        {
            if (int.TryParse(text, out int index))
                this.ItemIndex = Math.Clamp(index, 0, Math.Max(0, this.ItemCount - 1));
            else if (text.Length == 0)
                this.ItemIndex = 0;
        }

        private Texture2D? GetOriginal(FarmerLayer layer)
        {
            if (!this.Originals.TryGetValue(layer.Id, out Texture2D? texture))
            {
                texture = OriginalContent.LoadTexture(layer.AssetName);
                this.Originals[layer.Id] = texture;
            }
            return texture;
        }

        private Texture2D? GetHdSheet(string file)
        {
            if (!this.HdSheets.TryGetValue(file, out Texture2D? texture))
            {
                texture = this.Store.DecodeForEditor(file)?.ToTexture();
                this.HdSheets[file] = texture;
            }
            return texture;
        }

        /// <summary>Get the HD sheet chosen for a layer, and a status line.</summary>
        private (Texture2D? Hd, int Factor, string Status) GetSheet(FarmerLayer layer)
        {
            if (!this.Files.TryGetValue(layer.Id, out string? file))
                return (null, 1, "Uses the game's sheet.");
            Texture2D? hd = this.GetHdSheet(file);
            if (hd == null)
                return (null, 1, $"Can't read '{file}'.");
            return FarmerHd.TryGetFactor(layer, hd.Width, hd.Height, out int factor, out string? error)
                ? (hd, factor, $"Uses your {factor}x sheet.")
                : (null, 1, error);
        }

        private void SyncButtons()
        {
            bool hasOriginal = this.GetOriginal(this.Layer) != null;
            this.ExportButton.Enabled = hasOriginal;
            this.ChooseButton.Enabled = hasOriginal;
            this.RemoveButton.Visible = this.Files.ContainsKey(this.Layer.Id);
            this.TryOnBox.Visible = this.Layer.Id is "hats" or "accessories"; // only these are picked by number, so only these can be put on
            if (!this.TryOnBox.Visible)
                this.TryOnBox.Checked = false;
        }

        private void ExportOriginal()
        {
            if (this.GetOriginal(this.Layer) is not { } original)
                return;
            try
            {
                string path = ImageExport.Export(original, null, $"Farmer {this.Layer.Id}", ImageExport.DefaultScale);
                this.Message = $"Exported to {path}";
                this.MessageColor = Color.DarkGreen;
                Game1.playSound("coin");
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't export: {ex.Message}");
            }
        }

        private void Browse()
        {
            FarmerLayer layer = this.Layer;
            string? start = Directory.Exists(ImageExport.ExportFolder) ? ImageExport.ExportFolder : null;
            this.Root.Push(new FileBrowserScreen(start, path =>
            {
                // check the size before copying it into the mod folder
                try
                {
                    Pixels source = ImageProcessor.Decode(path);
                    if (!FarmerHd.TryGetFactor(layer, source.Width, source.Height, out _, out string? sizeError))
                    {
                        this.ShowError(sizeError);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't read the image: {ex.Message}");
                    return;
                }

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

                Texture2D? hd = this.GetHdSheet(file);
                if (hd == null)
                {
                    this.ShowError($"Couldn't read '{file}'.");
                    return;
                }
                if (!FarmerHd.TryGetFactor(layer, hd.Width, hd.Height, out int factor, out string? error))
                {
                    this.ShowError(error);
                    return;
                }

                this.Files[layer.Id] = file;
                this.Message = $"Using your {factor}x sheet for {layer.Label}. Save to see it on your farmer.";
                this.MessageColor = Color.DarkGreen;
                this.SyncButtons();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Open the paint screen on this sheet: your HD version if you have one, else the game's own sheet at the size you pick.</summary>
        private void Paint()
        {
            FarmerLayer layer = this.Layer;
            (Texture2D? hd, int factor, _) = this.GetSheet(layer);

            // editing a sheet you already have: paint it at the size it already is
            if (hd != null && this.Files.TryGetValue(layer.Id, out string? file) && this.Store.DecodeForEditor(file) is { } mine)
            {
                this.OpenPaint(layer, mine, factor);
                return;
            }

            if (this.GetOriginal(layer) is not { } original)
            {
                this.ShowError("There's nothing to paint on yet.");
                return;
            }

            // starting from the game's sheet: it's copied, never changed, and you choose the size you draw at
            this.Root.Push(new ChoiceScreen(
                $"Paint a copy of the game's {layer.Label} sheet ({original.Width}x{original.Height}). The game's own art is never changed.\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{original.Width}x{original.Height}: one pixel is one game pixel.", () => this.OpenPaint(layer, ImageProcessor.FromTexture(original), 1)),
                ("Twice the size (2x)", $"{original.Width * 2}x{original.Height * 2}: room for finer detail.", () => this.OpenPaint(layer, ImageProcessor.Enlarge(ImageProcessor.FromTexture(original), 2), 2)),
                ("Four times the size (4x)", $"{original.Width * 4}x{original.Height * 4}: the usual size for HD art.", () => this.OpenPaint(layer, ImageProcessor.Enlarge(ImageProcessor.FromTexture(original), 4), 4))));
        }

        /// <summary>Open the paint screen and use whatever comes back as this layer's sheet.</summary>
        /// <param name="layer">The sheet being painted.</param>
        /// <param name="image">The pixels to start from.</param>
        /// <param name="factor">How many times bigger than the game's sheet those pixels are, for the grid.</param>
        private void OpenPaint(FarmerLayer layer, Pixels image, int factor)
        {
            this.Root.Push(new PaintScreen(image, $"Paint {layer.Label}", pixels =>
            {
                try
                {
                    string saved = CustomContent.SaveImage(this.Store.ImageFolder, $"Farmer {layer.Id} painted", pixels);
                    this.HdSheets.Remove(saved);
                    this.Files[layer.Id] = saved;
                    this.Message = $"Painted {layer.Label}. Save to see it on your farmer.";
                    this.MessageColor = Color.DarkGreen;
                    this.SyncButtons();
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the image: {ex.Message}");
                }
            }, layer.CellWidth * factor, layer.CellHeight * factor, layer.PartHeight > 0 ? layer.CellWidth * factor : 0, layer.PartHeight * factor, layer.Parts));
        }

        private void RemoveSheet()
        {
            this.Files.Remove(this.Layer.Id);
            this.Message = null;
            this.SyncButtons();
        }

        private void ShowError(string message)
        {
            this.Message = message;
            this.MessageColor = Color.DarkRed;
            Game1.playSound("cancel");
        }

        private void Save()
        {
            CharactersFile file = this.Store.ReadFile();
            file.Farmer = new Dictionary<string, string>(this.Files);
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
            this.Message = "Saved.";
            this.MessageColor = Color.DarkGreen;
            this.OnSaved();
        }
    }
}
