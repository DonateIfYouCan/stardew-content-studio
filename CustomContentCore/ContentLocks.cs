using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>
    /// Keeps two players from changing the same thing at once in a multiplayer game. Whoever opens something holds it until they
    /// save or close; everyone else sees who has it. The host decides, because the host's content is the one everyone is using.
    /// </summary>
    /// <remarks>
    /// A lock is a courtesy between players, not a security measure: the host checks again before writing anything a player sends.
    /// The host can take a lock back from anyone, and a lock runs out by itself if the player who holds it goes quiet.
    /// </remarks>
    internal sealed class ContentLocks
    {
        /*********
        ** Messages
        *********/
        /// <summary>Player → host: I'd like to change this.</summary>
        public sealed class LockRequest
        {
            public string Key { get; set; } = "";
            public string Label { get; set; } = "";
        }

        /// <summary>Host → player: yes, or no with who has it.</summary>
        public sealed class LockReply
        {
            public string Key { get; set; } = "";
            public bool Granted { get; set; }
            public string Holder { get; set; } = "";

            /// <summary>Why not, in words to show the player.</summary>
            public string Reason { get; set; } = "";
        }

        /// <summary>Player → host: I'm still on it / I'm done.</summary>
        public sealed class LockUpdate
        {
            public string Key { get; set; } = "";
            public bool Finished { get; set; }
        }

        /// <summary>Host → everyone: what's being changed right now.</summary>
        public sealed class LockList
        {
            public List<LockEntry> Locks { get; set; } = new();
        }

        public sealed class LockEntry
        {
            public string Key { get; set; } = "";
            public string Holder { get; set; } = "";
            public string Label { get; set; } = "";

            /// <summary>Who holds it, so each player can leave their own holds out of "someone else is changing this". Names alone can match.</summary>
            public long PlayerId { get; set; }
        }


        /*********
        ** Fields
        *********/
        private const string RequestType = "LockRequest", ReplyType = "LockReply", UpdateType = "LockUpdate", ListType = "LockList";

        /// <summary>How long a lock lasts without word from the player holding it, and how often that word is sent.</summary>
        private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3), RenewEvery = TimeSpan.FromSeconds(45);

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly string ModId;

        /// <summary>As host: who holds what, by key.</summary>
        private readonly Dictionary<string, (long Player, string Holder, string Label, DateTime Expires)> Held = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>What we're holding ourselves, and when to say we're still on it.</summary>
        private readonly Dictionary<string, DateTime> Mine = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>When we took each of those, so a list sent before we asked doesn't look like the host disagreeing.</summary>
        private readonly Dictionary<string, DateTime> MineSince = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>As host: players whose hold we took back, by key, so their next change is refused until they ask again.</summary>
        private readonly Dictionary<string, long> TakenBack = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>What the host last said is being changed, so a list can show it.</summary>
        private Dictionary<string, string> Others = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Waiting for the host's answer, by key.</summary>
        private readonly Dictionary<string, Action<bool, string>> Waiting = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>What each thing we're waiting on is called, for the message shown if the answer is no.</summary>
        private readonly Dictionary<string, string> WaitingLabels = new(StringComparer.OrdinalIgnoreCase);


        /*********
        ** Public methods
        *********/
        public ContentLocks(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.ModId = manifest.UniqueID;

            helper.Events.Multiplayer.ModMessageReceived += this.OnMessageReceived;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Reset();
        }

        /// <summary>Whether locks matter right now: only in a game where everyone is using one player's content.</summary>
        public bool InSharedGame => Context.IsMultiplayer && (CoreMod.Sync?.UsingHostContent == true || (Context.IsMainPlayer && CoreMod.Config.ShareContentAsHost));

        /// <summary>Ask to be the one changing something, so nobody else changes it at the same time.</summary>
        /// <param name="key">What's being changed, e.g. a mod's item or one of its files.</param>
        /// <param name="label">What to call it when telling someone else it's taken.</param>
        /// <param name="onReply">Called with whether it's yours and, if not, why not in words to show. Called straight away outside a shared game.</param>
        public void Take(string key, string label, Action<bool, string> onReply)
        {
            if (!this.InSharedGame)
            {
                onReply(true, "");
                return;
            }
            if (this.Mine.ContainsKey(key))
            {
                onReply(true, "");
                return;
            }

            if (Context.IsMainPlayer)
            {
                // the host decides, so there's nobody to ask
                if (this.Held.TryGetValue(key, out var held) && held.Expires > DateTime.UtcNow && held.Player != Game1.player.UniqueMultiplayerID)
                {
                    onReply(false, $"{held.Holder} is changing {label} right now.");
                    return;
                }
                this.Held[key] = (Game1.player.UniqueMultiplayerID, Game1.player.Name, label, DateTime.UtcNow + Lease);
                this.TakenBack.Remove(key);
                this.Mine[key] = DateTime.UtcNow + RenewEvery;
                this.MineSince[key] = DateTime.UtcNow;
                this.TellEveryone();
                onReply(true, "");
                return;
            }

            this.WaitingLabels[key] = label;
            this.Waiting[key] = onReply;
            this.SendToHost(new LockRequest { Key = key, Label = label }, RequestType);
        }

        /// <summary>Let go of something, so someone else can change it.</summary>
        public void Release(string key)
        {
            this.MineSince.Remove(key);
            if (!this.Mine.Remove(key))
                return;

            // the host's list still names us until its next update; without this, for that moment we'd be told we're
            // changing it ourselves, and the buttons for the thing we just let go of would stay greyed out
            this.Others.Remove(key);
            Version++;

            if (Context.IsMainPlayer)
            {
                this.Held.Remove(key);
                this.TellEveryone();
            }
            else
                this.SendToHost(new LockUpdate { Key = key, Finished = true }, UpdateType);
        }

        /// <summary>Whether we're the one holding something.</summary>
        public bool IHoldIt(string key) => this.Mine.ContainsKey(key);

        /// <summary>Goes up whenever who's holding what changes, so a screen can tell its buttons need checking again.</summary>
        /// <remarks>Rows that say who's changing something are drawn fresh each time; buttons are enabled once, so they need this.</remarks>
        public static int Version { get; private set; }

        /// <summary>Who is changing something right now, if anyone else is.</summary>
        public string? WhoHas(string key)
        {
            if (this.Mine.ContainsKey(key))
                return null;
            if (Context.IsMainPlayer)
                return this.Held.TryGetValue(key, out var held) && held.Expires > DateTime.UtcNow ? held.Holder : null;
            return this.Others.TryGetValue(key, out string? holder) ? holder : null;
        }

        /// <summary>Everything being changed right now, for the host's list.</summary>
        public IEnumerable<(string Key, string Holder, string Label)> All()
        {
            DateTime now = DateTime.UtcNow;
            return Context.IsMainPlayer
                ? this.Held.Where(l => l.Value.Expires > now).Select(l => (l.Key, l.Value.Holder, l.Value.Label)).ToArray()
                : this.Others.Select(l => (l.Key, l.Value, "")).ToArray();
        }

        /// <summary>As host: take something back from whoever is changing it (their next save is refused).</summary>
        public void ForceRelease(string key)
        {
            if (!Context.IsMainPlayer || !this.Held.TryGetValue(key, out var held))
                return;

            this.Held.Remove(key);
            if (held.Player != Game1.player.UniqueMultiplayerID)
                this.TakenBack[key] = held.Player; // their next change is refused until they ask again
            this.Mine.Remove(key);
            this.MineSince.Remove(key);
            this.TellEveryone();
            this.Monitor.Log($"You took back '{key}'; whoever was changing it has to ask again.", LogLevel.Info);
        }

        /// <summary>Whether a player may write to something: they hold it, or nobody does.</summary>
        public bool MayChange(long playerId, string key)
        {
            if (this.TakenBack.TryGetValue(key, out long tookFrom) && tookFrom == playerId)
                return false;
            return !this.Held.TryGetValue(key, out var held) || held.Expires <= DateTime.UtcNow || held.Player == playerId;
        }


        /*********
        ** Private methods
        *********/
        private void OnMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != this.ModId)
                return;
            try
            {
                switch (e.Type)
                {
                    case RequestType when Context.IsMainPlayer:
                        this.OnRequest(e.FromPlayerID, e.ReadAs<LockRequest>());
                        break;
                    case UpdateType when Context.IsMainPlayer:
                        this.OnUpdate(e.FromPlayerID, e.ReadAs<LockUpdate>());
                        break;
                    case ReplyType:
                        this.OnReply(e.FromPlayerID, e.ReadAs<LockReply>());
                        break;
                    case ListType:
                        this.OnList(e.FromPlayerID, e.ReadAs<LockList>());
                        break;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't handle a message about who's changing what: {ex.Message}", LogLevel.Warn);
            }
        }

        private void OnRequest(long playerId, LockRequest request)
        {
            string key = Clean(request.Key, 200);
            if (key.Length == 0)
                return;

            // adding something, or changing what they added, is theirs to do; the rest needs 'Let players change my content'
            if (CoreMod.Sync?.MayPlayerChange(playerId, key) is { Allowed: false } verdict)
            {
                this.SendTo(playerId, new LockReply { Key = key, Granted = false, Reason = verdict.Reason }, ReplyType);
                return;
            }

            if (this.Held.TryGetValue(key, out var held) && held.Expires > DateTime.UtcNow && held.Player != playerId)
            {
                this.SendTo(playerId, new LockReply { Key = key, Granted = false, Holder = held.Holder }, ReplyType);
                return;
            }

            string name = this.NameOf(playerId);
            this.TakenBack.Remove(key); // they're asking again, so the slate is clean
            this.Held[key] = (playerId, name, Clean(request.Label, 60), DateTime.UtcNow + Lease);
            this.SendTo(playerId, new LockReply { Key = key, Granted = true }, ReplyType);
            this.TellEveryone();
            this.Monitor.Log($"{name} is changing '{key}'.", LogLevel.Trace);
        }

        private void OnUpdate(long playerId, LockUpdate update)
        {
            string key = Clean(update.Key, 200);
            if (!this.Held.TryGetValue(key, out var held) || held.Player != playerId)
                return;

            if (update.Finished)
            {
                this.Held.Remove(key);
                this.TellEveryone();
            }
            else
                this.Held[key] = (held.Player, held.Holder, held.Label, DateTime.UtcNow + Lease);
        }

        private void OnReply(long playerId, LockReply reply)
        {
            if (playerId != Game1.MasterPlayer?.UniqueMultiplayerID) // only the host decides
                return;
            if (!this.Waiting.Remove(reply.Key, out Action<bool, string>? callback))
                return;
            this.WaitingLabels.Remove(reply.Key, out string? label);

            if (reply.Granted)
            {
                this.Mine[reply.Key] = DateTime.UtcNow + RenewEvery;
                Version++;
                this.MineSince[reply.Key] = DateTime.UtcNow;
                callback(true, "");
                return;
            }

            string why = Clean(reply.Reason, 120);
            callback(false, why.Length > 0
                ? char.ToUpper(why[0]) + why.Substring(1) + "."
                : $"{Clean(reply.Holder, 40)} is changing {label ?? "that"} right now.");
        }

        private void OnList(long playerId, LockList list)
        {
            if (playerId != Game1.MasterPlayer?.UniqueMultiplayerID)
                return;

            Version++;
            // our own holds aren't someone else changing it: leave them out, or a list sent just before we let go would tell us
            // we're busy with the thing we just closed
            long me = Game1.player.UniqueMultiplayerID;
            this.Others = list.Locks
                .Where(l => l.Key.Length > 0 && l.PlayerId != me)
                .GroupBy(l => Clean(l.Key, 200), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Clean(g.First().Holder, 40), StringComparer.OrdinalIgnoreCase);

            // anything we thought was ours but the host doesn't list is no longer ours: the host took it back, or it ran out
            foreach ((string key, DateTime since) in this.MineSince.ToArray())
            {
                if (this.Others.ContainsKey(key) || DateTime.UtcNow - since < TimeSpan.FromSeconds(15))
                    continue;
                bool listedForUs = list.Locks.Any(l => Clean(l.Key, 200).Equals(key, StringComparison.OrdinalIgnoreCase));
                if (listedForUs)
                    continue;

                this.Mine.Remove(key);
                this.MineSince.Remove(key);
                this.Monitor.Log($"The host took '{key}' back; anything saved for it now is refused until it's asked for again.", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage("The host took back what you were changing. Open it again to carry on.") { noIcon = true });
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(60))
                return;

            DateTime now = DateTime.UtcNow;

            // as host: drop locks of players who went quiet
            if (Context.IsMainPlayer && this.Held.Any(l => l.Value.Expires <= now))
            {
                foreach ((string key, var held) in this.Held.Where(l => l.Value.Expires <= now).ToArray())
                {
                    this.Held.Remove(key);
                    this.Monitor.Log($"{held.Holder} stopped changing '{key}'.", LogLevel.Trace);
                }
                this.TellEveryone();
            }

            // as player: say we're still on the things we hold
            if (!Context.IsMainPlayer)
            {
                foreach ((string key, DateTime next) in this.Mine.Where(m => m.Value <= now).ToArray())
                {
                    this.Mine[key] = now + RenewEvery;
                    this.SendToHost(new LockUpdate { Key = key }, UpdateType);
                }
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            foreach ((string key, var held) in this.Held.Where(l => l.Value.Player == e.Peer.PlayerID).ToArray())
                this.Held.Remove(key);
            foreach ((string key, long player) in this.TakenBack.Where(t => t.Value == e.Peer.PlayerID).ToArray())
                this.TakenBack.Remove(key);
            this.TellEveryone();
        }

        /// <summary>As host: tell everyone what's being changed, so their screens can say so.</summary>
        private void TellEveryone()
        {
            Version++; // the host's own screens watch this too
            if (!Context.IsMainPlayer || !Context.IsMultiplayer)
                return;

            LockList list = new()
            {
                Locks = this.Held.Where(l => l.Value.Expires > DateTime.UtcNow)
                    .Select(l => new LockEntry { Key = l.Key, Holder = l.Value.Holder, Label = l.Value.Label, PlayerId = l.Value.Player })
                    .ToList()
            };
            this.Helper.Multiplayer.SendMessage(list, ListType, new[] { this.ModId });
        }

        private void Reset()
        {
            this.Held.Clear();
            this.Mine.Clear();
            this.MineSince.Clear();
            this.TakenBack.Clear();
            this.Others.Clear();
            this.Waiting.Clear();
            this.WaitingLabels.Clear();
        }

        private void SendToHost<T>(T message, string type)
        {
            long host = Game1.MasterPlayer?.UniqueMultiplayerID ?? 0;
            if (host != 0)
                this.Helper.Multiplayer.SendMessage(message, type, new[] { this.ModId }, new[] { host });
        }

        private void SendTo<T>(long playerId, T message, string type)
        {
            if (playerId != 0)
                this.Helper.Multiplayer.SendMessage(message, type, new[] { this.ModId }, new[] { playerId });
        }

        private string NameOf(long playerId)
        {
            string? name = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId)?.Name;
            return Clean(string.IsNullOrWhiteSpace(name) ? "Another player" : name, 40);
        }

        /// <summary>Text from another player, made safe to show and store: printable characters only, limited length.</summary>
        private static string Clean(string? text, int limit)
        {
            return new string((text ?? "").Where(ch => !char.IsControl(ch)).Take(limit).ToArray()).Trim();
        }
    }
}
