using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using StardewModdingAPI;

namespace CustomContentCore
{
    /// <summary>Exports all custom content (data files and images) of the Custom Content mods into one zip file, and imports it again (e.g. on another PC).</summary>
    public static class ContentPacks
    {
        /*********
        ** Types
        *********/
        /// <summary>A mod's content registered for packs.</summary>
        private sealed record Registration(IManifest Mod, string Folder, string[] Paths, Action Reload, Func<IEnumerable<string>>? SharedFiles);

        /// <summary>The manifest stored in each pack.</summary>
        private sealed class PackManifest
        {
            public int Format { get; set; } = 1;
            public DateTime Created { get; set; }
            public List<PackMod> Mods { get; set; } = new();
        }

        private sealed class PackMod
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
        }

        /// <summary>The result of importing a pack.</summary>
        /// <param name="Imported">The mods whose content was imported.</param>
        /// <param name="Skipped">Mods in the pack that aren't installed.</param>
        /// <param name="BackupPath">Where the previous content was saved.</param>
        public sealed record ImportResult(List<string> Imported, List<string> Skipped, string BackupPath);


        /*********
        ** Fields
        *********/
        private const string ManifestFile = "pack.json";
        private static readonly List<Registration> Registrations = new();

        /// <summary>Content folders to use instead of a mod's own folder (e.g. a multiplayer host's content), by mod ID.</summary>
        private static readonly Dictionary<string, string> RootOverrides = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The registered mods: their ID, name, own folder, content paths and reload callback.</summary>
        internal static IEnumerable<(IManifest Mod, string Folder, string[] Paths, Action Reload)> GetRegistrations()
        {
            return Registrations.Select(r => (r.Mod, r.Folder, r.Paths, r.Reload)).ToList();
        }

        /// <summary>Get the folder a mod should load its content from: its own folder, or (in multiplayer) the host's content.</summary>
        /// <param name="mod">The mod's manifest.</param>
        /// <param name="ownFolder">The mod's own folder.</param>
        public static string GetContentRoot(IManifest mod, string ownFolder)
        {
            return RootOverrides.TryGetValue(mod.UniqueID, out string? root) ? root : ownFolder;
        }

        /// <summary>Goes up every time content is reloaded, so an open screen can tell that its list is out of date.</summary>
        public static int ContentVersion { get; private set; }

        /// <summary>Note that content was reloaded (e.g. the host's arrived, or a player changed something).</summary>
        internal static void NotifyReloaded() => ContentVersion++;

        /// <summary>How many earlier versions of a data file to keep.</summary>
        private const int KeptVersions = 10;

