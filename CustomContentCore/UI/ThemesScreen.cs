using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Switch between whole sets of custom content, one per project, and open backups and packs as themes.</summary>
    internal sealed class ThemesScreen : Screen
    {
        private readonly ScrollList<string> ThemeList;
        private readonly ScrollList<string> BackupList;
        private readonly TextField NameField;
        private readonly Button UseButton;
        private readonly Button NewEmptyButton;
        private readonly Button NewCopyButton;
        private readonly Button RenameButton;
        private readonly Button DeleteButton;
        private readonly Button ExportButton;
        private readonly Button OpenBackupButton;
        private readonly Button ChooseFileButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, string> Sizes = new(StringComparer.OrdinalIgnoreCase);
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public ThemesScreen()
        {
            this.NameField = this.Add(new TextField("", _ => { }, limit: ThemeFiles.MaxNameLength));
            this.ThemeList = this.Add(new ScrollList<string>(56, this.DrawThemeRow)
            {
                OnSelect = (name, _) => { this.NameField.Text = name; this.SyncButtons(); },
                OnDoubleClick = _ => this.UseSelected()
            });
            this.BackupList = this.Add(new ScrollList<string>(56, this.DrawBackupRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.OpenSelectedBackup(),
                EmptyText = "No backups or packs yet."
            });
            this.UseButton = this.Add(new Button("Use this theme", this.UseSelected, "Put your content away in its theme, and use this one instead."));
            this.NewEmptyButton = this.Add(new Button("New empty theme", () => this.Create(copy: false), "Start a new theme with nothing in it, named as typed above. Your content now is kept as its own theme."));
            this.NewCopyButton = this.Add(new Button("New copy", () => this.Create(copy: true), "Start a new theme as a copy of the one in use, named as typed above."));
            this.RenameButton = this.Add(new Button("Rename", this.RenameSelected, "Give the selected theme the name typed above."));
            this.DeleteButton = this.Add(new Button("Delete", this.DeleteSelected, "Delete the selected theme. A copy is kept in the backups."));
            this.ExportButton = this.Add(new Button("Export pack", this.Export, "Save the theme in use as one file, e.g. to use on another PC or share."));
            this.OpenBackupButton = this.Add(new Button("Open as a theme", this.OpenSelectedBackup, "Make a new theme from this backup or pack. The theme in use isn't touched."));
            this.ChooseFileButton = this.Add(new Button("Choose a pack...", this.ChooseFile, "Pick a pack (.zip) from anywhere on your PC and open it as a theme."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 36, gap = 32;
            int top = area.Y + 150, bottom = area.Bottom - 110;
            int half = (area.Width - pad * 2 - gap) / 2;
            int lx = area.X + pad, rx = lx + half + gap;
            int buttonsH = 48 + 12 + 56 + 8 + 56 + 8 + 56;

            this.ThemeList.Bounds = new Rectangle(lx, top, half, bottom - top - buttonsH - 16);
            int y = this.ThemeList.Bounds.Bottom + 16;
            this.NameField.Bounds = new Rectangle(lx + 90, y, half - 90, 48);
            y += 60;
            int third = (half - 16) / 3;
            this.UseButton.Bounds = new Rectangle(lx, y, half, 56);
            y += 64;
            this.NewEmptyButton.Bounds = new Rectangle(lx, y, third + 40, 56);
            this.NewCopyButton.Bounds = new Rectangle(lx + third + 48, y, third - 20, 56);
            this.ExportButton.Bounds = new Rectangle(lx + third * 2 + 36, y, half - third * 2 - 36, 56);
            y += 64;
            this.RenameButton.Bounds = new Rectangle(lx, y, (half - 8) / 2, 56);
            this.DeleteButton.Bounds = new Rectangle(lx + (half - 8) / 2 + 8, y, (half - 8) / 2, 56);

            this.BackupList.Bounds = new Rectangle(rx, top, half, this.ThemeList.Bounds.Height + 60);
            this.OpenBackupButton.Bounds = new Rectangle(rx, this.UseButton.Bounds.Y, half, 56);
            this.ChooseFileButton.Bounds = new Rectangle(rx, this.NewEmptyButton.Bounds.Y, half, 56);

            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle area = this.Area;
            Gfx.Panel(b, area);
            Gfx.Text(b, "Themes & backups", new Vector2(area.X + 36, area.Y + 24), null, Gfx.TitleFont);
            string intro = Themes.WhyNot ?? "A theme is a whole set of your content. Keep one per project and switch between them.";
            Gfx.Text(b, Gfx.Fit(intro, area.Width - 72), new Vector2(area.X + 36, area.Y + 68), Themes.WhyNot != null ? Color.DarkRed : Color.DimGray);
            Gfx.Text(b, "Themes", new Vector2(this.ThemeList.Bounds.X, this.ThemeList.Bounds.Y - 36));
            Gfx.Text(b, "Backups and packs", new Vector2(this.BackupList.Bounds.X, this.BackupList.Bounds.Y - 36));
            Gfx.Text(b, "Name", new Vector2(this.ThemeList.Bounds.X, this.NameField.Bounds.Y + 10));
            base.Draw(b, mouseX, mouseY);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - area.X - 60, new Vector2(area.X + 36, area.Bottom - 70), this.MessageColor);
        }

        private void DrawThemeRow(SpriteBatch b, string name, Rectangle row, bool selected, bool hover)
        {
            bool active = name.Equals(Themes.Active, StringComparison.OrdinalIgnoreCase);
            string detail = (active ? "in use · " : "") + this.SizeOf(name);
            Vector2 size = Gfx.Font.MeasureString(detail);
            Gfx.Text(b, Gfx.Fit(name, row.Width - (int)size.X - 40), new Vector2(row.X + 12, row.Y + 12), active ? new Color(40, 110, 40) : null);
            Gfx.Text(b, detail, new Vector2(row.Right - size.X - 16, row.Y + 12), active ? new Color(40, 110, 40) : Color.DimGray);
        }

        private void DrawBackupRow(SpriteBatch b, string path, Rectangle row, bool selected, bool hover)
        {
            string when = File.GetLastWriteTime(path).ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);
            Vector2 size = Gfx.Font.MeasureString(when);
            Gfx.Text(b, Gfx.Fit(Path.GetFileNameWithoutExtension(path), row.Width - (int)size.X - 40), new Vector2(row.X + 12, row.Y + 12));
            Gfx.Text(b, when, new Vector2(row.Right - size.X - 16, row.Y + 12), Color.DimGray);
        }

        /// <summary>How many files a theme has, worked out once per refresh.</summary>
        private string SizeOf(string name)
        {
            if (!this.Sizes.TryGetValue(name, out string? size))
            {
                int files = 0;
                if (name.Equals(Themes.Active, StringComparison.OrdinalIgnoreCase))
                {
                    foreach ((IManifest mod, _, _, _) in ContentPacks.GetRegistrations())
                        foreach (string _ in ContentPacks.GetOwnFiles(mod.UniqueID))
                            files++;
                }
                else if (Directory.Exists(Path.Combine(Themes.Folder, name)))
                    files = Directory.GetFiles(Path.Combine(Themes.Folder, name), "*", SearchOption.AllDirectories).Length;
                size = files == 1 ? "1 file" : $"{files} files";
                this.Sizes[name] = size;
            }
            return size;
        }

        private void Refresh()
        {
            this.Sizes.Clear();
            string? selected = this.ThemeList.Selected;
            this.ThemeList.Items = Themes.List();
            this.ThemeList.SelectedIndex = this.ThemeList.Items.FindIndex(n => n == selected);
            string? selectedBackup = this.BackupList.Selected;
            this.BackupList.Items = Themes.Backups();
            this.BackupList.SelectedIndex = this.BackupList.Items.FindIndex(p => p == selectedBackup);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            string? theme = this.ThemeList.Selected;
            bool active = theme != null && theme.Equals(Themes.Active, StringComparison.OrdinalIgnoreCase);
            bool blocked = Themes.WhyNot != null;
            this.UseButton.Enabled = theme != null && !active && !blocked;
            this.UseButton.Label = active ? "In use" : "Use this theme";
            this.NewEmptyButton.Enabled = this.NewCopyButton.Enabled = !blocked;
            this.RenameButton.Enabled = theme != null;
            this.DeleteButton.Enabled = theme != null && !active;
            this.DeleteButton.Tooltip = active ? "Switch to another theme before deleting this one." : "Delete the selected theme. A copy is kept in the backups.";
            this.OpenBackupButton.Enabled = this.BackupList.Selected != null;
        }

        private void UseSelected()
        {
            if (this.ThemeList.Selected is not { } name || name.Equals(Themes.Active, StringComparison.OrdinalIgnoreCase))
                return;
            void Switch() => this.Try(() => Themes.SwitchTo(name), $"Now using '{name}'. '{Themes.Active}' is kept as a theme.");
            if (Context.IsWorldReady)
                this.Root.Push(new ConfirmScreen($"Use the theme '{name}'?\n\nWhile it's in use, anything from '{Themes.Active}' in this world (planted, placed or carried) may show as an Error Item. It's safest to switch on the title screen.", "Switch", Switch));
            else
                Switch();
        }

        private void Create(bool copy)
        {
            string name = Themes.FreeName(this.NameField.Text.Trim().Length > 0 && !Themes.Exists(this.NameField.Text.Trim()) ? this.NameField.Text : copy ? $"{Themes.Active} copy" : "New theme");
            void Make() => this.Try(() => Themes.Create(name, copy), copy ? $"Now using '{name}', a copy of '{Themes.Active}'." : $"Now using '{name}', empty.");
            if (Context.IsWorldReady && !copy)
                this.Root.Push(new ConfirmScreen($"Start the empty theme '{name}'?\n\nWhile it's in use, anything from '{Themes.Active}' in this world may show as an Error Item. It's safest to switch on the title screen.", "Start it", Make));
            else
                Make();
        }

        private void RenameSelected()
        {
            if (this.ThemeList.Selected is not { } name)
                return;
            string newName = ThemeFiles.CleanName(this.NameField.Text);
            if (newName.Length == 0 || newName == name)
            {
                this.ShowMessage("Type the new name in the box first.", error: true);
                return;
            }
            if (Themes.Exists(newName) && !newName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                this.ShowMessage($"There's already a theme called '{newName}'.", error: true);
                return;
            }
            this.Try(() => Themes.Rename(name, newName), $"Renamed '{name}' to '{newName}'.");
            this.ThemeList.SelectedIndex = this.ThemeList.Items.IndexOf(newName);
            this.SyncButtons();
        }

        private void DeleteSelected()
        {
            if (this.ThemeList.Selected is not { } name || name.Equals(Themes.Active, StringComparison.OrdinalIgnoreCase))
                return;
            this.Root.Push(new ConfirmScreen($"Delete the theme '{name}'?\n\nA copy is kept in the backups, so it can be opened again.", "Delete", () =>
                this.Try(() => Themes.Delete(name), $"Deleted '{name}'. A copy is in the backups.")));
        }

        private void Export()
        {
            this.Try(() =>
            {
                string path = ContentPacks.Export(prefix: Themes.Active);
                CoreMod.StaticMonitor.Log($"Exported the theme '{Themes.Active}' to {path}.", LogLevel.Info);
            }, $"Exported '{Themes.Active}' to your {Path.GetFileName(ImageExport.ExportFolder)} folder.");
        }

        private void OpenSelectedBackup()
        {
            if (this.BackupList.Selected is { } path)
                this.OpenAsTheme(path);
        }

        private void ChooseFile()
        {
            this.Root.Push(new FileBrowserScreen(ImageExport.ExportFolder, this.OpenAsTheme, extensions: new[] { ".zip" }, title: "Choose a content pack (.zip)"));
        }

        private void OpenAsTheme(string path)
        {
            string name = Themes.FreeName(Path.GetFileNameWithoutExtension(path));
            this.Try(() => Themes.OpenAsTheme(path, name), $"Opened it as the theme '{name}'. Select it and click 'Use this theme' to use it.");
            this.ThemeList.SelectedIndex = this.ThemeList.Items.IndexOf(name);
            this.NameField.Text = name;
            this.SyncButtons();
        }

        /// <summary>Do something to the themes, and say how it went.</summary>
        /// <param name="action">What to do.</param>
        /// <param name="success">What to say when it worked, worked out before the action, so it names the theme in use before it.</param>
        private void Try(Action action, string success)
        {
            try
            {
                action();
                this.ShowMessage(success);
                Game1.playSound("newArtifact");
            }
            catch (Exception ex)
            {
                CoreMod.StaticMonitor.Log($"Themes: {ex}", LogLevel.Warn);
                this.ShowMessage($"Couldn't do that: {ex.Message}", error: true);
            }
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            if (error)
                Game1.playSound("cancel");
            this.Refresh();
        }
    }
}
