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

namespace CustomCharacters.UI
{
    /// <summary>Edits a villager's sprite sheets: export the originals, import HD versions (a main sheet plus optional per-outfit sheets), and preview the walking animation.</summary>
    internal sealed class SpriteEditorScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>The size of one frame in villager sprite sheets.</summary>
        private const int FrameWidth = 16, FrameHeight = 32;

        private static readonly string[] Directions = { "Down", "Right", "Up", "Left" };

        private readonly CharacterStore Store;
        private readonly string Npc;
        private readonly string DisplayName;
        private readonly Action OnSaved;
        private readonly SpriteSheetSet Set;
        private readonly List<string> Outfits;

        /// <summary>The game's original sheet for each outfit.</summary>
        private readonly Dictionary<string, Texture2D?> Originals = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Decoded HD sheets by file.</summary>
        private readonly Dictionary<string, Texture2D?> HdSheets = new(StringComparer.OrdinalIgnoreCase);

        private string Outfit = "";
        private string? Message;
        private Color MessageColor = Color.DarkRed;

        /// <summary>The sheet image the painter is open on, if any, as asked for with <see cref="CustomContent.TakeLock"/>.</summary>
        /// <remarks>The main sheet is used by every outfit without its own, so while Player A paints it nobody else may.</remarks>
        private string? HeldSheet;

        private readonly Cycler OutfitCycler;
        private readonly Button ExportButton;
        private readonly Button ChooseButton;
        private readonly Button PaintButton;
        private readonly Button RemoveButton;
        private readonly Button SaveButton;
        private readonly Button CancelButton;

        private Rectangle SheetArea;
        private Rectangle PreviewArea;


        /*********
        ** Public methods
        *********/
        public SpriteEditorScreen(CharacterStore store, string npc, string displayName, Action onSaved)
        {
            this.Store = store;
            this.Npc = npc;
            this.DisplayName = displayName;
            this.OnSaved = onSaved;
            this.Outfits = CharacterStore.GetOutfits(npc);

            SpriteSheetSet? existing = store.File.Sprites.Find(s => string.Equals(s.Npc, npc, StringComparison.OrdinalIgnoreCase));
            this.Set = existing != null
                ? JsonConvert.DeserializeObject<SpriteSheetSet>(JsonConvert.SerializeObject(existing))! // edit a copy so Cancel discards changes
                : new SpriteSheetSet { Npc = npc };

            this.OutfitCycler = this.Add(new Cycler(this.Outfits.Select(o => (o, OutfitLabel(o))).ToList(), "", v => { this.Outfit = v; this.Message = null; this.SyncButtons(); },
                "Villagers wear different outfits in some seasons and places. Your main sheet is used for all of them unless an outfit has its own."));
            this.ExportButton = this.Add(new Button("Export original sheet", this.ExportOriginal, "Save the game's sheet for this outfit (enlarged, with sharp pixels) to paint over in another program."));
            this.ChooseButton = this.Add(new Button("Choose HD sheet", this.Browse, "Pick your HD version. It must be the original size times a whole number."));
            this.PaintButton = this.Add(new Button("Paint", this.Paint, "Draw on the sheet here in the game. Starting from the game's sheet copies it; the game's art is never changed."));
            this.RemoveButton = this.Add(new Button("Remove", this.RemoveSheet));
            this.SaveButton = this.Add(new Button("Save", this.Save));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.SyncButtons();
        }

        public override void OnResume()
        {
            this.ReleaseSheet(); // the painter (or whatever else was opened) is closed again
        }

