using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The rules that decide which files a mod shares and how its items are named apart. These are the ones that broke in a
    /// rewrite before: content was looked for in the wrong folder, and a mod stopped sending its items one at a time.
    /// </summary>
    public class ItemSyncTests
    {
        [Theory]
        [InlineData("paintings.json", true)]
        [InlineData("crops.json", true)]
        [InlineData("images/sunset.png", false)]
        [InlineData("paintings/sunset.JSON", true)]
        public void KnowsWhichFilesHoldTheItems(string path, bool isData)
        {
            Assert.Equal(isData, ContentPacks.IsDataFile(path));
        }

        [Fact]
        public void EveryModThatCanBeEditedSendsItsItemsOneAtATime()
        {
            // a mod without this sends its whole data file instead, so Player A editing one item holds everything
            IEnumerable<string> withoutEditing = ModRegistrations()
                .Where(mod => !mod.HasEditing)
                .Select(mod => mod.Name);

            Assert.True(!withoutEditing.Any(), $"these mods don't send items one at a time: {string.Join(", ", withoutEditing)}");
        }

        [Fact]
        public void ItemPrefixesDontClash()
        {
            // each mod that keeps several kinds of thing in one file marks them apart, or a wallpaper could land on a chair
            string[] prefixes = { "w:", "p:", "s:", "f:", CustomContent.GameItemPrefix };
            Assert.Equal(prefixes.Length, prefixes.Distinct().Count());
            Assert.All(prefixes, prefix => Assert.EndsWith(":", prefix));
        }

        [Theory]
        [InlineData("CustomFurniture", "FurnitureStore.cs")]
        [InlineData("CustomCrops", "CropStore.cs")]
        [InlineData("CustomPaintings", "PaintingStore.cs")]
        [InlineData("CustomFish", "FishStore.cs")]
        [InlineData("CustomMining", "MiningStore.cs")]
        public void AChangeToAGameItemTravelsOnItsOwn(string mod, string store)
        {
            // each change to one of the game's items is its own item, marked with the Core's prefix so the Host knows it
            // changes the game (and needs the Host's say-so even the first time), and sent one by one like everything else
            string code = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), mod, store));
            Assert.Contains("CustomContent.GameItemPrefix + ", code);
            Assert.Matches(@"GetItemIds\(\)[\s\S]*?(GameChanges|GameTargets)", code);
            Assert.Matches(@"ApplyItemJson[\s\S]*?GameItemPrefix", code);
            Assert.Matches(@"RemoveItem[\s\S]*?GameItemPrefix", code);
        }

        [Theory]
        [InlineData("CustomFurniture", "FurnitureListScreen.cs")]
        [InlineData("CustomCrops", "CropListScreen.cs")]
        [InlineData("CustomPaintings", "PaintingListScreen.cs")]
        [InlineData("CustomFish", "FishListScreen.cs")]
        [InlineData("CustomMining", "MineralListScreen.cs")]
        public void GameItemsAreHeldOneAtATime(string mod, string screen)
        {
            // Player A giving the game's lamp new art mustn't stop Player B hiding a game table, so no game-item button takes
            // the whole file: that's the per-list lock that was asked to go (paintings' auto-add setting still does, rightly)
            string code = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), mod, "UI", screen));
            foreach (string action in new[] { "EditSelected, keepHolding", "RestoreSelected", "ToggleHidden", "EditGameArt", "ToggleGameHidden", "RestoreGameArt" })
                Assert.DoesNotContain($"WhenNobodyElseIsChangingTheList(this.{action}", code);
            Assert.Contains("GameThing(", code);
        }

        [Theory]
        [InlineData("CustomFurniture", "FurnitureListScreen.cs")]
        [InlineData("CustomFurniture", "WallpaperListScreen.cs")]
        [InlineData("CustomCrops", "CropListScreen.cs")]
        [InlineData("CustomPaintings", "PaintingListScreen.cs")]
        [InlineData("CustomFish", "FishListScreen.cs")]
        [InlineData("CustomMining", "MineralListScreen.cs")]
        public void EveryGameItemCanBeCopied(string mod, string screen)
        {
            // each list of the game's own items offers a copy to make your own from, opened as a new item (so it's never a lock on the game's)
            string code = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), mod, "UI", screen));
            Assert.Contains("\"Make my own copy\"", code);
            Assert.Matches(@"CopyOfGame\w+\(", code);
            Assert.Contains("isNew: true", code);
        }

        private static string Root() => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));

        /// <summary>The mods in this repo and whether they let their items be sent one at a time.</summary>
        /// <remarks>Read from the source, since loading the mods needs the game running.</remarks>
        private static IEnumerable<(string Name, bool HasEditing)> ModRegistrations()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            foreach (string folder in System.IO.Directory.GetDirectories(root, "Custom*"))
            {
                string entry = System.IO.Path.Combine(folder, "ModEntry.cs");
                if (!System.IO.File.Exists(entry))
                    continue;

                string code = System.IO.File.ReadAllText(entry);
                if (!code.Contains("ContentPacks.Register("))
                    continue; // the Core itself registers nothing

                yield return (System.IO.Path.GetFileName(folder), code.Contains("ContentPacks.ContentEditing("));
            }
        }
    }
}
