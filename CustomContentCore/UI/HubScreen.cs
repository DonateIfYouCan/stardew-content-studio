using System.IO;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>The editor's start page when several mods add sections: pick what to edit.</summary>
    internal sealed class HubScreen : Screen
    {
        private readonly List<(CustomContent.EditorSection Section, Button Button)> Entries = new();
        private readonly Button SettingsButton;
        private readonly Button CloseButton;
        private readonly Button ExportButton;
        private readonly Button ImportButton;
        private readonly Button MultiplayerButton;
        private readonly Button DonateButton;
        private readonly Button GitHubButton;

        /// <summary>Where to support the author.</summary>
        public const string DonateUrl = "https://buymeacoffee.com/donateifyoucan";

        /// <summary>The mods' GitHub repo (source, docs and issues).</summary>
        public const string GitHubUrl = "https://github.com/DonateIfYouCan/stardew-content-studio";
        /// <summary>Whether there's room to show each section's description under its button.</summary>
        private bool ShowDescriptions = true;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public HubScreen()
        {
            foreach (CustomContent.EditorSection section in CustomContent.GetEditors())
            {
                CustomContent.EditorSection s = section;
                this.Entries.Add((s, this.Add(new Button(s.Title, () => this.Root.Push(s.CreateScreen()), s.Description))));
            }
            this.SettingsButton = this.Add(new Button("Settings", () => this.Root.Push(new SettingsScreen()), "The key that opens the editor, and where images are saved and looked for."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.ExportButton = this.Add(new Button("Export pack", this.ExportPack, "Save all your custom content (data and images) into one file, e.g. to use on another PC."));
            this.ImportButton = this.Add(new Button("Import pack", this.ImportPack, "Load a pack made with 'Export pack'. Your current content is backed up first."));
            this.ExportButton.Visible = this.ImportButton.Visible = ContentPacks.Any;
            this.MultiplayerButton = this.Add(new Button("Multiplayer", () => this.Root.Push(new MultiplayerScreen()), "Share your content with the other players in a game, take theirs, and let them change yours. All off unless you turn it on."));
            this.DonateButton = this.Add(new Button("Buy me a coffee", () => this.CopyLink(DonateUrl), $"Optional. Copies {DonateUrl} to paste in your browser. Nothing is unlocked by donating."));
            this.GitHubButton = this.Add(new Button("GitHub: bugs & ideas", () => this.CopyLink(GitHubUrl), $"Source, docs, bug reports, feedback and feature requests. Copies {GitHubUrl} to paste in your browser."));
        }

        /// <summary>Copy a link to the clipboard (opening a browser from the game isn't reliable on every OS).</summary>
        private void CopyLink(string url)
        {
            if (DesktopClipboard.SetText(url))
            {
                this.ShowMessage($"Copied {url} - paste it in your browser.");
                Game1.playSound("coin");
            }
            else
                this.ShowMessage($"Couldn't copy the link. It's {url}", error: true);
        }

        private void ExportPack()
        {
            try
            {
                string path = ContentPacks.Export();
                CoreMod.StaticMonitor.Log($"Exported a content pack to {path}.", LogLevel.Info);
                this.ShowMessage($"Exported '{Path.GetFileName(path)}' to your {Path.GetFileName(Path.GetDirectoryName(path))} folder.");
                Game1.playSound("coin");
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't export: {ex.Message}", error: true);
            }
        }

        private void ImportPack()
        {
            this.Root.Push(new FileBrowserScreen(ImageExport.ExportFolder, path =>
            {
                string description;
                try
                {
                    description = ContentPacks.Describe(path);
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Can't use that file: {ex.Message}", error: true);
                    return;
                }
                this.Root.Push(new ConfirmScreen($"Import this pack?\n\n{description}\n\nThis replaces your current content for these mods. Your current content is saved as a backup pack first.", "Import", () =>
                {
                    try
                    {
                        ContentPacks.ImportResult result = ContentPacks.Import(path);
                        string skipped = result.Skipped.Count > 0 ? $" Skipped (not installed): {string.Join(", ", result.Skipped)}." : "";
                        CoreMod.StaticMonitor.Log($"Imported the content pack '{path}'; the previous content is backed up in '{result.BackupPath}'.", LogLevel.Info);
                        this.ShowMessage($"Imported content for {result.Imported.Count} mod{(result.Imported.Count == 1 ? "" : "s")}.{skipped} Your old content is in the Backups folder.");
                        Game1.playSound("newArtifact");
                    }
                    catch (Exception ex)
                    {
                        this.ShowMessage($"Couldn't import: {ex.Message}", error: true);
                    }
                }));
            }, extensions: new[] { ".zip" }, title: "Choose a content pack (.zip)"));
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            if (error)
                Game1.playSound("cancel");
        }

        protected override void OnLayout(Rectangle area)
        {
            // fit the sections in the space above the multiplayer/support rows, however many mods are installed
            int w = Math.Min(560, area.Width - 64);
            int x = area.Center.X - w / 2;
            int note = CoreMod.Sync?.UsingPeerContent == true ? 34 : 0; // the line about other players' content sits under the title
            int top = area.Y + (this.Entries.Count > 4 ? 84 : 120) + note;
            int bottom = area.Bottom - 84 - 64 - 44; // above the bottom row of buttons, leaving room for the last description
            int count = Math.Max(1, this.Entries.Count);
            int step = Math.Clamp((bottom - top) / count, 48, 132); // in a small window the rows shrink rather than run into the multiplayer options
            this.ShowDescriptions = step >= 104;
            int buttonH = this.ShowDescriptions ? Math.Min(72, step - 56) : Math.Min(56, step - 8);
            int y = top;
            foreach ((_, Button button) in this.Entries)
            {
                button.Bounds = new Rectangle(x, y, w, buttonH);
                y += step;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - 32 - 180, area.Bottom - 84, 180, 60);
            this.SettingsButton.Bounds = new Rectangle(this.CloseButton.Bounds.X - 12 - 180, area.Bottom - 84, 180, 60);
            this.ExportButton.Bounds = new Rectangle(area.X + 32, area.Bottom - 84, 220, 60);
            this.ImportButton.Bounds = new Rectangle(area.X + 32 + 232, area.Bottom - 84, 220, 60);
            this.GitHubButton.Bounds = new Rectangle(area.Right - 32 - 300, area.Bottom - 84 - 64, 300, 52);
            this.DonateButton.Bounds = new Rectangle(this.GitHubButton.Bounds.X - 12 - 260, area.Bottom - 84 - 64, 260, 52);
            this.MultiplayerButton.Bounds = new Rectangle(area.X + 32, area.Bottom - 84 - 64, 220, 52);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "What do you want to edit?", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);

            if (CoreMod.Sync?.UsingPeerContent == true)
                Gfx.Text(b, Gfx.Fit("Other players' content is shown next to yours. You can only change your own, or ask them for a turn.", this.Area.Width - 72), new Vector2(this.Area.X + 36, this.Area.Y + 68), Color.DarkRed);
            Gfx.Text(b, "Support (optional)", new Vector2(this.DonateButton.Bounds.X, this.DonateButton.Bounds.Y - 40), Color.DimGray);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.SettingsButton.Bounds.X - this.ImportButton.Bounds.Right - 48, new Vector2(this.ImportButton.Bounds.Right + 24, this.CloseButton.Bounds.Y + 16), this.MessageColor);
            if (this.ShowDescriptions)
            {
                foreach ((CustomContent.EditorSection section, Button button) in this.Entries)
                    Gfx.TextCentered(b, Gfx.Fit(section.Description, this.Area.Width - 80), new Rectangle(this.Area.X, button.Bounds.Bottom + 8, this.Area.Width, 32), Color.DimGray);
            }
        }
    }

    /// <summary>Asks the player to confirm something, shown over the previous screen.</summary>
    public sealed class ConfirmScreen : Screen
    {
        private readonly string Message;
        private readonly Button YesButton;
        private readonly Button NoButton;
        private Rectangle Box;

        public override bool IsOverlay => true;

        public ConfirmScreen(string message, string yesLabel, Action onYes)
        {
            this.Message = message;
            this.YesButton = this.Add(new Button(yesLabel, () => { this.Root.Pop(); onYes(); }));
            this.NoButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = Math.Min(760, area.Width);
            string wrapped = Game1.parseText(this.Message, Game1.smallFont, w - 80);
            int h = (int)Game1.smallFont.MeasureString(wrapped).Y + 180;
            this.Box = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
            this.YesButton.Bounds = new Rectangle(this.Box.Right - 40 - 200, this.Box.Bottom - 96, 200, 60);
            this.NoButton.Bounds = new Rectangle(this.YesButton.Bounds.X - 16 - 180, this.Box.Bottom - 96, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Box);
            b.DrawString(Game1.smallFont, Game1.parseText(this.Message, Game1.smallFont, this.Box.Width - 80), new Vector2(this.Box.X + 40, this.Box.Y + 40), Game1.textColor);
            base.Draw(b, mouseX, mouseY);
        }
    }
}
