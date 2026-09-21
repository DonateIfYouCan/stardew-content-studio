using System;

namespace CustomContentCore
{
    /// <summary>
    /// Who may change what in a Host's game, in one place. The Host asks these whenever a player wants to hold something,
    /// offers a change, or sends a file, so the answer is the same however the change arrives.
    /// </summary>
    /// <remarks>
    /// The rules, from the Host's side:
    /// <list type="bullet">
    ///   <item>Adding something of your own is always yours to do, and so is changing or deleting what you added.</item>
    ///   <item>Changing what was already here - the Host's items, or the game's own - needs the Host's
    ///   <c>Let players change my content</c>.</item>
    ///   <item>The game's own items are never "new": replacing a game lamp or hiding a game painting changes the game for
    ///   everyone, so even the first change to one needs the Host's say-so.</item>
    /// </list>
    /// These are pure so they can be checked without the game; nothing here reads the config or the network.
    /// </remarks>
    internal static class ChangeRules
    {
        /// <summary>The mark on an item ID that says it changes one of the game's own items rather than adding one.</summary>
        /// <remarks>Like <c>g:1296</c> for the game's furniture 1296. Item IDs made from names never contain a colon, so this can't collide with one.</remarks>
        public const string GameItemPrefix = "g:";

        /// <summary>Why a player's change was refused, said so they know what to ask the Host.</summary>
        public const string OnlyWhatTheyAdded = "the host only lets players change what they added themselves";

        /// <summary>Why a change to one of the game's own items was refused.</summary>
        public const string NotTheGamesItems = "the host doesn't let players change the game's own items";

        /// <summary>Whether an item ID changes one of the game's own items.</summary>
        public static bool IsGameItem(string itemId) => itemId.StartsWith(GameItemPrefix, StringComparison.Ordinal);

        /// <summary>The answer to whether a player may do something, with the reason when they may not.</summary>
        /// <param name="Allowed">Whether they may.</param>
        /// <param name="Reason">Why not, in words for the player; empty when allowed.</param>
        public readonly record struct Verdict(bool Allowed, string Reason)
        {
            public static readonly Verdict Yes = new(true, "");
        }

        /// <summary>May a player add or change one item?</summary>
        /// <param name="exists">Whether the Host already has an item by that ID.</param>
        /// <param name="isGameItem">Whether it changes one of the game's own items (see <see cref="GameItemPrefix"/>).</param>
        /// <param name="hostLetsPlayersChange">The Host's <c>Let players change my content</c>.</param>
        /// <param name="addedByThisPlayer">Whether this player is the one who first added it.</param>
        public static Verdict ForItem(bool exists, bool isGameItem, bool hostLetsPlayersChange, bool addedByThisPlayer)
        {
            if (hostLetsPlayersChange || addedByThisPlayer)
                return Verdict.Yes;
            if (isGameItem)
                return new Verdict(false, NotTheGamesItems); // the game's items belong to the Host's game, even the first time
            if (!exists)
                return Verdict.Yes; // something of their own
            return new Verdict(false, OnlyWhatTheyAdded);
        }

        /// <summary>May a player take one item out: delete what they added, or put a game item back as the game has it?</summary>
        /// <param name="isGameItem">Whether it's a change to one of the game's own items.</param>
        /// <param name="hostLetsPlayersChange">The Host's <c>Let players change my content</c>.</param>
        /// <param name="addedByThisPlayer">Whether this player is the one who added it.</param>
        public static Verdict ForRemoval(bool isGameItem, bool hostLetsPlayersChange, bool addedByThisPlayer)
        {
            if (hostLetsPlayersChange || addedByThisPlayer)
                return Verdict.Yes;
            return new Verdict(false, isGameItem ? NotTheGamesItems : OnlyWhatTheyAdded);
        }

        /// <summary>May a player write a whole file into the Host's content: an image, or a data file?</summary>
        /// <param name="exists">Whether the Host already has a file at that path.</param>
        /// <param name="hostLetsPlayersChange">The Host's <c>Let players change my content</c>.</param>
        /// <remarks>
        /// A new file is always fine, because a new image only ever arrives with something being added or changed, and that
        /// item is checked on its own. Writing over one the Host already has needs the Host's say-so: the editors save a
        /// changed image under a new name, so overwriting is only ever someone changing a file that was already here.
        /// </remarks>
        public static Verdict ForFile(bool exists, bool hostLetsPlayersChange)
        {
            if (!exists || hostLetsPlayersChange)
                return Verdict.Yes;
            return new Verdict(false, OnlyWhatTheyAdded);
        }

        /// <summary>May a player hold something while they change it, so nobody else writes over them?</summary>
        /// <param name="lockKey">What they want to hold: <c>item:&lt;id&gt;</c> for one item, <c>file:&lt;path&gt;</c> for a whole file.</param>
        /// <param name="itemExists">Whether the Host has that item (ignored for files).</param>
        /// <param name="hostLetsPlayersChange">The Host's <c>Let players change my content</c>.</param>
        /// <param name="addedByThisPlayer">Whether this player added that item (ignored for files).</param>
        /// <remarks>Holding something is only worth it if they'd be allowed to save it, so this gives the same answer the save would.</remarks>
        public static Verdict ForHolding(string lockKey, bool itemExists, bool hostLetsPlayersChange, bool addedByThisPlayer)
        {
            if (lockKey.StartsWith("item:", StringComparison.Ordinal))
            {
                string id = lockKey.Substring("item:".Length);
                return ForItem(itemExists, IsGameItem(id), hostLetsPlayersChange, addedByThisPlayer);
            }

            // a whole file changes the list itself (its settings, or every item at once), which is the Host's own business
            return hostLetsPlayersChange ? Verdict.Yes : new Verdict(false, OnlyWhatTheyAdded);
        }
    }
}
