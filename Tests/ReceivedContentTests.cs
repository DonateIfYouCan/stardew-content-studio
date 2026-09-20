using System;
using System.Text;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The checks that stand between another player's message and this PC: which paths may be written, and what a received
    /// file is turned into before any mod sees it.
    /// </summary>
    public class ReceivedContentTests
    {
        [Theory]
        [InlineData("paintings.json")]
        [InlineData("paintings/sunset.png")]
        [InlineData("images/Abigail portrait (x4).png")]
        [InlineData("images/a-b_c.2.jpg")]
        public void AllowsPlainRelativePaths(string path)
        {
            Assert.True(ContentValidator.IsSafeRelativePath(path));
        }

        [Theory]
        [InlineData("../secrets.json")]                 // climbing out
        [InlineData("images/../../secrets.png")]
        [InlineData("/etc/passwd")]                     // absolute
        [InlineData("C:\\Windows\\win.ini")]            // absolute, other OS
        [InlineData("images\\sunset.png")]              // backslash
        [InlineData("CON.png")]                         // reserved on Windows
        [InlineData("images/con.json")]
        [InlineData("images/sunset.png ")]              // trailing space
        [InlineData("images/sunset.")]                  // trailing dot
        [InlineData("")]
        public void RefusesPathTricks(string path)
        {
            Assert.False(ContentValidator.IsSafeRelativePath(path));
        }

        [Fact]
        public void RewritesJsonWithoutCommentsOrTypeHints()
        {
            byte[] sent = Encoding.UTF8.GetBytes("""
                {
                    // a comment
                    "$type": "System.Something, mscorlib",
                    "Paintings": [ { "Id": "sunset", "Price": 500 } ]
                }
                """);

            Assert.True(ContentValidator.TrySanitize(".json", sent, out byte[] clean, out string error), error);

            string text = Encoding.UTF8.GetString(clean);
            Assert.DoesNotContain("$type", text);
            Assert.DoesNotContain("a comment", text);
            Assert.Contains("sunset", text);
        }

        [Fact]
        public void RefusesJsonThatIsntAnObject()
        {
            Assert.False(ContentValidator.TrySanitize(".json", Encoding.UTF8.GetBytes("[1, 2, 3]"), out _, out _));
        }

        [Fact]
        public void RefusesJsonThatIsntJson()
        {
            Assert.False(ContentValidator.TrySanitize(".json", Encoding.UTF8.GetBytes("not json at all"), out _, out _));
        }

        [Fact]
        public void RebuildsAnImageAsPlainPng()
        {
            byte[] sent = MakePng(8, 4);
            Assert.True(ContentValidator.TrySanitize(".png", sent, out byte[] clean, out string error), error);

            // a PNG, and only a PNG: whatever was appended to the original is gone
            Assert.True(clean.Length >= 8 && clean[0] == 0x89 && clean[1] == (byte)'P' && clean[2] == (byte)'N' && clean[3] == (byte)'G');
            Pixels rebuilt = SafePng.Decode(clean, 8192, 8192L * 8192);
            Assert.Equal(8, rebuilt.Width);
            Assert.Equal(4, rebuilt.Height);
        }

        [Fact]
        public void DropsAnythingHiddenAfterTheImage()
        {
            byte[] image = MakePng(4, 4);
            byte[] withZip = new byte[image.Length + 5];
            Array.Copy(image, withZip, image.Length);
            Encoding.ASCII.GetBytes("PK\u0003\u0004!").CopyTo(withZip, image.Length);

            Assert.True(ContentValidator.TrySanitize(".png", withZip, out byte[] clean, out string error), error);
            Assert.True(clean.Length < withZip.Length);
        }

        [Fact]
        public void RefusesAnImageThatIsntOne()
        {
            Assert.False(ContentValidator.TrySanitize(".png", Encoding.ASCII.GetBytes("MZ this is a program"), out _, out _));
        }

        [Theory]
        [InlineData("imported/sunset.png", "imported/sunset.png")]   // the folder matters: flattening it loses the file
        [InlineData("sunset.png", "sunset.png")]
        [InlineData("imported\\sunset.png", "imported/sunset.png")] // a Windows path still names the same image
        [InlineData("../../secrets.png", "secrets.png")]             // tricks keep only the name
        [InlineData("/etc/passwd", "passwd")]
        [InlineData(null, "")]
        public void KeepsTheFolderAnImageIsIn(string? reference, string expected)
        {
            Assert.Equal(expected, CustomContent.SafeContentPath(reference));
        }

        /// <summary>A small PNG to feed the checks.</summary>
        private static byte[] MakePng(int width, int height)
        {
            Microsoft.Xna.Framework.Color[] pixels = new Microsoft.Xna.Framework.Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Microsoft.Xna.Framework.Color(i * 7 % 256, i * 13 % 256, i * 29 % 256, 255);
            return SafePng.Encode(new Pixels(pixels, width, height));
        }
    }
}
