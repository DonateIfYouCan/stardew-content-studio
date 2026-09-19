using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Lets the player pick an image file from anywhere on their computer.</summary>
    public sealed class FileBrowserScreen : Screen
    {
        private sealed record Entry(string Path, string Name, bool IsFolder);

        private static string? LastFolder;

        private readonly Action<string> OnPicked;

        /// <summary>The file extensions that can be picked (lowercase, with dot), or null for images.</summary>
        private readonly string[]? Extensions;
        private readonly string Title;
        private readonly ScrollList<Entry> List;
        private readonly TextField PathField;
        private readonly Button UpButton;
        private readonly Button PickButton;
        private readonly Button CancelButton;
        private readonly List<Button> Places = new();

        private string Folder = "";
        private string? Error;
        private Texture2D? Preview;
        private string? PreviewPath;
        private string? PreviewInfo;

        /// <summary>Get the folder the browser should start in (from the Core config, else the user's Pictures folder).</summary>
        public static string DefaultStartFolder()
        {
            string configured = CoreMod.Config.BrowserStartFolder;
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
                return configured;
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return !string.IsNullOrEmpty(pictures) && Directory.Exists(pictures) ? pictures : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        /// <param name="startFolder">The folder to show first, or null for <see cref="DefaultStartFolder"/>.</param>
        /// <param name="onPicked">Called with the chosen image's full path.</param>
        /// <param name="extraPlaces">Extra shortcuts to show (like the calling mod's own image folder).</param>
        /// <param name="extensions">The file extensions that can be picked (like <c>.zip</c>), or null for PNG/JPEG images.</param>
        /// <param name="title">The screen title, or null for the image title.</param>
        public FileBrowserScreen(string? startFolder, Action<string> onPicked, IEnumerable<(string Label, string Path)>? extraPlaces = null, string[]? extensions = null, string? title = null)
        {
            startFolder ??= DefaultStartFolder();
            this.OnPicked = onPicked;
            this.Extensions = extensions?.Select(e => e.ToLowerInvariant()).ToArray();
            this.Title = title ?? "Choose an image (PNG or JPEG)";

            this.List = this.Add(new ScrollList<Entry>(44, this.DrawEntry)
            {
                OnSelect = (entry, _) =>
                {
                    if (entry.IsFolder)
                        this.Navigate(entry.Path); // folders open with a single click
                    else
                        this.Select(entry);
                },
                OnDoubleClick = this.Open
            });
            this.PathField = this.Add(new TextField("", _ => { }, limit: 1000));
            this.PathField.Box.OnEnterPressed += _ => this.Navigate(this.PathField.Text.Trim());
            this.UpButton = this.Add(new Button("Up", () => { if (Directory.GetParent(this.Folder) is { } parent) this.Navigate(parent.FullName); }, "Go to the parent folder"));
            this.PickButton = this.Add(new Button(extensions == null ? "Use this image" : "Use this file", this.Pick) { Enabled = false });
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            foreach ((string label, string path) in GetPlaces(extraPlaces))
                this.Places.Add(this.Add(new Button(label, () => this.Navigate(path), path)));

            this.Navigate(LastFolder != null && Directory.Exists(LastFolder) ? LastFolder : startFolder);
        }

        /// <summary>Common folders to jump to.</summary>
        private static IEnumerable<(string Label, string Path)> GetPlaces(IEnumerable<(string Label, string Path)>? extraPlaces)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<(string, string)> places = new()
            {
                ("Home", home),
                ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                ("Downloads", Path.Combine(home, "Downloads")),
                ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                ("Exports", ImageExport.ExportFolder)
            };
            if (extraPlaces != null)
                places.AddRange(extraPlaces);

            // drives on Windows, mounted volumes on Mac/Linux
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.DriveType is DriveType.Ram or DriveType.Unknown or DriveType.NoRootDirectory)
                        continue;
                    string root = drive.RootDirectory.FullName;
                    if (OperatingSystem.IsWindows())
                        places.Add((root.TrimEnd('\\'), root));
                    else if (root.StartsWith("/media/") || root.StartsWith("/run/media/") || root.StartsWith("/Volumes/") || root.StartsWith("/mnt/"))
                        places.Add((System.IO.Path.GetFileName(root.TrimEnd('/')), root));
                }
            }
            catch
            {
                // drive listing isn't essential
            }

            return places.Where(p => !string.IsNullOrEmpty(p.Item2) && Directory.Exists(p.Item2)).GroupBy(p => p.Item2).Select(g => g.First()).Take(12);
        }

        private void Navigate(string folder)
        {
            try
            {
                if (OperatingSystem.IsWindows() && folder.Length == 2 && folder[1] == ':' && char.IsLetter(folder[0]))
                    folder += "\\"; // "D:" alone means the current folder on drive D, not its root
                folder = Path.GetFullPath(folder);
                if (File.Exists(folder) && this.Accepts(folder))
                {
                    this.Navigate(Path.GetDirectoryName(folder)!);
                    int index = this.List.Items.FindIndex(e => string.Equals(e.Path, folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        this.List.SelectedIndex = index;
                        this.List.EnsureVisible(index);
                        this.Select(this.List.Items[index]);
                    }
                    return;
                }

                List<Entry> entries = new();
                DirectoryInfo dir = new(folder);
                if (dir.Parent != null)
                    entries.Add(new Entry(dir.Parent.FullName, "..", true));
                foreach (DirectoryInfo sub in dir.EnumerateDirectories().Where(d => !d.Name.StartsWith('.') && !d.Attributes.HasFlag(FileAttributes.Hidden)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    entries.Add(new Entry(sub.FullName, sub.Name, true));
                foreach (FileInfo file in dir.EnumerateFiles().Where(f => this.Accepts(f.FullName) && !f.Name.StartsWith('.')).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    entries.Add(new Entry(file.FullName, file.Name, false));

                this.Folder = folder;
                LastFolder = folder;
                this.List.Items = entries;
                this.List.SelectedIndex = -1;
                this.List.Scroll = 0;
                this.PathField.Text = folder;
                this.Error = null;
                this.ClearPreview();
            }
            catch (Exception ex)
            {
                this.Error = $"Can't open that folder: {ex.Message}";
                Game1.playSound("cancel");
            }
        }

        private void Select(Entry entry)
        {
            if (entry.IsFolder)
            {
                this.ClearPreview();
                return;
            }
            if (entry.Path == this.PreviewPath)
                return;

            this.ClearPreview();
            if (this.Extensions != null)
            {
                FileInfo info = new(entry.Path);
                this.PreviewInfo = $"{info.Name}\n{info.Length / 1024} KB, {info.LastWriteTime:g}";
                this.PreviewPath = entry.Path;
                this.PickButton.Enabled = true;
                return;
            }
            try
            {
                Pixels image = ImageProcessor.Decode(entry.Path);
                this.PreviewInfo = $"{image.Width} x {image.Height} px, {new FileInfo(entry.Path).Length / 1024} KB";
                this.Preview = image.Downscale(512).ToTexture();
                this.PreviewPath = entry.Path;
                this.PickButton.Enabled = true;
            }
            catch (Exception ex)
            {
                this.PreviewInfo = $"Can't read this image: {ex.Message}";
            }
        }

        private void Open(Entry entry)
        {
            if (entry.IsFolder)
                this.Navigate(entry.Path);
            else
            {
                this.Select(entry);
                this.Pick();
            }
        }

        private void Pick()
        {
            if (this.PreviewPath == null)
                return;
            string path = this.PreviewPath;
            this.Root.Pop();
            this.OnPicked(path);
        }

        private void ClearPreview()
        {
            this.Preview?.Dispose();
            this.Preview = null;
            this.PreviewPath = null;
            this.PreviewInfo = null;
            this.PickButton.Enabled = false;
        }

        private bool Accepts(string path)
        {
            return this.Extensions == null ? ImageProcessor.IsImageFile(path) : this.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        private void DrawEntry(SpriteBatch b, Entry entry, Rectangle row, bool selected, bool hover)
        {
            int textY = row.Y + (row.Height - Gfx.LineHeight) / 2;
            if (entry.IsFolder)
            {
                // simple folder icon: tab + body
                Color dark = new(150, 90, 30), light = new(230, 170, 70);
                int x = row.X + 10, y = row.Y + (row.Height - 26) / 2;
                Gfx.Rect(b, new Rectangle(x, y, 14, 6), dark);
                Gfx.Rect(b, new Rectangle(x, y + 4, 34, 22), dark);
                Gfx.Rect(b, new Rectangle(x + 2, y + 8, 30, 16), light);
            }
            else
            {
                // simple picture icon: frame + sky + hill
                int x = row.X + 12, y = row.Y + (row.Height - 24) / 2;
                Gfx.Rect(b, new Rectangle(x, y, 30, 24), new Color(90, 70, 50));
                Gfx.Rect(b, new Rectangle(x + 2, y + 2, 26, 20), new Color(140, 200, 240));
                Gfx.Rect(b, new Rectangle(x + 2, y + 14, 26, 8), new Color(90, 170, 80));
            }
            string label = entry.Name == ".." ? ".. (up one folder)" : entry.Name;
            Gfx.Text(b, Gfx.Fit(label, row.Width - 64), new Vector2(row.X + 56, textY), entry.IsFolder ? new Color(120, 60, 10) : Game1.textColor);
        }

        protected override void OnLayout(Rectangle area)
        {
            int x = area.X + 32, y = area.Y + 84;
            int placesW = 200;
            int previewW = Math.Min(460, area.Width / 3);

            this.UpButton.Bounds = new Rectangle(x, y, 100, 52);
            this.PathField.Bounds = new Rectangle(x + 112, y, area.Right - 32 - (x + 112), 52);
            y += 68;

            int listH = area.Bottom - 104 - y;
            for (int i = 0; i < this.Places.Count; i++)
                this.Places[i].Bounds = new Rectangle(x, y + i * 56, placesW, 52);
            this.List.Bounds = new Rectangle(x + placesW + 16, y, area.Width - 64 - placesW - 16 - previewW - 16, listH);

            this.PickButton.Bounds = new Rectangle(area.Right - 32 - 300, area.Bottom - 88, 300, 60);
            this.CancelButton.Bounds = new Rectangle(this.PickButton.Bounds.X - 16 - 180, area.Bottom - 88, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, this.Title, new Vector2(area.X + 36, area.Y + 28), null, Gfx.TitleFont);

            base.Draw(b, mouseX, mouseY);

            // preview
            int previewW = Math.Min(460, area.Width / 3);
            Rectangle previewBox = new(area.Right - 32 - previewW, this.List.Bounds.Y, previewW, Math.Min(previewW, this.List.Bounds.Height - 60));
            Gfx.Inset(b, previewBox, new Color(60, 50, 40));
            if (this.Preview != null)
                Gfx.Fitted(b, this.Preview, null, new Rectangle(previewBox.X + 12, previewBox.Y + 12, previewBox.Width - 24, previewBox.Height - 24), pixelated: false);
            else
                Gfx.TextCentered(b, this.Extensions == null ? "Select an image" : "Select a file", previewBox, Color.LightGray);
            if (this.PreviewInfo != null)
                Gfx.Text(b, Game1.parseText(this.PreviewInfo, Game1.smallFont, previewW), new Vector2(previewBox.X, previewBox.Bottom + 12));

            // wrap to the space left of the buttons (up to two lines)
            string message = this.Error ?? (this.Extensions == null ? "Click a folder to open it, or an image to preview it. The image is copied into the mod folder." : "Click a folder to open it, click a file to select it.");
            string wrapped = Game1.parseText(message, Gfx.Font, this.CancelButton.Bounds.X - area.X - 60);
            int lines = wrapped.Split('\n').Length;
            Gfx.Text(b, wrapped, new Vector2(area.X + 36, area.Bottom - 58 - Math.Min(lines, 2) * Gfx.LineHeight / 2), this.Error != null ? Color.DarkRed : Color.DimGray);
        }

        public override bool KeyPress(Keys key)
        {
            if (key == Keys.Back)
            {
                if (Directory.GetParent(this.Folder) is { } parent)
                    this.Navigate(parent.FullName);
                return true;
            }
            return false;
        }

        public override void Dispose()
        {
            this.ClearPreview();
        }
    }
}
