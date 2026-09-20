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

        /// <summary>Another player whose shared content we're taking.</summary>
        private sealed class Source
        {
            public long PlayerId;

            /// <summary>Their name, shown next to their items. It can change while they're still making their character.</summary>
            public string Name = "";

            /// <summary>The folder their files are kept in. Fixed when we first answer them, so a changing name can't orphan what we downloaded.</summary>
            public string Folder = "";

            /// <summary>The secret we made for them; only messages carrying it count as theirs.</summary>
            public string? Token;

            public OfferMessage? Offer;
            public readonly Dictionary<string, (int Count, byte[]?[] Parts)> Incoming = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Index = new(StringComparer.OrdinalIgnoreCase);
            public int Pending;
            public long ReceivedBytes;

            /// <summary>Whether their content is being shown (so it's only announced once).</summary>
            public bool InUse;
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

        /// <summary>The players we've told that we share content, so they're only told once.</summary>
        private readonly HashSet<long> Greeted = new();

        /// <summary>The players still to be told, once we're in the game and have a name to show.</summary>
        private readonly HashSet<long> ToGreet = new();

        // player: everyone whose content we're showing next to our own, by player ID
        private readonly Dictionary<long, Source> Sources = new();

        /// <summary>The players we turned down because 'Accept shared content' was off, so they can be answered if it's switched on.</summary>
        private readonly HashSet<long> Declined = new();

        private readonly List<(int Generation, long Player, string Key, OfferedFile File, Task<(bool Ok, byte[] Clean, string Error)> Task)> Sanitizing = new();
        private int Generation;

        /// <summary>Whether other players' content is currently mixed in with ours.</summary>
        public bool UsingPeerContent { get; private set; }


        /*********
        ** Public methods
        *********/
        public MultiplayerSync(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.ModId = manifest.UniqueID;

            helper.Events.Multiplayer.PeerContextReceived += this.OnPeerContextReceived;
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

        /// <summary>Tell everyone we share content, after 'Share my content' was turned on in a game we're already in.</summary>
        public void OnShareChanged()
        {
            if (!Context.IsMultiplayer)
                return;

            if (CoreMod.Config.ShareContentAsHost)
            {
                foreach (IMultiplayerPeer peer in this.Helper.Multiplayer.GetConnectedPlayers())
                    this.WillGreet(peer);
            }
            else
                this.ToGreet.Clear();
        }

        /// <summary>Answer the players we turned down, after 'Accept shared content' was switched on.</summary>
        public void OnAcceptChanged()
        {
            if (!Context.IsMultiplayer || !CoreMod.Config.AcceptContentFromHost)
                return;
            foreach (long playerId in this.Declined.ToArray())
            {
                this.Declined.Remove(playerId);
                this.OnHello(playerId, new HelloMessage());
            }
        }


        /*********
        ** Host
        *********/
        /// <summary>A player we host has joined. (As a player, we hear about the host through <see cref="OnPeerContextReceived"/> instead.)</summary>
        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            this.WillGreet(e.Peer);
        }

        /// <summary>We've learned who a player is. This is the only notice a player gets about the host, so the greeting starts here too.</summary>
        private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
        {
            this.WillGreet(e.Peer);
        }

        /// <summary>Note that a player should be told we share content, once we're in the game.</summary>
        private void WillGreet(IMultiplayerPeer peer)
        {
            if (peer.IsSplitScreen || peer.GetMod(this.ModId) == null || this.Greeted.Contains(peer.PlayerID))
                return;
            this.ToGreet.Add(peer.PlayerID);
        }

        /// <summary>Tell the players who are waiting for it that we share content.</summary>
        private void SendGreetings()
        {
            // wait until the player is made and named, so the other side can say who's sharing (the name is half-typed while the
            // character is still being created, and that's the name they'd remember)
            if (!CoreMod.Config.ShareContentAsHost || !Context.IsWorldReady || string.IsNullOrWhiteSpace(Game1.player?.Name) || Game1.activeClickableMenu is StardewValley.Menus.CharacterCustomization)
                return;

            foreach (long playerId in this.ToGreet.ToArray())
            {
                if (this.Helper.Multiplayer.GetConnectedPlayer(playerId) == null)
                {
                    this.ToGreet.Remove(playerId);
                    continue;
                }
                this.Helper.Multiplayer.SendMessage(new HelloMessage { HostName = Game1.player.Name }, HelloType, new[] { this.ModId }, new[] { playerId });
                this.Greeted.Add(playerId);
                this.ToGreet.Remove(playerId);
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            this.PlayerTokens.Remove(e.Peer.PlayerID);
            this.Greeted.Remove(e.Peer.PlayerID);
            this.ToGreet.Remove(e.Peer.PlayerID);
            this.Declined.Remove(e.Peer.PlayerID);

            // a player whose content we were showing has gone
            if (this.Sources.Remove(e.Peer.PlayerID))
                this.ApplyContent();
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
            if (this.ToGreet.Count > 0 && e.IsMultipleOf(15))
                this.SendGreetings();

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
        /// <summary>A player says they share their content: answer with a secret of our own, so we can tell their offer from anyone else's.</summary>
        private void OnHello(long fromPlayer, HelloMessage hello)
        {
            if (this.Sources.ContainsKey(fromPlayer))
                return; // already answered them

            string name = this.NameOf(fromPlayer, hello.HostName);
            if (!CoreMod.Config.AcceptContentFromHost)
            {
                this.Declined.Add(fromPlayer);
                this.SendTo(fromPlayer, new JoinMessage { Declined = true }, JoinType);
                Game1.addHUDMessage(new HUDMessage($"{name} is sharing custom content. Turn on 'Accept shared content' (K) to see it.") { noIcon = true });
                this.Monitor.Log($"{name} offered custom content, but 'Accept shared content' is off; declined.", LogLevel.Info);
                return;
            }

            Source source = new() { PlayerId = fromPlayer, Name = name, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
            this.Sources[fromPlayer] = source;
            this.SendTo(fromPlayer, new JoinMessage { Token = source.Token }, JoinType);
        }

        private void OnOffer(long fromPlayer, OfferMessage offer)
        {
            if (!CoreMod.Config.AcceptContentFromHost || !this.Sources.TryGetValue(fromPlayer, out Source? source) || source.Token == null || !TokensEqual(source.Token, offer.Token))
            {
                this.Monitor.Log("Ignored a content offer without the secret (not from a player we answered).", LogLevel.Trace);
                return;
            }

            // validate the offer
            if (offer.Files.Count > MaxFiles)
            {
                this.Monitor.Log($"{source.Name} offered {offer.Files.Count} files, more than the {MaxFiles} limit; ignored.", LogLevel.Warn);
                return;
            }
            source.Name = this.NameOf(fromPlayer, offer.HostName);
            offer.HostName = source.Name;
            long total = 0;
            List<OfferedFile> valid = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (OfferedFile file in offer.Files)
            {
                bool isJson = file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (!this.IsSafe(source, file.Mod, file.Path) || file.Size <= 0 || file.Size > (isJson ? ContentValidator.MaxJsonBytes : MaxFileBytes) || !IsValidHash(file.Hash) || !seen.Add($"{file.Mod}/{file.Path}"))
                {
                    this.Monitor.Log($"Ignored offered file '{file.Mod}/{file.Path}' (not allowed).", LogLevel.Trace);
                    continue;
                }
                total += file.Size;
                valid.Add(file);
            }
            if (total > MaxTotalBytes)
            {
                this.Monitor.Log($"{source.Name} offered {total / 1024 / 1024} MB of content, more than the {MaxTotalBytes / 1024 / 1024} MB limit; ignored.", LogLevel.Warn);
                return;
            }
            if (valid.Count == 0)
            {
                this.Monitor.Log($"{source.Name} shared no custom content.", LogLevel.Info);
                return;
            }
            offer.Files = valid;
            source.Offer = offer;
            this.LoadIndex(source);

            // request what's missing or changed (the cache has rebuilt files, so compare with the hash they sent before)
            List<string> needed = valid
                .Where(f => !File.Exists(this.GetCachePath(source, f.Mod, f.Path)) || !source.Index.TryGetValue($"{f.Mod}/{f.Path}", out string? hash) || hash != f.Hash)
                .Select(f => $"{f.Mod}/{f.Path}")
                .ToList();
            source.Incoming.Clear();
            this.Generation++;
            source.ReceivedBytes = 0;
            source.Pending = needed.Count;
            this.SendTo(fromPlayer, new RequestMessage { Token = source.Token, Files = needed }, RequestType);
            this.Monitor.Log($"{source.Name} offered {valid.Count} content files; downloading {needed.Count}.", LogLevel.Info);
            if (needed.Count > 0)
                Game1.addHUDMessage(new HUDMessage($"Downloading {source.Name}'s custom content ({needed.Count} files)...") { noIcon = true });
            else
                this.ApplyContent();
        }

        private void OnChunk(long fromPlayer, ChunkMessage chunk)
        {
            if (!this.Sources.TryGetValue(fromPlayer, out Source? source) || source.Offer == null || source.Token == null || !TokensEqual(source.Token, chunk.Token))
                return;
            OfferedFile? file = source.Offer.Files.FirstOrDefault(f => f.Mod.Equals(chunk.Mod, StringComparison.OrdinalIgnoreCase) && f.Path.Equals(chunk.Path, StringComparison.OrdinalIgnoreCase));
            long size = file?.Size ?? 0;
            int expectedCount = (int)Math.Max(1, (size + ChunkBytes - 1) / ChunkBytes);
            if (file == null || chunk.Count != expectedCount || chunk.Index < 0 || chunk.Index >= chunk.Count || chunk.Data.Length > (ChunkBytes / 3 + 1) * 4)
                return;

            byte[] data = Convert.FromBase64String(chunk.Data);
            if (data.Length > ChunkBytes || (source.ReceivedBytes += data.Length) > MaxTotalBytes)
            {
                this.Monitor.Log($"{source.Name} sent more content data than allowed; stopped accepting it.", LogLevel.Warn);
                source.Offer = null;
                source.Incoming.Clear();
                return;
            }

            string key = $"{file.Mod}/{file.Path}";
            if (!source.Incoming.TryGetValue(key, out var parts) || parts.Count != chunk.Count)
                source.Incoming[key] = parts = (chunk.Count, new byte[chunk.Count][]);
            if (parts.Parts[chunk.Index] != null)
                return; // duplicate
            parts.Parts[chunk.Index] = data;
            if (parts.Parts.Any(p => p == null))
                return;

            // file complete: check its size and checksum, then rebuild it from scratch before saving
            byte[] bytes = parts.Parts.SelectMany(p => p!).ToArray();
            source.Incoming.Remove(key);
            if (bytes.Length != file.Size || HashBytes(bytes) != file.Hash)
            {
                this.Monitor.Log($"Received '{key}' from {source.Name} but its size or checksum doesn't match; ignored.", LogLevel.Warn);
                if (--source.Pending <= 0)
                    this.ApplyContent();
                return;
            }
            // rebuilding a big image takes a moment, so do it in the background (see OnUpdateTicked)
            string extension = Path.GetExtension(file.Path).ToLowerInvariant();
            this.Sanitizing.Add((this.Generation, fromPlayer, key, file, Task.Run(() =>
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
                (int generation, long playerId, string key, OfferedFile file, Task<(bool Ok, byte[] Clean, string Error)> task) = this.Sanitizing[i];
                if (!task.IsCompleted)
                    continue;
                this.Sanitizing.RemoveAt(i--);
                if (generation != this.Generation || !this.Sources.TryGetValue(playerId, out Source? source) || source.Offer == null)
                    continue; // left that game or stopped taking their content meanwhile

                (bool ok, byte[] clean, string error) = task.IsCompletedSuccessfully ? task.Result : (false, Array.Empty<byte>(), "couldn't be processed");
                if (ok)
                {
                    string path = this.GetCachePath(source, file.Mod, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, clean);
                    source.Index[key] = file.Hash;
                    this.SaveIndex(source);
                }
                else
                    this.Monitor.Log($"Received '{key}' from {source.Name} but rejected it: {error}.", LogLevel.Warn);

                if (--source.Pending <= 0)
                    this.ApplyContent();
            }
        }

        /// <summary>Show every ready player's content next to our own.</summary>
        private void ApplyContent()
        {
            List<Source> ready = this.Sources.Values.Where(s => s.Offer != null && s.Pending <= 0).ToList();
            foreach ((IManifest mod, _, _, Action reload) in ContentPacks.GetRegistrations())
            {
                List<ContentPacks.ContentSource> peers = new();
                foreach (Source source in ready)
                {
                    string root = this.GetCacheRoot(source, mod.UniqueID);
                    if (!Directory.Exists(root))
                        continue; // they share nothing for this mod

                    // remove cached files they no longer have (or that were rejected)
                    HashSet<string> offered = source.Offer!.Files.Where(f => f.Mod == mod.UniqueID).Select(f => Path.GetFullPath(this.GetCachePath(source, f.Mod, f.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray())
                    {
                        if (!offered.Contains(Path.GetFullPath(file)))
                            File.Delete(file);
                    }
                    peers.Add(new ContentPacks.ContentSource(source.PlayerId, source.Name, root, IsOwn: false));
                }
                ContentPacks.SetPeerSources(mod.UniqueID, peers);
                this.SafeReload(mod, reload);
            }

            foreach (Source source in ready)
                source.Name = this.NameOf(source.PlayerId, source.Name); // they may have been mid-way through naming themselves when they first said hello

            foreach (Source source in ready.Where(s => !s.InUse))
            {
                source.InUse = true;
                Game1.addHUDMessage(new HUDMessage($"Showing {source.Name}'s custom content too.") { noIcon = true });
                this.Monitor.Log($"Showing {source.Name}'s custom content ({source.Offer!.Files.Count} files) next to your own.", LogLevel.Info);
            }
            this.UsingPeerContent = ready.Any(s => s.InUse);
            this.PruneCache();
        }

        /// <summary>Show only our own content again (we left the game, or the players sharing it went).</summary>
        public void StopUsingHostContent()
        {
            this.Generation++;
            this.Sources.Clear();
            this.Declined.Clear();
            this.Sanitizing.Clear();
            this.Outgoing.Clear();
            this.PlayerTokens.Clear();
            this.Greeted.Clear();
            this.ToGreet.Clear();
            if (!this.UsingPeerContent)
                return;

            this.UsingPeerContent = false;
            foreach ((IManifest mod, _, _, Action reload) in ContentPacks.GetRegistrations())
            {
                ContentPacks.SetPeerSources(mod.UniqueID, Array.Empty<ContentPacks.ContentSource>());
                this.SafeReload(mod, reload);
            }
            this.Monitor.Log("Showing only your own custom content again.", LogLevel.Info);
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
                switch (e.Type)
                {
                    case HelloType:
                        this.OnHello(e.FromPlayerID, e.ReadAs<HelloMessage>());
                        break;
                    case OfferType:
                        this.OnOffer(e.FromPlayerID, e.ReadAs<OfferMessage>());
                        break;
                    case ChunkType:
                        this.OnChunk(e.FromPlayerID, e.ReadAs<ChunkMessage>());
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

        /// <summary>Send a message to one player, and to nobody else.</summary>
        private void SendTo<T>(long playerId, T message, string type)
        {
            if (playerId != 0)
                this.Helper.Multiplayer.SendMessage(message, type, new[] { this.ModId }, new[] { playerId });
        }

        /// <summary>Reload a mod's content, so one mod failing never leaves the others half-switched.</summary>
        private void SafeReload(IManifest mod, Action reload)
        {
            try
            {
                reload();
                ContentPacks.NotifyReloaded();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"{mod.Name} couldn't reload its content: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>Whether a mod/path from the host is safe to store: a registered mod, one of its content paths, an allowed file type, and no path tricks.</summary>
        private bool IsSafe(Source source, string modId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(modId) || !ContentValidator.IsSafeRelativePath(relativePath) || Path.IsPathRooted(relativePath))
                return false;
            if (!AllowedExtensions.Contains(Path.GetExtension(relativePath).ToLowerInvariant()) || !ContentPacks.IsContentPath(modId, relativePath))
                return false;
            string root = Path.GetFullPath(this.GetCacheRoot(source, modId)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(this.GetCachePath(source, modId, relativePath)).StartsWith(root, StringComparison.Ordinal);
        }

        /// <summary>The folder holding one player's shared content.</summary>
        private string GetPlayerFolder(Source source)
        {
            if (source.Folder.Length == 0)
            {
                string name = new string((source.Name + "_" + source.PlayerId).Select(ch => IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
                source.Folder = Path.Combine(this.Helper.DirectoryPath, "host-content", name);
            }
            return source.Folder;
        }

        private string GetCacheRoot(Source source, string modId)
        {
            return Path.Combine(this.GetPlayerFolder(source), new string(modId.Select(ch => IsAsciiLetterOrDigit(ch) || ch is '.' or '_' ? ch : '_').ToArray()));
        }

        private string GetCachePath(Source source, string modId, string relativePath)
        {
            return Path.Combine(this.GetCacheRoot(source, modId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Load which version (hash) of each cached file we have from a player.</summary>
        private void LoadIndex(Source source)
        {
            source.Index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(this.GetPlayerFolder(source), IndexFile);
                if (File.Exists(path) && JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) is { } index)
                    source.Index = new Dictionary<string, string>(index, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // re-download everything
            }
        }

        private void SaveIndex(Source source)
        {
            Directory.CreateDirectory(this.GetPlayerFolder(source));
            File.WriteAllText(Path.Combine(this.GetPlayerFolder(source), IndexFile), JsonConvert.SerializeObject(source.Index));
        }

        /// <summary>Delete the content of older hosts, keeping the most recent few.</summary>
        private void PruneCache()
        {
            try
            {
                DirectoryInfo root = new(Path.Combine(this.Helper.DirectoryPath, "host-content"));
                HashSet<string> current = this.Sources.Values.Select(s => Path.GetFullPath(this.GetPlayerFolder(s))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (DirectoryInfo old in root.EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Skip(Math.Max(MaxCachedHosts, current.Count)))
                {
                    if (!current.Contains(Path.GetFullPath(old.FullName)) && old.LinkTarget == null)
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
            return clean.Length > 0 ? clean : "Another player";
        }

        /// <summary>The name to show for the player we're taking content from: the game's own name for them, or the one they sent.</summary>
        private string NameOf(long playerId, string? sentName)
        {
            string? known = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId)?.Name;
            return CleanName(!string.IsNullOrWhiteSpace(known) ? known : sentName);
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
