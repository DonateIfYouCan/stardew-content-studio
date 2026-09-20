using System;
using System.IO;
using CustomContentCore;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Finding the image a data file names. Content written before images were kept in sub-folders names the file on its own,
    /// and hand-written content packs do the same, so a bare name has to be found one level down as well - otherwise the item
    /// is dropped for a missing picture.
    /// </summary>
    public class FindImageTests : IDisposable
    {
        private readonly string Folder = Path.Combine(Path.GetTempPath(), "content-studio-tests-" + Guid.NewGuid().ToString("N"));

        public FindImageTests()
        {
            Directory.CreateDirectory(Path.Combine(this.Folder, "images", "imported"));
            File.WriteAllText(Path.Combine(this.Folder, "images", "sunset.png"), "x");
            File.WriteAllText(Path.Combine(this.Folder, "images", "imported", "Abigail_-_1.png"), "x");
        }

        public void Dispose()
        {
            if (Directory.Exists(this.Folder))
                Directory.Delete(this.Folder, recursive: true);
        }

        private string Images => Path.Combine(this.Folder, "images");

        [Fact]
        public void FindsAnImageInTheImagesFolder()
        {
            Assert.Equal(Path.Combine(this.Images, "sunset.png"), CustomContent.FindImage("sunset.png", this.Images));
        }

        [Fact]
        public void FindsAnImageNamedWithItsFolder()
        {
            Assert.Equal(Path.Combine(this.Images, "imported", "Abigail_-_1.png"), CustomContent.FindImage("imported/Abigail_-_1.png", this.Images));
        }

        [Fact]
        public void FindsAnImageInASubFolderNamedWithoutOne()
        {
            // older data names the file alone although it was imported into 'imported/'
            Assert.Equal(Path.Combine(this.Images, "imported", "Abigail_-_1.png"), CustomContent.FindImage("Abigail_-_1.png", this.Images));
        }

        [Fact]
        public void FindsNothingForAnImageThatIsntThere()
        {
            Assert.Null(CustomContent.FindImage("nope.png", this.Images));
            Assert.Null(CustomContent.FindImage("", this.Images));
            Assert.Null(CustomContent.FindImage(null, this.Images));
        }

        [Fact]
        public void StaysInsideTheFoldersItIsGiven()
        {
            // a reference from another player can't be used to read somewhere else
            File.WriteAllText(Path.Combine(this.Folder, "secret.png"), "x");

            Assert.Null(CustomContent.FindImage("../secret.png", this.Images));
            Assert.Null(CustomContent.FindImage("/etc/passwd", this.Images));
        }

        [Fact]
        public void TriesTheFoldersInOrder()
        {
            // paintings look in the images folder first, then the mod folder
            File.WriteAllText(Path.Combine(this.Folder, "sunset.png"), "x");

            Assert.Equal(Path.Combine(this.Images, "sunset.png"), CustomContent.FindImage("sunset.png", this.Images, this.Folder));
        }
    }
}
