using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.TokenizableStrings;

namespace CustomCharacters.UI
{
    /// <summary>The Characters section's main page: pick a villager to edit their portraits, or edit the farmer.</summary>
    internal sealed class CharacterListScreen : Screen
    {
        private sealed record Row(string Npc, string DisplayName);

        private readonly CharacterStore Store;
        private readonly ScrollList<Row> List;
        private readonly TextField SearchField;
        private readonly Button EditButton;
        private readonly Button SpriteButton;
        private readonly Button ResetButton;
        private readonly Button CloseButton;

        /// <summary>The buttons that write to the characters file, with the tooltips they normally show, so the "someone else has it" note can be taken off again.</summary>
        private readonly (Button Button, string? Tooltip)[] WritingButtons;

        private List<Row> AllRows = new();
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public CharacterListScreen(CharacterStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<Row>(88, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No villagers found"
            });
            this.SearchField = this.Add(new TextField("", _ => this.ApplyFilter(), limit: 40));
            this.EditButton = this.Add(new Button("Edit portraits", () => this.WhenNobodyElseIsChangingIt(this.EditSelected), "Replace this villager's portraits with your own images."));
            this.SpriteButton = this.Add(new Button("Edit sprite", () => this.WhenNobodyElseIsChangingIt(this.EditSprite, sprite: true), "Give this villager an HD body (their sprite in the world)."));
            this.ResetButton = this.Add(new Button("Restore original", () => this.WhenNobodyElseIsChangingIt(this.ResetSelected), "Go back to the game's own portraits and sprite."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.WritingButtons = new[] { (this.EditButton, this.EditButton.Tooltip), (this.SpriteButton, this.SpriteButton.Tooltip), (this.ResetButton, this.ResetButton.Tooltip) };
            this.Refresh();
        }

        /// <summary>Open the portrait editor for a villager by name.</summary>
        /// <param name="search">The villager's name.</param>
        /// <param name="sprite">Whether to open the sprite editor instead of the portrait editor.</param>
        public bool OpenByName(string search, bool sprite = false)
        {
            int index = this.List.Items.FindIndex(r => r.Npc.Equals(search, StringComparison.OrdinalIgnoreCase) || r.DisplayName.Equals(search, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            this.List.SelectedIndex = index;
            this.WhenNobodyElseIsChangingIt(sprite ? this.EditSprite : this.EditSelected, sprite);
            return true;
        }

        public override void OnResume()
        {
            this.LetGo(); // whatever was opened is closed again
            this.Refresh();
        }

        public override void Dispose()
        {
            this.LetGo();
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 84;
            int w = area.Width - pad * 2;
            this.SearchField.Bounds = new Rectangle(x + 110, y, 360, 48);
            y += 64;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
            this.EditButton.Bounds = new Rectangle(x + w - sideW, y, sideW, 56);
            this.SpriteButton.Bounds = new Rectangle(x + w - sideW, y + 64, sideW, 56);
            this.ResetButton.Bounds = new Rectangle(x + w - sideW, y + 128, sideW, 56);
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        /// <summary>The content version the rows were built from, so the list notices when another player's change arrives.</summary>
        private int BuiltVersion = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh(); // another player's change arrived while this list was open

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Villagers", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Search", new Vector2(this.Area.X + 32, this.SearchField.Bounds.Y + 10));
            base.Draw(b, mouseX, mouseY);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, Row row, Rectangle bounds, bool selected, bool hover)
        {
            Rectangle thumb = new(bounds.X + 8, bounds.Y + 4, bounds.Height - 8, bounds.Height - 8);
            if (this.Store.TryGetHdPortrait(row.Npc, 0, out Texture2D? custom, out _))
                b.Draw(custom, thumb, Color.White);
            else
            {
                try
                {
                    Texture2D sheet = Game1.content.Load<Texture2D>($"Portraits/{row.Npc}");
                    b.Draw(sheet, thumb, new Rectangle(0, 0, 64, 64), Color.White);
                }
                catch
                {
                    // no portrait
                }
            }
            Gfx.Text(b, row.DisplayName, new Vector2(thumb.Right + 16, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2));
            if (this.Store.HasCustom(row.Npc) || this.Store.HasCustomSprite(row.Npc))
            {
                string badge = string.Join(" + ", new[] { this.Store.HasCustom(row.Npc) ? "portraits" : null, this.Store.HasCustomSprite(row.Npc) ? "sprite" : null }.Where(p => p != null));
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2), new Color(160, 80, 20));
            }
        }

        private void Refresh()
        {
            this.BuiltVersion = CustomContent.ContentVersion;
            string? selected = this.List.Selected?.Npc;
            this.AllRows = GetVillagers().ToList();
            this.ApplyFilter();
            int index = this.List.Items.FindIndex(r => r.Npc == selected);
            this.List.SelectedIndex = index;
            if (index >= 0)
                this.List.EnsureVisible(index);
            this.SyncButtons();
        }