        /// <summary>Keep a copy of every mod's data file as it is now, so a change can be undone later.</summary>
        /// <remarks>Only the data files: they're small, and the images they point at are kept as long as something uses them.</remarks>
        internal static void KeepVersionOfDataFiles()
        {
            foreach (Registration registration in Registrations)
            {
                foreach (string relative in registration.Paths.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    // in a host's game this is the host's copy, so its versions are kept beside it rather than in this player's own folder
                    string root = GetContentRoot(registration.Mod, registration.Folder);
                    string path = Path.Combine(root, relative);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        string folder = Path.Combine(root, "versions");
                        Directory.CreateDirectory(folder);
                        string stem = Path.GetFileNameWithoutExtension(relative), extension = Path.GetExtension(relative);

                        // skip it if nothing changed since the last copy
                        FileInfo[] kept = new DirectoryInfo(folder).GetFiles($"{stem}.*{extension}").OrderByDescending(f => f.Name).ToArray();
                        if (kept.FirstOrDefault() is { } newest && newest.Length == new FileInfo(path).Length && File.ReadAllBytes(newest.FullName).SequenceEqual(File.ReadAllBytes(path)))
                            continue;

                        File.Copy(path, Path.Combine(folder, $"{stem}.{DateTime.Now:yyyyMMdd-HHmmss}{extension}"), overwrite: true);
                        foreach (FileInfo old in kept.Skip(KeptVersions - 1))
                            old.Delete();
                    }
                    catch
                    {
                        // keeping a copy is a convenience; never let it stop a save
                    }
                }
            }
        }

        /// <summary>Whether a mod is currently showing a multiplayer host's content (so editing should be disabled).</summary>
        public static bool IsUsingHostContent(IManifest mod) => RootOverrides.ContainsKey(mod.UniqueID);

        /// <summary>Use another folder for a mod's content (or null to go back to its own), then reload it.</summary>
        internal static void SetContentRoot(string modId, string? root)
        {
            if (root == null)
                RootOverrides.Remove(modId);
            else
                RootOverrides[modId] = root;
        }

        /// <summary>Get the content files of a registered mod (full paths), from its own folder.</summary>
        internal static IEnumerable<string> GetOwnFiles(string modId)
        {
            Registration? registration = Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(modId, StringComparison.OrdinalIgnoreCase));
            return registration != null ? GetFiles(registration) : Enumerable.Empty<string>();
        }

        /// <summary>Whether a relative path is one of a mod's registered content paths.</summary>
        internal static bool IsContentPath(string modId, string relativePath)
        {
            Registration? registration = Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(modId, StringComparison.OrdinalIgnoreCase));
            return registration != null && registration.Paths.Any(p => relativePath.Equals(p, StringComparison.OrdinalIgnoreCase) || relativePath.StartsWith(p.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
        }


        /*********
        ** Public methods
        *********/
        /// <summary>Register a mod's content for export/import packs.</summary>
        /// <param name="mod">The mod's manifest.</param>
        /// <param name="modFolder">The mod's folder (its <c>DirectoryPath</c>).</param>
        /// <param name="paths">Files and folders (relative to the mod folder) that make up its content, like <c>paintings.json</c> and <c>images</c>.</param>
        /// <param name="reload">Reloads the mod's content after an import.</param>
        /// <param name="sharedFiles">
        /// Gets the files actually in use (full paths: the data file and the images it references). In multiplayer, only these are
        /// shared, so unused or deleted images never leave the PC. If null, every file in <paramref name="paths"/> is shared.
        /// </param>
        public static void Register(IManifest mod, string modFolder, string[] paths, Action reload, Func<IEnumerable<string>>? sharedFiles = null)
        {
            Registrations.RemoveAll(r => r.Mod.UniqueID == mod.UniqueID);
            Registrations.Add(new Registration(mod, modFolder, paths, reload, sharedFiles));
        }

        /// <summary>Get the files a mod shares in multiplayer (full paths inside its own folder, no links).</summary>
        internal static IEnumerable<string> GetSharedFiles(string modId)
        {
            Registration? registration = Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(modId, StringComparison.OrdinalIgnoreCase));
            if (registration == null)
                return Enumerable.Empty<string>();
            // in a host's game the mod is reading and writing the host's copy, so that's the folder these files live in
            string root = GetContentRoot(registration.Mod, registration.Folder);
            IEnumerable<string> files = registration.SharedFiles?.Invoke() ?? GetFiles(registration, root);
            return files
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct()
                .Where(f => ContentValidator.IsInsideFolder(f, root) && IsContentPath(modId, Path.GetRelativePath(root, f).Replace('\\', '/')))
                .ToList();
        }

        /// <summary>Whether any mod registered content.</summary>
        public static bool Any => Registrations.Count > 0;

        /// <summary>Export all registered content into a zip file, returning its path.</summary>
        /// <param name="folder">The folder to write to (default: the export folder).</param>
        /// <param name="prefix">The file name prefix.</param>
        public static string Export(string? folder = null, string prefix = "Stardew custom content")
        {
            folder ??= ImageExport.ExportFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{prefix} {DateTime.Now:yyyy-MM-dd HHmmss}.zip");

            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            PackManifest manifest = new() { Created = DateTime.Now };
            foreach (Registration registration in Registrations)
            {
                manifest.Mods.Add(new PackMod { Id = registration.Mod.UniqueID, Name = registration.Mod.Name, Version = registration.Mod.Version.ToString() });
                foreach (string file in GetFiles(registration))
                {
                    string relative = Path.GetRelativePath(registration.Folder, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, $"{registration.Mod.UniqueID}/{relative}", CompressionLevel.Optimal);
                }
            }

            ZipArchiveEntry entry = zip.CreateEntry(ManifestFile);
            using (StreamWriter writer = new(entry.Open()))
                writer.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return path;
        }

        /// <summary>Describe a pack's contents, like "Content Studio: Paintings, Content Studio: Crops (12 files)".</summary>
        public static string Describe(string zipPath)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            PackManifest manifest = ReadManifest(zip);
            int files = zip.Entries.Count(e => e.FullName != ManifestFile && !e.FullName.EndsWith('/'));
            return $"{string.Join(", ", manifest.Mods.Select(m => m.Name))} ({files} files, made {manifest.Created:g})";
        }

        /// <summary>Import a pack, replacing the current content of the mods it contains (after backing it up).</summary>
        public static ImportResult Import(string zipPath)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            PackManifest manifest = ReadManifest(zip);

            // back up everything first, so an import can always be undone
            string backup = Export(Path.Combine(ImageExport.ExportFolder, "Backups"), "Backup before import");

            List<string> imported = new(), skipped = new();
            foreach (PackMod packMod in manifest.Mods)
            {
                Registration? registration = Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(packMod.Id, StringComparison.OrdinalIgnoreCase));
                if (registration == null)
                {
                    skipped.Add(packMod.Name);
                    continue;
                }

                // pick the pack's files for this mod first (only into the registered paths), so nothing is deleted for a pack that can't be used
                string prefix = packMod.Id + "/";
                List<(ZipArchiveEntry Entry, string Destination)> files = new();
                foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith('/')))
                {
                    // never write outside the mod's content: plain relative paths only (no '..', backslashes, drive letters or
                    // device names), inside one of the registered content paths, and still inside the mod folder once resolved
                    string relative = entry.FullName[prefix.Length..];
                    if (!ContentValidator.IsSafeRelativePath(relative) || !IsContentPath(registration.Mod.UniqueID, relative))
                        continue;
                    string destination = Path.GetFullPath(Path.Combine(registration.Folder, relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!ContentValidator.IsInsideFolder(destination, registration.Folder) || !IsContentPath(registration.Mod.UniqueID, Path.GetRelativePath(registration.Folder, destination).Replace('\\', '/')))
                        continue;
                    files.Add((entry, destination));
                }

                // replace the current content (if a file is locked, e.g. open in another program on Windows, say how to recover)
                try
                {
                    foreach (string relative in registration.Paths)
                    {
                        string target = Path.Combine(registration.Folder, relative);
                        if (Directory.Exists(target))
                            Directory.Delete(target, recursive: true);
                        else if (File.Exists(target))
                            File.Delete(target);
                    }
                    foreach ((ZipArchiveEntry entry, string destination) in files)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, overwrite: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    registration.Reload();
                    throw new IOException($"{registration.Mod.Name} was only partly imported ({ex.Message}). Close any program using its files, then import again, or import the backup '{Path.GetFileName(backup)}' to go back.", ex);
                }

                registration.Reload();
                imported.Add(registration.Mod.Name);
            }

            return new ImportResult(imported, skipped, backup);
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Get a mod's content files (full paths), following its registered paths.</summary>
        /// <param name="registration">The mod's registration.</param>
        /// <param name="root">The folder to read from: the mod's own, or (in a host's game) the host's copy.</param>
        private static IEnumerable<string> GetFiles(Registration registration, string? root = null)
        {
            root ??= registration.Folder;
            foreach (string relative in registration.Paths)
            {
                string path = Path.Combine(root, relative);
                if (File.Exists(path) && ContentValidator.IsInsideFolder(path, root))
                    yield return path;
                else if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (ContentValidator.IsInsideFolder(file, root))
                            yield return file; // skip links pointing elsewhere
                    }
                }
            }
        }

        private static PackManifest ReadManifest(ZipArchive zip)
        {
            ZipArchiveEntry entry = zip.GetEntry(ManifestFile) ?? throw new InvalidOperationException("this isn't a Custom Content pack (no pack.json)");
            using StreamReader reader = new(entry.Open());
            return JsonConvert.DeserializeObject<PackManifest>(reader.ReadToEnd()) ?? throw new InvalidOperationException("the pack's pack.json is invalid");
        }
    }
}
