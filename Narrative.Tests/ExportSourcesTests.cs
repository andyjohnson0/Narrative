using Narrative.App;
using Narrative.App.Sources;
using Narrative.App.WhatsApp;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class ExportSourcesTests : IDisposable
    {
        private readonly TempDirectory temp = new();

        public void Dispose()
        {
            temp.Dispose();
        }

        private static List<string> Resolve(params string[] specs)
        {
            using var exports = ExportSources.Resolve(specs, AndroidUkHeaderFormat.Instance);
            return exports.Sources.Select(s => s.DisplayName).ToList();
        }

        [Fact]
        public void FolderYieldsChatFilesAndZipsButNotOtherTextFiles()
        {
            var chat = SyntheticChat.Generate(seed: 60, messageCount: 50);
            var folder = temp.SubDirectory("exports");
            chat.Write(folder, new ExportSpec(0, 30));
            chat.WriteZip(folder, new ExportSpec(0, 50));
            File.WriteAllText(Path.Combine(folder, "readme.txt"), "not a chat\n");
            File.WriteAllText(Path.Combine(folder, "renamed.txt"), chat.Render(new ExportSpec(10, 20)));

            var sources = Resolve(folder);

            Assert.Equal(new[]
            {
                "renamed.txt",
                $"{chat.DefaultStem(new ExportSpec(0, 30))}.txt",
                $"{chat.DefaultStem(new ExportSpec(0, 50))}.zip/WhatsApp Chat with Bob Sample.txt",
            }, sources);
        }

        [Fact]
        public void WildcardMatchesZips()
        {
            var chat = SyntheticChat.Generate(seed: 61, messageCount: 60);
            chat.WriteZip(temp.Path, new ExportSpec(0, 30));
            chat.WriteZip(temp.Path, new ExportSpec(20, 60));

            var sources = Resolve(Path.Combine(temp.Path, "*.zip"));

            Assert.Equal(2, sources.Count);
            Assert.All(sources, s => Assert.EndsWith(".zip/WhatsApp Chat with Bob Sample.txt", s));
        }

        [Fact]
        public void ExplicitTextFileIsImportedWhateverItsName()
        {
            var file = temp.File("anything.txt");
            File.WriteAllText(file, "hello\n");

            Assert.Equal(new[] { "anything.txt" }, Resolve(file));
        }

        [Fact]
        public void ZipWithoutAChatFileIsAnError()
        {
            var zip = temp.File("photos.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                archive.CreateEntry("IMG-20220101-WA0001.jpg");
            }

            var ex = Assert.Throws<NarrativeException>(() => Resolve(zip));
            Assert.Contains("No WhatsApp chat text file", ex.Message);
        }

        [Fact]
        public void FolderWithNoExportsIsAnError()
        {
            var folder = temp.SubDirectory("empty");

            var ex = Assert.Throws<NarrativeException>(() => Resolve(folder));
            Assert.Contains("No WhatsApp exports", ex.Message);
        }

        [Fact]
        public void DirectoryContainerRejectsNamesOutsideTheFolder()
        {
            using var folder = new DirectoryContainer(temp.SubDirectory("inner"));
            File.WriteAllText(temp.File("secret.jpg"), "x");

            Assert.False(folder.Contains(@"..\secret.jpg"));
            Assert.False(folder.Contains("../secret.jpg"));
        }
    }
}
