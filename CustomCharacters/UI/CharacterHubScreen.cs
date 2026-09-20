using System;
using System.Collections.Generic;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CustomCharacters.UI
{
    /// <summary>The Characters section's start page: villagers, your farmer, or the clothes and hats everyone wears.</summary>
    internal sealed class CharacterHubScreen : Screen
    {
        private readonly List<(Button Button, string Description)> Entries = new();
        private readonly Button CloseButton;
        private string? Message;

        public CharacterHubScreen(CharacterStore store)
        {
            this.Add("Villagers", "Portraits (one per emotion) and sprites for the game's characters.",
                () => this.Root.Push(new CharacterListScreen(store)));
            this.Add("Farmer", "Your character: body, hairstyles, beards and glasses, in HD.",
                () => this.Root.Push(new FarmerEditorScreen(store, FarmerHd.FarmerGroup, () => this.Message = "Saved the farmer's HD sheets.")));
            this.Add("Clothes & hats", "The shirts, pants and hats anyone can put on, in HD.",
                () => this.Root.Push(new FarmerEditorScreen(store, FarmerHd.ClothesGroup, () => this.Message = "Saved the HD clothes sheets.")));
            this.CloseButton = base.Add(new Button("Close", () => this.Root.Pop()));
        }

        private void Add(string label, string description, Action onClick)
        {
            this.Entries.Add((base.Add(new Button(label, onClick, description)), description));
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = Math.Min(560, area.Width - 64);
            int x = area.Center.X - w / 2;
            int y = area.Y + 140;
            foreach ((Button button, _) in this.Entries)
            {
                button.Bounds = new Rectangle(x, y, w, 72);
                y += 132;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - 32 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Characters", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            foreach ((Button button, string description) in this.Entries)
                Gfx.TextCentered(b, Gfx.Fit(description, this.Area.Width - 80), new Rectangle(this.Area.X, button.Bounds.Bottom + 8, this.Area.Width, 32), Color.DimGray);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), Color.DarkGreen);
        }
    }
}
