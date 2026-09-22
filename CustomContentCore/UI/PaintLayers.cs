using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace CustomContentCore.UI
{
    /// <summary>One layer of the image being painted: its own pixels, shown or hidden, and how see-through it is.</summary>
    internal sealed class PaintLayer
    {
        private static int LastId;

        /// <summary>Which layer this is, kept by its copies, so undoing a stroke finds its layer after layers were added, moved or merged.</summary>
        public readonly int Id;

        public string Name;

        /// <summary>The layer's pixels, straight alpha, the size of the image.</summary>
        public Color[] Pixels;

        public bool Visible = true;

        /// <summary>How much of the layer shows, from 0 (not at all) to 100 (fully).</summary>
        public int Opacity = 100;

        public PaintLayer(string name, Color[] pixels, int? id = null)
        {
            this.Id = id ?? ++LastId;
            this.Name = name;
            this.Pixels = pixels;
        }

        /// <summary>A copy of the same layer (same <see cref="Id"/>), pixels and all, as it is now.</summary>
        public PaintLayer Clone() => new(this.Name, (Color[])this.Pixels.Clone(), this.Id) { Visible = this.Visible, Opacity = this.Opacity };
    }

    /// <summary>
    /// The layers of the image being painted, bottom first, and which one is being painted on. What's shown and saved is the
    /// visible layers laid over each other. Pure, so it's checked without the game.
    /// </summary>
    internal sealed class PaintLayerStack
    {
        public readonly int Width;
        public readonly int Height;

        /// <summary>The layers, bottom first.</summary>
        public readonly List<PaintLayer> Layers = new();

        /// <summary>The position in <see cref="Layers"/> of the layer being painted on.</summary>
        public int ActiveIndex { get; private set; }

        /// <summary>The layer being painted on.</summary>
        public PaintLayer Active => this.Layers[this.ActiveIndex];

        /// <summary>Start with the image as the only layer.</summary>
        public PaintLayerStack(Color[] image, int width, int height)
        {
            this.Width = width;
            this.Height = height;
            this.Layers.Add(new PaintLayer("Image", image));
        }

        /// <summary>Paint on another layer.</summary>
        public void Select(int index) => this.ActiveIndex = Math.Clamp(index, 0, this.Layers.Count - 1);

        /// <summary>Add an empty layer just above the one being painted on, and paint on it.</summary>
        public PaintLayer AddEmpty()
        {
            return this.Insert(new PaintLayer(this.FreeName("Layer"), new Color[this.Width * this.Height]));
        }

        /// <summary>Add a copy of the layer being painted on just above it, and paint on the copy.</summary>
        public PaintLayer Duplicate()
        {
            string original = System.Text.RegularExpressions.Regex.Replace(this.Active.Name, @" copy( \d+)?$", ""); // a copy of a copy is "copy 2", not "copy copy"
            PaintLayer copy = new(this.FreeName($"{original} copy"), (Color[])this.Active.Pixels.Clone()) { Visible = this.Active.Visible, Opacity = this.Active.Opacity };
            return this.Insert(copy);
        }

        /// <summary>The layer with an ID, if it's still there.</summary>
        public PaintLayer? Find(int id) => this.Layers.FirstOrDefault(l => l.Id == id);

        /// <summary>Take out the layer being painted on and paint on the one below it. The last layer can't be taken out.</summary>
        /// <returns>Whether it was taken out.</returns>
        public bool RemoveActive()
        {
            if (this.Layers.Count <= 1)
                return false;
            this.Layers.RemoveAt(this.ActiveIndex);
            this.ActiveIndex = Math.Max(0, this.ActiveIndex - 1);
            return true;
        }

        /// <summary>Move the layer being painted on up (+1) or down (-1), and keep painting on it.</summary>
        /// <returns>Whether it moved.</returns>
        public bool MoveActive(int direction)
        {
            int to = this.ActiveIndex + Math.Sign(direction);
            if (to < 0 || to >= this.Layers.Count || direction == 0)
                return false;
            (this.Layers[this.ActiveIndex], this.Layers[to]) = (this.Layers[to], this.Layers[this.ActiveIndex]);
            this.ActiveIndex = to;
            return true;
        }

        /// <summary>
        /// Lay the layer being painted on over the one below it, as they look now (its see-through setting included), making
        /// them one layer, and paint on that. A hidden layer is merged as it would look shown: merging is asking to keep it.
        /// </summary>
        /// <returns>Whether there was a layer below to merge into.</returns>
        public bool MergeDown()
        {
            if (this.ActiveIndex == 0)
                return false;
            PaintLayer top = this.Active, below = this.Layers[this.ActiveIndex - 1];
            Color[] merged = (Color[])below.Pixels.Clone();
            float opacity = top.Opacity / 100f;
            for (int i = 0; i < merged.Length; i++)
                merged[i] = Over(top.Pixels[i], opacity, merged[i]);
            below.Pixels = merged;
            this.Layers.RemoveAt(this.ActiveIndex);
            this.ActiveIndex--;
            return true;
        }

        /// <summary>What one pixel looks like with every visible layer laid over each other.</summary>
        public Color CompositeAt(int index)
        {
            Color result = Color.Transparent;
            foreach (PaintLayer layer in this.Layers)
                if (layer.Visible && layer.Opacity > 0)
                    result = Over(layer.Pixels[index], layer.Opacity / 100f, result);
            return result;
        }

        /// <summary>The whole image as it's shown: every visible layer laid over each other. This is what's saved.</summary>
        /// <remarks>With one visible layer, fully shown, it's that layer exactly, pixel for pixel.</remarks>
        public Color[] Flatten()
        {
            List<PaintLayer> shown = this.Layers.Where(l => l.Visible && l.Opacity > 0).ToList();
            if (shown.Count == 1 && shown[0].Opacity >= 100)
                return (Color[])shown[0].Pixels.Clone();
            Color[] result = new Color[this.Width * this.Height];
            for (int i = 0; i < result.Length; i++)
                result[i] = this.CompositeAt(i);
            return result;
        }

        /// <summary>A copy of every layer and which one is being painted on, to put back later (for undo).</summary>
        public (List<PaintLayer> Layers, int Active) Snapshot() => (this.Layers.Select(l => l.Clone()).ToList(), this.ActiveIndex);

        /// <summary>Put back a snapshot taken with <see cref="Snapshot"/>.</summary>
        public void Restore((List<PaintLayer> Layers, int Active) snapshot)
        {
            this.Layers.Clear();
            this.Layers.AddRange(snapshot.Layers.Select(l => l.Clone()));
            this.ActiveIndex = Math.Clamp(snapshot.Active, 0, this.Layers.Count - 1);
        }

        /// <summary>Lay a straight-alpha pixel, shown at an opacity, over another.</summary>
        internal static Color Over(Color top, float opacity, Color below)
        {
            float a = top.A / 255f * opacity;
            if (a <= 0)
                return below;
            float b = below.A / 255f * (1 - a);
            float outA = a + b;
            if (outA <= 0)
                return Color.Transparent;
            return new Color(
                (byte)Math.Round((top.R * a + below.R * b) / outA),
                (byte)Math.Round((top.G * a + below.G * b) / outA),
                (byte)Math.Round((top.B * a + below.B * b) / outA),
                (byte)Math.Round(outA * 255));
        }

        private PaintLayer Insert(PaintLayer layer)
        {
            this.Layers.Insert(this.ActiveIndex + 1, layer);
            this.ActiveIndex++;
            return layer;
        }

        /// <summary>A layer name nobody has yet: "Layer 1", "Layer 2"... or "Image copy", "Image copy 2"...</summary>
        private string FreeName(string stem)
        {
            bool numbered = stem == "Layer";
            if (!numbered && this.Layers.All(l => l.Name != stem))
                return stem;
            for (int i = numbered ? 1 : 2; ; i++)
            {
                string name = $"{stem} {i}";
                if (this.Layers.All(l => l.Name != name))
                    return name;
            }
        }
    }
}
