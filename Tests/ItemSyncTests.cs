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
            string[] prefixes = { "w:", "p:", "s:", "f:" };
            Assert.Equal(prefixes.Length, prefixes.Distinct().Count());
            Assert.All(prefixes, prefix => Assert.EndsWith(":", prefix));
        }

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
