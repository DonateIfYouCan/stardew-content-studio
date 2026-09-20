using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        public MultiplayerScreen()
        {
            this.ShareBox = this.Add(new Checkbox("Share my content when I host", CoreMod.Config.ShareContentAsHost,
                v => { CoreMod.Config.ShareContentAsHost = v; CoreMod.SaveConfig(); CoreMod.Sync?.OnShareChanged(); },
                "The players in your game use your content instead of their own, so everyone sees the same things. Their own content is saved first and comes back when they leave."));
            this.AcceptBox = this.Add(new Checkbox("Accept shared content", CoreMod.Config.AcceptContentFromHost, this.OnAcceptToggled,
                "In a host's game, use their content instead of your own. Your own is saved first and comes back when you leave.\nOnly turn this on if you play with people you trust."));
            this.ChangeBox = this.Add(new Checkbox("Let players change my content", CoreMod.Config.LetOthersChangeMyContent,
                v => { CoreMod.Config.LetOthersChangeMyContent = v; CoreMod.SaveConfig(); },
                "When you host: the players in your game can edit the content everyone is using, and what they save is sent to you and kept as yours. One player at a time per thing.\nThe previous version of anything they change is kept."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 36;
            int w = System.Math.Min(700, area.Width - pad * 2);
            int y = area.Y + 120;
            this.ShareBox.Bounds = new Rectangle(area.X + pad, y, w, 44);
            this.AcceptBox.Bounds = new Rectangle(area.X + pad, y + 96, w, 44);
            this.ChangeBox.Bounds = new Rectangle(area.X + pad, y + 192, w, 44);
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Multiplayer", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, Gfx.Fit("Everything here is off unless you turn it on. Only play this way with people you trust.", area.Width - 72), new Vector2(area.X + 36, area.Y + 68), Color.DarkRed);

            base.Draw(b, mouseX, mouseY);

            int noteWidth = area.Width - 80;
            Gfx.Message(b, "As host: your paintings, crops, furniture, wallpaper and character art are what the game runs on for everyone.", noteWidth, new Vector2(area.X + 40, this.ShareBox.Bounds.Bottom + 6), Color.DimGray);
            Gfx.Message(b, "As a player in someone else's game: their set is used while you're there, and your own is put back when you leave.", noteWidth, new Vector2(area.X + 40, this.AcceptBox.Bounds.Bottom + 6), Color.DimGray);
            Gfx.Message(b, "A player's save is sent to you, checked, and written into your content; the version it replaces is kept in a 'versions' folder.", noteWidth, new Vector2(area.X + 40, this.ChangeBox.Bounds.Bottom + 6), Color.DimGray);
            if (CoreMod.Sync?.UsingHostContent == true)
                Gfx.Text(b, "You're using the host's content in this game; your own comes back when you leave.", new Vector2(area.X + 36, area.Bottom - 120), Color.DimGray);
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
            CoreMod.SaveConfig();
            CoreMod.Sync?.OnAcceptChanged();
            Game1.playSound("smallSelect");
        }
    }
}
