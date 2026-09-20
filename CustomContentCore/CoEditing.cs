using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>
    /// Lets players change each other's custom content in a multiplayer game: you ask the owner for a turn on one item, edit it,
    /// and send the change back. The owner is the only one who ever writes their own files.
    /// </summary>
    /// <remarks>
    /// Security: the other side may run a modified game, so the owner checks everything again.
    /// <list type="bullet">
    ///   <item>The owner hands out a secret with the turn; only messages carrying it can change that item, and SMAPI's sender ID
    ///   (which another player can forge) is never trusted on its own.</item>
    ///   <item>One player has a turn on an item at a time, it runs out by itself, and it's dropped when they leave.</item>
    ///   <item>The owner names the files it stores, so a sent file name can't point anywhere; images are decoded and re-encoded
    ///   (<see cref="ContentValidator.TrySanitize"/>) before the mod ever sees them.</item>
    ///   <item>A change is refused if the item changed since the editor last saw it, so nobody's work is silently overwritten.</item>
    /// </list>
    /// </remarks>
    internal sealed class CoEditing
    {
        /*********
        ** Messages
        *********/
        /// <summary>Editor → owner: may I change this item?</summary>
        public sealed class TurnRequest
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
        }

        /// <summary>Owner → editor: yes (with a secret and how long the turn lasts), or no (with who has it).</summary>
        public sealed class TurnReply
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public bool Granted { get; set; }
            public string Token { get; set; } = "";
            public int Seconds { get; set; }
            public string Reason { get; set; } = "";

            /// <summary>The item's data as the owner has it now, so the editor starts from the current version.</summary>
            public string Json { get; set; } = "";
        }

        /// <summary>Editor → owner: I'm still editing / I'm done.</summary>
        public sealed class TurnUpdate
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public string Token { get; set; } = "";
            public bool Finished { get; set; }
        }

        /// <summary>A file that comes with a change.</summary>
        public sealed class EditFile
        {
            /// <summary>How the data refers to it (a file name, no folders).</summary>
            public string Name { get; set; } = "";
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }

        /// <summary>Editor → owner: here's the changed item.</summary>
        public sealed class EditSubmit
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public string Token { get; set; } = "";

            /// <summary>The item's data as it was when the editor started, so a change made meanwhile isn't lost.</summary>
            public string BaseHash { get; set; } = "";
            public string Json { get; set; } = "";
            public List<EditFile> Files { get; set; } = new();
        }

        /// <summary>Owner → editor: send me these files.</summary>
        public sealed class EditFilesWanted
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public string Token { get; set; } = "";
            public List<string> Names { get; set; } = new();
        }

        /// <summary>Editor → owner: part of a file.</summary>
        public sealed class EditChunk
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public string Token { get; set; } = "";
            public string Name { get; set; } = "";
            public int Index { get; set; }
            public int Count { get; set; }
            public string Data { get; set; } = "";
        }

        /// <summary>Owner → editor: what happened to the change.</summary>
        public sealed class EditResult
        {
            public string Mod { get; set; } = "";
            public string Item { get; set; } = "";
            public bool Applied { get; set; }
            public string Message { get; set; } = "";
        }


        /*********
        ** Types
        *********/
        /// <summary>A turn the owner handed out on one of their items.</summary>
        private sealed class Turn
        {
            public long Editor;
            public string EditorName = "";
            public string Token = "";
            public DateTime Expires;

            /// <summary>The files of the change being sent, by the name the owner gave them.</summary>
            public readonly Dictionary<string, (EditFile File, int Count, byte[]?[] Parts)> Incoming = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Where the received files are kept until the change is applied.</summary>
            public string StagingFolder = "";

            public EditSubmit? Submitted;
        }

        /// <summary>The turn this player has on someone else's item.</summary>
        private sealed class MyTurn
        {
            public long Owner;
            public string Mod = "";
            public string Item = "";
            public string Token = "";
            public DateTime Expires;
            public DateTime NextRenew;

            /// <summary>The files we offered with the change, by name.</summary>
            public Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);
        }


        /*********
        ** Fields
        *********/
        private const string TurnRequestType = "EditTurnRequest", TurnReplyType = "EditTurnReply", TurnUpdateType = "EditTurnUpdate",
            SubmitType = "EditSubmit", WantedType = "EditFilesWanted", ChunkType = "EditChunk", ResultType = "EditResult";

        /// <summary>How long a turn lasts before it runs out, and how often the editor says it's still going.</summary>
        private static readonly TimeSpan TurnLength = TimeSpan.FromSeconds(150), RenewEvery = TimeSpan.FromSeconds(45);

        private const int ChunkBytes = 48 * 1024;
        private const int MaxEditFiles = 20;

        /// <summary>How many of our items one player may have a turn on at once, so nobody can tie up everything we own.</summary>
        private const int MaxTurnsPerPlayer = 3;
        private const long MaxEditBytes = 24L * 1024 * 1024;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly string ModId;

        /// <summary>The turns handed out on our own items, by "mod|item".</summary>
        private readonly Dictionary<string, Turn> Turns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The turn we have on someone else's item, if any (one at a time, so a mistake can only affect one item).</summary>
        private MyTurn? Mine;

        /// <summary>Called with the answer to our request for a turn.</summary>
        private Action<bool, string, string>? WaitingForTurn;

        /// <summary>Called when the owner says what happened to our change.</summary>
        private Action<bool, string>? WaitingForResult;

        /// <summary>The chunks still to send, so a big image doesn't block the game.</summary>
        private readonly Queue<(long Player, EditChunk Chunk)> Outgoing = new();


        /*********
        ** Public methods
        *********/
        public CoEditing(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.ModId = manifest.UniqueID;

            helper.Events.Multiplayer.ModMessageReceived += this.OnMessageReceived;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Reset();
        }

        /// <summary>Whether someone else's item is being edited here right now.</summary>
        public bool Editing => this.Mine != null;

        /// <summary>Ask the owner of an item for a turn at changing it.</summary>
        /// <param name="mod">The mod the item belongs to.</param>
        /// <param name="ownerId">The player who owns it.</param>
        /// <param name="itemId">The item's ID in the owner's own data (without the tag that keeps their IDs apart from yours).</param>
        /// <param name="onReply">Called with whether you got the turn, the owner's version of the item (JSON), and a message to show.</param>
        public void RequestTurn(IManifest mod, long ownerId, string itemId, Action<bool, string, string> onReply)
        {
            if (this.Mine != null)
            {
                onReply(false, "", "You're already changing something of another player's; finish that first.");
                return;
            }

            this.WaitingForTurn = onReply;
            this.Mine = new MyTurn { Owner = ownerId, Mod = mod.UniqueID, Item = itemId, Expires = DateTime.UtcNow.AddSeconds(20) };
            this.Send(ownerId, new TurnRequest { Mod = mod.UniqueID, Item = itemId }, TurnRequestType);
        }

        /// <summary>Give up the turn without changing anything.</summary>
        public void EndTurn()
        {
            if (this.Mine is { } mine && mine.Token.Length > 0)
                this.Send(mine.Owner, new TurnUpdate { Mod = mine.Mod, Item = mine.Item, Token = mine.Token, Finished = true }, TurnUpdateType);
            this.Mine = null;
            this.WaitingForTurn = null;
            this.WaitingForResult = null;
        }

        /// <summary>Send the changed item to its owner, who decides whether it can be applied.</summary>
        /// <param name="json">The item's data.</param>
        /// <param name="baseJson">The item's data as it was when the turn started, so a change made meanwhile isn't overwritten.</param>
        /// <param name="files">The images the item uses, by the name the data refers to them by.</param>
        /// <param name="onResult">Called with whether it was applied and a message to show.</param>
        public void SubmitEdit(string json, string baseJson, IDictionary<string, string> files, Action<bool, string> onResult)
        {
            if (this.Mine is not { } mine || mine.Token.Length == 0)
            {
                onResult(false, "Your turn at changing that item has ended; ask for it again.");
                return;
            }
            if (files.Count > MaxEditFiles)
            {
                onResult(false, $"That change uses {files.Count} images, more than the {MaxEditFiles} allowed in one go.");
                return;
            }

            List<EditFile> list = new();
            long total = 0;
            foreach ((string name, string path) in files)
            {
                if (!File.Exists(path))
                    continue;
                byte[] bytes = File.ReadAllBytes(path);
                total += bytes.Length;
                list.Add(new EditFile { Name = Path.GetFileName(name), Hash = Hash(bytes), Size = bytes.Length });
                mine.Files[Path.GetFileName(name)] = path;
            }
            if (total > MaxEditBytes)
            {
                onResult(false, $"That change is {total / 1024 / 1024} MB, more than the {MaxEditBytes / 1024 / 1024} MB allowed in one go.");
                return;
            }

            this.WaitingForResult = onResult;
            this.Send(mine.Owner, new EditSubmit
            {
                Mod = mine.Mod,
                Item = mine.Item,
                Token = mine.Token,
                BaseHash = Hash(Encoding.UTF8.GetBytes(baseJson)),
                Json = json,
                Files = list
            }, SubmitType);
        }

        /// <summary>Who has a turn on one of our items right now, if anyone.</summary>
        public string? WhoIsEditing(string modId, string itemId)
        {
            return this.Turns.TryGetValue(Key(modId, itemId), out Turn? turn) && turn.Expires > DateTime.UtcNow
                ? turn.EditorName
                : null;
        }


        /*********
        ** Owner: handing out turns
        *********/
        private void OnTurnRequest(long fromPlayer, TurnRequest request)
        {
            string key = Key(request.Mod, request.Item);
            string name = NameOf(fromPlayer);

            if (!CoreMod.Config.LetOthersChangeMyContent || !CoreMod.Config.ShareContentAsHost)
            {
                this.Send(fromPlayer, new TurnReply { Mod = request.Mod, Item = request.Item, Reason = "They don't let other players change their content." }, TurnReplyType);
                this.Monitor.Log($"{name} asked to change your '{request.Item}', but 'Let others change my content' is off.", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage($"{name} wants to change your '{request.Item}'. Turn on 'Let others change my content' (K) to allow it.") { noIcon = true });
                return;
            }
            if (ContentPacks.GetEditing(request.Mod) is not { } editing)
            {
                this.Send(fromPlayer, new TurnReply { Mod = request.Mod, Item = request.Item, Reason = "That mod doesn't let other players change its items." }, TurnReplyType);
                return;
            }
            if (editing.GetItemJson(request.Item) is not { } json)
            {
                this.Send(fromPlayer, new TurnReply { Mod = request.Mod, Item = request.Item, Reason = "That item isn't here any more." }, TurnReplyType);
                return;
            }
            if (this.Turns.TryGetValue(key, out Turn? existing) && existing.Expires > DateTime.UtcNow && existing.Editor != fromPlayer)
            {
                this.Send(fromPlayer, new TurnReply { Mod = request.Mod, Item = request.Item, Reason = $"{existing.EditorName} is changing that right now." }, TurnReplyType);
                return;
            }
            if (this.Turns.Count(t => t.Value.Editor == fromPlayer && t.Value.Expires > DateTime.UtcNow) >= MaxTurnsPerPlayer)
            {
                this.Send(fromPlayer, new TurnReply { Mod = request.Mod, Item = request.Item, Reason = "You're already changing enough of their things; finish those first." }, TurnReplyType);
                return;
            }

            Turn turn = new()
            {
                Editor = fromPlayer,
                EditorName = name,
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Expires = DateTime.UtcNow + TurnLength
            };
            this.Turns[key] = turn;
            this.Send(fromPlayer, new TurnReply
            {
                Mod = request.Mod,
                Item = request.Item,
                Granted = true,
                Token = turn.Token,
                Seconds = (int)TurnLength.TotalSeconds,
                Json = json
            }, TurnReplyType);
            this.Monitor.Log($"{name} is changing your '{request.Item}'.", LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage($"{name} is changing your '{request.Item}'.") { noIcon = true });
        }

        private void OnTurnUpdate(long fromPlayer, TurnUpdate update)
        {
            if (this.GetTurn(fromPlayer, update.Mod, update.Item, update.Token) is not { } turn)
                return;

            if (update.Finished)
            {
                this.DropTurn(Key(update.Mod, update.Item), turn);
                return;
            }
            turn.Expires = DateTime.UtcNow + TurnLength;
        }

        private void OnEditSubmit(long fromPlayer, EditSubmit submit)
        {
            string key = Key(submit.Mod, submit.Item);
            if (this.GetTurn(fromPlayer, submit.Mod, submit.Item, submit.Token) is not { } turn)
                return;

            if (ContentPacks.GetEditing(submit.Mod) is not { } editing || editing.GetItemJson(submit.Item) is not { } current)
            {
                this.Refuse(fromPlayer, submit, "that item isn't here any more");
                this.DropTurn(key, turn);
                return;
            }
            if (Hash(Encoding.UTF8.GetBytes(current)) != submit.BaseHash)
            {
                this.Refuse(fromPlayer, submit, "it changed while you were editing, so your version wasn't used");
                this.DropTurn(key, turn);
                return;
            }
            if (submit.Json.Length > ContentValidator.MaxJsonBytes || submit.Files.Count > MaxEditFiles || submit.Files.Sum(f => f.Size) > MaxEditBytes)
            {
                this.Refuse(fromPlayer, submit, "the change is bigger than allowed");
                this.DropTurn(key, turn);
                return;
            }

            // keep the received files apart from our own content until they're checked
            turn.Submitted = submit;
            turn.Incoming.Clear();
            turn.StagingFolder = Path.Combine(this.Helper.DirectoryPath, "incoming-edits", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(turn.StagingFolder);

            List<string> wanted = new();
            foreach (EditFile file in submit.Files)
            {
                string name = Path.GetFileName(file.Name);
                if (name.Length == 0 || name.Length > 100 || !IsAllowedImage(name) || file.Size <= 0 || file.Size > MaxEditBytes || !IsHash(file.Hash) || turn.Incoming.ContainsKey(name))
                    continue;
                turn.Incoming[name] = (file, 0, Array.Empty<byte[]?>());
                wanted.Add(name);
            }
            this.Send(fromPlayer, new EditFilesWanted { Mod = submit.Mod, Item = submit.Item, Token = submit.Token, Names = wanted }, WantedType);
            if (wanted.Count == 0)
                this.ApplyEdit(key, turn);
        }

        private void OnEditChunk(long fromPlayer, EditChunk chunk)
        {
            if (this.GetTurn(fromPlayer, chunk.Mod, chunk.Item, chunk.Token) is not { } turn || turn.Submitted == null)
                return;

            string name = Path.GetFileName(chunk.Name);
            if (!turn.Incoming.TryGetValue(name, out var slot))
                return;

            int expected = (int)Math.Max(1, (slot.File.Size + ChunkBytes - 1) / ChunkBytes);
            if (chunk.Count != expected || chunk.Index < 0 || chunk.Index >= chunk.Count || chunk.Data.Length > (ChunkBytes / 3 + 1) * 4)
                return;

            byte[] data = Convert.FromBase64String(chunk.Data);
            if (data.Length > ChunkBytes)
                return;
            if (slot.Parts.Length != chunk.Count)
                slot = (slot.File, chunk.Count, new byte[chunk.Count][]);
            if (slot.Parts[chunk.Index] != null)
                return;
            slot.Parts[chunk.Index] = data;
            turn.Incoming[name] = slot;
            if (slot.Parts.Any(p => p == null))
                return;

            // the file is complete: check it against what was promised, then rebuild it from scratch before it's stored
            byte[] bytes = slot.Parts.SelectMany(p => p!).ToArray();
            if (bytes.Length != slot.File.Size || Hash(bytes) != slot.File.Hash)
            {
                this.Refuse(fromPlayer, turn.Submitted, $"'{name}' didn't arrive in one piece");
                this.DropTurn(Key(chunk.Mod, chunk.Item), turn);
                return;
            }
            if (!ContentValidator.TrySanitize(Path.GetExtension(name).ToLowerInvariant(), bytes, out byte[] clean, out string error))
            {
                this.Refuse(fromPlayer, turn.Submitted, $"'{name}' was refused: {error}");
                this.DropTurn(Key(chunk.Mod, chunk.Item), turn);
                return;
            }

            // we name the file ourselves, so nothing they send can point outside the staging folder
            File.WriteAllBytes(Path.Combine(turn.StagingFolder, SafeStagedName(name)), clean);
            turn.Incoming[name] = (slot.File, slot.Count, Array.Empty<byte[]?>()); // done: drop the parts, keep the entry
            if (turn.Incoming.Values.All(v => v.Parts.Length == 0))
                this.ApplyEdit(Key(chunk.Mod, chunk.Item), turn);
        }

        /// <summary>Give the checked change to the mod, which writes it into our own content.</summary>
        private void ApplyEdit(string key, Turn turn)
        {
            EditSubmit submit = turn.Submitted!;
            bool applied = false;
            string message;
            try
            {
                Dictionary<string, string> files = turn.Incoming.Keys.ToDictionary(
                    name => name,
                    name => Path.Combine(turn.StagingFolder, SafeStagedName(name)),
                    StringComparer.OrdinalIgnoreCase);
                applied = ContentPacks.GetEditing(submit.Mod)?.ApplyItemJson(submit.Item, submit.Json, files) == true;
                message = applied
                    ? $"{turn.EditorName} changed your '{submit.Item}'."
                    : $"{turn.EditorName}'s change to '{submit.Item}' didn't fit and wasn't used.";
            }
            catch (Exception ex)
            {
                message = $"{turn.EditorName}'s change to '{submit.Item}' couldn't be used: {ex.Message}";
            }

            this.Monitor.Log(message, applied ? LogLevel.Info : LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(message) { noIcon = true });
            this.Send(turn.Editor, new EditResult { Mod = submit.Mod, Item = submit.Item, Applied = applied, Message = applied ? "Your change was applied." : message }, ResultType);
            this.DropTurn(key, turn);

            if (applied)
                CoreMod.Sync?.QueueOfferToAcceptingPlayers(); // everyone gets the new version
        }

        private void Refuse(long playerId, EditSubmit submit, string reason)
        {
            this.Send(playerId, new EditResult { Mod = submit.Mod, Item = submit.Item, Applied = false, Message = $"Your change wasn't used: {reason}." }, ResultType);
            this.Monitor.Log($"Refused a change to '{submit.Item}' from player {playerId}: {reason}.", LogLevel.Info);
        }

        private Turn? GetTurn(long fromPlayer, string modId, string itemId, string? token)
        {
            if (!this.Turns.TryGetValue(Key(modId, itemId), out Turn? turn) || turn.Editor != fromPlayer || turn.Expires <= DateTime.UtcNow)
                return null;
            return token is { Length: 64 } && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(turn.Token), Encoding.ASCII.GetBytes(token))
                ? turn
                : null;
        }

        private void DropTurn(string key, Turn turn)
        {
            this.Turns.Remove(key);
            this.Delete(turn.StagingFolder);
        }


        /*********
        ** Editor: taking a turn
        *********/
        private void OnTurnReply(long fromPlayer, TurnReply reply)
        {
            if (this.Mine is not { } mine || mine.Owner != fromPlayer || !mine.Mod.Equals(reply.Mod, StringComparison.OrdinalIgnoreCase) || mine.Item != reply.Item)
                return;

            Action<bool, string, string>? callback = this.WaitingForTurn;
            this.WaitingForTurn = null;
            if (!reply.Granted || reply.Token is not { Length: 64 })
            {
                this.Mine = null;
                callback?.Invoke(false, "", reply.Reason.Length > 0 ? CleanText(reply.Reason) : "The owner didn't give you a turn.");
                return;
            }

            mine.Token = reply.Token;
            mine.Expires = DateTime.UtcNow.AddSeconds(Math.Clamp(reply.Seconds, 30, 600));
            mine.NextRenew = DateTime.UtcNow + RenewEvery;
            callback?.Invoke(true, reply.Json, "");
        }

        private void OnFilesWanted(long fromPlayer, EditFilesWanted wanted)
        {
            if (this.Mine is not { } mine || mine.Owner != fromPlayer || mine.Token != wanted.Token)
                return;

            foreach (string name in wanted.Names.Take(MaxEditFiles))
            {
                if (!mine.Files.TryGetValue(Path.GetFileName(name), out string? path) || !File.Exists(path))
                    continue;

                byte[] bytes = File.ReadAllBytes(path);
                int count = (int)Math.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
                for (int i = 0; i < count; i++)
                {
                    int start = i * ChunkBytes;
                    int length = Math.Min(ChunkBytes, bytes.Length - start);
                    this.Outgoing.Enqueue((fromPlayer, new EditChunk
                    {
                        Mod = mine.Mod,
                        Item = mine.Item,
                        Token = mine.Token,
                        Name = Path.GetFileName(name),
                        Index = i,
                        Count = count,
                        Data = Convert.ToBase64String(bytes, start, length)
                    }));
                }
            }
        }

        private void OnEditResult(long fromPlayer, EditResult result)
        {
            if (this.Mine is not { } mine || mine.Owner != fromPlayer)
                return;

            Action<bool, string>? callback = this.WaitingForResult;
            this.WaitingForResult = null;
            this.Mine = null;
            callback?.Invoke(result.Applied, CleanText(result.Message));
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
                switch (e.Type)
                {
                    case TurnRequestType:
                        this.OnTurnRequest(e.FromPlayerID, e.ReadAs<TurnRequest>());
                        break;
                    case TurnUpdateType:
                        this.OnTurnUpdate(e.FromPlayerID, e.ReadAs<TurnUpdate>());
                        break;
                    case SubmitType:
                        this.OnEditSubmit(e.FromPlayerID, e.ReadAs<EditSubmit>());
                        break;
                    case ChunkType:
                        this.OnEditChunk(e.FromPlayerID, e.ReadAs<EditChunk>());
                        break;
                    case TurnReplyType:
                        this.OnTurnReply(e.FromPlayerID, e.ReadAs<TurnReply>());
                        break;
                    case WantedType:
                        this.OnFilesWanted(e.FromPlayerID, e.ReadAs<EditFilesWanted>());
                        break;
                    case ResultType:
                        this.OnEditResult(e.FromPlayerID, e.ReadAs<EditResult>());
                        break;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't handle an editing message: {ex.Message}", LogLevel.Warn);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            for (int i = 0; i < 6 && this.Outgoing.Count > 0; i++)
            {
                (long player, EditChunk chunk) = this.Outgoing.Dequeue();
                this.Helper.Multiplayer.SendMessage(chunk, ChunkType, new[] { this.ModId }, new[] { player });
            }

            if (!e.IsMultipleOf(60))
                return;

            // turns that ran out (the editor left, or their game froze)
            DateTime now = DateTime.UtcNow;
            foreach ((string key, Turn turn) in this.Turns.Where(t => t.Value.Expires <= now).ToArray())
            {
                this.Monitor.Log($"{turn.EditorName}'s turn at '{key}' ran out.", LogLevel.Trace);
                this.DropTurn(key, turn);
            }

            // tell the owner we're still editing
            if (this.Mine is { } mine && mine.Token.Length > 0 && now >= mine.NextRenew)
            {
                mine.NextRenew = now + RenewEvery;
                this.Send(mine.Owner, new TurnUpdate { Mod = mine.Mod, Item = mine.Item, Token = mine.Token }, TurnUpdateType);
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            foreach ((string key, Turn turn) in this.Turns.Where(t => t.Value.Editor == e.Peer.PlayerID).ToArray())
                this.DropTurn(key, turn);
            if (this.Mine?.Owner == e.Peer.PlayerID)
                this.Reset();
        }

        /// <summary>Forget every turn, e.g. after leaving the game.</summary>
        private void Reset()
        {
            foreach ((string key, Turn turn) in this.Turns.ToArray())
                this.DropTurn(key, turn);
            this.Mine = null;
            this.WaitingForTurn = null;
            this.WaitingForResult = null;
            this.Outgoing.Clear();
            this.Delete(Path.Combine(this.Helper.DirectoryPath, "incoming-edits"));
        }

        private void Send<T>(long playerId, T message, string type)
        {
            if (playerId != 0)
                this.Helper.Multiplayer.SendMessage(message, type, new[] { this.ModId }, new[] { playerId });
        }

        private void Delete(string folder)
        {
            try
            {
                if (folder.Length > 0 && Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't clean up '{folder}': {ex.Message}", LogLevel.Trace);
            }
        }

        private static string Key(string modId, string itemId) => $"{modId}|{itemId}";

        /// <summary>A file name of our own making, so nothing sent can decide where a file lands.</summary>
        private static string SafeStagedName(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).Substring(0, 16) + (extension is ".png" or ".jpg" or ".jpeg" ? extension : ".png");
        }

        private static bool IsAllowedImage(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            return extension is ".png" or ".jpg" or ".jpeg";
        }

        private static string NameOf(long playerId)
        {
            string? name = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId)?.Name;
            return CleanText(string.IsNullOrWhiteSpace(name) ? "Another player" : name);
        }

        /// <summary>Text from another player, made safe to show: printable characters only, limited length.</summary>
        private static string CleanText(string? text)
        {
            string clean = new string((text ?? "").Where(ch => !char.IsControl(ch)).Take(120).ToArray()).Trim();
            return clean.Length > 0 ? clean : "No reason given.";
        }

        private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    }
}
