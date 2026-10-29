using System.Text.Json;
using Narrative.App;
using Narrative.App.Data;

namespace Narrative.Tests
{
    /// <summary>
    /// A person who changed their name between exports: older exports call them Sam, newer ones
    /// Samuel, and the exports overlap.
    /// </summary>
    public class AliasTests : IDisposable
    {
        private readonly TempDirectory temp = new();
        private readonly string db;
        private readonly string oldExport;
        private readonly string newExport;

        private static readonly string[] Shared =
        {
            "01/03/2021, 10:00 - {0}: Morning",
            "01/03/2021, 10:00 - Bob Sample: Hi",
            "01/03/2021, 10:00 - {0}: Morning",
            "02/03/2021, 18:30 - {0}: <Media omitted>",
            "02/03/2021, 18:31 - Bob Sample: Nice",
        };

        public AliasTests()
        {
            db = temp.File("test.db");
            oldExport = Write("WhatsApp Chat with Bob Sample 2021-06-01.txt", Lines("Sam Taylor"));
            newExport = Write("WhatsApp Chat with Bob Sample 2024-01-06.txt", Lines("Samuel Taylor").Concat(new[]
            {
                "05/01/2024, 09:00 - Samuel Taylor: New name, same me",
                "05/01/2024, 09:01 - Bob Sample: Ha",
            }));
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private static IEnumerable<string> Lines(string me)
        {
            return new[] { "01/03/2021, 10:00 - Messages and calls are end-to-end encrypted. No one outside of this chat can read them." }
                .Concat(Shared.Select(l => string.Format(l, me)));
        }

        private string Write(string name, IEnumerable<string> lines)
        {
            var path = temp.File(name);
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return path;
        }

        private (int ExitCode, string Output, string Error) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = Cli.Run(args.Concat(new[] { "-dbname", db }).ToArray(), output, error);
            return (exitCode, output.ToString(), error.ToString());
        }

        private void RunOk(params string[] args)
        {
            var (exitCode, _, error) = Run(args);
            Assert.True(exitCode == 0, error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OverlappingExportsUnderBothNamesMergeIntoOnePerson(bool newestFirst)
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor");
            var files = newestFirst ? new[] { newExport, oldExport } : new[] { oldExport, newExport };
            RunOk(new[] { "-import" }.Concat(files).ToArray());

            var messages = TestDatabase.Messages(db);
            Assert.Equal(7, messages.Count);
            Assert.Equal(4, messages.Count(m => m.Sender == "Samuel Taylor"));
            Assert.DoesNotContain(messages, m => m.Sender == "Sam Taylor");
            Assert.Equal("Bob Sample\nSamuel Taylor (also Sam Taylor)\n", Run("-list-people").Output.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void WithoutAnAliasTheOverlapIsStoredTwice()
        {
            RunOk("-init", "-import", oldExport, newExport);

            Assert.Equal(10, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void TheOriginalNameIsKeptInTheSourceLines()
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor", "-import", oldExport, newExport);

            using var context = NarrativeDatabase.Open(db);
            var names = context.MessageSources
                .Where(s => s.RawText == "Morning")
                .Select(s => s.SenderName).ToList();
            Assert.Equal(4, names.Count);
            Assert.Equal(2, names.Count(n => n == "Sam Taylor"));
            Assert.Equal(2, names.Count(n => n == "Samuel Taylor"));
            Assert.Single(context.Messages.Where(m => m.Text == "Morning").Select(m => m.SenderId).Distinct());
        }

        [Fact]
        public void AnAliasCanBeAddedToSomeoneAlreadyImported()
        {
            RunOk("-init", "-import", newExport);
            RunOk("-alias", "Sam Taylor", "Samuel Taylor");
            RunOk("-import", oldExport);

            Assert.Equal(7, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void AnAliasWithImportedMessagesIsRefused()
        {
            RunOk("-init", "-import", oldExport);

            var (exitCode, _, error) = Run("-alias", "Sam Taylor", "Samuel Taylor");

            Assert.Equal(1, exitCode);
            Assert.Contains("'Sam Taylor' already has 3 imported messages", error);
            Assert.Contains("-init", error);
        }

        [Fact]
        public void SettingAnAliasAgainIsHarmless()
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor");

            var (exitCode, output, _) = Run("-alias", "Sam Taylor", "Samuel Taylor");

            Assert.Equal(0, exitCode);
            Assert.Contains("already another name for 'Samuel Taylor'", output);
        }

        [Fact]
        public void AliasesChainOntoTheLatestName()
        {
            RunOk("-init", "-alias", "Sam T", "Sam Taylor");
            RunOk("-alias", "Sam Taylor", "Samuel Taylor");

            using var context = NarrativeDatabase.Open(db);
            var directory = new PersonDirectory(context);
            Assert.Equal("Samuel Taylor", directory.Find("Sam T")!.DisplayName);
            Assert.Equal("Samuel Taylor", directory.Find("Sam Taylor")!.DisplayName);
            Assert.Single(context.People);
        }

        [Fact]
        public void AnAliasOfAnAliasJoinsTheSamePerson()
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor");
            RunOk("-alias", "AJ", "Sam Taylor");

            using var context = NarrativeDatabase.Open(db);
            Assert.Equal("Samuel Taylor", new PersonDirectory(context).Find("AJ")!.DisplayName);
        }

        [Fact]
        public void ANameCannotBeAnAliasOfItself()
        {
            RunOk("-init");

            var (exitCode, _, error) = Run("-alias", "Sam Taylor", "Sam Taylor");

            Assert.Equal(1, exitCode);
            Assert.Contains("can't be an alias of itself", error);
        }

        [Fact]
        public void AliasNeedsTwoNames()
        {
            RunOk("-init");

            Assert.NotEqual(0, Run("-alias", "Sam Taylor").ExitCode);
        }

        [Fact]
        public void CsvUsesTheMainName()
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor", "-import", oldExport, newExport);
            var path = temp.File("out.csv");

            RunOk("-export", path, "-chat", "Bob Sample");

            var lines = File.ReadAllLines(path);
            Assert.Equal(8, lines.Length);
            Assert.Equal("2021-03-01 10:00:00,\"Samuel Taylor\",\"Morning\",text,false,\"\",WhatsApp,\"Bob Sample\",\"\"", lines[1]);
            Assert.DoesNotContain(lines, l => l.Contains("Sam Taylor"));
        }

        [Fact]
        public void EitherNameSelectsThePersonForListingAndExport()
        {
            RunOk("-init", "-alias", "Sam Taylor", "Samuel Taylor", "-import", oldExport, newExport);

            Assert.Contains("Bob Sample", Run("-list-chats", "-person", "Sam Taylor").Output);

            var path = temp.File("out.json");
            RunOk("-export", path, "-chat", "Bob Sample", "-person", "Sam Taylor", "-me", "Sam Taylor");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var selection = json.RootElement.GetProperty("selection");
            Assert.Equal(new[] { "Samuel Taylor" }, selection.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
            Assert.Equal("Samuel Taylor", selection.GetProperty("me").GetString());
            var senders = json.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("sender").GetString()).ToList();
            Assert.Equal(4, senders.Count);
            Assert.All(senders, s => Assert.Equal("Samuel Taylor", s));
        }
    }
}
