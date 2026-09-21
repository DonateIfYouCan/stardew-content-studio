using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Habits every editor screen has to keep. These are checked by reading the source, because the screens can't be built
    /// without the game running - but each one is a mistake that has been made here at least once.
    /// </summary>
    public class ScreenHabitTests
    {
        /// <summary>Every field a screen declares is assigned in its constructor.</summary>
        /// <remarks>A button that's declared and never made is null when the screen is laid out, and the editor won't open at all.</remarks>
        [Fact]
        public void EveryScreenMakesTheWidgetsItDeclares()
        {
            List<string> missing = new();
            foreach (string path in ScreenFiles())
            {
                string code = File.ReadAllText(path);
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(code, @"private readonly (?:Button|Checkbox|TextField|Cycler) (\w+);"))
                {
                    string field = match.Groups[1].Value;
                    if (!code.Contains($"this.{field} = "))
                        missing.Add($"{Path.GetFileName(path)}: {field}");
                }
            }

            Assert.True(missing.Count == 0, $"declared but never made: {string.Join(", ", missing)}");
        }

        /// <summary>A screen that takes a lock gives it back.</summary>
        /// <remarks>Whoever opens something holds it until they close it; a screen that never releases leaves it stuck until the lease runs out.</remarks>
        [Fact]
        public void EveryScreenThatHoldsSomethingLetsItGoAgain()
        {
            List<string> stuck = new();
            foreach (string path in ScreenFiles())
            {
                string code = File.ReadAllText(path);
                if (!code.Contains("CustomContent.TakeLock("))
                    continue;
                if (!code.Contains("CustomContent.ReleaseLock("))
                    stuck.Add(Path.GetFileName(path));
            }

            Assert.True(stuck.Count == 0, $"these screens take a lock and never release it: {string.Join(", ", stuck)}");
        }

        /// <summary>A list that shows content rebuilds itself when the content changes underneath it.</summary>
        /// <remarks>Otherwise a change from another player only shows after the screen is closed and opened again.</remarks>
        [Fact]
        public void EveryListNoticesWhenContentChanges()
        {
            List<string> stale = new();
            foreach (string path in ScreenFiles().Where(p => Path.GetFileName(p).EndsWith("ListScreen.cs")))
            {
                string code = File.ReadAllText(path);
                if (!code.Contains("CustomContent.ContentVersion"))
                    stale.Add(Path.GetFileName(path));
            }

            Assert.True(stale.Count == 0, $"these lists don't rebuild when content changes: {string.Join(", ", stale)}");
        }

        /// <summary>A screen that greys out buttons while someone else is changing something checks them again when that changes.</summary>
        /// <remarks>
        /// The rows saying who's changing what are drawn fresh every frame, but buttons are only set when something is picked.
        /// Without this, Player A closing an editor left their own buttons greyed out, because they were set while A still held it.
        /// </remarks>
        [Fact]
        public void EveryScreenThatGreysOutButtonsForALockNoticesWhenItGoes()
        {
            List<string> stale = new();
            foreach (string path in ScreenFiles())
            {
                string code = File.ReadAllText(path);
                if (!code.Contains("CustomContent.WhoIsChanging(") || !code.Contains(".Enabled"))
                    continue;
                if (!code.Contains("CustomContent.LockVersion"))
                    stale.Add(Path.GetFileName(path));
            }

            Assert.True(stale.Count == 0, $"these screens grey out buttons for a lock but never check them again when it's let go: {string.Join(", ", stale)}");
        }

        private static IEnumerable<string> ScreenFiles()
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Directory.GetDirectories(root, "Custom*")
                .Select(folder => Path.Combine(folder, "UI"))
                .Where(Directory.Exists)
                .SelectMany(ui => Directory.GetFiles(ui, "*Screen.cs"));
        }
    }
}
