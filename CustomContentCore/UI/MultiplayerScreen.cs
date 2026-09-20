using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>What you share with the other players in a multiplayer game, and what you take from them.</summary>
    internal sealed class MultiplayerScreen : Screen
    {
        private readonly Checkbox ShareBox;
        private readonly Checkbox AcceptBox;
        private readonly Checkbox ChangeBox;
        private readonly Button CloseButton;

        /// <summary>What's being changed right now, and by whom.</summary>
        private readonly ScrollList<(string Key, string Holder, string Label)> Busy;

        /// <summary>Takes something back from the player changing it (the host only).</summary>
        private readonly Button FreeButton;

        public MultiplayerScreen()
        {
            this.ShareBox = this.Add(new Checkbox("Share my content when I host", CoreMod.Config.ShareContentAsHost,
                v => { CoreMod.Config.ShareContentAsHost = v; CoreMod.SaveConfig(); CoreMod.Sync?.OnShareChanged(); },
                "The players in your game use your content instead of their own, so everyone sees the same things. Their own content is saved first and comes back when they leave."));
            this.AcceptBox = this.Add(new Checkbox("Accept shared content", CoreMod.Config.AcceptContentFromHost, this.OnAcceptToggled,
                "In a host's game, use their content instead of your own. Your own is saved first and comes back when you leave.\nOnly turn this on if you play with people you trust."));
            this.ChangeBox = this.Add(new Checkbox("Let players change my content", CoreMod.Config.LetOthersChangeMyContent, this.OnChangeToggled,
                "When you host: the players in your game can also change what was already yours, not only what they added themselves. One player at a time per thing, and the version they replace is kept.\nNeeds 'Accept shared content', since it's the same trust either way."));
            this.Busy = this.Add(new ScrollList<(string Key, string Holder, string Label)>(44, this.DrawBusyRow)
            {
                EmptyText = "Nobody is changing anything right now."
            });
            this.FreeButton = this.Add(new Button("Take it back", this.FreeSelected, "Stop that player changing it. What they send afterwards is refused, so they'd have to open it again."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
        }

        /// <summary>One thing being changed: what it is, and who has it.</summary>
        private void DrawBusyRow(SpriteBatch b, (string Key, string Holder, string Label) row, Rectangle bounds, bool selected, bool hover)
        {
            string what = row.Label.Length > 0 ? row.Label : row.Key;
            Gfx.Text(b, Gfx.Fit(what, bounds.Width - 220), new Vector2(bounds.X + 12, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2));
            Gfx.Text(b, row.Holder, new Vector2(bounds.Right - 200, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2), Color.DimGray);
        }

        private void FreeSelected()
        {
            if (this.Busy.Selected is { Key.Length: > 0 } row)
            {
                CoreMod.Locks?.ForceRelease(row.Key);
                Game1.playSound("smallSelect");
            }
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 36;
            int w = System.Math.Min(700, area.Width - pad * 2);
            int y = area.Y + 120;
            this.ShareBox.Bounds = new Rectangle(area.X + pad, y, w, 44);
            this.AcceptBox.Bounds = new Rectangle(area.X + pad, y + 96, w, 44);
            this.ChangeBox.Bounds = new Rectangle(area.X + pad, y + 192, w, 44);
            int listTop = this.ChangeBox.Bounds.Bottom + 76;
            this.Busy.Bounds = new Rectangle(area.X + pad, listTop, w, System.Math.Max(88, area.Bottom - 84 - 16 - listTop));
            this.FreeButton.Bounds = new Rectangle(area.X + pad, area.Bottom - 84, 220, 60);
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            // who's changing what only means something in a game where everyone is using one set
            bool shared = CoreMod.Locks?.InSharedGame == true;
            this.Busy.Visible = shared;
            this.Busy.Items = shared ? CoreMod.Locks!.All().ToList() : new List<(string, string, string)>();
            this.FreeButton.Visible = shared && Context.IsMainPlayer && this.Busy.Items.Count > 0;

            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Multiplayer", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, Gfx.Fit("Everything here is off unless you turn it on. Only play this way with people you trust.", area.Width - 72), new Vector2(area.X + 36, area.Y + 68), Color.DarkRed);

            base.Draw(b, mouseX, mouseY);

            int noteWidth = area.Width - 80;
            Gfx.Message(b, "As host: your paintings, crops, furniture, wallpaper and character art are what the game runs on for everyone.", noteWidth, new Vector2(area.X + 40, this.ShareBox.Bounds.Bottom + 6), Color.DimGray);
            Gfx.Message(b, "As a player in someone else's game: their set is used while you're there, and your own is put back when you leave.", noteWidth, new Vector2(area.X + 40, this.AcceptBox.Bounds.Bottom + 6), Color.DimGray);
            Gfx.Message(b, "A player's save is sent to you, checked, and written into your content; the version it replaces is kept in a 'versions' folder.", noteWidth, new Vector2(area.X + 40, this.ChangeBox.Bounds.Bottom + 6), Color.DimGray);
            if (this.Busy.Visible)
                Gfx.Text(b, "Being changed right now", new Vector2(area.X + 36, this.Busy.Bounds.Y - 34), Color.DimGray);
        }

        /// <summary>Ask for confirmation before accepting other players' content.</summary>
        private void OnAcceptToggled(bool accept)
        {
            if (!accept)
            {
                this.SetAccept(false);
                return;
            }
            this.AcceptBox.Checked = false; // until confirmed
            this.Root.Push(new ConfirmScreen(
                "Only turn this on if you play multiplayer with people you trust.\n\n"
                + "The pictures and custom content of whoever shares - the host, or another player - are downloaded to your PC. "
                + "They're checked and cleaned first, but someone can still show you any pictures they like. Messages between "
                + "players pass through the host, so the host's word is the last one either way.",
                "Turn on",
                () => this.SetAccept(true)));
        }

        private void SetAccept(bool accept)
        {
            this.AcceptBox.Checked = accept;
            CoreMod.Config.AcceptContentFromHost = accept;

            // letting players change your content is the wider of the two, so it can't stay on without the narrower one
            if (!accept && CoreMod.Config.LetOthersChangeMyContent)
            {
                CoreMod.Config.LetOthersChangeMyContent = false;
                this.ChangeBox.Checked = false;
            }

            CoreMod.SaveConfig();
            CoreMod.Sync?.OnAcceptChanged();
            Game1.playSound("smallSelect");
        }

        /// <summary>Turning this on means trusting the others with what's already yours, so it follows 'Accept shared content'.</summary>
        private void OnChangeToggled(bool allow)
        {
            if (allow && !CoreMod.Config.AcceptContentFromHost)
            {
                this.ChangeBox.Checked = false;
                Game1.addHUDMessage(new HUDMessage("Turn on 'Accept shared content' first: this is the same trust, the other way round.") { noIcon = true });
                return;
            }

            CoreMod.Config.LetOthersChangeMyContent = allow;
            CoreMod.SaveConfig();
        }
    }
}
