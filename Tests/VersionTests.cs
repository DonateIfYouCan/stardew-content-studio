using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomContentCore;
using StardewModdingAPI;
using Xunit;

namespace Tests
{
    /// <summary>Keeping a copy of a data file before it's written over, which is what the 'Earlier versions' screen puts back.</summary>
    public class VersionTests : IDisposable
    {
        private readonly string Folder = Path.Combine(Path.GetTempPath(), "content-studio-tests-" + Guid.NewGuid().ToString("N"));

        public VersionTests()
        {
            Directory.CreateDirectory(this.Folder);
            ContentPacks.Register(new FakeManifest(), this.Folder, new[] { "things.json", "images" }, () => { });
        }

        public void Dispose()
        {
            if (Directory.Exists(this.Folder))
                Directory.Delete(this.Folder, recursive: true);
        }

        [Fact]
        public void KeepsACopyOfTheDataFile()
        {
            this.Write("""{ "Things": [ { "Id": "one" } ] }""");
            ContentPacks.KeepVersionOfDataFiles();

            Assert.Single(this.Versions());
        }

        [Fact]
        public void KeepsOneCopyPerChange()
        {
            this.Write("""{ "Things": [ { "Id": "one" } ] }""");
            ContentPacks.KeepVersionOfDataFiles();
            this.Write("""{ "Things": [ { "Id": "two" } ] }""");
            ContentPacks.KeepVersionOfDataFiles();

            Assert.Equal(2, this.Versions().Length);
        }

        [Fact]
        public void DoesntKeepTheSameThingTwice()
        {
            this.Write("""{ "Things": [] }""");
            ContentPacks.KeepVersionOfDataFiles();
            ContentPacks.KeepVersionOfDataFiles();
            ContentPacks.KeepVersionOfDataFiles();

            Assert.Single(this.Versions());
        }

        [Fact]
        public void KeepsTheVersionsWhereTheScreenLooksForThem()
        {
            // the 'Earlier versions' screen reads "<stem>.<yyyyMMdd-HHmmss><extension>" from the mod's own versions folder
            this.Write("""{ "Things": [] }""");
            ContentPacks.KeepVersionOfDataFiles();

            string kept = this.Versions().Single();
            string name = Path.GetFileNameWithoutExtension(kept);
            Assert.StartsWith("things.", name);
            Assert.EndsWith(".json", kept);
            Assert.True(DateTime.TryParseExact(name.Split('.').Last(), "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out _), $"'{kept}' isn't named for a time the screen can read");
        }

        private void Write(string json)
        {
            File.WriteAllText(Path.Combine(this.Folder, "things.json"), json);
            System.Threading.Thread.Sleep(1100); // the copies are named by the second, so a change needs a new second
        }

        private string[] Versions()
        {
            string folder = Path.Combine(this.Folder, "versions");
            return Directory.Exists(folder) ? Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().ToArray() : Array.Empty<string>();
        }

        /// <summary>A mod registration for the tests, since registering needs a manifest.</summary>
        private sealed class FakeManifest : IManifest
        {
            public string Name => "Test mod";
            public string Author => "tests";
            public ISemanticVersion Version => new SemanticVersion(1, 0, 0);
            public string Description => "";
            public string UniqueID => "tests.content-studio";
            public string? EntryDll => null;
            public IManifestContentPackFor? ContentPackFor => null;
            public IManifestDependency[] Dependencies => Array.Empty<IManifestDependency>();
            public string[] UpdateKeys => Array.Empty<string>();
            public ISemanticVersion? MinimumApiVersion => null;
            public ISemanticVersion? MinimumGameVersion => null;
            public IDictionary<string, object> ExtraFields => new Dictionary<string, object>();
        }
    }
}
