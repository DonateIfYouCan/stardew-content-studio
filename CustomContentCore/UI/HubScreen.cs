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
        private readonly Button ThemesButton;
        private readonly Button MultiplayerButton;
        private readonly Button VersionsButton;
        private readonly Button DonateButton;
        private readonly Button GitHubButton;

        /// <summary>Where to support the author.</summary>
        public const string DonateUrl = "https://buymeacoffee.com/donateifyoucan";

        /// <summary>The mods' GitHub repo (source, docs and issues).</summary>
        public const string GitHubUrl = "https://github.com/DonateIfYouCan/stardew-content-studio";
        /// <summary>Whether there's room to show each section's description under its button.</summary>
        private bool ShowDescriptions = true;

        /// <summary>How many columns the sections are in.</summary>
        private int Columns = 1;

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
            this.ThemesButton = this.Add(new Button("Themes & backups", () => this.Root.Push(new ThemesScreen()), "Keep a whole set of content per project and switch between them. Backups, exported packs and importing are here too."));
            this.ThemesButton.Visible = ContentPacks.Any;
            this.VersionsButton = this.Add(new Button("Earlier versions", () => this.Root.Push(new VersionsScreen()), "Put back a file as it was before it was last written over, e.g. after a player in your game changed something."));
            this.MultiplayerButton = this.Add(new Button("Multiplayer", () => this.Root.Push(new MultiplayerScreen()), "Share your content with the players in your game, use a host's content, and let players change it. All off unless you turn it on."));
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

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            if (error)
                Game1.playSound("cancel");
        }

        protected override void OnLayout(Rectangle area)
        {
            // fit the sections in the space above the multiplayer/support rows, however many mods are installed: one column for
            // a few, two side by side past five, so each keeps room for its description
            this.Columns = this.Entries.Count > 5 ? 2 : 1;
            int columnGap = 48;
            int w = this.Columns == 1 ? Math.Min(560, area.Width - 64) : Math.Min(640, (area.Width - 64 - columnGap) / 2);
            int left = area.Center.X - (w * this.Columns + columnGap * (this.Columns - 1)) / 2;
            int note = CoreMod.Sync?.UsingHostContent == true ? 34 : 0; // the line about the host's content sits under the title
            int rows = Math.Max(1, (this.Entries.Count + this.Columns - 1) / this.Columns);
            int top = area.Y + (rows > 4 ? 84 : 120) + note;
            int bottom = area.Bottom - 84 - 64 - 44; // above the bottom row of buttons, leaving room for the last description
            int step = Math.Clamp((bottom - top) / rows, 48, 132); // in a small window the rows shrink rather than run into the buttons below
            this.ShowDescriptions = step >= 104;
            int buttonH = this.ShowDescriptions ? Math.Min(72, step - 56) : Math.Min(56, step - 8);
            if (this.Columns > 1)
                top += Math.Max(0, (bottom - top - rows * step) / 2); // a short grid sits in the middle rather than at the top
            for (int i = 0; i < this.Entries.Count; i++)
            {
                // fill the first column top to bottom, then the second, so the sections still read in order
                int column = i / rows, row = i % rows;
                this.Entries[i].Button.Bounds = new Rectangle(left + column * (w + columnGap), top + row * step, w, buttonH);
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - 32 - 180, area.Bottom - 84, 180, 60);
            this.SettingsButton.Bounds = new Rectangle(this.CloseButton.Bounds.X - 12 - 180, area.Bottom - 84, 180, 60);
            this.ThemesButton.Bounds = new Rectangle(area.X + 32, area.Bottom - 84, 300, 60);
            this.GitHubButton.Bounds = new Rectangle(area.Right - 32 - 300, area.Bottom - 84 - 64, 300, 52);
            this.DonateButton.Bounds = new Rectangle(this.GitHubButton.Bounds.X - 12 - 260, area.Bottom - 84 - 64, 260, 52);
            this.MultiplayerButton.Bounds = new Rectangle(area.X + 32, area.Bottom - 84 - 64, 220, 52);
            this.VersionsButton.Bounds = new Rectangle(area.X + 32 + 232, area.Bottom - 84 - 64, 240, 52);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "What do you want to edit?", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            if (ContentPacks.Any && CoreMod.Sync?.UsingHostContent != true)
            {
                string theme = Gfx.Fit($"Theme: {Themes.Active}", 420);
                Gfx.Text(b, theme, new Vector2(this.Area.Right - 36 - Gfx.Font.MeasureString(theme).X, this.Area.Y + 34), Color.DimGray);
            }
            base.Draw(b, mouseX, mouseY);
            if (CoreMod.Sync?.UsingHostContent == true)
            {
                string note = CoreMod.Sync.CanChangeHostContent
                    ? "You're using the host's content. What you save here is sent to them and kept in their game."
                    : "You're using the host's content. You can add to it and change what you added; the rest is the host's.";
                Gfx.Text(b, Gfx.Fit(note, this.Area.Width - 72), new Vector2(this.Area.X + 36, this.Area.Y + 68), Color.DarkRed);
            }
            Gfx.Text(b, "Support (optional)", new Vector2(this.DonateButton.Bounds.X, this.DonateButton.Bounds.Y - 40), Color.DimGray);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.SettingsButton.Bounds.X - this.ThemesButton.Bounds.Right - 48, new Vector2(this.ThemesButton.Bounds.Right + 24, this.CloseButton.Bounds.Y + 16), this.MessageColor);
            if (this.ShowDescriptions)
            {
                // under its own button; in two columns a description can use its column and half the gap either side
                int room = this.Columns == 1 ? this.Area.Width - 80 : this.Entries[0].Button.Bounds.Width + 48;
                foreach ((CustomContent.EditorSection section, Button button) in this.Entries)
                    Gfx.TextCentered(b, Gfx.Fit(section.Description, room), new Rectangle(button.Bounds.Center.X - room / 2, button.Bounds.Bottom + 8, room, 32), Color.DimGray);
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

        /// <param name="message">What's being asked or said.</param>
        /// <param name="yesLabel">The button that goes ahead (or, for a notice, closes it).</param>
        /// <param name="onYes">What to do when it's clicked.</param>
        /// <param name="cancelLabel">The button that backs out, or null for a notice with one button.</param>
        public ConfirmScreen(string message, string yesLabel, Action onYes, string? cancelLabel = "Cancel")
        {
            this.Message = message;
            this.YesButton = this.Add(new Button(yesLabel, () => { this.Root.Pop(); onYes(); }));
            this.NoButton = this.Add(new Button(cancelLabel ?? "", () => this.Root.Pop()));
            this.NoButton.Visible = cancelLabel != null;
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
