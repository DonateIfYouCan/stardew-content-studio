using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>The list of villagers used to set an item's gift tastes: click a villager to step through love, like, dislike, hate and the game's own choice.</summary>
    public static class GiftTasteList
    {
        /// <summary>Make the list for a set of tastes, which it changes as villagers are clicked.</summary>
        /// <param name="tastes">The taste by villager's internal name; a villager left out is left to the game.</param>
        public static ScrollList<(string Name, string Label)> Create(Dictionary<string, string> tastes)
        {
            void Cycle(string villager)
            {
                string next = GiftTastes.Next(tastes.GetValueOrDefault(villager));
                if (next.Length == 0)
                    tastes.Remove(villager);
                else
                    tastes[villager] = next;
            }

            return new ScrollList<(string Name, string Label)>(56, (b, villager, bounds, selected, hover) =>
            {
                Gfx.Text(b, villager.Label, new Vector2(bounds.X + 12, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2));
                (string text, Color color) = GiftTastes.Describe(tastes.GetValueOrDefault(villager.Name));
                Vector2 size = Gfx.Font.MeasureString(text);
                Gfx.Text(b, text, new Vector2(bounds.Right - size.X - 16, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2), color);
            })
            {
                OnSelect = (v, _) => Cycle(v.Name),
                OnDoubleClick = v => Cycle(v.Name), // quick clicks step twice rather than being taken as opening something
                Items = GetVillagers()
            };
        }

        /// <summary>The villagers who can be given gifts, as (internal name, display name), by display name.</summary>
        public static List<(string Name, string Label)> GetVillagers()
        {
            IDictionary<string, string> tastes = Game1.content.Load<Dictionary<string, string>>("Data\\NPCGiftTastes");
            var characters = DataLoader.Characters(Game1.content);
            return tastes.Keys
                .Where(name => !name.StartsWith("Universal_") && characters.ContainsKey(name))
                .Select(name => (name, NPC.GetDisplayName(name) ?? name))
                .OrderBy(v => v.Item2, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Set which villagers love or hate an item as a gift, over the editor that opened it.</summary>
    public sealed class GiftTastesScreen : Screen
    {
        private readonly string ItemName;
        private readonly ScrollList<(string Name, string Label)> List;
        private readonly Button DoneButton;
        private readonly Button ClearButton;
        private readonly Dictionary<string, string> Tastes;
        private Rectangle Box;

        public override bool IsOverlay => true;

        /// <param name="itemName">What the item is called, for the title.</param>
        /// <param name="tastes">The tastes to change; changed in place, and kept when the editor is saved.</param>
        public GiftTastesScreen(string itemName, Dictionary<string, string> tastes)
        {
            this.ItemName = itemName;
            this.Tastes = tastes;
            this.List = this.Add(GiftTasteList.Create(tastes));
            this.ClearButton = this.Add(new Button("Leave all to the game", () => tastes.Clear(), "Forget every taste set here, so each villager feels about it as the game decides."));
            this.DoneButton = this.Add(new Button("Done", () => this.Root.Pop()));
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = Math.Min(760, area.Width - 40), h = Math.Min(860, area.Height - 40);
            this.Box = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
            this.List.Bounds = new Rectangle(this.Box.X + 32, this.Box.Y + 120, w - 64, h - 120 - 100);
            this.DoneButton.Bounds = new Rectangle(this.Box.Right - 32 - 180, this.Box.Bottom - 84, 180, 60);
            this.ClearButton.Bounds = new Rectangle(this.Box.X + 32, this.Box.Bottom - 84, 320, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Box);
            Gfx.Text(b, Gfx.Fit($"Gifts: {this.ItemName}", this.Box.Width - 64), new Vector2(this.Box.X + 32, this.Box.Y + 24), null, Gfx.TitleFont);
            string note = this.Tastes.Count == 0 ? "Click a villager to change how they feel about it as a gift." : $"{this.Tastes.Count} villager{(this.Tastes.Count == 1 ? "" : "s")} set. Click again to change.";
            Gfx.Text(b, note, new Vector2(this.Box.X + 32, this.Box.Y + 76), Color.DimGray);
            base.Draw(b, mouseX, mouseY);
        }
    }
}
