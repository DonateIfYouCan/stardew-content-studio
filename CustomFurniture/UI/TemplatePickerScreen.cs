using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomFurniture.UI
{
    /// <summary>Pick the game furniture a new piece is based on (its type, size and behavior).</summary>
    internal sealed class TemplatePickerScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly Action<FurnitureTemplate> OnPicked;
        private readonly ScrollList<FurnitureTemplate> List;
        private readonly TextField SearchField;
        private readonly Cycler KindCycler;
        private readonly Button PickButton;
        private readonly Button CancelButton;
        private readonly List<FurnitureTemplate> All;
        private readonly Dictionary<string, Texture2D?> Thumbnails = new();

        public TemplatePickerScreen(FurnitureStore store, Action<FurnitureTemplate> onPicked)
        {
            this.Store = store;
            this.OnPicked = onPicked;
            this.All = store.GetTemplates();

            List<(string, string)> kinds = this.All.Select(t => t.Kind).Distinct().Select(k => (k, k)).ToList();
            kinds.Insert(0, ("", "All kinds"));
            this.KindCycler = this.Add(new Cycler(kinds, "", _ => this.Filter()));
            this.SearchField = this.Add(new TextField("", _ => this.Filter(), limit: 40));
            this.List = this.Add(new ScrollList<FurnitureTemplate>(80, this.DrawRow) { OnSelect = (_, _) => this.PickButton!.Enabled = true, OnDoubleClick = this.Pick });
            this.PickButton = this.Add(new Button("Use this", () => { if (this.List.Selected is { } t) this.Pick(t); }) { Enabled = false });
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.Filter();
        }

        public override void Dispose()
        {
            foreach (Texture2D? texture in this.Thumbnails.Values)
                texture?.Dispose();
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, top = area.Y + 84;
            this.KindCycler.Bounds = new Rectangle(area.X + pad, top, 300, 48);
            this.SearchField.Bounds = new Rectangle(area.X + pad + 420, top, 360, 48);
            this.List.Bounds = new Rectangle(area.X + pad, top + 64, area.Width - pad * 2, area.Bottom - 96 - (top + 64));
            this.PickButton.Bounds = new Rectangle(area.Right - pad - 200, area.Bottom - 84, 200, 60);
            this.CancelButton.Bounds = new Rectangle(this.PickButton.Bounds.X - 16 - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Based on which game furniture?", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));
            base.Draw(b, mouseX, mouseY);
            Gfx.Text(b, "Yours gets its size, type and behavior (light, fire, sleeping, holding items...).", new Vector2(this.Area.X + 36, this.Area.Bottom - 70), Color.DimGray);
        }

        private void DrawRow(SpriteBatch b, FurnitureTemplate template, Rectangle row, bool selected, bool hover)
        {
            Texture2D? thumb = this.GetThumbnail(template);
            if (thumb != null)
                Gfx.Fitted(b, thumb, new Rectangle(0, 0, template.Source.Width, template.Source.Height), new Rectangle(row.X + 8, row.Y + 4, 100, row.Height - 8), pixelated: true);
            Gfx.Text(b, template.Name, new Vector2(row.X + 124, row.Y + 8));
            string details = $"{template.Kind} · {template.TilesWide}x{template.TilesHigh} tiles" + (template.Frames == 2 ? $" · {string.Join(" + ", template.FrameLabels)}" : "");
            Gfx.Text(b, details, new Vector2(row.X + 124, row.Y + 42), Color.DimGray);
        }

        private Texture2D? GetThumbnail(FurnitureTemplate template)
        {
            if (!this.Thumbnails.TryGetValue(template.Id, out Texture2D? texture))
            {
                texture = FurnitureStore.LoadTemplateFrames(template)?.ToTexture();
                this.Thumbnails[template.Id] = texture;
            }
            return texture;
        }

        private void Filter()
        {
            string kind = this.KindCycler.Value;
            string search = this.SearchField.Text.Trim();
            this.List.Items = this.All
                .Where(t => kind.Length == 0 || t.Kind == kind)
                .Where(t => search.Length == 0 || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            this.List.SelectedIndex = -1;
            this.List.Scroll = 0;
            this.PickButton.Enabled = false;
        }

        private void Pick(FurnitureTemplate template)
        {
            this.Root.Pop();
            this.OnPicked(template);
        }
    }
}
