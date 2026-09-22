using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CustomContentCore.UI
{
    /// <summary>
    /// A text box for typing a number, with a unit after it (like <c>%</c>). Anything that isn't a number is dropped as
    /// it's typed, and what's typed is kept between the smallest and biggest allowed once you've finished typing.
    /// </summary>
    /// <remarks>
    /// Use this rather than a list of set values wherever any number in a range would do: a chance of 12.5% is as sensible
    /// as 10%, and nobody should have to pick the nearest one offered.
    /// </remarks>
    public sealed class NumberField : Widget
    {
        private readonly TextField Field;
        private readonly Action<double> OnChange;
        private readonly double Min;
        private readonly double Max;
        private readonly int Decimals;
        private readonly string Unit;

        /// <param name="value">The number to start with.</param>
        /// <param name="onChange">Called with the number as it's typed, already kept in range.</param>
        /// <param name="min">The smallest allowed.</param>
        /// <param name="max">The biggest allowed.</param>
        /// <param name="decimals">How many decimal places to keep; 0 for whole numbers only.</param>
        /// <param name="unit">Drawn after the box, like "%".</param>
        /// <param name="tooltip">What the number means.</param>
        public NumberField(double value, Action<double> onChange, double min = 0, double max = 100, int decimals = 2, string unit = "%", string? tooltip = null)
        {
            this.OnChange = onChange;
            this.Min = min;
            this.Max = max;
            this.Decimals = decimals;
            this.Unit = unit;
            this.Tooltip = tooltip;
            this.Field = new TextField(Format(value, decimals), this.OnTyped, limit: 10);
        }

        /// <summary>The number shown now, kept in range.</summary>
        public double Value => Clean(this.Field.Text, this.Min, this.Max, this.Decimals);

        /// <summary>Show a number, e.g. when another setting changed it.</summary>
        public void Set(double value)
        {
            string text = Format(value, this.Decimals);
            if (this.Field.Text != text)
                this.Field.Text = text;
        }

        /// <summary>Read a typed number, keeping it in range. Empty or nonsense reads as the smallest allowed.</summary>
        /// <remarks>Pure, so it's checked without the game.</remarks>
        public static double Clean(string? text, double min, double max, int decimals)
        {
            string digits = Keep(text);
            if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return min;
            return Math.Round(Math.Clamp(value, min, max), Math.Max(0, decimals), MidpointRounding.AwayFromZero);
        }

        /// <summary>Drop anything that isn't part of a number as it's typed: letters, spaces, a second dot, a stray minus.</summary>
        /// <remarks>A comma is read as a dot, so a decimal comma keyboard works too.</remarks>
        public static string Keep(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            System.Text.StringBuilder result = new();
            bool dot = false;
            foreach (char c in text)
            {
                if (char.IsDigit(c))
                    result.Append(c);
                else if ((c == '.' || c == ',') && !dot)
                {
                    result.Append('.');
                    dot = true;
                }
                else if (c == '-' && result.Length == 0)
                    result.Append('-');
            }
            return result.ToString();
        }

        /// <summary>Write a number the short way: no trailing zeros, and no dot when it's a whole number.</summary>
        public static string Format(double value, int decimals)
        {
            return Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero)
                .ToString(decimals > 0 ? "0." + new string('#', decimals) : "0", CultureInfo.InvariantCulture);
        }

        private void OnTyped(string text)
        {
            // keep what's being typed readable (a lone "." or "12." is fine mid-typing), but only the number reaches the caller
            string kept = Keep(text);
            if (kept != text)
                this.Field.Text = kept;
            this.OnChange(Clean(kept, this.Min, this.Max, this.Decimals));
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            int unitWidth = this.Unit.Length > 0 ? (int)Gfx.Font.MeasureString(this.Unit).X + 12 : 0;
            this.Field.Bounds = new Rectangle(this.Bounds.X, this.Bounds.Y, Math.Max(40, this.Bounds.Width - unitWidth), this.Bounds.Height);
            this.Field.Visible = true;
            this.Field.Draw(b, mouseX, mouseY);
            if (unitWidth > 0)
                Gfx.Text(b, this.Unit, new Vector2(this.Field.Bounds.Right + 8, this.Bounds.Y + (this.Bounds.Height - Gfx.LineHeight) / 2));
        }

        public override bool Click(int x, int y) => this.Field.Click(x, y);
    }
}
