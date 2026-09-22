using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StardewModdingAPI;

namespace CustomContentCore
{
    /// <summary>
    /// Themes: whole sets of custom content, one for each project, and the one in use switched in a click. The theme in use lives
    /// in the mods' own folders as always; switching saves it into its theme folder and brings the other one in.
    /// </summary>
    internal static class Themes
    {
        /// <summary>What the content is called before any theme was made.</summary>
        public const string DefaultName = "My content";

        /// <summary>Where themes not in use wait, one folder each, next to the backups.</summary>
        public static string Folder => Path.Combine(ImageExport.ExportFolder, "Themes");

        /// <summary>Where backups are made (before joining a host, before an import, and of deleted themes).</summary>
        public static string BackupFolder => Path.Combine(ImageExport.ExportFolder, "Backups");

        /// <summary>The theme in use.</summary>
        public static string Active => string.IsNullOrWhiteSpace(CoreMod.Config.ActiveTheme) ? DefaultName : CoreMod.Config.ActiveTheme;

        /// <summary>Why themes can't be switched right now, or null if they can.</summary>
        public static string? WhyNot => CoreMod.Sync?.UsingHostContent == true
            ? "You're using the host's content in this game. Themes can be switched once you've left."
            : null;

        /// <summary>The themes, the one in use first.</summary>
        public static List<string> List()
        {
            List<string> names = Directory.Exists(Folder)
                ? Directory.GetDirectories(Folder).Select(Path.GetFileName).OfType<string>().Where(n => !n.EndsWith(".saving")).ToList()
                : new();
            names.RemoveAll(n => n.Equals(Active, StringComparison.OrdinalIgnoreCase));
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            names.Insert(0, Active);
            return names;
        }

        /// <summary>Whether a theme by that name exists (in use or waiting).</summary>
        public static bool Exists(string name) => List().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Switch to another theme: save the one in use into its folder, then bring the other one into the mods.</summary>
        public static void SwitchTo(string name)
        {
            EnsureCanSwitch();
            if (name.Equals(Active, StringComparison.OrdinalIgnoreCase))
                return;
            string source = Path.Combine(Folder, name);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"there's no theme called '{name}'");

            ThemeFiles.Save(Path.Combine(Folder, Active), Mods());
            ThemeFiles.Load(source, Mods());
            Directory.Delete(source, recursive: true); // it's in the mods now; the mods' folders are its only copy while it's in use
            SetActive(name);
        }

        /// <summary>Start a new theme and switch to it, empty or as a copy of the one in use.</summary>
        public static void Create(string name, bool copyCurrent)
        {
            EnsureCanSwitch();
            ThemeFiles.Save(Path.Combine(Folder, Active), Mods());
            if (!copyCurrent)
                foreach (ThemeMod mod in Mods())
                    ThemeFiles.Clear(mod);
            SetActive(name);
        }

        /// <summary>Give a theme another name.</summary>
        public static void Rename(string oldName, string newName)
        {
            if (oldName.Equals(Active, StringComparison.OrdinalIgnoreCase))
            {
                CoreMod.Config.ActiveTheme = newName;
                CoreMod.SaveConfig();
                return;
            }
            Directory.Move(Path.Combine(Folder, oldName), Path.Combine(Folder, newName));
        }

        /// <summary>Delete a theme that isn't in use, keeping a copy of it in the backups.</summary>
        /// <returns>The backup's path.</returns>
        public static string Delete(string name)
        {
            if (name.Equals(Active, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("switch to another theme before deleting this one");
            string folder = Path.Combine(Folder, name);
            string backup = ZipTheme(folder, $"Deleted theme {name}");
            Directory.Delete(folder, recursive: true);
            return backup;
        }

        /// <summary>Open a backup or content pack as a new theme, without touching the one in use.</summary>
        public static void OpenAsTheme(string zipPath, string name)
        {
            ContentPacks.Describe(zipPath); // throws if it isn't a pack
            ThemeFiles.Unzip(zipPath, Path.Combine(Folder, name));
        }

        /// <summary>A theme name that isn't taken yet, based on the one given.</summary>
        public static string FreeName(string wanted)
        {
            string name = ThemeFiles.CleanName(wanted);
            if (name.Length == 0)
                name = "New theme";
            string candidate = name;
            for (int i = 2; Exists(candidate); i++)
                candidate = $"{name} {i}";
            return candidate;
        }

        /// <summary>The backups and exported packs, newest first.</summary>
        public static List<string> Backups()
        {
            IEnumerable<string> Zips(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.zip") : Array.Empty<string>();
            return Zips(BackupFolder).Concat(Zips(ImageExport.ExportFolder))
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }


        /*********
        ** Private methods
        *********/
        private static IEnumerable<ThemeMod> Mods()
        {
            // each theme keeps its own earlier versions, so putting one back never brings in another theme's file
            return ContentPacks.GetRegistrations().Select(r => new ThemeMod(r.Mod.UniqueID, r.Folder, r.Paths.Append("versions").ToArray())).ToList();
        }

        private static void EnsureCanSwitch()
        {
            if (WhyNot is { } why)
                throw new InvalidOperationException(why);
            Directory.CreateDirectory(Folder);
        }

        /// <summary>Note the theme in use and have every mod load it.</summary>
        private static void SetActive(string name)
        {
            CoreMod.Config.ActiveTheme = name;
            CoreMod.SaveConfig();
            foreach ((IManifest mod, _, _, Action reload) in ContentPacks.GetRegistrations())
            {
                try
                {
                    reload();
                }
                catch (Exception ex)
                {
                    CoreMod.StaticMonitor.Log($"{mod.Name} couldn't load the theme '{name}': {ex.Message}", LogLevel.Error);
                }
            }
            CustomContent.NotifyContentChanged(); // as a multiplayer host, the players get the new theme too
            CoreMod.StaticMonitor.Log($"Using the theme '{name}'.", LogLevel.Info);
        }

        /// <summary>Zip a theme folder into the backups, as a pack that can be opened again.</summary>
        private static string ZipTheme(string folder, string prefix)
        {
            Directory.CreateDirectory(BackupFolder);
            string path = Path.Combine(BackupFolder, $"{prefix} {DateTime.Now:yyyy-MM-dd HHmmss}.zip");
            var manifest = new
            {
                Format = 1,
                Created = DateTime.Now,
                Mods = ContentPacks.GetRegistrations()
                    .Where(r => Directory.Exists(Path.Combine(folder, r.Mod.UniqueID)))
                    .Select(r => new { Id = r.Mod.UniqueID, r.Mod.Name, Version = r.Mod.Version.ToString() })
                    .ToList()
            };
            ThemeFiles.Zip(folder, path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return path;
        }
    }
}
