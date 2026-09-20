using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Puts back an earlier version of a content file, e.g. after a player in a multiplayer game changed something.</summary>
    internal sealed class VersionsScreen : Screen
    {
        /// <summary>One earlier version of a file.</summary>
        private sealed class Version
        {
            public IManifest Mod = null!;
            public string ModFolder = "";

            /// <summary>The file it's a version of, relative to the mod's folder.</summary>
            public string Original = "";

            /// <summary>The kept copy.</summary>
            public string Path = "";

            public DateTime When;
        }

        private readonly ScrollList<Version> List;
        private readonly Button RestoreButton;
        private readonly Button CloseButton;
        private string? Message;
        private Color MessageColour = Color.DarkGreen;

        public VersionsScreen()
        {
            this.List = this.Add(new ScrollList<Version>(52, this.DrawRow)
            {
                Items = Load(),
                EmptyText = "Nothing has been written over yet.",
                OnSelect = (_, _) => this.SyncButtons()
            });
            this.RestoreButton = this.Add(new Button("Put this one back", this.Restore, "Write this version over the file as it is now. The version it replaces is kept as well."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.SyncButtons();
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 36;
            this.List.Bounds = new Rectangle(area.X + pad, area.Y + 110, area.Width - pad * 2, area.Height - 110 - 110);
            this.RestoreButton.Bounds = new Rectangle(area.X + pad, area.Bottom - 84, 280, 60);
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Earlier versions", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, Gfx.Fit("Every time a file is written over - by you, or by a player in your game - the version it replaces is kept here.", area.Width - 72), new Vector2(area.X + 36, area.Y + 68), Color.DimGray);
            base.Draw(b, mouseX, mouseY);
            if (this.Message != null)
                Gfx.Message(b, this.Message, area.Width - 560, new Vector2(this.RestoreButton.Bounds.Right + 24, this.RestoreButton.Bounds.Y + 16), this.MessageColour);
        }

        private void DrawRow(SpriteBatch b, Version version, Rectangle row, bool selected, bool hover)
        {
            Gfx.Text(b, Gfx.Fit($"{version.Mod.Name} · {version.Original}", row.Width - 260), new Vector2(row.X + 12, row.Y + 6));
            Gfx.Text(b, version.When.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture), new Vector2(row.Right - 240, row.Y + 6), Color.DimGray);
        }

        private void SyncButtons()
        {
            this.RestoreButton.Visible = this.List.Selected != null;
        }

        /// <summary>Find every kept version of every registered mod's content.</summary>
        private static List<Version> Load()
        {
            List<Version> versions = new();
            foreach ((IManifest mod, string folder, _, _) in ContentPacks.GetRegistrations())
            {
                string root = Path.Combine(folder, "versions");
                if (!Directory.Exists(root))
                    continue;

                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    // the name is "<original stem>.<yyyyMMdd-HHmmss><extension>"
                    string name = Path.GetFileNameWithoutExtension(path);
                    int dot = name.LastIndexOf('.');
                    if (dot <= 0 || !DateTime.TryParseExact(name.Substring(dot + 1), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                        continue;

                    string relativeFolder = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
                    string original = Path.Combine(relativeFolder == "." ? "" : relativeFolder, name.Substring(0, dot) + Path.GetExtension(path)).Replace('\\', '/');
                    versions.Add(new Version { Mod = mod, ModFolder = folder, Original = original, Path = path, When = when });
                }
            }
            return versions.OrderByDescending(v => v.When).ToList();
        }

        private void Restore()
        {
            if (this.List.Selected is not { } version)
                return;

            try
            {
                string target = Path.Combine(version.ModFolder, version.Original.Replace('/', Path.DirectorySeparatorChar));
                if (!ContentValidator.IsInsideFolder(target, version.ModFolder))
                    throw new InvalidOperationException("that file isn't in the mod's folder any more");

                CoreMod.Sync?.KeepVersionOf(version.ModFolder, version.Original, target); // so putting it back can be undone too
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(version.Path, target, overwrite: true);

                foreach ((IManifest mod, _, _, Action reload) in ContentPacks.GetRegistrations().Where(r => r.Mod.UniqueID == version.Mod.UniqueID))
                    reload();
                CustomContent.NotifyContentChanged();

                this.List.Items = Load();
                this.List.SelectedIndex = -1;
                this.SyncButtons();
                this.Message = $"Put back {version.Original} as it was on {version.When:d MMM, HH:mm}.";
                this.MessageColour = Color.DarkGreen;
                Game1.playSound("newArtifact");
            }
            catch (Exception ex)
            {
                this.Message = $"Couldn't put it back: {ex.Message}";
                this.MessageColour = Color.DarkRed;
            }
        }
    }
}