        public override void Dispose()
        {
            this.ReleaseSheet();
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
            this.OutfitCycler.Bounds = new Rectangle(area.X + pad + 110, top, 360, 48);
            top += 64;
            int sheetW = (int)(area.Width * 0.3);
            this.SheetArea = new Rectangle(area.X + pad, top + 36, sheetW, bottom - top - 36 - 64);
            this.ExportButton.Bounds = new Rectangle(area.X + pad, this.SheetArea.Bottom + 12, Math.Min(300, sheetW), 48);
            this.PreviewArea = new Rectangle(this.SheetArea.Right + 32, top + 36, area.Right - pad - this.SheetArea.Right - 32, bottom - top - 36 - 64);
            this.ChooseButton.Bounds = new Rectangle(this.PreviewArea.X, this.PreviewArea.Bottom + 12, 300, 48);
            this.PaintButton.Bounds = new Rectangle(this.ChooseButton.Bounds.Right + 10, this.PreviewArea.Bottom + 12, 120, 48);
            this.RemoveButton.Bounds = new Rectangle(this.PaintButton.Bounds.Right + 10, this.PreviewArea.Bottom + 12, 240, 48);
            this.SaveButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.SaveButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, $"Sprite: {this.DisplayName}", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Outfit", new Vector2(area.X + 32, this.OutfitCycler.Bounds.Y + 10));

            Texture2D? original = this.GetOriginal(this.Outfit);
            (Texture2D? hd, int factor, Rectangle? hdArea, string status) = this.GetEffectiveSheet(this.Outfit);
            Gfx.Text(b, Gfx.Fit(status, area.Right - 60 - this.OutfitCycler.Bounds.Right - 24), new Vector2(this.OutfitCycler.Bounds.Right + 24, this.OutfitCycler.Bounds.Y + 10), Color.DimGray);

            // sheet overview
            Gfx.Text(b, hd != null ? $"Your sheet ({factor}x)" : "Original sheet", new Vector2(this.SheetArea.X, this.SheetArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.SheetArea, new Color(200, 190, 170));
            Texture2D? sheet = hd ?? original;
            if (sheet != null)
            {
                Rectangle inner = new(this.SheetArea.X + 12, this.SheetArea.Y + 12, this.SheetArea.Width - 24, this.SheetArea.Height - 24);
                Rectangle drawn = Gfx.Fitted(b, sheet, hd != null ? hdArea : null, inner, pixelated: hd == null);
                if (original != null)
                {
                    int columns = Math.Max(1, original.Width / FrameWidth), rows = Math.Max(1, original.Height / FrameHeight);
                    Color grid = Color.Black * 0.15f;
                    for (int c = 1; c < columns; c++)
                        Gfx.Rect(b, new Rectangle(drawn.X + drawn.Width * c / columns, drawn.Y, 1, drawn.Height), grid);
                    for (int r = 1; r < rows; r++)
                        Gfx.Rect(b, new Rectangle(drawn.X, drawn.Y + drawn.Height * r / rows, drawn.Width, 1), grid);
                }
            }

            // walking previews: original vs yours, per direction
            Gfx.Text(b, "Walking preview", new Vector2(this.PreviewArea.X, this.PreviewArea.Y - 36), Color.DimGray);
            Gfx.Inset(b, this.PreviewArea, new Color(120, 170, 90));
            if (original != null)
            {
                int frame = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 150 % 4);
                int cellW = (this.PreviewArea.Width - 40) / 4;
                int scale = Math.Max(1, Math.Min(4, Math.Min((cellW - 16) / (FrameWidth * 2 + 4), (this.PreviewArea.Height - 120) / FrameHeight)));
                for (int dir = 0; dir < 4 && (dir + 1) * FrameHeight <= original.Height; dir++)
                {
                    int x = this.PreviewArea.X + 20 + dir * cellW;
                    int y = this.PreviewArea.Y + 20;
                    Gfx.Text(b, Directions[dir], new Vector2(x, y), Color.White);
                    Rectangle source = new(frame * FrameWidth, dir * FrameHeight, FrameWidth, FrameHeight);
                    Rectangle dest = new(x, y + 44, FrameWidth * scale, FrameHeight * scale);
                    b.Draw(original, dest, source, Color.White);
                    if (hd != null)
                        b.Draw(hd, new Rectangle(dest.Right + 8, dest.Y, dest.Width, dest.Height), new Rectangle(source.X * factor, source.Y * factor, source.Width * factor, source.Height * factor), Color.White);
                }
                Gfx.Text(b, hd != null ? "Left: original. Right: yours." : "Choose an HD sheet to compare it with the original.", new Vector2(this.PreviewArea.X + 20, this.PreviewArea.Bottom - 44), Color.White);
            }
            else
                Gfx.TextCentered(b, "The game has no sheet for this outfit.", this.PreviewArea, Color.White);

            base.Draw(b, mouseX, mouseY);

            string help = original != null
                ? $"Tip: export the original, paint over it at {original.Width * 4}x{original.Height * 4} (or any whole-number size), then choose it here."
                : "";
            Gfx.Message(b, this.Message ?? help, this.CancelButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }


        /*********
        ** Private methods
        *********/
        private static string OutfitLabel(string outfit) => outfit.Length == 0 ? "Normal (main sheet)" : outfit;

        private Texture2D? GetOriginal(string outfit)
        {
            if (!this.Originals.TryGetValue(outfit, out Texture2D? texture))
            {
                texture = OriginalContent.LoadTexture($"Characters/{CharacterStore.GetSheetName(this.Npc, outfit)}");
                this.Originals[outfit] = texture;
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

        /// <summary>Get the HD sheet an outfit will use, and a status line explaining where it comes from.</summary>
        private (Texture2D? Hd, int Factor, Rectangle? Area, string Status) GetEffectiveSheet(string outfit)
        {
            Texture2D? original = this.GetOriginal(outfit);
            bool own = outfit.Length == 0 ? !string.IsNullOrEmpty(this.Set.File) : this.Set.Outfits.ContainsKey(outfit);
            string file = this.Set.GetFile(outfit);
            if (original == null || string.IsNullOrEmpty(file))
                return (null, 1, null, outfit.Length == 0 ? "Uses the game's sprite." : "Uses the game's sprite (no main sheet yet).");

            Texture2D? hd = this.GetHdSheet(file);
            if (hd == null)
                return (null, 1, null, $"Can't read '{file}'.");

            if (CharacterStore.TryGetSheetFactor(hd.Width, hd.Height, original.Width, original.Height, out int factor, out string? error))
                return (hd, factor, null, own ? (outfit.Length == 0 ? "Used for every outfit without its own sheet." : "Uses its own sheet.") : "Uses your main sheet.");
            if (!own && CharacterStore.TryGetPartialSheetFactor(hd.Width, hd.Height, original.Width, original.Height, out factor))
                return (hd, factor, new Rectangle(0, 0, original.Width * factor, original.Height * factor), "Uses the top of your main sheet (it has fewer frames). Give it its own sheet for its poses.");
            return (null, 1, null, own ? error : "Your main sheet doesn't fit this outfit's layout; it uses the game's sprite. Choose a sheet for it.");
        }

        private void SyncButtons()
        {
            bool isMain = this.Outfit.Length == 0;
            bool hasOwn = isMain ? !string.IsNullOrEmpty(this.Set.File) : this.Set.Outfits.ContainsKey(this.Outfit);
            this.ExportButton.Enabled = this.GetOriginal(this.Outfit) != null;
            this.ChooseButton.Enabled = this.GetOriginal(this.Outfit) != null;
            this.ChooseButton.Label = isMain ? "Choose main HD sheet" : $"Choose {this.Outfit} sheet";
            this.RemoveButton.Visible = hasOwn;
            this.RemoveButton.Label = isMain ? "Remove all sheets" : "Use main sheet";
        }

        private void ExportOriginal()
        {
            if (this.GetOriginal(this.Outfit) is not { } original)
                return;
            try
            {
                string path = ImageExport.Export(original, null, $"{CharacterStore.GetSheetName(this.Npc, this.Outfit)} sprite", ImageExport.DefaultScale);
                this.Message = $"Exported to {path}";
                this.MessageColor = Color.DarkGreen;
                Game1.playSound("coin");
            }
            catch (Exception ex)
            {
                this.ShowError($"Couldn't export: {ex.Message}");
            }
        }

        /// <summary>Open the paint screen: on your sheet if you have one, else on a copy of the game's sheet at the size you pick.</summary>
        private void Paint()
        {
            string outfit = this.Outfit;
            string? file = outfit.Length == 0 ? this.Set.File : this.Set.Outfits.GetValueOrDefault(outfit);
            if (!string.IsNullOrEmpty(file) && this.Store.DecodeForEditor(file) is { } mine)
            {
                // a sheet the mod already has: hold that file while the painter is open, so Player B can't paint over Player A's work
                string thing = $"file:{CharacterStore.ImageFolderName}/{file}";
                string label = $"{this.DisplayName}'s sprite sheet";
                CustomContent.TakeLock(this.Store.Manifest, thing, label, (granted, why) =>
                {
                    if (!granted)
                    {
                        this.ShowError(why);
                        return;
                    }
                    this.HeldSheet = thing;
                    this.OpenPaint(outfit, mine);
                });
                return;
            }

            if (this.GetOriginal(outfit) is not { } original)
            {
                this.ShowError("There's nothing to paint on yet.");
                return;
            }
            this.Root.Push(new ChoiceScreen(
                $"Paint a copy of the game's sheet ({original.Width}x{original.Height}). The game's own art is never changed.\n\nWhat size do you want to draw at?",
                ("The game's size (1x)", $"{original.Width}x{original.Height}: one pixel is one game pixel.", () => this.OpenPaint(outfit, ImageProcessor.FromTexture(original))),
                ("Twice the size (2x)", $"{original.Width * 2}x{original.Height * 2}: room for finer detail.", () => this.OpenPaint(outfit, ImageProcessor.Enlarge(ImageProcessor.FromTexture(original), 2))),
                ("Four times the size (4x)", $"{original.Width * 4}x{original.Height * 4}: the usual size for HD art.", () => this.OpenPaint(outfit, ImageProcessor.Enlarge(ImageProcessor.FromTexture(original), 4)))));
        }

        /// <summary>Open the paint screen and use whatever comes back as this outfit's sheet.</summary>
        private void OpenPaint(string outfit, Pixels image)
        {
            this.Root.Push(new PaintScreen(image, $"Paint {this.DisplayName}'s sprite", pixels =>
            {
                try
                {
                    string saved = CustomContent.SaveImage(this.Store.ImageFolder, $"{this.Npc}{(outfit.Length == 0 ? "" : " " + outfit)} painted", pixels);
                    if (outfit.Length == 0)
                        this.Set.File = saved;
                    else
                        this.Set.Outfits[outfit] = saved;
                    this.SyncButtons();
                    this.Save(); // saving in the paint screen is meant to be the only save
                    this.Message = "Painted and saved.";
                    this.MessageColor = Color.DarkGreen;
                }
                catch (Exception ex)
                {
                    this.ShowError($"Couldn't save the image: {ex.Message}");
                }
            }, 16, 32));
        }

        private void Browse()
        {
            string outfit = this.Outfit;
            string? start = Directory.Exists(ImageExport.ExportFolder) ? ImageExport.ExportFolder : null;
            this.Root.Push(new FileBrowserScreen(start, path =>
            {
                // check the size before copying it into the mod folder
                if (this.GetOriginal(outfit) is { } sheet)
                {
                    try
                    {
                        Pixels source = ImageProcessor.Decode(path);
                        if (!CharacterStore.TryGetSheetFactor(source.Width, source.Height, sheet.Width, sheet.Height, out _, out string? sizeError))
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

                // check it fits this outfit before using it
                Texture2D? original = this.GetOriginal(outfit);
                Texture2D? hd = this.GetHdSheet(file);
                if (original == null || hd == null)
                {
                    this.ShowError($"Couldn't read '{file}'.");
                    return;
                }
                if (!CharacterStore.TryGetSheetFactor(hd.Width, hd.Height, original.Width, original.Height, out int factor, out string? error))
                {
                    this.ShowError(error);
                    return;
                }

                if (outfit.Length == 0)
                    this.Set.File = file;
                else
                    this.Set.Outfits[outfit] = file;
                this.Message = $"Using your {factor}x sheet. Check the walking preview (and the other outfits), then save.";
                this.MessageColor = Color.DarkGreen;
                this.SyncButtons();
            }, this.Store.BrowserPlaces));
        }

        /// <summary>Let go of the sheet image the painter was open on, so another player can take their turn at it.</summary>
        private void ReleaseSheet()
        {
            if (this.HeldSheet is not { } thing)
                return;
            this.HeldSheet = null;
            CustomContent.ReleaseLock(this.Store.Manifest, thing);
        }

        private void RemoveSheet()
        {
            if (this.Outfit.Length == 0)
            {
                this.Set.File = "";
                this.Set.Outfits.Clear();
            }
            else
                this.Set.Outfits.Remove(this.Outfit);
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
            if (string.IsNullOrEmpty(this.Set.File) && this.Set.Outfits.Count > 0)
            {
                this.ShowError("Choose a main sheet first; outfits without their own sheet use it.");
                return;
            }

            CharactersFile file = this.Store.ReadFile();
            file.Sprites.RemoveAll(s => string.Equals(s.Npc, this.Npc, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(this.Set.File))
                file.Sprites.Add(this.Set);
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
