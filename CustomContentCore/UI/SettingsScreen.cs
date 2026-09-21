using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>The settings shared by all the mods: the key that opens the editor, and where images come from and go.</summary>
    internal sealed class SettingsScreen : Screen
    {
        private readonly Button EditorKeyButton;
        private readonly Cycler ScaleCycler;
        private readonly Checkbox HoverTipsBox;
        private readonly TextField ExportFolderField;
        private readonly TextField BrowserFolderField;
        private readonly Button CloseButton;

        /// <summary>Whether the next key pressed becomes the key that opens the editor.</summary>
        private bool ListeningForKey;

        public SettingsScreen()
        {
            this.EditorKeyButton = this.Add(new Button(CoreMod.Config.EditorKey.ToString(), () => { this.ListeningForKey = true; }, "Click, then press the key you want to open and close the editor with."));
            this.ScaleCycler = this.Add(new Cycler(
                new() { ("1", "The game's size"), ("2", "2x"), ("4", "4x"), ("8", "8x") },
                Math.Clamp(CoreMod.Config.ExportScale, 1, 8).ToString(),
                v => { CoreMod.Config.ExportScale = int.Parse(v); CoreMod.SaveConfig(); },
                "The size offered first when you export, and the one the console commands use. Every export asks, so this is only where it starts."));
            this.HoverTipsBox = this.Add(new Checkbox("Show hover tips", CoreMod.Config.ShowHoverTips, v => { CoreMod.Config.ShowHoverTips = v; CoreMod.SaveConfig(); }, "The line of help that follows the mouse over buttons. Turn it off once you know your way around."));
            this.ExportFolderField = this.Add(new TextField(CoreMod.Config.ExportFolder, v => { CoreMod.Config.ExportFolder = v.Trim(); CoreMod.SaveConfig(); }, limit: 200));
            this.BrowserFolderField = this.Add(new TextField(CoreMod.Config.BrowserStartFolder, v => { CoreMod.Config.BrowserStartFolder = v.Trim(); CoreMod.SaveConfig(); }, limit: 200));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 36, labelW = 320;
            int x = area.X + pad, y = area.Y + 110, w = Math.Min(760, area.Width - pad * 2);

            this.EditorKeyButton.Bounds = new Rectangle(x + labelW, y, 200, 48);
            y += 76;
            this.ScaleCycler.Bounds = new Rectangle(x + labelW, y, 320, 48);
            y += 76;
            this.HoverTipsBox.Bounds = new Rectangle(x + labelW, y, 320, 48);
            y += 76;
            this.ExportFolderField.Bounds = new Rectangle(x + labelW, y, w - labelW, 48);
            y += 84;
            this.BrowserFolderField.Bounds = new Rectangle(x + labelW, y, w - labelW, 48);

            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Settings", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);

            Gfx.Text(b, "Key that opens the editor", new Vector2(area.X + 36, this.EditorKeyButton.Bounds.Y + 12));
            Gfx.Text(b, "Export images at", new Vector2(area.X + 36, this.ScaleCycler.Bounds.Y + 12));
            Gfx.Text(b, "Help on hover", new Vector2(area.X + 36, this.HoverTipsBox.Bounds.Y + 12));
            Gfx.Text(b, "Save exports in", new Vector2(area.X + 36, this.ExportFolderField.Bounds.Y + 12));
            Gfx.Text(b, "File browser starts in", new Vector2(area.X + 36, this.BrowserFolderField.Bounds.Y + 12));

            base.Draw(b, mouseX, mouseY);

            Gfx.Text(b, "Leave a folder empty to use your Pictures folder.", new Vector2(area.X + 36, this.BrowserFolderField.Bounds.Bottom + 12), Color.DimGray);
            string note = this.ListeningForKey
                ? "Press the key you want, or Escape to leave it alone."
                : "The keys used while painting are changed with the 'Keys' button in the paint screen.";
            Gfx.Message(b, note, this.CloseButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.ListeningForKey ? new Color(160, 80, 20) : Color.DimGray);
        }

        public override bool KeyPress(Keys key)
        {
            if (!this.ListeningForKey)
                return false;

            this.ListeningForKey = false;
            if (key == Keys.Escape)
                return true;

            CoreMod.Config.EditorKey = KeybindList.Parse(key.ToString());
            CoreMod.SaveConfig();
            this.EditorKeyButton.Label = CoreMod.Config.EditorKey.ToString();
            Game1.playSound("smallSelect");
            return true;
        }
    }
}
