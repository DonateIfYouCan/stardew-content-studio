using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Tests
{
    /// <summary>How the tests build the mods.</summary>
    public class BuildTests
    {
        [Fact]
        public void RunningTheTestsNeverDeploysAMod()
        {
            // every mod copies itself into the game's Mods folder when built; built for the tests it must not, since the player's
            // game may be running from that folder (this went unnoticed until a test run swapped a mod under a running game)
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            string project = File.ReadAllText(Path.Combine(root, "Tests", "Tests.csproj"));
            foreach (Match reference in Regex.Matches(project, @"<ProjectReference [^>]*>"))
                Assert.Contains("EnableModDeploy=false", reference.Value);
        }
    }
}
