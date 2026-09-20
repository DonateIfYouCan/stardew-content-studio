using System;
using System.Collections.Generic;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CustomCharacters.UI
{
    /// <summary>The Characters section's start page: villagers, your farmer, or the clothes and hats everyone wears.</summary>
    internal sealed class CharacterHubScreen : Screen
    {
        private readonly CharacterStore Store;
        private readonly List<(Button Button, string Description)> Entries = new();

        /// <summary>The buttons that open an editor which writes to the characters file, with the tooltips they normally show.</summary>
        private readonly List<(Button Button, string Tooltip)> WritingButtons = new();

        private readonly Button CloseButton;
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public CharacterHubScreen(CharacterStore store)
        {
            this.Store = store;
            this.Add("Villagers", "Portraits (one per emotion) and sprites for the game's characters.",
                () => this.Root.Push(new CharacterListScreen(store))); // that screen asks for the characters file itself, per villager
            this.Add("Farmer", "Your character: body, hairstyles, beards and glasses, in HD.",
                () => this.WhenNobodyElseIsChangingIt(() => this.Root.Push(new FarmerEditorScreen(store, FarmerHd.FarmerGroup, () => this.Message = "Saved the farmer's HD sheets."))), writes: true);
            this.Add("Clothes & hats", "The shirts, pants and hats anyone can put on, in HD.",
                () => this.WhenNobodyElseIsChangingIt(() => this.Root.Push(new FarmerEditorScreen(store, FarmerHd.ClothesGroup, () => this.Message = "Saved the HD clothes sheets."))), writes: true);
            this.CloseButton = base.Add(new Button("Close", () => this.Root.Pop()));
        }

        public override void OnResume()
        {
            CustomContent.ReleaseLock(this.Store.Manifest, LockThing); // the editor that was opened is closed again
        }

        public override void Dispose()
        {
            CustomContent.ReleaseLock(this.Store.Manifest, LockThing);
        }

        /// <summary>What the characters file is called when asking to be the only one changing it.</summary>
        private const string LockThing = "file:" + CharacterStore.DataFileName;

        /// <summary>Open an editor that writes to the characters file, unless another player in the game is already changing it.</summary>
        /// <remarks>The lock is held until the editor is closed again, so Player B can't save over Player A halfway through.</remarks>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomContent.TakeLock(this.Store.Manifest, LockThing, "the characters", (granted, holder) =>
            {
                if (granted)
                    action();
                else
                {
                    this.Message = $"{holder} is changing the characters right now.";
                    this.MessageColor = Color.DarkRed;
                }
            });
        }

        /// <param name="writes">Whether clicking it leads to a change to the characters file, so it's greyed out while another player is changing it.</param>
        private void Add(string label, string description, Action onClick, bool writes = false)
        {
            Button button = base.Add(new Button(label, onClick, description));
            this.Entries.Add((button, description));
            if (writes)
                this.WritingButtons.Add((button, description));
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
            this.SyncButtons();
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Characters", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            foreach ((Button button, string description) in this.Entries)
                Gfx.TextCentered(b, Gfx.Fit(description, this.Area.Width - 80), new Rectangle(this.Area.X, button.Bounds.Bottom + 8, this.Area.Width, 32), Color.DimGray);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        /// <summary>Grey out the editors that write while another player in the game is changing the characters file.</summary>
        private void SyncButtons()
        {
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, LockThing);
            foreach ((Button button, string tooltip) in this.WritingButtons)
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing the characters right now." : tooltip;
            }
        }
    }
}
