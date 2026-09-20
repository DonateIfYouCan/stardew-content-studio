using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Shows what each key does and lets you change it. The choices are kept in the Core's <c>config.json</c>.</summary>
    public sealed class KeysScreen : Screen
    {
        /// <summary>One thing a key can do.</summary>
        /// <param name="Id">How it's stored in the config.</param>
        /// <param name="Label">What it does, in words.</param>
        /// <param name="Default">The key it uses unless you change it.</param>
        public sealed record Binding(string Id, string Label, Keys Default);

        private readonly IReadOnlyList<Binding> Bindings;
        private readonly Dictionary<string, string> Keys;
        private readonly Action OnChanged;
        private readonly ScrollList<Binding> List;
        private readonly Button ResetButton;
        private readonly Button CloseButton;

        /// <summary>The binding waiting for a new key, if any.</summary>
        private Binding? Listening;

        public override bool IsOverlay => true;

        /// <param name="bindings">Everything that can be given a key.</param>
        /// <param name="keys">The chosen keys by binding ID; changed in place.</param>
        /// <param name="onChanged">Called after a change, to save it.</param>
        public KeysScreen(IReadOnlyList<Binding> bindings, Dictionary<string, string> keys, Action onChanged)
        {
            this.Bindings = bindings;
            this.Keys = keys;
            this.OnChanged = onChanged;
            this.List = this.Add(new ScrollList<Binding>(52, this.DrawRow)
            {
                Items = new List<Binding>(bindings),
                OnSelect = (binding, _) => this.Listening = binding
            });
            this.ResetButton = this.Add(new Button("Use the usual keys", this.ResetAll, "Put every key back to what it was."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
        }

        /// <summary>The key a binding uses now.</summary>
        public static Keys KeyFor(Binding binding, IReadOnlyDictionary<string, string> keys)
        {
            return keys.TryGetValue(binding.Id, out string? name) && Enum.TryParse(name, ignoreCase: true, out Keys chosen)
                ? chosen
                : binding.Default;
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = Math.Min(640, area.Width - 80), h = Math.Min(660, area.Height - 60);
            Rectangle box = new(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
            this.List.Bounds = new Rectangle(box.X + 28, box.Y + 84, w - 56, h - 84 - 84);
            this.ResetButton.Bounds = new Rectangle(box.X + 28, box.Bottom - 72, 260, 52);
            this.CloseButton.Bounds = new Rectangle(box.Right - 28 - 150, box.Bottom - 72, 150, 52);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            int w = Math.Min(640, this.Area.Width - 80), h = Math.Min(660, this.Area.Height - 60);
            Rectangle box = new(this.Area.Center.X - w / 2, this.Area.Center.Y - h / 2, w, h);
            Gfx.Rect(b, this.Area, new Color(0, 0, 0, 120));
            Gfx.Panel(b, box);
            Gfx.Text(b, "Keys", new Vector2(box.X + 28, box.Y + 22), null, Gfx.TitleFont);
            Gfx.Text(b, this.Listening != null ? "Press the key you want, or Escape to leave it alone." : "Click one, then press the key you want.", new Vector2(box.X + 28, box.Y + 58), Color.DimGray);
            base.Draw(b, mouseX, mouseY);
        }

        private void DrawRow(SpriteBatch b, Binding binding, Rectangle row, bool selected, bool hover)
        {
            Gfx.Text(b, binding.Label, new Vector2(row.X + 12, row.Y + (row.Height - Gfx.LineHeight) / 2));
            string key = this.Listening == binding ? "press a key..." : KeyFor(binding, this.Keys).ToString();
            Gfx.Text(b, key, new Vector2(row.Right - 200, row.Y + (row.Height - Gfx.LineHeight) / 2), this.Listening == binding ? new Color(160, 80, 20) : Color.DimGray);
        }

        public override bool KeyPress(Keys key)
        {
            if (this.Listening is not { } binding)
                return false;

            if (key != Microsoft.Xna.Framework.Input.Keys.Escape)
            {
                this.Keys[binding.Id] = key.ToString();
                this.OnChanged();
                Game1.playSound("smallSelect");
            }
            this.Listening = null;
            this.List.SelectedIndex = -1;
            return true;
        }

        private void ResetAll()
        {
            this.Keys.Clear();
            this.OnChanged();
            this.Listening = null;
            Game1.playSound("smallSelect");
        }
    }
}
