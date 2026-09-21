using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>Saves game images as PNG files so they can be edited in an external image editor and imported again.</summary>
    public static class ImageExport
    {
        /// <summary>
        /// The folder exports (and import backups) are written to: the Core config's <c>ExportFolder</c>, else Pictures/Stardew Custom
        /// Content. If that folder can't be written (e.g. Windows' Controlled Folder Access blocks Pictures), an <c>exports</c> folder
        /// in the Core mod folder is used instead.
        /// </summary>
        public static string ExportFolder
        {
            get
            {
                string preferred = PreferredFolder;
                if (preferred == CheckedFolder)
                    return CheckedResult!;

                CheckedFolder = preferred;
                CheckedResult = CanWrite(preferred) ? preferred : Path.Combine(CoreMod.StaticHelper.DirectoryPath, "exports");
                if (CheckedResult != preferred)
                    CoreMod.StaticMonitor.Log($"Can't write to '{preferred}' (it may be protected, e.g. by Windows' Controlled Folder Access), so exports and backups go to '{CheckedResult}' instead. Set ExportFolder in the Core's config.json to choose another folder.", StardewModdingAPI.LogLevel.Warn);
                return CheckedResult;
            }
        }

        /// <summary>The folder chosen in the config, or the default one.</summary>
        private static string PreferredFolder
        {
            get
            {
                string configured = CoreMod.Config.ExportFolder;
                if (!string.IsNullOrWhiteSpace(configured))
                    return configured;
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pictures))
                    pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(pictures, "Stardew Custom Content");
            }
        }

        private static string? CheckedFolder;
        private static string? CheckedResult;

        /// <summary>Whether files can be created in a folder (creating it if needed).</summary>
        private static bool CanWrite(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllBytes(probe, Array.Empty<byte>());
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>The enlargement offered first, and the one the console commands use.</summary>
        public static int DefaultScale => System.Math.Clamp(CoreMod.Config.ExportScale, 1, 16);

        /// <summary>
        /// Ask how big the export should be, then do it. The game's own size comes first, because that's what you want to
        /// repaint pixel for pixel; enlarging is for making room to add detail, and it's remembered as the next first offer.
        /// </summary>
        /// <param name="screen">The screen asking, whose root the question is shown on.</param>
        /// <param name="export">Writes the file at the chosen enlargement and returns where it went.</param>
        public static void AskScale(UI.Screen screen, System.Func<int, string> export)
        {
            (string Label, string Description, int Scale)[] sizes =
            {
                ("The game's size", "The sheet exactly as the game has it, to repaint pixel for pixel.", 1),
                ("2x", "Twice as big, for a little more detail than the game has.", 2),
                ("4x", "Four times as big, for HD art.", 4)
            };

            screen.Root.Push(new UI.ChoiceScreen(
                "How big should the exported image be?",
                sizes
                    .Select(size => (
                        size.Scale == DefaultScale ? size.Label + " (last used)" : size.Label,
                        size.Description,
                        (System.Action)(() =>
                        {
                            CoreMod.Config.ExportScale = size.Scale;
                            CoreMod.SaveConfig();
                            export(size.Scale);
                        })))
                    .ToArray()));
        }

        /// <summary>Export part of a texture as a PNG file. Must be called on the main thread.</summary>
        /// <param name="texture">The texture (as loaded by the game, i.e. premultiplied alpha).</param>
        /// <param name="source">The area to export, or null for the whole texture.</param>
        /// <param name="name">The file name without extension (made safe automatically).</param>
        /// <param name="upscale">How many times to enlarge it (nearest-neighbor, so pixels stay sharp).</param>
        /// <returns>The full path of the written file.</returns>
        public static string Export(Texture2D texture, Rectangle? source, string name, int upscale = 1)
        {
            Rectangle area = source ?? texture.Bounds;
            area = Rectangle.Intersect(area, texture.Bounds);
            Color[] data = new Color[area.Width * area.Height];
            texture.GetData(0, area, data, 0, data.Length);

            // undo premultiplied alpha so semi-transparent edges keep their color
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                if (c.A is > 0 and < 255)
                    data[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A);
            }

            upscale = Math.Clamp(upscale, 1, 16);
            int w = area.Width * upscale, h = area.Height * upscale;
            Color[] output = data;
            if (upscale > 1)
            {
                output = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        output[y * w + x] = data[(y / upscale) * area.Width + x / upscale];
            }

            string folder = ExportFolder;
            Directory.CreateDirectory(folder);
            string safeName = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ' ? ch : '_').ToArray()).Trim();
            if (safeName.Length > CustomContent.MaxFileNameLength)
                safeName = safeName[..CustomContent.MaxFileNameLength].Trim();
            if (upscale > 1)
                safeName += $" (x{upscale})";
            string path = Path.Combine(folder, safeName + ".png");

            using Texture2D temp = new(Game1.graphics.GraphicsDevice, w, h);
            temp.SetData(output);
            using FileStream stream = File.Create(path);
            temp.SaveAsPng(stream, w, h);
            return path;
        }
    }
}
