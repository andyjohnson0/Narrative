using Narrative.App;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class CliTests : IDisposable
    {
        private readonly TempDirectory temp = new();

        public void Dispose()
        {
            temp.Dispose();
        }

        private (int ExitCode, string Output, string Error) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = Cli.Run(args, output, error);
            return (exitCode, output.ToString(), error.ToString());
        }

        [Theory]
        [InlineData("-help")]
        [InlineData("-h")]
        [InlineData("--help")]
        public void HelpListsTheOptions(string option)
        {
            var (exitCode, output, _) = Run(option);

            Assert.Equal(0, exitCode);
            Assert.Contains("-export <file>", output);
            Assert.Contains("-help", output);
        }

        [Theory]
        [InlineData("-version")]
        [InlineData("--version")]
        public void VersionIsShown(string option)
        {
            var (exitCode, output, error) = Run(option);

            Assert.Equal(0, exitCode);
            Assert.Matches(@"^\d+\.\d+", output);
            Assert.Equal("", error);
        }

        [Fact]
        public void ArgumentsStartingWithAtAreNotResponseFiles()
        {
            var db = temp.File("my.db");
            Run("-init", "-dbname", db);

            var (exitCode, _, error) = Run("-list-people", "-chat", "@someone", "-dbname", db);

            Assert.True(exitCode == 0, error);
        }

        [Fact]
        public void InitCreatesDatabase()
        {
            var db = temp.File("my.db");

            var (exitCode, output, _) = Run("-init", "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(db));
            Assert.Contains("Created", output);
        }

        [Fact]
        public void InitCreatesMissingFolders()
        {
            var db = Path.Combine(temp.Path, "new", "folder", "my.db");

            var (exitCode, _, error) = Run("-init", "-dbname", db);

            Assert.True(exitCode == 0, error);
            Assert.True(File.Exists(db));
        }

        [Fact]
        public void OlderDatabasesAreReported()
        {
            var db = temp.File("old.db");
            Run("-init", "-dbname", db);
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 1";
                command.ExecuteNonQuery();
            }

            var (exitCode, _, error) = Run("-list-chats", "-dbname", db);

            Assert.Equal(1, exitCode);
            Assert.Contains("older version of narrative", error);
            Assert.Contains("-init -force", error);
        }

        [Fact]
        public void InitRefusesExistingDatabase()
        {
            var db = temp.File("my.db");
            Run("-init", "-dbname", db);

            var (exitCode, _, error) = Run("-init", "-dbname", db);

            Assert.Equal(1, exitCode);
            Assert.Contains("already exists", error);
        }

        [Fact]
        public void InitWithForceReplacesDatabase()
        {
            var db = temp.File("my.db");
            var chat = SyntheticChat.Generate(seed: 30, messageCount: 20);
            Run("-init", "-dbname", db);
            Run("-import", chat.Write(temp.Path, new ExportSpec(0, 20)), "-dbname", db);

            var (exitCode, _, _) = Run("-init", "-force", "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.Empty(TestDatabase.Messages(db));
        }

        [Fact]
        public void ImportNeedsAnExistingDatabase()
        {
            var chat = SyntheticChat.Generate(seed: 31, messageCount: 20);

            var (exitCode, _, error) = Run("-import", chat.Write(temp.Path, new ExportSpec(0, 20)), "-dbname", temp.File("missing.db"));

            Assert.Equal(1, exitCode);
            Assert.Contains("-init", error);
        }

        [Fact]
        public void ImportsWildcardAndReportsProgress()
        {
            var db = temp.File("my.db");
            var exports = temp.SubDirectory("exports");
            var chat = SyntheticChat.Generate(seed: 32, messageCount: 300);
            chat.Write(exports, new ExportSpec(0, 200));
            chat.Write(exports, new ExportSpec(150, 300));
            Run("-init", "-dbname", db);

            var (exitCode, output, error) = Run("-import", Path.Combine(exports, "*.txt"), "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.Equal("", error);
            Assert.Contains("200 new", output);
            Assert.Contains("100 new, 50 already stored", output);
            Assert.Contains("Total: 300 new", output);
            Assert.Equal(300, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void ImportAcceptsSeveralFiles()
        {
            var db = temp.File("my.db");
            var chat = SyntheticChat.Generate(seed: 33, messageCount: 100);
            var a = chat.Write(temp.Path, new ExportSpec(0, 60));
            var b = chat.Write(temp.Path, new ExportSpec(40, 100));

            var (exitCode, _, _) = Run("-init", "-import", a, b, "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.Equal(100, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void QuietSuppressesProgress()
        {
            var db = temp.File("my.db");
            var chat = SyntheticChat.Generate(seed: 34, messageCount: 20);

            var (exitCode, output, _) = Run("-init", "-import", chat.Write(temp.Path, new ExportSpec(0, 20)), "-dbname", db, "-quiet");

            Assert.Equal(0, exitCode);
            Assert.Equal("", output);
            Assert.Equal(20, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void MissingImportFileIsAnError()
        {
            var db = temp.File("my.db");
            Run("-init", "-dbname", db);

            var (exitCode, _, error) = Run("-import", temp.File("nope.txt"), "-dbname", db);

            Assert.Equal(1, exitCode);
            Assert.Contains("not found", error);
        }

        [Fact]
        public void ListsChatsAndPeople()
        {
            var db = temp.File("my.db");
            var bob = SyntheticChat.Generate(seed: 35, messageCount: 50, name: "Bob Sample");
            var carol = SyntheticChat.Generate(seed: 36, messageCount: 50, name: "Carol Test");
            Run("-init", "-import", bob.Write(temp.Path, new ExportSpec(0, 50)), carol.Write(temp.Path, new ExportSpec(0, 50)), "-dbname", db);

            var chats = Run("-list-chats", "-dbname", db).Output;
            var bobChats = Run("-list-chats", "-person", "Bob Sample", "-dbname", db).Output;
            var people = Run("-list-people", "-dbname", db).Output;
            var carolPeople = Run("-list-people", "-chat", "Carol Test", "-dbname", db).Output;

            Assert.Equal(new[] { "Bob Sample", "Carol Test" }, Lines(chats).Select(l => l[..l.IndexOf(" (")]));
            Assert.Contains("50 messages", chats);
            Assert.Single(Lines(bobChats));
            Assert.Equal(new[] { "Alice Example", "Bob Sample", "Carol Test" }, Lines(people));
            Assert.Equal(new[] { "Alice Example", "Carol Test" }, Lines(carolPeople));
        }

        [Fact]
        public void ListOutputIsNotSuppressedByQuiet()
        {
            var db = temp.File("my.db");
            var chat = SyntheticChat.Generate(seed: 37, messageCount: 20);
            Run("-init", "-import", chat.Write(temp.Path, new ExportSpec(0, 20)), "-dbname", db);

            var (_, output, _) = Run("-list-people", "-quiet", "-dbname", db);

            Assert.Equal(2, Lines(output).Length);
        }

        [Fact]
        public void ImportsZipsByWildcardAndReportsAttachments()
        {
            var db = temp.File("my.db");
            var exports = temp.SubDirectory("zips");
            var chat = SyntheticChat.Generate(seed: 38, messageCount: 300);
            chat.WriteZip(exports, new ExportSpec(0, 200));
            chat.WriteZip(exports, new ExportSpec(150, 300), new[] { ("stray.jpg", new byte[] { 1 }) });

            var (exitCode, output, error) = Run("-init", "-import", Path.Combine(exports, "*.zip"), "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.Contains(".zip/WhatsApp Chat with Bob Sample.txt: chat 'Bob Sample'", output);
            Assert.Contains("attachments named", output);
            Assert.Contains("attachments stored, 0 missing", output);
            Assert.Contains("1 file(s) in the zip aren't named", error);
            Assert.Equal(300, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void ImportsAFolder()
        {
            var db = temp.File("my.db");
            var folder = temp.SubDirectory("mixed");
            var chat = SyntheticChat.Generate(seed: 39, messageCount: 200);
            chat.WriteZip(folder, new ExportSpec(0, 120));
            chat.WriteFolder(folder, new ExportSpec(100, 200));

            var (exitCode, _, error) = Run("-init", "-import", folder, "-dbname", db);

            Assert.Equal(0, exitCode);
            Assert.Equal("", error);
            Assert.Equal(200, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void NothingToDoIsAnError()
        {
            var (exitCode, _, error) = Run();

            Assert.Equal(1, exitCode);
            Assert.Contains("Nothing to do", error);
        }

        private static string[] Lines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
