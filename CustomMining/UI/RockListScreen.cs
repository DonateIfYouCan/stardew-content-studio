using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomMining.UI
{
    /// <summary>The Rocks section's main page: your own rocks, which turn up in the mines and give what you put in them.</summary>
    internal sealed class RockListScreen : Screen
    {
        private readonly MiningStore Store;
        private readonly ScrollList<CustomRock> List;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DuplicateButton;
        private readonly Button DeleteButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Icons = new();

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public RockListScreen(MiningStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomRock>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No rocks yet. Click 'New rock' to make one."
            });
            this.NewButton = this.Add(new Button("+ New rock", this.CreateNew, "Make a rock that turns up in the mines: how it looks, where it is, and what it gives."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Put one down", this.PlaceSelected, "Puts one on the ground next to you, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo();
            this.Refresh();
        }

        public override void Dispose()
        {
            this.LetGo();
            this.ClearIcons();
        }

        /// <summary>Open the editor for a rock by name or ID.</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(r => r.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || r.Id.Equals(search, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            this.List.SelectedIndex = index;
            this.WhenNobodyElseIsChangingIt(this.EditSelected);
            return true;
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 96;
            int w = area.Width - pad * 2;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);

            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DuplicateButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
            this.SyncButtons();
        }

        /// <summary>The content version the rows were built from, so the list notices when another player's change arrives.</summary>
        private int BuiltVersion = -1;

        /// <summary>The lock version the buttons were last checked against.</summary>
        private int LockVersionSeen = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.LockVersionSeen != CustomContent.LockVersion)
            {
                this.LockVersionSeen = CustomContent.LockVersion;
                this.SyncButtons();
            }
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh();

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Custom rocks", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            Gfx.Message(b, this.Message ?? "Rocks turn up in the mines in place of the game's, and give what you put in them on top of what any rock gives.",
                this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void DrawRow(SpriteBatch b, CustomRock rock, Rectangle bounds, bool selected, bool hover)
        {
            if (this.GetIcon(rock) is { } icon)
            {
                int size = bounds.Height - 16;
                b.Draw(icon, new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(rock.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(rock.Name, textWidth), new Vector2(textX, bounds.Y + 8));
            Gfx.Text(b, Gfx.Fit(RockData.Describe(rock), bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), Color.DimGray);
            if (busy != null)
            {
                string badge = $"{busy} is changing this";
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
            }
        }

        private Texture2D? GetIcon(CustomRock rock)
        {
            if (this.Icons.TryGetValue(rock.Id, out Texture2D? icon))
                return icon;
            if (!this.Store.Rocks.TryGetValue(rock.Id, out MiningStore.RenderedMineral? rendered))
                return null;
            icon = new Texture2D(Game1.graphics.GraphicsDevice, rendered.IconHd.Width, rendered.IconHd.Height);
            icon.SetData(rendered.IconHd.Data);
            this.Icons[rock.Id] = icon;
            return icon;
        }

        private void ClearIcons()
        {
            foreach (Texture2D icon in this.Icons.Values)
                icon.Dispose();
            this.Icons.Clear();
        }

        private void Refresh()
        {
            this.BuiltVersion = CustomContent.ContentVersion;
            this.ClearIcons();
            string? selected = this.List.Selected?.Id;
            this.List.Items = this.Store.File.Rocks.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(r => r.Id == selected);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            CustomRock? rock = this.List.Selected;
            this.EditButton.Visible = rock != null;
            this.DuplicateButton.Visible = rock != null;
            this.DeleteButton.Visible = rock != null;
            this.GiveButton.Visible = rock != null;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Puts one on the ground next to you, for testing." : "Load a save first.";

            string? busy = rock != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(rock.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{rock?.Name}' right now." : null;
            }
        }

        /// <summary>The rock we're holding at the moment, if any.</summary>
        private string? HeldItem;

        /// <summary>What a rock is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{MiningStore.RockPrefix}{id}";

        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
        }

        /// <summary>Change one rock, unless another player in the game is already changing that one.</summary>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomRock? rock = this.List.Selected;
            if (rock == null)
            {
                action();
                return;
            }
            CustomContent.TakeLock(this.Store.Manifest, ItemThing(rock.Id), rock.Name, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = ItemThing(rock.Id);
                action();
            });
        }

        private void CreateNew()
        {
            CustomRock rock = new() { Name = "New rock", Places = { ["mines"] = 0.1 } };
            this.Root.Push(new RockEditorScreen(this.Store, rock, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } rock)
                this.Root.Push(new RockEditorScreen(this.Store, rock, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Put one of the rocks on the ground next to the player, so it can be broken to see what it gives.</summary>
        private void PlaceSelected()
        {
            if (this.List.Selected is not { } rock || !Context.IsWorldReady)
                return;
            Vector2 tile = Game1.player.Tile + new Vector2(1, 0);
            GameLocation location = Game1.player.currentLocation;
            if (location.objects.ContainsKey(tile) || !location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport))
            {
                this.ShowMessage("Stand somewhere with a free tile to your right.", error: true);
                return;
            }
            location.objects.Add(tile, new StardewValley.Object(this.Store.GetRockItemId(rock.Id), 1) { MinutesUntilReady = RockData.CleanHits(rock.Hits) });
            Game1.playSound("hammer");
            this.ShowMessage($"Put '{rock.Name}' down next to you.");
        }

        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } rock)
                return;
            MiningFile file = this.Store.ReadFile();
            CustomRock copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomRock>(Newtonsoft.Json.JsonConvert.SerializeObject(rock))!;
            copy.Name = $"{rock.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Rock");
            string id = baseId;
            for (int i = 2; file.Rocks.Exists(r => r.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Rocks.Add(copy);
            try
            {
                this.Store.Save(file);
                this.ShowMessage($"Copied to '{copy.Name}'.");
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't copy: {ex.Message}", error: true);
            }
        }

        private void DeleteSelected()
        {
            if (this.List.Selected is not { } rock)
                return;
            this.Root.Push(new ConfirmScreen($"Delete '{rock.Name}'?\n\nAny of it already in a mine level, or picked up, will turn into an Error Item.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                MiningFile file = this.Store.ReadFile();
                file.Rocks.RemoveAll(r => r.Id == rock.Id);
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"Deleted '{rock.Name}'.");
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't delete: {ex.Message}", error: true);
                }
            })));
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            this.Refresh();
        }
    }
}
