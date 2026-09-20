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
        private sealed record Registration(IManifest Mod, string Folder, string[] Paths, Action Reload, Func<IEnumerable<string>>? SharedFiles, ContentEditing? Editing);

        /// <summary>How a mod lets other players change one of its items in a multiplayer game.</summary>
        /// <param name="GetItemJson">Get one of your own items as JSON, or null if there's no such item.</param>
        /// <param name="ApplyItemJson">
        /// Write a changed item into your own content: the item's ID, its new JSON, and the images that came with it
        /// (the name the data refers to, and the full path of a checked file to copy in). Returns whether it was applied.
        /// </param>
        public sealed record ContentEditing(Func<string, string?> GetItemJson, Func<string, string, IDictionary<string, string>, bool> ApplyItemJson);

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

        /// <summary>The content of other players in this multiplayer game, by mod ID, in the order they should be loaded.</summary>
        private static readonly Dictionary<string, List<ContentSource>> PeerSources = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The registered mods: their ID, name, own folder, content paths and reload callback.</summary>
        internal static IEnumerable<(IManifest Mod, string Folder, string[] Paths, Action Reload)> GetRegistrations()
        {
            return Registrations.Select(r => (r.Mod, r.Folder, r.Paths, r.Reload)).ToList();
        }

        /// <summary>Where a mod loads content from: its own folder, or another player's content in a multiplayer game.</summary>
        /// <param name="OwnerId">The player the content belongs to (0 for your own).</param>
        /// <param name="OwnerName">The player's name, to show next to their items.</param>
        /// <param name="Folder">The folder to read the data file and images from.</param>
        /// <param name="IsOwn">Whether this is your own content, the only content you can write to.</param>
        public sealed record ContentSource(long OwnerId, string OwnerName, string Folder, bool IsOwn);

        /// <summary>Get everywhere a mod should load content from: its own folder first, then the other players who share theirs.</summary>
        /// <param name="mod">The mod's manifest.</param>
        /// <param name="ownFolder">The mod's own folder.</param>
        /// <remarks>Only the mod's own folder is ever written to; other players' items are edited by asking their owner (that comes later, with locks).</remarks>
        public static IReadOnlyList<ContentSource> GetContentSources(IManifest mod, string ownFolder)
        {
            List<ContentSource> sources = new() { new ContentSource(0, "", GetContentRoot(mod, ownFolder), IsOwn: true) };
            if (PeerSources.TryGetValue(mod.UniqueID, out List<ContentSource>? peers))
                sources.AddRange(peers.Where(p => Directory.Exists(p.Folder)));
            return sources;
        }

        /// <summary>Set the other players' content for a mod (empty to clear it).</summary>
        internal static void SetPeerSources(string modId, IEnumerable<ContentSource> sources)
        {
            List<ContentSource> list = sources.ToList();
            if (list.Count > 0)
                PeerSources[modId] = list;
            else
                PeerSources.Remove(modId);
        }

        /// <summary>Get the folder a mod should load its content from: its own folder, or (in multiplayer) the host's content.</summary>
        /// <param name="mod">The mod's manifest.</param>
        /// <param name="ownFolder">The mod's own folder.</param>
        public static string GetContentRoot(IManifest mod, string ownFolder)
        {
            return RootOverrides.TryGetValue(mod.UniqueID, out string? root) ? root : ownFolder;
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
        /// <param name="editing">How other players may change this mod's items in multiplayer (null: they can't).</param>
        public static void Register(IManifest mod, string modFolder, string[] paths, Action reload, Func<IEnumerable<string>>? sharedFiles = null, ContentEditing? editing = null)
        {
            Registrations.RemoveAll(r => r.Mod.UniqueID == mod.UniqueID);
            Registrations.Add(new Registration(mod, modFolder, paths, reload, sharedFiles, editing));
        }

        /// <summary>Goes up every time content is reloaded, so an open screen can tell that its list is out of date.</summary>
        public static int ContentVersion { get; private set; }

        /// <summary>Note that content was reloaded (e.g. another player's arrived, or they changed something of yours).</summary>
        internal static void NotifyReloaded() => ContentVersion++;

        /// <summary>How a mod lets other players change its items, if it does.</summary>
        internal static ContentEditing? GetEditing(string modId)
        {
            return Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(modId, StringComparison.OrdinalIgnoreCase))?.Editing;
        }

        /// <summary>Get the files a mod shares in multiplayer (full paths inside its own folder, no links).</summary>
        internal static IEnumerable<string> GetSharedFiles(string modId)
        {
            Registration? registration = Registrations.FirstOrDefault(r => r.Mod.UniqueID.Equals(modId, StringComparison.OrdinalIgnoreCase));
            if (registration == null)
                return Enumerable.Empty<string>();
            IEnumerable<string> files = registration.SharedFiles?.Invoke() ?? GetFiles(registration);
            return files
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct()
                .Where(f => ContentValidator.IsInsideFolder(f, registration.Folder) && IsContentPath(modId, Path.GetRelativePath(registration.Folder, f).Replace('\\', '/')))
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
        private static IEnumerable<string> GetFiles(Registration registration)
        {
            foreach (string relative in registration.Paths)
            {
                string path = Path.Combine(registration.Folder, relative);
                if (File.Exists(path) && ContentValidator.IsInsideFolder(path, registration.Folder))
                    yield return path;
                else if (Directory.Exists(path))
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (ContentValidator.IsInsideFolder(file, registration.Folder))
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
