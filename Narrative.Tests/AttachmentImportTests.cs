using System.IO.Compression;
using System.Text;
using Narrative.App.Data;
using Narrative.App.WhatsApp;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class AttachmentImportTests : IDisposable
    {
        private readonly TempDirectory temp = new();

        public void Dispose()
        {
            temp.Dispose();
        }

        /// <summary>
        /// Expected attachment state and file hash per message, in conversation order.
        /// </summary>
        private static List<(AttachmentState, string?)> ExpectedAttachments(
            SyntheticChat chat, ExportSpec[] exports, ExportSpec[] supplying)
        {
            return chat.Messages
                .Where(m => exports.Any(e => e.Contains(m.Index)))
                .Select(m =>
                {
                    var (state, key) = chat.ExpectedAttachment(m, exports, supplying);
                    return (state, key == null ? null : TestDatabase.Sha256(SyntheticChat.AttachmentBytes(key)));
                })
                .ToList();
        }

        private static List<StoredMessage> ExpectedMessages(SyntheticChat chat, ExportSpec[] exports)
        {
            return chat.Messages
                .Where(m => exports.Any(e => e.Contains(m.Index)))
                .Select(m => new StoredMessage(chat.Name, m.Timestamp, m.Sender, chat.ExpectedContent(m, exports)))
                .ToList();
        }

        [Fact]
        public void ZipImportStoresAttachments()
        {
            var chat = SyntheticChat.Generate(seed: 40, messageCount: 400);
            var spec = new ExportSpec(0, 400);
            var db = TestDatabase.Create(temp);

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, spec) }));

            Assert.Equal(ImportStatus.Imported, result.Status);
            Assert.Equal("Bob Sample", result.ChatName);
            Assert.True(result.AttachmentsReferenced > 10);
            Assert.Equal(0, result.AttachmentsMissing);
            Assert.Equal(ExpectedMessages(chat, new[] { spec }), TestDatabase.Messages(db));
            Assert.Equal(ExpectedAttachments(chat, new[] { spec }, new[] { spec }), TestDatabase.Attachments(db));
        }

        [Fact]
        public void StoredAttachmentHasTypeAndBytes()
        {
            var t = new DateTime(2022, 5, 1, 12, 0, 0);
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = t, Sender = "Bob Sample", Attachment = "PTT-20220501-WA0001.opus" },
            });
            var db = TestDatabase.Create(temp);

            TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, new ExportSpec(0, 1)) });

            using var ctx = NarrativeDatabase.Open(db);
            var attachment = Assert.Single(ctx.Attachments);
            var data = Assert.Single(ctx.AttachmentData);
            var expected = SyntheticChat.AttachmentBytes("PTT-20220501-WA0001.opus");
            Assert.Equal(AttachmentKind.VoiceNote, attachment.Kind);
            Assert.Equal("audio/ogg", attachment.MediaType);
            Assert.Equal("PTT-20220501-WA0001.opus", attachment.FileName);
            Assert.Equal(expected.LongLength, attachment.Size);
            Assert.Equal(expected, data.Bytes);
        }

        [Fact]
        public void TextFileWithoutItsAttachmentsLeavesThemMissing()
        {
            var chat = SyntheticChat.Generate(seed: 41, messageCount: 300);
            var spec = new ExportSpec(0, 300);
            var db = TestDatabase.Create(temp);

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { chat.Write(temp.Path, spec) }));

            Assert.Equal(result.AttachmentsReferenced, result.AttachmentsMissing);
            var states = TestDatabase.Attachments(db).Select(a => a.State).ToList();
            Assert.Contains(AttachmentState.Missing, states);
            Assert.DoesNotContain(AttachmentState.Stored, states);
        }

        [Fact]
        public void MediaOutsideTheWindowIsOmittedAndDroppedFilesAreMissing()
        {
            var chat = SyntheticChat.Generate(seed: 42, messageCount: 600);
            var droppable = chat.Messages.Where(m => m.IsMedia && m.Index >= 400 && m.DeletedAt == null).Take(3).Select(m => m.Index).ToHashSet();
            var spec = new ExportSpec(0, 600) { MediaWindow = 300, DroppedFiles = droppable };
            var db = TestDatabase.Create(temp);

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, spec) }));

            Assert.Equal(3, result.AttachmentsMissing);
            var actual = TestDatabase.Attachments(db);
            Assert.Equal(ExpectedAttachments(chat, new[] { spec }, new[] { spec }), actual);
            Assert.Contains(actual, a => a.State == AttachmentState.Omitted);
            Assert.Equal(3, actual.Count(a => a.State == AttachmentState.Missing));
        }

        [Fact]
        public void ZipSuppliesAttachmentsForTextImportedEarlier()
        {
            var chat = SyntheticChat.Generate(seed: 43, messageCount: 300);
            var spec = new ExportSpec(0, 300);
            var db = TestDatabase.Create(temp);
            TestDatabase.ImportPaths(db, new[] { chat.Write(temp.Path, spec) });

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, spec) }));

            Assert.Equal(ImportStatus.AlreadyImported, result.Status);
            Assert.True(result.AttachmentsLinked > 0);
            Assert.Equal(ExpectedMessages(chat, new[] { spec }), TestDatabase.Messages(db));
            Assert.Equal(ExpectedAttachments(chat, new[] { spec }, new[] { spec }), TestDatabase.Attachments(db));
        }

        [Fact]
        public void LaterTextOnlyExportKeepsStoredAttachments()
        {
            var chat = SyntheticChat.Generate(seed: 44, messageCount: 300);
            var withMedia = new ExportSpec(0, 300);
            var withoutMedia = new ExportSpec(0, 300, WithMedia: false);
            var db = TestDatabase.Create(temp);

            TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, withMedia), chat.Write(temp.Path, withoutMedia) });

            var all = new[] { withMedia, withoutMedia };
            Assert.Equal(ExpectedAttachments(chat, all, new[] { withMedia }), TestDatabase.Attachments(db));
        }

        [Fact]
        public void IdenticalFilesAreStoredOnce()
        {
            var chat = SyntheticChat.Generate(seed: 45, messageCount: 1500);
            var a = new ExportSpec(0, 1000);
            var b = new ExportSpec(600, 1500);
            var db = TestDatabase.Create(temp);

            TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.Path, a), chat.WriteZip(temp.Path, b) });

            var distinctFiles = chat.Messages
                .Where(m => SyntheticChat.SuppliesFile(a, m) || SyntheticChat.SuppliesFile(b, m))
                .Select(m => m.ContentKey)
                .Distinct()
                .Count();
            var suppliedMessages = chat.Messages.Count(m => SyntheticChat.SuppliesFile(a, m) || SyntheticChat.SuppliesFile(b, m));
            Assert.True(distinctFiles < suppliedMessages, "The generator should have produced some forwarded files.");
            using var ctx = NarrativeDatabase.Open(db);
            Assert.Equal(distinctFiles, ctx.Attachments.Count());
            Assert.Equal(distinctFiles, ctx.AttachmentData.Count());
        }

        [Fact]
        public void UnreferencedFilesInAZipAreReportedNotStored()
        {
            var chat = SyntheticChat.Generate(seed: 46, messageCount: 100);
            var spec = new ExportSpec(0, 100);
            var db = TestDatabase.Create(temp);
            var zip = chat.WriteZip(temp.Path, spec, new[] { ("IMG-20991231-WA9999.jpg", new byte[] { 1, 2, 3 }) });

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { zip }));

            Assert.Equal(1, result.UnreferencedFiles);
            using var ctx = NarrativeDatabase.Open(db);
            Assert.DoesNotContain(ctx.Attachments, x => x.FileName == "IMG-20991231-WA9999.jpg");
        }

        [Fact]
        public void TextFileReadsAttachmentsFromItsFolder()
        {
            var chat = SyntheticChat.Generate(seed: 47, messageCount: 300);
            var spec = new ExportSpec(0, 300);
            var db = TestDatabase.Create(temp);

            TestDatabase.ImportPaths(db, new[] { chat.WriteFolder(temp.SubDirectory("extracted"), spec) });

            Assert.Equal(ExpectedAttachments(chat, new[] { spec }, new[] { spec }), TestDatabase.Attachments(db));
        }

        public static TheoryData<int> Seeds => new() { 50, 51, 52 };

        [Theory]
        [MemberData(nameof(Seeds))]
        public void MixedExportsGiveTheSameResultInAnyOrder(int seed)
        {
            var chat = SyntheticChat.Generate(seed, messageCount: 1500);
            var dropped = chat.Messages.Where(m => m.IsMedia && m.Index >= 1000 && m.Index < 1300).Take(4).Select(m => m.Index).ToHashSet();
            var exports = new[]
            {
                new ExportSpec(0, 700, WithMedia: false),
                new ExportSpec(0, 1000) { MediaWindow = 500 },
                new ExportSpec(950, 1300) { DroppedFiles = dropped },
                new ExportSpec(1250, 1500),
            };
            // The first two are zips, the third a bare text file (attachments missing), the last a folder.
            var supplying = new[] { exports[1], exports[3] };
            var expectedMessages = ExpectedMessages(chat, exports);
            var expectedAttachments = ExpectedAttachments(chat, exports, supplying);

            var paths = new[]
            {
                chat.WriteZip(temp.SubDirectory("z0"), exports[0]),
                chat.WriteZip(temp.SubDirectory("z1"), exports[1]),
                chat.Write(temp.SubDirectory("t2"), exports[2]),
                chat.WriteFolder(temp.SubDirectory("f3"), exports[3]),
            };
            foreach (var order in new[] { new[] { 0, 1, 2, 3 }, new[] { 3, 2, 1, 0 }, new[] { 2, 0, 3, 1 } })
            {
                var db = TestDatabase.Create(temp, $"mixed-{string.Join("", order)}.db");
                TestDatabase.ImportPaths(db, order.Select(i => paths[i]));
                Assert.Equal(expectedMessages, TestDatabase.Messages(db));
                Assert.Equal(expectedAttachments, TestDatabase.Attachments(db));
            }
        }

        [Fact]
        public void SharedTextDocumentInAZipIsNotTakenForTheChat()
        {
            var zipPath = temp.File("WhatsApp Chat with Bob Sample 2022-05-01.zip");
            var notes = "01/05/2022, 09:00 - Someone Else: this document looks like an export too\n";
            var chatText =
                "01/05/2022, 12:00 - Bob Sample: notes.txt (file attached)\n" +
                "01/05/2022, 12:01 - Alice Example: thanks\n";
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in new[] { ("WhatsApp Chat with Bob Sample.txt", chatText), ("notes.txt", notes) })
                {
                    using var stream = zip.CreateEntry(name).Open();
                    stream.Write(Encoding.UTF8.GetBytes(text));
                }
            }
            var db = TestDatabase.Create(temp);

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { zipPath }));

            Assert.Equal(2, result.Parsed);
            Assert.Equal(1, result.AttachmentsLinked);
            using var ctx = NarrativeDatabase.Open(db);
            Assert.Equal(AttachmentKind.Document, Assert.Single(ctx.Attachments).Kind);
        }

        [Fact]
        public void ChatNameFallsBackToTheZipName()
        {
            var zipPath = temp.File("WhatsApp Chat with Carol Test 2022-05-01.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using var stream = zip.CreateEntry("_chat.txt").Open();
                stream.Write(Encoding.UTF8.GetBytes("01/05/2022, 12:00 - Carol Test: hi\n"));
            }
            var db = TestDatabase.Create(temp);

            var result = Assert.Single(TestDatabase.ImportPaths(db, new[] { zipPath }));

            Assert.Equal("Carol Test", result.ChatName);
        }
    }
}
