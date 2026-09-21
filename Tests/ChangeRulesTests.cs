using System.IO;
using System.Text.RegularExpressions;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Who may change what in the Host's game. Each case is a Player asking the Host; "the switch" is the Host's
    /// <c>Let players change my content</c>. These rules used to be written out four times, in four places, and one of those
    /// copies drifted: it threw away the image of anything Player A added while the switch was off.
    /// </summary>
    public class ChangeRulesTests
    {
        private const bool SwitchOn = true, SwitchOff = false;
        private const bool TheirOwn = true, NotTheirs = false;
        private const bool Exists = true, New = false;
        private const bool GameItem = true, Ordinary = false;

        /*********
        ** Items: adding and changing
        *********/
        [Fact]
        public void PlayerACanAddSomethingNewWithTheSwitchOff()
        {
            Assert.True(ChangeRules.ForItem(New, Ordinary, SwitchOff, NotTheirs).Allowed);
        }

        [Fact]
        public void PlayerACanChangeWhatTheyAddedWithTheSwitchOff()
        {
            Assert.True(ChangeRules.ForItem(Exists, Ordinary, SwitchOff, TheirOwn).Allowed);
        }

        [Fact]
        public void PlayerACantChangeTheHostsItemWithTheSwitchOff()
        {
            ChangeRules.Verdict verdict = ChangeRules.ForItem(Exists, Ordinary, SwitchOff, NotTheirs);
            Assert.False(verdict.Allowed);
            Assert.Equal(ChangeRules.OnlyWhatTheyAdded, verdict.Reason);
        }

        [Fact]
        public void PlayerACantChangeWhatPlayerBAddedWithTheSwitchOff()
        {
            // B added it, so to A it's no different from the Host's own
            Assert.False(ChangeRules.ForItem(Exists, Ordinary, SwitchOff, NotTheirs).Allowed);
        }

        [Fact]
        public void PlayerACanChangeAnythingWithTheSwitchOn()
        {
            Assert.True(ChangeRules.ForItem(Exists, Ordinary, SwitchOn, NotTheirs).Allowed);
            Assert.True(ChangeRules.ForItem(New, Ordinary, SwitchOn, NotTheirs).Allowed);
        }

        /*********
        ** The game's own items
        *********/
        [Fact]
        public void PlayerACantReplaceAGameItemWithTheSwitchOffEvenTheFirstTime()
        {
            // the difference from an ordinary add: nobody has changed the game's lamp yet, but it's still the Host's game
            ChangeRules.Verdict verdict = ChangeRules.ForItem(New, GameItem, SwitchOff, NotTheirs);
            Assert.False(verdict.Allowed);
            Assert.Equal(ChangeRules.NotTheGamesItems, verdict.Reason);
        }

        [Fact]
        public void PlayerACanReplaceAGameItemWithTheSwitchOn()
        {
            Assert.True(ChangeRules.ForItem(New, GameItem, SwitchOn, NotTheirs).Allowed);
        }

        [Fact]
        public void PlayerACanKeepChangingAGameItemTheyReplacedAfterTheSwitchGoesOff()
        {
            // they replaced it while the switch was on, so it's theirs; turning the switch off doesn't take it away
            Assert.True(ChangeRules.ForItem(Exists, GameItem, SwitchOff, TheirOwn).Allowed);
        }

        [Fact]
        public void PlayerACantChangeAGameItemPlayerBReplacedWithTheSwitchOff()
        {
            ChangeRules.Verdict verdict = ChangeRules.ForItem(Exists, GameItem, SwitchOff, NotTheirs);
            Assert.False(verdict.Allowed);
            Assert.Equal(ChangeRules.NotTheGamesItems, verdict.Reason);
        }

        [Theory]
        [InlineData("g:1296", true)]
        [InlineData("g:Emily_plant", true)]
        [InlineData("Emily_plant", false)]
        [InlineData("w:MyFloor", false)]    // a wallpaper of your own, not a game item
        [InlineData("G:1296", false)]       // the mark is exact
        [InlineData("", false)]
        public void KnowsAGameItemByItsMark(string id, bool isGameItem)
        {
            Assert.Equal(isGameItem, ChangeRules.IsGameItem(id));
        }

        /*********
        ** Taking things out
        *********/
        [Fact]
        public void PlayerACanDeleteWhatTheyAddedWithTheSwitchOff()
        {
            Assert.True(ChangeRules.ForRemoval(Ordinary, SwitchOff, TheirOwn).Allowed);
        }

        [Fact]
        public void PlayerACantDeleteTheHostsItemWithTheSwitchOff()
        {
            Assert.False(ChangeRules.ForRemoval(Ordinary, SwitchOff, NotTheirs).Allowed);
        }

        [Fact]
        public void PlayerACantPutAGameItemBackWithTheSwitchOffUnlessTheyChangedIt()
        {
            // putting the game's lamp back undoes a change to the game, which is the same question as making one
            Assert.False(ChangeRules.ForRemoval(GameItem, SwitchOff, NotTheirs).Allowed);
            Assert.Equal(ChangeRules.NotTheGamesItems, ChangeRules.ForRemoval(GameItem, SwitchOff, NotTheirs).Reason);
            Assert.True(ChangeRules.ForRemoval(GameItem, SwitchOff, TheirOwn).Allowed);
        }

        /*********
        ** Files
        *********/
        [Fact]
        public void PlayerACanSendANewImageWithTheSwitchOff()
        {
            // the bug these tests were written for: an added item's image was refused unless the switch was on
            Assert.True(ChangeRules.ForFile(New, SwitchOff).Allowed);
        }

        [Fact]
        public void PlayerACantWriteOverTheHostsImageWithTheSwitchOff()
        {
            Assert.False(ChangeRules.ForFile(Exists, SwitchOff).Allowed);
        }

        [Fact]
        public void PlayerACanWriteOverAnImageWithTheSwitchOn()
        {
            Assert.True(ChangeRules.ForFile(Exists, SwitchOn).Allowed);
        }

        /*********
        ** Holding something while changing it
        *********/
        [Fact]
        public void HoldingGivesTheSameAnswerAsSaving()
        {
            // asking to hold something you'd then be refused to save is only a later disappointment
            foreach (bool exists in new[] { Exists, New })
                foreach (bool open in new[] { SwitchOn, SwitchOff })
                    foreach (bool theirs in new[] { TheirOwn, NotTheirs })
                        foreach (string id in new[] { "Emily_plant", "g:1296" })
                        {
                            bool save = ChangeRules.ForItem(exists, ChangeRules.IsGameItem(id), open, theirs).Allowed;
                            bool hold = ChangeRules.ForHolding($"item:{id}", exists, open, theirs).Allowed;
                            Assert.True(save == hold, $"{id}, exists={exists}, switch={open}, theirs={theirs}: saving says {save} but holding says {hold}");
                        }
        }

        [Fact]
        public void AWholeFileIsTheHostsUnlessTheSwitchIsOn()
        {
            Assert.False(ChangeRules.ForHolding("file:paintings.json", false, SwitchOff, false).Allowed);
            Assert.True(ChangeRules.ForHolding("file:paintings.json", false, SwitchOn, false).Allowed);
        }

        /*********
        ** The rules live in one place
        *********/
        [Fact]
        public void TheHostDecidesEveryChangeWithTheRules()
        {
            // a check of the switch written out by hand is how the copies drifted apart; the host's code should only ask
            // ChangeRules, apart from passing the switch in and telling a joining player whether it's on
            string code = File.ReadAllText(Path.Combine(Root(), "CustomContentCore", "MultiplayerSync.cs"));
            int handWritten = Regex.Matches(code, @"(?<!=\s)CoreMod\.Config\.LetOthersChangeMyContent(?!\s*[;,}])").Count;
            Assert.True(handWritten == 0, $"MultiplayerSync checks 'Let players change my content' by hand {handWritten} time(s); ask ChangeRules instead");
        }

        [Fact]
        public void AFileThatWasOfferedIsntRefusedAgainWhenItArrives()
        {
            // the image of an added item is requested once it's been allowed; refusing its pieces on arrival lost the item
            string code = File.ReadAllText(Path.Combine(Root(), "CustomContentCore", "MultiplayerSync.cs"));
            int start = code.IndexOf("private void OnChangeChunk(", System.StringComparison.Ordinal);
            int end = code.IndexOf("private void WriteOwnFile(", System.StringComparison.Ordinal);
            Assert.True(start > 0 && end > start, "couldn't find OnChangeChunk; move this check to wherever it went");

            string chunk = code[start..end];
            Assert.DoesNotContain("LetOthersChangeMyContent", Regex.Replace(chunk, @"//.*", ""));
        }

        [Fact]
        public void AFileKeptFromAnEarlierVisitIsntMistakenForAChange()
        {
            // rejoining a host reuses the files already here instead of downloading them again, so nothing recorded what they
            // looked like - and a file with no record looked changed, so Player A's first save sent all of them back
            string code = File.ReadAllText(Path.Combine(Root(), "CustomContentCore", "MultiplayerSync.cs"));
            int needed = code.IndexOf("List<string> needed = valid", System.StringComparison.Ordinal);
            int request = code.IndexOf("new RequestMessage", needed, System.StringComparison.Ordinal);
            Assert.True(needed > 0 && request > needed, "couldn't find where a player decides what to download; move this check to wherever it went");

            string decide = Regex.Replace(code[needed..request], @"//.*", "");
            Assert.Matches(@"this\.Downloaded\[[^\]]+\]\s*=", decide);
        }

        [Fact]
        public void WhatAPlayerAddedIsStillTheirsAfterTheHostRestarts()
        {
            // "changing what you added is always yours" quietly ran out when the host quit: who added what was only kept in memory
            string code = File.ReadAllText(Path.Combine(Root(), "CustomContentCore", "MultiplayerSync.cs"));
            Assert.Matches(@"this\.AddedBy\[key\] = playerId;[\s\S]{0,200}this\.SaveAddedBy\(\)", code);
            Assert.Matches(@"SaveLoaded \+= [^;]*LoadAddedBy", code);
            Assert.DoesNotMatch(@"this\.AddedBy\.Clear\(\);\s*this\.HostAllowsChanges", code); // the reset on leaving a game mustn't wipe it
        }

        [Fact]
        public void APlayerIsNeverToldTheyreTheOneInTheWay()
        {
            // a list the host sent just before Player A let go still named A, so A was told "A is changing this" about the
            // thing A had just closed; the list now says who holds each thing, and each player leaves their own out
            string code = File.ReadAllText(Path.Combine(Root(), "CustomContentCore", "ContentLocks.cs"));
            Assert.Matches(@"new LockEntry \{[^}]*PlayerId = ", code);
            Assert.Matches(@"this\.Others = list\.Locks\s*\.Where\([^)]*PlayerId != me", code);
        }

        private static string Root() => Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
