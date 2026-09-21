using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The two settings players asked to be in charge of: how big an export is, and whether help follows the mouse. Both
    /// had a default baked in that couldn't be changed, so these check the defaults and that nothing skips the setting.
    /// </summary>
    public class SettingsTests
    {
        /// <summary>The defaults a new <c>config.json</c> gets.</summary>
        /// <remarks>
        /// Read from the source: building a <see cref="CoreConfig"/> runs the initialiser for its keybind, which loads SMAPI
        /// and then the game itself, and these tests are the ones that run without the game.
        /// </remarks>
        [Theory]
        // an HD sheet is a choice: exporting at 4x by default made everyone scale back down to repaint the original
        [InlineData("ExportScale", "1")]
        [InlineData("ShowHoverTips", "true")]
        public void ConfigDefault(string property, string expected)
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            string code = File.ReadAllText(Path.Combine(root, "CustomContentCore", "CoreMod.cs"));

            var match = System.Text.RegularExpressions.Regex.Match(code, $@"public \w+ {property} {{ get; set; }} = ([^;]+);");
            Assert.True(match.Success, $"CoreConfig no longer declares {property} with a default");
            Assert.Equal(expected, match.Groups[1].Value.Trim());
        }

        [Fact]
        public void EveryExportButtonAsksHowBig()
        {
            // a screen that calls Export directly would silently use the remembered size and never offer the choice
            List<string> skipped = new();
            foreach (string path in ScreenFiles())
            {
                string code = File.ReadAllText(path);
                // only the image exports; HubScreen's "Export pack" writes a zip of your content and has nothing to size
                if (!code.Contains("ImageExport.Export(") && !code.Contains("ExportOriginal(") && !code.Contains("ExportTemplate(") && !code.Contains("ExportVanillaGrowth("))
                    continue;
                if (!code.Contains("ImageExport.AskScale("))
                    skipped.Add(Path.GetFileName(path));
            }

            Assert.True(skipped.Count == 0, $"these screens export without asking how big: {string.Join(", ", skipped)}");
        }

        [Fact]
        public void TheHoverTipIsDrawnOnlyWhenTheSettingIsOn()
        {
            // the tip is drawn in one place, so the setting has to be read there or it does nothing
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            string code = File.ReadAllText(Path.Combine(root, "CustomContentCore", "UI", "EditorRoot.cs"));

            int draw = code.IndexOf("drawHoverText(", System.StringComparison.Ordinal);
            Assert.True(draw > 0, "EditorRoot no longer draws a hover tip; move this check to wherever it moved to");

            string before = code[..draw];
            Assert.Contains("ShowHoverTips", before);
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
