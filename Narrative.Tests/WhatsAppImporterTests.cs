using Microsoft.EntityFrameworkCore;
using Narrative.App;
using Narrative.App.Data;
using Narrative.App.WhatsApp;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class WhatsAppImporterTests
    {
        /// <summary>
        /// The messages the database should hold after importing <paramref name="exports"/> of <paramref name="chat"/>.
        /// </summary>
        private static List<StoredMessage> Expected(SyntheticChat chat, params ExportSpec[] exports)
        {
            // OrderBy is stable, so messages keep their sent order within a minute.
            return chat.Messages
                .Where(m => exports.Any(e => e.Contains(m.Index)))
                .Select(m => new StoredMessage(chat.Name, SyntheticChat.ExpectedTimestamp(m, exports), m.Sender, chat.ExpectedContent(m, exports)))
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        [Fact]
        public void ImportsASingleExport()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 1, messageCount: 500);
            var spec = new ExportSpec(0, 500);

            var result = Assert.Single(TestDatabase.Import(db, new[] { chat.Write(temp.Path, spec) }));

            Assert.Equal(ImportStatus.Imported, result.Status);
            Assert.Equal("Bob Sample", result.ChatName);
            Assert.Equal(500, result.Parsed);
            Assert.Equal(500, result.Added);
            Assert.Equal(Expected(chat, spec), TestDatabase.Messages(db));
        }

        [Fact]
        public void SkipsAFileAlreadyImported()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 2, messageCount: 100);
            var file = chat.Write(temp.Path, new ExportSpec(0, 100));

            var results = TestDatabase.Import(db, new[] { file, file });

            Assert.Equal(ImportStatus.AlreadyImported, results[1].Status);
            Assert.Equal(100, TestDatabase.Messages(db).Count);
        }

        [Fact]
        public void IdenticalContentInADifferentFileAddsNothing()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 3, messageCount: 200);
            var spec = new ExportSpec(0, 200);
            var a = chat.Write(temp.Path, spec, "WhatsApp Chat with Bob Sample.txt");
            var b = temp.File("WhatsApp Chat with Bob Sample 2024-01-01.txt");
            File.WriteAllText(b, chat.Render(spec) + "\n"); // different bytes, same messages

            var results = TestDatabase.Import(db, new[] { a, b });

            Assert.Equal(0, results[1].Added);
            Assert.Equal(200, results[1].Matched);
            Assert.Equal(Expected(chat, spec), TestDatabase.Messages(db));
        }

        public static TheoryData<int> Seeds => new() { 10, 11, 12, 13, 14 };

        [Theory]
        [MemberData(nameof(Seeds))]
        public void OverlappingExportsGiveTheSameResultInAnyOrder(int seed)
        {
            var chat = SyntheticChat.Generate(seed, messageCount: 1500);
            // Like real exports: two from the start (one without media), then later partial ones,
            // with edits and deletes happening between them.
            var exports = new[]
            {
                new ExportSpec(0, 700, WithMedia: false),
                new ExportSpec(0, 1000),
                new ExportSpec(950, 1300),
                new ExportSpec(1250, 1500),
            };
            var expected = Expected(chat, exports);

            using var temp = new TempDirectory();
            var files = exports.Select(e => chat.Write(temp.Path, e)).ToArray();
            var orders = new[]
            {
                new[] { 0, 1, 2, 3 },
                new[] { 3, 2, 1, 0 },
                new[] { 1, 3, 0, 2 },
            };
            foreach (var order in orders)
            {
                var db = TestDatabase.Create(temp, $"order-{string.Join("", order)}.db");
                TestDatabase.Import(db, order.Select(i => files[i]));
                Assert.Equal(expected, TestDatabase.Messages(db));
            }
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void ExportsThatRoundTimesGiveTheSameResultInAnyOrder(int seed)
        {
            var chat = SyntheticChat.Generate(seed, messageCount: 1500);
            // As above, but two of the exports round times to the nearest minute, so about half their
            // messages are a minute later than in the others. One of those is without media too, so
            // its shifted messages are variants of the stored ones, not copies.
            var exports = new[]
            {
                new ExportSpec(0, 700, WithMedia: false) { RoundsTimes = true },
                new ExportSpec(0, 1000),
                new ExportSpec(950, 1300) { RoundsTimes = true },
                new ExportSpec(1250, 1500),
            };
            var expected = Expected(chat, exports);

            using var temp = new TempDirectory();
            var files = exports.Select(e => chat.Write(temp.Path, e)).ToArray();
            var orders = new[]
            {
                new[] { 0, 1, 2, 3 },
                new[] { 3, 2, 1, 0 },
                new[] { 1, 3, 0, 2 },
                new[] { 2, 0, 3, 1 },
            };
            foreach (var order in orders)
            {
                var db = TestDatabase.Create(temp, $"order-{string.Join("", order)}.db");
                TestDatabase.Import(db, order.Select(i => files[i]));
                Assert.Equal(expected, TestDatabase.Messages(db));
            }
        }

        [Fact]
        public void MediaExportFillsInAttachmentNames()
        {
            var t = new DateTime(2022, 5, 1, 12, 0, 0);
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = t, Sender = "Bob Sample", Attachment = "IMG-20220501-WA0001.jpg" },
                new SyntheticMessage { Index = 1, Timestamp = t, Sender = "Bob Sample", Attachment = "IMG-20220501-WA0002.jpg", Text = "look" },
            });
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);

            var results = TestDatabase.Import(db, new[]
            {
                chat.Write(temp.Path, new ExportSpec(0, 2, WithMedia: false)),
                chat.Write(temp.Path, new ExportSpec(0, 2, WithMedia: true)),
            });

            Assert.Equal(2, results[1].Matched);
            Assert.Equal(2, results[1].Updated);
            var stored = TestDatabase.Messages(db);
            Assert.Equal(new[] { "IMG-20220501-WA0001.jpg", "IMG-20220501-WA0002.jpg" }, stored.Select(m => m.Content.AttachmentName));
            Assert.Equal("look", stored[1].Content.Text);
        }

        [Fact]
        public void DeletedLaterKeepsTheOriginalText()
        {
            var t = new DateTime(2022, 5, 1, 12, 0, 0);
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = t, Sender = "Alice Example", Text = "oops", DeletedAt = 1 },
                new SyntheticMessage { Index = 1, Timestamp = t.AddMinutes(1), Sender = "Bob Sample", Text = "what?" },
            });
            using var temp = new TempDirectory();

            foreach (var reverse in new[] { false, true })
            {
                var db = TestDatabase.Create(temp, $"deleted-{reverse}.db");
                var files = new[]
                {
                    chat.Write(temp.Path, new ExportSpec(0, 1)),
                    chat.Write(temp.Path, new ExportSpec(0, 2)),
                };
                TestDatabase.Import(db, reverse ? files.Reverse() : files);

                var stored = TestDatabase.Messages(db);
                Assert.Equal(2, stored.Count);
                Assert.Equal(new MessageContent(MessageKind.Text, "oops", false, null), stored[0].Content);
            }
        }

        [Fact]
        public void EditedLaterTakesTheEditedText()
        {
            var t = new DateTime(2022, 5, 1, 12, 0, 0);
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = t, Sender = "Bob Sample", Text = "see you at 6", EditedAt = 1, EditedText = "see you at 7" },
                new SyntheticMessage { Index = 1, Timestamp = t.AddMinutes(5), Sender = "Alice Example", Text = "ok" },
            });
            using var temp = new TempDirectory();

            foreach (var reverse in new[] { false, true })
            {
                var db = TestDatabase.Create(temp, $"edited-{reverse}.db");
                var files = new[]
                {
                    chat.Write(temp.Path, new ExportSpec(0, 1)),
                    chat.Write(temp.Path, new ExportSpec(0, 2)),
                };
                TestDatabase.Import(db, reverse ? files.Reverse() : files);

                var stored = TestDatabase.Messages(db);
                Assert.Equal(2, stored.Count);
                Assert.Equal(new MessageContent(MessageKind.Text, "see you at 7", true, null), stored[0].Content);
            }
        }

        [Fact]
        public void IdenticalMessagesInTheSameMinuteAreBothKept()
        {
            var t = new DateTime(2022, 5, 1, 12, 0, 0);
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = t, Sender = "Bob Sample", Text = "ha" },
                new SyntheticMessage { Index = 1, Timestamp = t, Sender = "Alice Example", Text = "what" },
                new SyntheticMessage { Index = 2, Timestamp = t, Sender = "Bob Sample", Text = "ha" },
            });
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);

            TestDatabase.Import(db, new[]
            {
                chat.Write(temp.Path, new ExportSpec(0, 3)),
                chat.Write(temp.Path, new ExportSpec(1, 3)),
            });

            var stored = TestDatabase.Messages(db);
            Assert.Equal(new[] { "Bob Sample", "Alice Example", "Bob Sample" }, stored.Select(m => m.Sender));
        }

        [Fact]
        public void RecordsEverySourceOfAMessage()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 4, messageCount: 50);

            TestDatabase.Import(db, new[]
            {
                chat.Write(temp.Path, new ExportSpec(0, 30)),
                chat.Write(temp.Path, new ExportSpec(20, 50)),
            });

            using var ctx = NarrativeDatabase.Open(db);
            Assert.Equal(2, ctx.ImportFiles.Count());
            Assert.Equal(60, ctx.MessageSources.Count());
            Assert.Equal(10, ctx.Messages.Count(m => m.Sources.Count == 2));
        }

        [Fact]
        public void CreatesPeopleAndParticipants()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 5, messageCount: 100);

            TestDatabase.Import(db, new[] { chat.Write(temp.Path, new ExportSpec(0, 100)) });

            using var ctx = NarrativeDatabase.Open(db);
            var conversation = ctx.Conversations.Include(c => c.Participants).ThenInclude(p => p.Person).Single();
            Assert.Equal("Bob Sample", conversation.Name);
            Assert.Equal(new[] { "Alice Example", "Bob Sample" }, conversation.Participants.Select(p => p.Person.DisplayName).Order());
        }

        [Fact]
        public void SeparateChatsAreKeptApart()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var bob = SyntheticChat.Generate(seed: 6, messageCount: 80, name: "Bob Sample");
            var carol = SyntheticChat.Generate(seed: 6, messageCount: 80, name: "Carol Test");

            TestDatabase.Import(db, new[]
            {
                bob.Write(temp.Path, new ExportSpec(0, 80)),
                carol.Write(temp.Path, new ExportSpec(0, 80)),
            });

            var stored = TestDatabase.Messages(db);
            Assert.Equal(80, stored.Count(m => m.Chat == "Bob Sample"));
            Assert.Equal(80, stored.Count(m => m.Chat == "Carol Test"));
        }

        [Fact]
        public void ChatNameCanBeGivenExplicitly()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 7, messageCount: 20);
            var file = chat.Write(temp.Path, new ExportSpec(0, 20), "export.txt");

            var result = Assert.Single(TestDatabase.Import(db, new[] { file }, chatName: "Bob"));

            Assert.Equal("Bob", result.ChatName);
        }

        [Fact]
        public void UnrecognisedFileNameNeedsAChatName()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var chat = SyntheticChat.Generate(seed: 8, messageCount: 20);
            var file = chat.Write(temp.Path, new ExportSpec(0, 20), "export.txt");

            var ex = Assert.Throws<NarrativeException>(() => TestDatabase.Import(db, new[] { file }));
            Assert.Contains("-chat", ex.Message);
        }

        [Fact]
        public void FileWithNoMessagesIsReportedEmpty()
        {
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            var file = temp.File("WhatsApp Chat with Nobody.txt");
            File.WriteAllText(file, "nothing here\n");

            var result = Assert.Single(TestDatabase.Import(db, new[] { file }));

            Assert.Equal(ImportStatus.Empty, result.Status);
            Assert.Equal(1, result.OrphanLines);
        }
    }
}
