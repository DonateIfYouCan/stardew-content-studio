using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>Moving content between the mods' folders and theme folders, checked in a throwaway folder.</summary>
    public sealed class ThemeFilesTests : IDisposable
    {
        private readonly string Root = Path.Combine(Path.GetTempPath(), "ThemeFilesTests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(this.Root))
                Directory.Delete(this.Root, recursive: true);
        }

        /// <summary>A mod folder with a data file, an image in a sub-folder, and files that aren't content.</summary>
        private ThemeMod MakeMod(string id, string data)
        {
            string folder = Path.Combine(this.Root, "Mods", id);
            Write(Path.Combine(folder, "fish.json"), data);
            Write(Path.Combine(folder, "images", "koi", "koi.png"), "png:" + data);
            Write(Path.Combine(folder, "manifest.json"), "the mod's own");
            Write(Path.Combine(folder, "versions", "fish.1.json"), "an old version");
            return new ThemeMod(id, folder, new[] { "fish.json", "images" });
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        [Fact]
        public void AThemeHoldsOnlyTheContent()
        {
            ThemeMod mod = this.MakeMod("Fish", "one");
            string theme = Path.Combine(this.Root, "Themes", "One");
            ThemeFiles.Save(theme, new[] { mod });

            string[] files = Directory.GetFiles(theme, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(theme, f).Replace('\\', '/')).OrderBy(f => f).ToArray();
            Assert.Equal(new[] { "Fish/fish.json", "Fish/images/koi/koi.png" }, files); // not the mod's manifest or its kept versions
        }

        [Fact]
        public void SwitchingThemesSwapsTheContentAndBack()
        {
            ThemeMod mod = this.MakeMod("Fish", "one");
            string themes = Path.Combine(this.Root, "Themes");
            ThemeFiles.Save(Path.Combine(themes, "One"), new[] { mod });

            // a second theme with different content
            File.WriteAllText(Path.Combine(mod.Folder, "fish.json"), "two");
            File.Delete(Path.Combine(mod.Folder, "images", "koi", "koi.png"));
            ThemeFiles.Save(Path.Combine(themes, "Two"), new[] { mod });

            ThemeFiles.Load(Path.Combine(themes, "One"), new[] { mod });
            Assert.Equal("one", File.ReadAllText(Path.Combine(mod.Folder, "fish.json")));
            Assert.True(File.Exists(Path.Combine(mod.Folder, "images", "koi", "koi.png")));
            Assert.Equal("the mod's own", File.ReadAllText(Path.Combine(mod.Folder, "manifest.json"))); // the mod itself is never touched

            ThemeFiles.Load(Path.Combine(themes, "Two"), new[] { mod });
            Assert.Equal("two", File.ReadAllText(Path.Combine(mod.Folder, "fish.json")));
            Assert.Empty(Directory.GetFiles(Path.Combine(mod.Folder, "images"), "*", SearchOption.AllDirectories)); // theme One's image doesn't linger in theme Two
            Assert.True(Directory.Exists(Path.Combine(mod.Folder, "images"))); // but the folder the mod saves images into is still there
        }

        [Fact]
        public void AModTheThemeHasNothingForStartsEmpty()
        {
            ThemeMod fish = this.MakeMod("Fish", "fish");
            ThemeMod crops = this.MakeMod("Crops", "crops");
            string theme = Path.Combine(this.Root, "Themes", "FishOnly");
            ThemeFiles.Save(theme, new[] { fish });

            ThemeFiles.Load(theme, new[] { fish, crops });
            Assert.True(File.Exists(Path.Combine(fish.Folder, "fish.json")));
            Assert.False(File.Exists(Path.Combine(crops.Folder, "fish.json")));
        }

        [Fact]
        public void AThemeCantWriteOutsideTheModsContent()
        {
            ThemeMod mod = this.MakeMod("Fish", "one");
            string theme = Path.Combine(this.Root, "Themes", "Passed around");
            Write(Path.Combine(theme, "Fish", "fish.json"), "theirs");
            Write(Path.Combine(theme, "Fish", "CustomFish.dll"), "not content");
            Write(Path.Combine(theme, "Fish", "manifest.json"), "not content");

            ThemeFiles.Load(theme, new[] { mod });
            Assert.Equal("theirs", File.ReadAllText(Path.Combine(mod.Folder, "fish.json")));
            Assert.False(File.Exists(Path.Combine(mod.Folder, "CustomFish.dll")));
            Assert.Equal("the mod's own", File.ReadAllText(Path.Combine(mod.Folder, "manifest.json")));
        }

        [Fact]
        public void APackOpensAsTheSameTheme()
        {
            ThemeMod mod = this.MakeMod("Fish", "one");
            string theme = Path.Combine(this.Root, "Themes", "One");
            ThemeFiles.Save(theme, new[] { mod });
            string zip = Path.Combine(this.Root, "one.zip");
            ThemeFiles.Zip(theme, zip, "{\"Format\":1,\"Mods\":[{\"Id\":\"Fish\"}]}");

            string opened = Path.Combine(this.Root, "Themes", "Opened");
            ThemeFiles.Unzip(zip, opened);
            Assert.Equal(File.ReadAllText(Path.Combine(theme, "Fish", "fish.json")), File.ReadAllText(Path.Combine(opened, "Fish", "fish.json")));
            Assert.True(File.Exists(Path.Combine(opened, "Fish", "images", "koi", "koi.png")));
            Assert.False(File.Exists(Path.Combine(opened, "pack.json")));
        }

        [Fact]
        public void APackCantClimbOutOfItsTheme()
        {
            string zip = Path.Combine(this.Root, "bad.zip");
            Directory.CreateDirectory(this.Root);
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "../escaped.txt", "Fish/../../escaped2.txt", "loose.txt", "Fish/fish.json" })
                    using (StreamWriter writer = new(archive.CreateEntry(name).Open()))
                        writer.Write("x");
            }

            string opened = Path.Combine(this.Root, "Themes", "Opened");
            ThemeFiles.Unzip(zip, opened);
            Assert.Equal(new[] { Path.Combine(opened, "Fish", "fish.json") }, Directory.GetFiles(opened, "*", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(this.Root, "escaped.txt")));
            Assert.False(File.Exists(Path.Combine(this.Root, "Themes", "escaped2.txt")));
        }

        [Theory]
        [InlineData("Summer project", "Summer project")]
        [InlineData("a/b:c*d", "a b c d")]
        [InlineData("  ..hidden..  ", "hidden")]
        [InlineData("CON", "_CON")]
        [InlineData("com1.txt", "_com1.txt")]
        [InlineData("///", "")]
        public void ThemeNamesAreSafeFolderNames(string name, string expected)
        {
            Assert.Equal(expected, ThemeFiles.CleanName(name));
        }

        [Fact]
        public void LongThemeNamesAreShortened()
        {
            Assert.Equal(ThemeFiles.MaxNameLength, ThemeFiles.CleanName(new string('x', 200)).Length);
        }
    }
}