        /// <summary>Get villagers who have portraits.</summary>
        private static IEnumerable<Row> GetVillagers()
        {
            List<Row> rows = new();
            foreach ((string name, CharacterData data) in DataLoader.Characters(Game1.content))
            {
                if (!Game1.content.DoesAssetExist<Texture2D>($"Portraits/{name}"))
                    continue;
                string displayName = TokenParser.ParseText(data.DisplayName ?? name);
                rows.Add(new Row(name, string.IsNullOrWhiteSpace(displayName) ? name : displayName));
            }
            return rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private void ApplyFilter()
        {
            string search = this.SearchField.Text.Trim();
            this.List.Items = search.Length == 0
                ? this.AllRows
                : this.AllRows.Where(r => r.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Npc.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            this.List.SelectedIndex = -1;
            this.List.Scroll = 0;
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            Row? row = this.List.Selected;
            this.EditButton.Visible = row != null;
            this.SpriteButton.Visible = row != null;
            this.ResetButton.Visible = row != null && (this.Store.HasCustom(row.Npc) || this.Store.HasCustomSprite(row.Npc));

            // in a game where everyone uses the Host's set, say who's changing this villager rather than let Player A write over Player B
            string? portraitBusy = row != null ? CustomContent.WhoIsChanging(this.Store.Manifest, PortraitThing(row.Npc)) : null;
            string? spriteBusy = row != null ? CustomContent.WhoIsChanging(this.Store.Manifest, SpriteThing(row.Npc)) : null;
            foreach ((Button button, string? tooltip) in this.WritingButtons)
            {
                string? busy = button == this.SpriteButton ? spriteBusy : portraitBusy;
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing {(button == this.SpriteButton ? "that sprite" : "those portraits")} right now." : tooltip;
            }
        }

        /// <summary>The villager's portraits or sprites being held at the moment, so they can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What one villager's portraits are called when asking to be the only one changing them.</summary>
        private static string PortraitThing(string npc) => $"item:{CharacterStore.PortraitItemId(npc)}";

        /// <summary>What one villager's sprites are called when asking to be the only one changing them.</summary>
        private static string SpriteThing(string npc) => $"item:{CharacterStore.SpriteItemId(npc)}";

        /// <summary>Change one villager's portraits or sprites, unless another player in the game is already changing that one.</summary>
        /// <remarks>Player A on Abigail's portraits doesn't stop Player B on her sprites, let alone on another villager.</remarks>
        private void WhenNobodyElseIsChangingIt(Action action, bool sprite = false)
        {
            if (this.List.Selected is not { } row)
            {
                action();
                return;
            }

            string thing = sprite ? SpriteThing(row.Npc) : PortraitThing(row.Npc);
            string label = sprite ? $"{row.Npc}'s sprite" : $"{row.Npc}'s portraits";
            CustomContent.TakeLock(this.Store.Manifest, thing, label, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = thing;
                action();
            });
        }

        /// <summary>Let go of the villager we were holding, so another player can change them.</summary>
        private void LetGo()
        {
            if (this.HeldItem == null)
                return;
            CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
            this.HeldItem = null;
        }

        private void EditSelected()
        {
            if (this.List.Selected is not { } row)
                return;
            PortraitSet set = this.Store.File.Portraits.FirstOrDefault(p => string.Equals(p.Npc, row.Npc, StringComparison.OrdinalIgnoreCase))
                ?? new PortraitSet { Npc = row.Npc };
            this.Root.Push(new PortraitEditorScreen(this.Store, set, row.DisplayName, () => this.ShowMessage($"Saved {row.DisplayName}'s portraits.")));
        }

        private void EditSprite()
        {
            if (this.List.Selected is not { } row)
                return;
            this.Root.Push(new SpriteEditorScreen(this.Store, row.Npc, row.DisplayName, () => this.ShowMessage($"Saved {row.DisplayName}'s sprite.")));
        }

        private void ResetSelected()
        {
            if (this.List.Selected is not { } row)
                return;
            this.Root.Push(new ConfirmScreen($"Restore {row.DisplayName}'s original portraits and sprite?\n\nYour images stay in the mod's images folder.", "Restore", () =>
            {
                CharactersFile file = this.Store.ReadFile();
                file.Portraits.RemoveAll(p => string.Equals(p.Npc, row.Npc, StringComparison.OrdinalIgnoreCase));
                file.Sprites.RemoveAll(p => string.Equals(p.Npc, row.Npc, StringComparison.OrdinalIgnoreCase));
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"{row.DisplayName} looks like the original again.");
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't save: {ex.Message}", error: true);
                }
            }));
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            this.Refresh();
        }
    }
}
