using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>
    /// Sends the host's custom content to players who join, so everyone sees the same paintings, crops, etc. Both sides must opt in:
    /// the host with <see cref="CoreConfig.ShareContentAsHost"/>, and each player with <see cref="CoreConfig.AcceptContentFromHost"/>.
    /// Received content is stored in a per-host cache and only used while in that host's game.
    /// </summary>
    /// <remarks>
    /// Security: the other side may run a modified game, so nothing received is trusted.
    /// <list type="bullet">
    ///   <item>SMAPI's sender ID can be forged by another farmhand (the host forwards their messages), so the player makes a random
    ///   secret and sends it only to the host (such messages are never forwarded). Content is only accepted with that secret.</item>
    ///   <item>Paths, file types, sizes and counts are checked before anything is requested or stored.</item>
    ///   <item>Every received file is rebuilt from scratch (<see cref="ContentValidator.TrySanitize"/>): images are decoded in managed
    ///   code and re-encoded as plain PNG, JSON is rewritten. Images are always sent as PNG data (the host converts JPEGs).</item>
    /// </list>
    /// </remarks>
    internal sealed class MultiplayerSync
    {
        /*********
        ** Messages
        *********/
        /// <summary>Host → player: the host shares content (sent when a player connects).</summary>
        public sealed class HelloMessage
        {
            public string HostName { get; set; } = "";
        }

        /// <summary>Player → host only: the player wants the content (with the secret for this session), or declines.</summary>
        public sealed class JoinMessage
        {
            public bool Declined { get; set; }
            public string Token { get; set; } = "";
        }

        /// <summary>A content file the host offers.</summary>
        public sealed class OfferedFile
        {
            public string Mod { get; set; } = "";
            public string Path { get; set; } = "";
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }

        /// <summary>Host → player: the host's content files.</summary>
        public sealed class OfferMessage
        {
            public string Token { get; set; } = "";
            public string HostName { get; set; } = "";
            public List<OfferedFile> Files { get; set; } = new();
        }

        /// <summary>Player → host: the files the player needs.</summary>
        public sealed class RequestMessage
        {
            public string Token { get; set; } = "";
            public List<string> Files { get; set; } = new(); // "mod/path"
        }

        /// <summary>Host → player: part of a file.</summary>
        public sealed class ChunkMessage
        {
            public string Token { get; set; } = "";
            public string Mod { get; set; } = "";
            public string Path { get; set; } = "";
            public int Index { get; set; }
            public int Count { get; set; }
            public string Data { get; set; } = ""; // base64
        }


        /*********
        ** Fields
        *********/
        private const string HelloType = "Hello", JoinType = "Join", OfferType = "Offer", RequestType = "Request", ChunkType = "Chunk";
        private const int ChunkBytes = 48 * 1024;
        private const int ChunksPerTick = 6;

        /// <summary>The most a host may send in total, and per file.</summary>
        private const long MaxTotalBytes = 200L * 1024 * 1024, MaxFileBytes = 64L * 1024 * 1024;

        /// <summary>The most files one offer may contain.</summary>
        private const int MaxFiles = 2000;

        /// <summary>The most chunks queued for one player (limits how much a player can make the host send).</summary>
        private const int MaxQueuedChunksPerPlayer = (int)(MaxTotalBytes / ChunkBytes) + 100;

        /// <summary>How many hosts' content to keep cached.</summary>
        private const int MaxCachedHosts = 5;

        private const string IndexFile = "sync-index.json";

        private static readonly string[] AllowedExtensions = { ".json", ".png", ".jpg", ".jpeg" };

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly string ModId;

        // host
        private readonly Queue<(long Player, ChunkMessage Chunk)> Outgoing = new();
        private readonly Dictionary<long, string> PlayerTokens = new();
        private readonly Dictionary<string, (DateTime Modified, long Length, string Hash, long Size)> TransferInfoCache = new();
        private bool OfferQueued;

        // player
        private string? Token;
        private bool HelloHandled;
        private string HostName = "";
        private OfferMessage? CurrentOffer;

        /// <summary>The player whose content we're taking: the host, or another player who shares. Only one at a time.</summary>
        private long SenderId;
        private readonly Dictionary<string, (int Count, byte[]?[] Parts)> Incoming = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> Index = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Generation, string Key, OfferedFile File, Task<(bool Ok, byte[] Clean, string Error)> Task)> Sanitizing = new();
        private int Generation;
        private int Pending;
        private long ReceivedBytes;

        /// <summary>Whether this player is currently using a host's content.</summary>
        public bool UsingHostContent { get; private set; }


        /*********
        ** Public methods
        *********/
        public MultiplayerSync(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.ModId = manifest.UniqueID;

            helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
            helper.Events.Multiplayer.ModMessageReceived += this.OnMessageReceived;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.StopUsingHostContent();
        }

        /// <summary>As host, send the (changed) content to players who accepted it, e.g. after editing.</summary>
        public void QueueOfferToAcceptingPlayers()
        {
            if (Context.IsMultiplayer && CoreMod.Config.ShareContentAsHost && this.PlayerTokens.Count > 0)
                this.OfferQueued = true;
        }

        /// <summary>As player, ask the host for its content if 'Accept content from hosts' was just turned on.</summary>
        public void OnAcceptChanged()
        {
            if (Context.IsMultiplayer && this.HelloHandled && CoreMod.Config.AcceptContentFromHost && this.Token == null)
                this.SendJoin();
        }


        /*********
        ** Host
        *********/
        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!CoreMod.Config.ShareContentAsHost || e.Peer.IsSplitScreen || e.Peer.GetMod(this.ModId) == null)
                return;
            this.Helper.Multiplayer.SendMessage(new HelloMessage { HostName = Game1.player.Name }, HelloType, new[] { this.ModId }, new[] { e.Peer.PlayerID });
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            this.PlayerTokens.Remove(e.Peer.PlayerID);

            // whoever's content we were using has gone
            if (e.Peer.PlayerID == this.SenderId || e.Peer.IsHost && this.SenderId == 0)
                this.StopUsingHostContent();
        }

        private void OnJoin(long playerId, JoinMessage join)
        {
            if (!CoreMod.Config.ShareContentAsHost || this.PlayerTokens.ContainsKey(playerId))
                return; // the first answer per connection counts (another player can't replace the secret)

            if (join.Declined)
            {
                string name = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId)?.Name ?? "A player";
                Game1.addHUDMessage(new HUDMessage($"{name} doesn't accept shared custom content.") { noIcon = true });
                this.Monitor.Log($"{name} declined your custom content (their 'Accept content from hosts' option is off).", LogLevel.Info);
                return;
            }
            if (!IsValidToken(join.Token))
                return;

            this.PlayerTokens[playerId] = join.Token;
            this.SendOffer(playerId);
        }

        private OfferMessage BuildOffer(string token)
        {
            OfferMessage offer = new() { Token = token, HostName = Game1.player.Name };
            foreach ((IManifest mod, string folder, _, _) in ContentPacks.GetRegistrations())
            {
                Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase); // Windows/macOS players can't store names differing only in case
                foreach (string file in ContentPacks.GetSharedFiles(mod.UniqueID))
                {
                    string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    if (!AllowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) || !ContentValidator.IsSafeRelativePath(relative) || new FileInfo(file).Length > MaxFileBytes)
                        continue;
                    if (seen.TryGetValue(relative, out string? other))
                    {
                        this.Monitor.LogOnce($"{mod.Name}: '{other}' and '{relative}' differ only in capital letters, so players on Windows or macOS can only get one of them; '{relative}' isn't shared. Rename one of them to share both.", LogLevel.Warn);
                        continue;
                    }
                    seen[relative] = relative;
                    try
                    {
                        (string hash, long size) = this.GetTransferInfo(file);
                        offer.Files.Add(new OfferedFile { Mod = mod.UniqueID, Path = relative, Hash = hash, Size = size });
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log($"Couldn't share '{relative}': {ex.Message}", LogLevel.Warn);
                    }
                }
            }
            return offer;
        }

        private void SendOffer(long playerId)
        {
            if (!this.PlayerTokens.TryGetValue(playerId, out string? token))
                return;
            OfferMessage offer = this.BuildOffer(token);
            this.Helper.Multiplayer.SendMessage(offer, OfferType, new[] { this.ModId }, new[] { playerId });
            this.Monitor.Log($"Offered {offer.Files.Count} content files to player {playerId}.", LogLevel.Trace);
        }

        private void OnRequest(long playerId, RequestMessage request)
        {
            if (!CoreMod.Config.ShareContentAsHost || !this.PlayerTokens.TryGetValue(playerId, out string? token) || !TokensEqual(token, request.Token))
            {
                this.Monitor.Log($"Ignored a content request for player {playerId} without their secret.", LogLevel.Trace);
                return;
            }

            // one request at a time per player, so a player can't make the host send everything over and over
            if (this.Outgoing.Any(o => o.Player == playerId))
            {
                this.Monitor.Log($"Ignored a content request from player {playerId}: still sending the previous one.", LogLevel.Trace);
                return;
            }

            OfferMessage offer = this.BuildOffer(token);
            int queued = 0;
            foreach (string key in request.Files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                OfferedFile? file = offer.Files.FirstOrDefault(f => $"{f.Mod}/{f.Path}".Equals(key, StringComparison.OrdinalIgnoreCase));
                (IManifest Mod, string Folder, string[] Paths, Action Reload) registration = ContentPacks.GetRegistrations().FirstOrDefault(r => r.Mod.UniqueID == file?.Mod);
                if (file == null || registration.Mod == null)
                    continue; // only files we offered

                string path = Path.Combine(registration.Folder, file.Path);
                if (!ContentValidator.IsInsideFolder(path, registration.Folder))
                    continue;
                byte[] bytes = GetTransferBytes(path);
                int count = Math.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
                if ((queued += count) > MaxQueuedChunksPerPlayer)
                    break;
                for (int i = 0; i < count; i++)
                {
                    int length = Math.Min(ChunkBytes, bytes.Length - i * ChunkBytes);
                    this.Outgoing.Enqueue((playerId, new ChunkMessage
                    {
                        Token = token,
                        Mod = file.Mod,
                        Path = file.Path,
                        Index = i,
                        Count = count,
                        Data = Convert.ToBase64String(bytes, i * ChunkBytes, Math.Max(0, length))
                    }));
                }
            }
        }

        /// <summary>Get the bytes to send for a file: images are always sent as PNG (players only accept PNG data).</summary>
        private static byte[] GetTransferBytes(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg"
                ? SafePng.Encode(ImageProcessor.Decode(path)) // our own file, so the game's decoder is fine here
                : File.ReadAllBytes(path);
        }

        /// <summary>Get the hash and size of what would be sent for a file (cached, since converting a JPEG takes a moment).</summary>
        private (string Hash, long Size) GetTransferInfo(string path)
        {
            FileInfo info = new(path);
            if (this.TransferInfoCache.TryGetValue(path, out var cached) && cached.Modified == info.LastWriteTimeUtc && cached.Length == info.Length)
                return (cached.Hash, cached.Size);
            byte[] bytes = GetTransferBytes(path);
            string hash = HashBytes(bytes);
            this.TransferInfoCache[path] = (info.LastWriteTimeUtc, info.Length, hash, bytes.Length);
            return (hash, bytes.Length);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (this.Sanitizing.Count > 0)
                this.SaveSanitizedFiles();

            if (this.OfferQueued && e.IsMultipleOf(30))
            {
                this.OfferQueued = false;
                foreach (long player in this.PlayerTokens.Keys.ToArray())
                    this.SendOffer(player);
            }

            for (int i = 0; i < ChunksPerTick && this.Outgoing.Count > 0; i++)
            {
                (long player, ChunkMessage chunk) = this.Outgoing.Dequeue();
                this.Helper.Multiplayer.SendMessage(chunk, ChunkType, new[] { this.ModId }, new[] { player });
            }
        }


        /*********
        ** Player
        *********/
        private void OnHello(long fromPlayer, HelloMessage hello)
        {
            // one source at a time: the first player who offers is the one we answer, and our secret only goes to them
            if (this.HelloHandled)
                return;
            this.HelloHandled = true;
            this.SenderId = fromPlayer;
            this.HostName = CleanName(hello.HostName);

            if (!CoreMod.Config.AcceptContentFromHost)
            {
                this.SendToSender(new JoinMessage { Declined = true }, JoinType);
                Game1.addHUDMessage(new HUDMessage($"{this.HostName} is sharing custom content. Turn on 'Accept shared content' (K) to see it.") { noIcon = true });
                this.Monitor.Log($"{this.HostName} offered custom content, but 'Accept shared content' is off; declined.", LogLevel.Info);
                return;
            }
            this.SendJoin();
        }

        /// <summary>Make a new secret for this game and send it to the host.</summary>
        private void SendJoin()
        {
            this.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            this.SendToSender(new JoinMessage { Token = this.Token }, JoinType);
        }

        private void OnOffer(OfferMessage offer)
        {
            if (!CoreMod.Config.AcceptContentFromHost || this.Token == null || !TokensEqual(this.Token, offer.Token))
            {
                this.Monitor.Log("Ignored a content offer without the secret (not from the host).", LogLevel.Trace);
                return;
            }

            // validate the offer
            if (offer.Files.Count > MaxFiles)
            {
                this.Monitor.Log($"The host offered {offer.Files.Count} files, more than the {MaxFiles} limit; ignored.", LogLevel.Warn);
                return;
            }
            offer.HostName = CleanName(offer.HostName);
            long total = 0;
            List<OfferedFile> valid = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (OfferedFile file in offer.Files)
            {
                bool isJson = file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (!this.IsSafe(file.Mod, file.Path) || file.Size <= 0 || file.Size > (isJson ? ContentValidator.MaxJsonBytes : MaxFileBytes) || !IsValidHash(file.Hash) || !seen.Add($"{file.Mod}/{file.Path}"))
                {
                    this.Monitor.Log($"Ignored offered file '{file.Mod}/{file.Path}' (not allowed).", LogLevel.Trace);
                    continue;
                }
                total += file.Size;
                valid.Add(file);
            }
            if (total > MaxTotalBytes)
            {
                this.Monitor.Log($"The host offered {total / 1024 / 1024} MB of content, more than the {MaxTotalBytes / 1024 / 1024} MB limit; ignored.", LogLevel.Warn);
                return;
            }
            offer.Files = valid;
            this.CurrentOffer = offer;
            this.LoadIndex();

            // request what's missing or changed (the cache has rebuilt files, so compare with the hash the host sent before)
            List<string> needed = valid
                .Where(f => !File.Exists(this.GetCachePath(f.Mod, f.Path)) || !this.Index.TryGetValue($"{f.Mod}/{f.Path}", out string? hash) || hash != f.Hash)
                .Select(f => $"{f.Mod}/{f.Path}")
                .ToList();
            this.Incoming.Clear();
            this.Generation++;
            this.ReceivedBytes = 0;
            this.Pending = needed.Count;
            this.SendToSender(new RequestMessage { Token = this.Token, Files = needed }, RequestType);
            this.Monitor.Log($"The host offered {valid.Count} content files; downloading {needed.Count}.", LogLevel.Info);
            if (needed.Count > 0)
                Game1.addHUDMessage(new HUDMessage($"Downloading {offer.HostName}'s custom content ({needed.Count} files)...") { noIcon = true });
            else
                this.UseHostContent();
        }

        private void OnChunk(ChunkMessage chunk)
        {
            if (this.CurrentOffer == null || this.Token == null || !TokensEqual(this.Token, chunk.Token))
                return;
            OfferedFile? file = this.CurrentOffer.Files.FirstOrDefault(f => f.Mod.Equals(chunk.Mod, StringComparison.OrdinalIgnoreCase) && f.Path.Equals(chunk.Path, StringComparison.OrdinalIgnoreCase));
            long size = file?.Size ?? 0;
            int expectedCount = (int)Math.Max(1, (size + ChunkBytes - 1) / ChunkBytes);
            if (file == null || chunk.Count != expectedCount || chunk.Index < 0 || chunk.Index >= chunk.Count || chunk.Data.Length > (ChunkBytes / 3 + 1) * 4)
                return;

            byte[] data = Convert.FromBase64String(chunk.Data);
            if (data.Length > ChunkBytes || (this.ReceivedBytes += data.Length) > MaxTotalBytes)
            {
                this.Monitor.Log("Received more content data than allowed; stopped accepting it.", LogLevel.Warn);
                this.CurrentOffer = null;
                this.Incoming.Clear();
                return;
            }

            string key = $"{file.Mod}/{file.Path}";
            if (!this.Incoming.TryGetValue(key, out var parts) || parts.Count != chunk.Count)
                this.Incoming[key] = parts = (chunk.Count, new byte[chunk.Count][]);
            if (parts.Parts[chunk.Index] != null)
                return; // duplicate
            parts.Parts[chunk.Index] = data;
            if (parts.Parts.Any(p => p == null))
                return;

            // file complete: check its size and checksum, then rebuild it from scratch before saving
            byte[] bytes = parts.Parts.SelectMany(p => p!).ToArray();
            this.Incoming.Remove(key);
            if (bytes.Length != file.Size || HashBytes(bytes) != file.Hash)
            {
                this.Monitor.Log($"Received '{key}' but its size or checksum doesn't match; ignored.", LogLevel.Warn);
                if (--this.Pending <= 0)
                    this.UseHostContent();
                return;
            }
            // rebuilding a big image takes a moment, so do it in the background (see OnUpdateTicked)
            string extension = Path.GetExtension(file.Path).ToLowerInvariant();
            this.Sanitizing.Add((this.Generation, key, file, Task.Run(() =>
            {
                bool ok = ContentValidator.TrySanitize(extension, bytes, out byte[] clean, out string error);
                return (ok, clean, error);
            })));
        }

        /// <summary>Store the received files that finished being rebuilt.</summary>
        private void SaveSanitizedFiles()
        {
            for (int i = 0; i < this.Sanitizing.Count; i++)
            {
                (int generation, string key, OfferedFile file, Task<(bool Ok, byte[] Clean, string Error)> task) = this.Sanitizing[i];
                if (!task.IsCompleted)
                    continue;
                this.Sanitizing.RemoveAt(i--);
                if (generation != this.Generation || this.CurrentOffer == null)
                    continue; // left that game meanwhile

                (bool ok, byte[] clean, string error) = task.IsCompletedSuccessfully ? task.Result : (false, Array.Empty<byte>(), "couldn't be processed");
                if (ok)
                {
                    string path = this.GetCachePath(file.Mod, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, clean);
                    this.Index[key] = file.Hash;
                    this.SaveIndex();
                }
                else
                    this.Monitor.Log($"Received '{key}' but rejected it: {error}.", LogLevel.Warn);

                if (--this.Pending <= 0)
                    this.UseHostContent();
            }
        }

        /// <summary>Switch the mods to the host's cached content.</summary>
        private void UseHostContent()
        {
            if (this.CurrentOffer == null)
                return;

            foreach ((IManifest mod, _, string[] paths, Action reload) in ContentPacks.GetRegistrations())
            {
                string root = this.GetCacheRoot(mod.UniqueID);
                Directory.CreateDirectory(root);

                // remove cached files the host no longer has (or that were rejected)
                HashSet<string> offered = this.CurrentOffer.Files.Where(f => f.Mod == mod.UniqueID).Select(f => Path.GetFullPath(this.GetCachePath(f.Mod, f.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray())
                    if (!offered.Contains(Path.GetFullPath(file)))
                        File.Delete(file);

                ContentPacks.SetContentRoot(mod.UniqueID, root);
                this.SafeReload(mod, reload);
            }
            this.PruneCache();
            this.UsingHostContent = true;
            Game1.addHUDMessage(new HUDMessage($"Using {this.CurrentOffer.HostName}'s custom content.") { noIcon = true });
            this.Monitor.Log($"Using the host's custom content ({this.CurrentOffer.Files.Count} files).", LogLevel.Info);
        }

        /// <summary>Switch back to this player's own content.</summary>
        public void StopUsingHostContent()
        {
            this.CurrentOffer = null;
            this.Generation++;
            this.Token = null;
            this.HelloHandled = false;
            this.SenderId = 0;
            this.Incoming.Clear();
            this.Outgoing.Clear();
            this.PlayerTokens.Clear();
            if (!this.UsingHostContent)
                return;

            this.UsingHostContent = false;
            foreach ((IManifest mod, _, _, Action reload) in ContentPacks.GetRegistrations())
            {
                ContentPacks.SetContentRoot(mod.UniqueID, null);
                this.SafeReload(mod, reload);
            }
            this.Monitor.Log("Switched back to your own custom content.", LogLevel.Info);
        }


        /*********
        ** Shared
        *********/
        private void OnMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != this.ModId)
                return;
            try
            {
                // note: FromPlayerID can be forged, and messages between players pass through the host; the secret is what
                // proves an offer belongs to the player we answered, and everything received is rebuilt before it's used
                bool fromOurSender = this.SenderId != 0 && e.FromPlayerID == this.SenderId;
                switch (e.Type)
                {
                    case HelloType:
                        this.OnHello(e.FromPlayerID, e.ReadAs<HelloMessage>());
                        break;
                    case OfferType when fromOurSender:
                        this.OnOffer(e.ReadAs<OfferMessage>());
                        break;
                    case ChunkType when fromOurSender:
                        this.OnChunk(e.ReadAs<ChunkMessage>());
                        break;
                    case JoinType when CoreMod.Config.ShareContentAsHost:
                        this.OnJoin(e.FromPlayerID, e.ReadAs<JoinMessage>());
                        break;
                    case RequestType when CoreMod.Config.ShareContentAsHost:
                        this.OnRequest(e.FromPlayerID, e.ReadAs<RequestMessage>());
                        break;
                    default:
                        this.Monitor.Log($"Ignored a '{e.Type}' content message from player {e.FromPlayerID} (not allowed from them).", LogLevel.Trace);
                        break;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't handle a content sync message: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Send a message to the one player whose content we're taking, and to nobody else.</summary>
        private void SendToSender<T>(T message, string type)
        {
            long id = this.SenderId != 0 ? this.SenderId : Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
            if (id != 0)
                this.Helper.Multiplayer.SendMessage(message, type, new[] { this.ModId }, new[] { id });
        }

        /// <summary>Reload a mod's content, so one mod failing never leaves the others half-switched.</summary>
        private void SafeReload(IManifest mod, Action reload)
        {
            try
            {
                reload();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"{mod.Name} couldn't reload its content: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Whether a mod/path from the host is safe to store: a registered mod, one of its content paths, an allowed file type, and no path tricks.</summary>
        private bool IsSafe(string modId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(modId) || !ContentValidator.IsSafeRelativePath(relativePath) || Path.IsPathRooted(relativePath))
                return false;
            if (!AllowedExtensions.Contains(Path.GetExtension(relativePath).ToLowerInvariant()) || !ContentPacks.IsContentPath(modId, relativePath))
                return false;
            string root = Path.GetFullPath(this.GetCacheRoot(modId)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(this.GetCachePath(modId, relativePath)).StartsWith(root, StringComparison.Ordinal);
        }

        private string GetHostFolder()
        {
            string host = new string((this.HostName + "_" + this.SenderId).Select(ch => IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
            return Path.Combine(this.Helper.DirectoryPath, "host-content", host);
        }

        private string GetCacheRoot(string modId)
        {
            return Path.Combine(this.GetHostFolder(), new string(modId.Select(ch => IsAsciiLetterOrDigit(ch) || ch is '.' or '_' ? ch : '_').ToArray()));
        }

        private string GetCachePath(string modId, string relativePath)
        {
            return Path.Combine(this.GetCacheRoot(modId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Load which host version (hash) each cached file came from.</summary>
        private void LoadIndex()
        {
            this.Index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(this.GetHostFolder(), IndexFile);
                if (File.Exists(path) && JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) is { } index)
                    this.Index = new Dictionary<string, string>(index, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // re-download everything
            }
        }

        private void SaveIndex()
        {
            Directory.CreateDirectory(this.GetHostFolder());
            File.WriteAllText(Path.Combine(this.GetHostFolder(), IndexFile), JsonConvert.SerializeObject(this.Index));
        }

        /// <summary>Delete the content of older hosts, keeping the most recent few.</summary>
        private void PruneCache()
        {
            try
            {
                DirectoryInfo root = new(Path.Combine(this.Helper.DirectoryPath, "host-content"));
                string current = Path.GetFullPath(this.GetHostFolder());
                foreach (DirectoryInfo old in root.EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Skip(MaxCachedHosts))
                {
                    if (Path.GetFullPath(old.FullName) != current && old.LinkTarget == null)
                        old.Delete(recursive: true);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't clean up old host content: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>A host name that's safe to show: printable characters only, limited length.</summary>
        private static string CleanName(string? name)
        {
            string clean = new string((name ?? "").Where(ch => !char.IsControl(ch)).Take(32).ToArray()).Trim();
            return clean.Length > 0 ? clean : "The host";
        }

        private static bool IsAsciiLetterOrDigit(char ch) => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

        private static bool IsValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);

        private static bool IsValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

        private static bool TokensEqual(string expected, string? actual)
        {
            return actual != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
        }

        private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    }
}
