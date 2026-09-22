using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CustomContentCore
{
    /// <summary>One mod's content as themes see it: where it lives and which files and folders make it up.</summary>
    /// <param name="Id">The mod's unique ID, which is also its folder's name inside a theme.</param>
    /// <param name="Folder">The mod's own folder, where the theme in use lives.</param>
    /// <param name="Paths">Its content files and folders, relative to <paramref name="Folder"/>, like <c>fish.json</c> and <c>images</c>.</param>
    internal sealed record ThemeMod(string Id, string Folder, string[] Paths);

    /// <summary>
    /// Moving content between the mods' folders and theme folders. A theme is a folder holding one folder per mod, each laid out
    /// like that mod's own folder. The theme in use lives in the mods' own folders, where the mods read and write it; the
    /// others wait in their theme folders. Pure file work, so it's checked without the game.
    /// </summary>
    internal static class ThemeFiles
    {
        /// <summary>The longest theme name.</summary>
        public const int MaxNameLength = 60;

        /// <summary>A theme name that's safe as a folder name on every system, or an empty string if nothing's left of it.</summary>
        public static string CleanName(string? name)
        {
            char[] invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToArray();
            string clean = new string((name ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
            clean = string.Join(" ", clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
            if (clean.Length > MaxNameLength)
                clean = clean[..MaxNameLength].TrimEnd('.', ' ');
            // names Windows keeps for devices can't be folders there
            string stem = clean.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                clean = "_" + clean;
            return clean;
        }

        /// <summary>Copy the mods' content as it is now into a theme folder, replacing what the folder had.</summary>
        /// <param name="themeFolder">The theme's folder.</param>
        /// <param name="mods">The mods whose content to copy.</param>
        public static void Save(string themeFolder, IEnumerable<ThemeMod> mods)
        {
            // write it beside the old copy first, so a failure halfway leaves the old copy whole
            string temporary = themeFolder.TrimEnd(Path.DirectorySeparatorChar) + ".saving";
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
            foreach (ThemeMod mod in mods)
            {
                foreach (string file in ContentFiles(mod.Folder, mod.Paths))
                {
                    string target = Path.Combine(temporary, mod.Id, Path.GetRelativePath(mod.Folder, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
            }
            Directory.CreateDirectory(temporary); // a theme with nothing in it is still a theme

            if (Directory.Exists(themeFolder))
                Directory.Delete(themeFolder, recursive: true);
            Directory.Move(temporary, themeFolder);
        }

        /// <summary>Put a theme's content in the mods' own folders, replacing their content. A mod the theme has nothing for is left empty.</summary>
        /// <param name="themeFolder">The theme's folder.</param>
        /// <param name="mods">The mods to fill.</param>
        /// <remarks>Only the mods' content paths are written, so a theme folder someone passed around can't put a file anywhere else.</remarks>
        public static void Load(string themeFolder, IEnumerable<ThemeMod> mods)
        {
            foreach (ThemeMod mod in mods)
            {
                Clear(mod);
                string source = Path.Combine(themeFolder, mod.Id);
                foreach (string file in ContentFiles(source, mod.Paths))
                {
                    // the path comes from a real file under the theme's folder, so it has no '..'; any name the mod saved is kept
                    string target = Path.GetFullPath(Path.Combine(mod.Folder, Path.GetRelativePath(source, file)));
                    if (!ContentValidator.IsInsideFolder(target, mod.Folder))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
            }
        }

        /// <summary>Empty a mod's content, for a theme that starts with nothing.</summary>
        /// <remarks>Folders are left there, empty: the mods make their image folders when the game starts and save into them later.</remarks>
        public static void Clear(ThemeMod mod)
        {
            foreach (string relative in mod.Paths)
            {
                string target = Path.Combine(mod.Folder, relative);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                    Directory.CreateDirectory(target);
                }
                else if (File.Exists(target))
                    File.Delete(target);
            }
        }

        /// <summary>Zip a theme folder as a content pack, so it can be opened again as a theme or imported.</summary>
        /// <param name="themeFolder">The theme's folder.</param>
        /// <param name="zipPath">The zip to write.</param>
        /// <param name="manifestJson">The pack's <c>pack.json</c>, which says which mods it holds.</param>
        public static void Zip(string themeFolder, string zipPath, string manifestJson)
        {
            using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (string file in Directory.EnumerateFiles(themeFolder, "*", SearchOption.AllDirectories))
            {
                if (ContentValidator.IsInsideFolder(file, themeFolder))
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(themeFolder, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
            using StreamWriter writer = new(zip.CreateEntry("pack.json").Open());
            writer.Write(manifestJson);
        }

        /// <summary>Unpack a content pack into a new theme folder. A pack is laid out like a theme, plus its <c>pack.json</c>.</summary>
        /// <param name="zipPath">The pack.</param>
        /// <param name="themeFolder">The theme's folder, which mustn't exist yet.</param>
        /// <remarks>Only plain paths one folder down (a mod's folder) are unpacked; <see cref="Load"/> later takes only each mod's content paths from them.</remarks>
        public static void Unzip(string zipPath, string themeFolder)
        {
            if (Directory.Exists(themeFolder))
                throw new IOException($"there's already a theme called '{Path.GetFileName(themeFolder)}'");
            Directory.CreateDirectory(themeFolder);
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.EndsWith('/') || name == "pack.json" || !name.Contains('/') || !ContentValidator.IsSafeRelativePath(name))
                    continue;
                string target = Path.GetFullPath(Path.Combine(themeFolder, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!ContentValidator.IsInsideFolder(target, themeFolder))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        /// <summary>The content files under a folder that belong to the given content paths, skipping links that point elsewhere.</summary>
        private static IEnumerable<string> ContentFiles(string root, string[] paths)
        {
            if (!Directory.Exists(root))
                yield break;
            foreach (string relative in paths)
            {
                string path = Path.Combine(root, relative);
                if (File.Exists(path) && ContentValidator.IsInsideFolder(path, root))
                    yield return path;
                else if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        if (ContentValidator.IsInsideFolder(file, root))
                            yield return file;
                }
            }
        }
    }
}
