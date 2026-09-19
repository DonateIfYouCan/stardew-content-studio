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
    /// <summary>Edits the farmer's HD sheets (body, hair, clothes, hats, accessories): export the originals, choose HD versions, and preview your farmer.</summary>
    internal sealed class FarmerEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        private readonly CharacterStore Store;
        private readonly Action OnSaved;

        /// <summary>The HD sheet per layer ID being edited (a copy, so Cancel discards changes).</summary>
        private readonly Dictionary<string, string> Files;

        /// <summary>The game's original sheets by layer ID.</summary>
        private readonly Dictionary<string, Texture2D?> Originals = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Decoded HD sheets by file.</summary>
        private readonly Dictionary<string, Texture2D?> HdSheets = new(StringComparer.OrdinalIgnoreCase);

        private FarmerLayer Layer = FarmerHd.Layers[0];
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        private readonly Cycler LayerCycler;
        private readonly Button ExportButton;
        private readonly Button ChooseButton;
        private readonly Button RemoveButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle SheetArea;
        private Rectangle ZoomArea;
        private Rectangle FarmerArea;


        /*********
        ** Public methods
        *********/
        public FarmerEditorScreen(CharacterStore store, Action onSaved)
        {
            this.Store = store;
            this.OnSaved = onSaved;
            this.Files = new Dictionary<string, string>(store.File.Farmer, StringComparer.OrdinalIgnoreCase);

            this.LayerCycler = this.Add(new Cycler(FarmerHd.Layers.Select(l => (l.Id, l.Label)).ToList(), this.Layer.Id, v => { this.Layer = FarmerHd.GetLayer(v)!; this.Message = null; this.SyncButtons(); },
                "The farmer is drawn in layers. Each can have an HD sheet; the others stay as they are."));
            this.ExportButton = this.Add(new Button("Export original sheet", this.ExportOriginal, "Save the game's sheet (enlarged, with sharp pixels) to paint over in another program."));
            this.ChooseButton = this.Add(new Button("Choose HD sheet", this.Browse, "Pick your HD version. It must be the original size times a whole number."));
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
        protected override void OnLayout(Rectangle area)
        {
            int pad = 32;
            int top = area.Y + 84;
            int bottom = area.Bottom - 96;
            this.LayerCycler.Bounds = new Rectangle(area.X + pad + 110, top, 460, 48);
            top += 64;
            int sheetW = (int)(area.Width * 0.3);
            int farmerW = 220;
            this.SheetArea = new Rectangle(area.X + pad, top + 36, sheetW, bottom - top - 36 - 64);
            this.ExportButton.Bounds = new Rectangle(area.X + pad, this.SheetArea.Bottom + 12, Math.Min(300, sheetW), 48);
            this.FarmerArea = new Rectangle(area.Right - pad - farmerW, top + 36, farmerW, bottom - top - 36 - 64);
            this.ZoomArea = new Rectangle(this.SheetArea.Right + 24, top + 36, this.FarmerArea.X - 24 - this.SheetArea.Right - 24, bottom - top - 36 - 64);
            this.ChooseButton.Bounds = new Rectangle(this.ZoomArea.X, this.ZoomArea.Bottom + 12, 300, 48);
            this.RemoveButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.ZoomArea.Bottom + 12, 180, 48);
            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Farmer (HD)", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Sheet", new Vector2(area.X + 32, this.LayerCycler.Bounds.Y + 10));

            Texture2D? original = this.GetOriginal(this.Layer);
            (Texture2D? hd, int factor, string status) = this.GetSheet(this.Layer);
            Gfx.Text(b, Gfx.Fit(status, area.Right - 60 - this.LayerCycler.Bounds.Right - 24), new Vector2(this.LayerCycler.Bounds.Right + 24, this.LayerCycler.Bounds.Y + 10), Color.DimGray);

            // sheet overview
            Gfx.Text(b, hd != null ? $"Your sheet ({factor}x)" : "Original sheet", new Vector2(this.SheetArea.X, this.SheetArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.SheetArea, new Color(200, 190, 170));
            if ((hd ?? original) is { } sheet)
                Gfx.Fitted(b, sheet, null, new Rectangle(this.SheetArea.X + 12, this.SheetArea.Y + 12, this.SheetArea.Width - 24, this.SheetArea.Height - 24), pixelated: hd == null);

            // close-up: the top-left of the sheet, original vs yours
            Gfx.Text(b, "Close-up", new Vector2(this.ZoomArea.X, this.ZoomArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.ZoomArea, new Color(120, 170, 90));
            if (original != null)
            {
                Rectangle source = new(0, 0, Math.Min(original.Width, 32), Math.Min(original.Height, 32));
                int cellW = hd != null ? (this.ZoomArea.Width - 48) / 2 : this.ZoomArea.Width - 32;
                int scale = Math.Max(1, Math.Min(cellW / source.Width, (this.ZoomArea.Height - 80) / source.Height));
                Rectangle dest = new(this.ZoomArea.X + 16, this.ZoomArea.Y + 16, source.Width * scale, source.Height * scale);
                b.Draw(original, dest, source, Color.White);
                if (hd != null)
                    b.Draw(hd, new Rectangle(dest.Right + 16, dest.Y, dest.Width, dest.Height), new Rectangle(0, 0, source.Width * factor, source.Height * factor), Color.White);
                string caption = hd != null ? "Left: original. Right: yours." : "Choose an HD sheet to compare it with the original.";
                Gfx.Text(b, Gfx.Fit(caption, this.ZoomArea.Width - 32), new Vector2(this.ZoomArea.X + 16, this.ZoomArea.Bottom - 44), Color.White);
            }

            // your farmer as currently saved (with their own colors)
            Gfx.Text(b, "Your farmer", new Vector2(this.FarmerArea.X, this.FarmerArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.FarmerArea, new Color(200, 190, 170));
            if (Context.IsWorldReady)
            {
                // all four directions (the game only lines up the farmer's layers at its normal size, 64x128)
                this.DrawFarmer(b, new Vector2(this.FarmerArea.Center.X - 72, this.FarmerArea.Y + 16));
                Gfx.Text(b, Gfx.Fit("As saved", this.FarmerArea.Width - 24), new Vector2(this.FarmerArea.X + 12, this.FarmerArea.Bottom - 44), Color.DimGray);
            }
            else
                Gfx.TextCentered(b, "Load a save to see your farmer.", this.FarmerArea, Color.DimGray);

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
        /// <summary>Draw the player facing down, right, up and left, in a 2x2 grid.</summary>
        private void DrawFarmer(SpriteBatch b, Vector2 topLeft)
        {
            Farmer player = Game1.player;
            int facing = player.FacingDirection;
            try
            {
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
                FarmerRenderer.isDrawingForUI = false;
            }
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
