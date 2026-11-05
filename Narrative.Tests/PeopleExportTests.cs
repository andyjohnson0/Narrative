using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Narrative.App;
using Narrative.App.Data;

namespace Narrative.Tests
{
    /// <summary>
    /// Conversations between people across WhatsApp and email: Alex and Sam have a WhatsApp chat, a
    /// family group with Jo, and emails, some copied to Jo and one to Jo alone.
    /// </summary>
    public class PeopleExportTests : IDisposable
    {
        private readonly TempDirectory temp = new();
        private readonly string db;
        private readonly string input;

        public PeopleExportTests()
        {
            input = temp.SubDirectory("in");
            File.WriteAllText(Path.Combine(input, "WhatsApp Chat with Sam Taylor.txt"), string.Join("\n",
                "01/07/2021, 09:00 - Messages and calls are end-to-end encrypted. No one outside of this chat can read them.",
                "01/07/2021, 09:00 - Alex Smith: Chat 1",
                "01/07/2021, 12:00 - Sam Taylor: Chat 2",
                ""));
            File.WriteAllText(Path.Combine(input, "WhatsApp Chat with Family.txt"), string.Join("\n",
                "01/07/2021, 09:00 - Messages and calls are end-to-end encrypted. No one outside of this chat can read them.",
                "01/07/2021, 10:30 - Jo Brown: Group 1",
                "01/07/2021, 10:31 - Sam Taylor: Group 2",
                "01/07/2021, 10:32 - Alex Smith: Group 3",
                ""));
            WriteEmail("1.eml", "Alex Smith <alex@example.com>", "Sam Taylor <sam@example.org>", null, "Thu, 1 Jul 2021 09:30:20 +0100", "Lunch?", "Email 1", "<1@x>", null);
            WriteEmail("2.eml", "Sam Taylor <sam@example.org>", "Alex Smith <alex@example.com>", "Jo Brown <jo@example.net>", "Thu, 1 Jul 2021 10:45:00 +0100", "Re: Lunch?", "Email 2\n\n> Email 1\n\nSee you", "<2@x>", "<1@x>");
            WriteEmail("3.eml", "Alex Smith <alex@example.com>", "Jo Brown <jo@example.net>", null, "Thu, 1 Jul 2021 11:00:00 +0100", "Just Jo", "Email 3", "<3@x>", null);
            db = temp.File("test.db");
            RunOk("-init", "-import", input);
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private void WriteEmail(string name, string from, string to, string? cc, string date, string subject, string body, string id, string? inReplyTo)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            if (cc != null)
            {
                message.Cc.Add(MailboxAddress.Parse(cc));
            }
            message.Headers.Replace(HeaderId.Date, date);
            message.Subject = subject;
            message.MessageId = id.Trim('<', '>');
            if (inReplyTo != null)
            {
                message.InReplyTo = inReplyTo.Trim('<', '>');
            }
            message.Body = new TextPart("plain") { Text = body };
            message.WriteTo(Path.Combine(input, name));
        }

        private (int ExitCode, string Output, string Error) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = Cli.Run(args.Concat(new[] { "-dbname", db }).ToArray(), output, error);
            return (exitCode, output.ToString(), error.ToString());
        }

        private string RunOk(params string[] args)
        {
            var (exitCode, output, error) = Run(args);
            Assert.True(exitCode == 0, error);
            return output;
        }

        private List<string> ExportTexts(params string[] extra)
        {
            var path = temp.File($"out{Guid.NewGuid():N}.json");
            RunOk(new[] { "-export", path }.Concat(extra).ToArray());
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("messages").EnumerateArray()
                .Select(m => m.GetProperty("text").GetString()!.Split('\n')[0])
                .ToList();
        }

        [Fact]
        public void TheConversationBetweenTwoPeopleSpansChatsAndEmail()
        {
            Assert.Equal(
                new[] { "Chat 1", "Email 1", "Group 2", "Group 3", "Email 2", "Chat 2" },
                ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor"));
        }

        [Fact]
        public void PeopleCanBeNamedByAddress()
        {
            Assert.Equal(
                ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor"),
                ExportTexts("-person", "ALEX@example.com", "-person", "sam@example.org"));
        }

        [Fact]
        public void DirectLeavesOutGroupsAndOtherRecipients()
        {
            Assert.Equal(
                new[] { "Chat 1", "Email 1", "Chat 2" },
                ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor", "-direct"));
        }

        [Fact]
        public void SourceLimitsTheExport()
        {
            Assert.Equal(new[] { "Email 1", "Email 2" }, ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor", "-source", "email"));
            Assert.Equal(new[] { "Chat 1", "Group 2", "Group 3", "Chat 2" }, ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor", "-source", "WhatsApp"));
        }

        [Fact]
        public void JsonDescribesEachEmail()
        {
            var path = temp.File("out.json");
            RunOk("-export", path, "-person", "Alex Smith", "-person", "Sam Taylor", "-source", "email", "-me", "Alex Smith");

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            Assert.Equal("Alex Smith and Sam Taylor", root.GetProperty("title").GetString());
            Assert.False(root.TryGetProperty("chat", out _));
            Assert.Equal("Email", root.GetProperty("selection").GetProperty("source").GetString());
            var reply = root.GetProperty("messages")[1];
            Assert.Equal("2021-07-01T10:45:00", reply.GetProperty("timestamp").GetString());
            Assert.Equal("Email", reply.GetProperty("source").GetString());
            Assert.Equal("Lunch? (2021-07-01)", reply.GetProperty("chat").GetString());
            Assert.Equal("Email 2\n\n> Email 1\n\nSee you", reply.GetProperty("text").GetString());
            var email = reply.GetProperty("email");
            Assert.Equal("Re: Lunch?", email.GetProperty("subject").GetString());
            Assert.Equal("2@x", email.GetProperty("messageId").GetString());
            Assert.Equal("2021-07-01T10:45:00+01:00", email.GetProperty("date").GetString());
            Assert.Equal(new[] { "Alex Smith" }, email.GetProperty("to").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(new[] { "Jo Brown" }, email.GetProperty("cc").EnumerateArray().Select(e => e.GetString()));
        }

        [Fact]
        public void CsvHasTheSourceChatAndSubject()
        {
            var path = temp.File("out.csv");
            RunOk("-export", path, "-person", "Alex Smith", "-person", "Sam Taylor");

            var lines = File.ReadAllLines(path);
            Assert.Equal("timestamp,person,text,kind,edited,attachment,source,chat,subject", lines[0]);
            Assert.Equal("2021-07-01 09:00:00,\"Alex Smith\",\"Chat 1\",text,false,\"\",WhatsApp,\"Sam Taylor\",\"\"", lines[1]);
            Assert.Equal("2021-07-01 09:30:20,\"Alex Smith\",\"Email 1\",text,false,\"\",Email,\"Lunch? (2021-07-01)\",\"Lunch?\"", lines[2]);
            Assert.Equal("2021-07-01 10:45:00,\"Sam Taylor\",\"Email 2 > Email 1 See you\",text,false,\"\",Email,\"Lunch? (2021-07-01)\",\"Re: Lunch?\"", lines[5]);
        }

        [Fact]
        public void JsonlLeavesOutEmailQuotes()
        {
            var path = temp.File("out.jsonl");
            RunOk("-export", path, "-person", "Alex Smith", "-person", "Sam Taylor");

            var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement)
                .Select(m => (m.GetProperty("userid").GetString(), m.GetProperty("content").GetString()));
            Assert.Equal(
                new[]
                {
                    ("Alex Smith", "Chat 1"), ("Alex Smith", "Email 1"), ("Sam Taylor", "Group 2"), ("Alex Smith", "Group 3"),
                    ("Sam Taylor", "Email 2\n\nSee you"), ("Sam Taylor", "Chat 2"),
                },
                lines);
        }

        [Fact]
        public void HtmlAndMarkdownShowSubjects()
        {
            var html = temp.File("out.html");
            var md = temp.File("out.md");
            RunOk("-export", html, "-person", "Alex Smith", "-person", "Sam Taylor", "-me", "Alex Smith");
            RunOk("-export", md, "-person", "Alex Smith", "-person", "Sam Taylor");

            var page = File.ReadAllText(html);
            Assert.Contains("<title>Alex Smith and Sam Taylor</title>", page);
            Assert.Contains("<p class=\"subject\">Re: Lunch?</p>", page);
            Assert.Contains("Conversation · 6 messages", page);
            Assert.Contains("✉ *Re: Lunch?*", File.ReadAllText(md));
        }

        [Fact]
        public void AnEmailThreadExportsLikeAChat()
        {
            var path = temp.File("thread.html");
            RunOk("-export", path, "-chat", "Lunch? (2021-07-01)");

            var page = File.ReadAllText(path);
            Assert.Contains("Email thread · 2 messages", page);
            Assert.Contains("Email 2", page);
        }

        [Fact]
        public void WhatsAppFormatWritesEmailAsMessages()
        {
            var path = temp.File("out.txt");
            RunOk("-export", path, "-person", "Alex Smith", "-person", "Sam Taylor");

            Assert.Equal(new[]
            {
                "01/07/2021, 09:00 - Messages and calls are end-to-end encrypted. Only people in this chat can read, listen to, or share them. Learn more.",
                "01/07/2021, 09:00 - Alex Smith: Chat 1",
                "01/07/2021, 09:30 - Alex Smith: ✉ Lunch?",
                "Email 1",
                "01/07/2021, 10:31 - Sam Taylor: Group 2",
                "01/07/2021, 10:32 - Alex Smith: Group 3",
                "01/07/2021, 10:45 - Sam Taylor: ✉ Re: Lunch?",
                "Email 2",
                "",
                "> Email 1",
                "",
                "See you",
                "01/07/2021, 12:00 - Sam Taylor: Chat 2",
            }, File.ReadAllText(path).Split('\n').SkipLast(1));
        }

        /// <summary>Imports an email from Sam to Alex that is just one attached file.</summary>
        private void ImportAttachmentEmail(string id, string date, string subject, string fileName, byte[] bytes)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse("Sam Taylor <sam@example.org>"));
            message.To.Add(MailboxAddress.Parse("Alex Smith <alex@example.com>"));
            message.Headers.Replace(HeaderId.Date, date);
            message.Subject = subject;
            message.MessageId = id;
            message.Body = new MimePart("image", "png")
            {
                Content = new MimeContent(new MemoryStream(bytes)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = fileName,
            };
            var path = Path.Combine(input, id + ".eml");
            message.WriteTo(path);
            RunOk("-import", path);
        }

        [Fact]
        public void WhatsAppZipHoldsEmailAttachmentsAndImportsAsAChat()
        {
            ImportAttachmentEmail("4@x", "Thu, 1 Jul 2021 11:30:00 +0100", "Photo", "beach.jpg", new byte[] { 1, 2, 3 });

            var zip = temp.File("out.zip");
            RunOk("-export", zip, "-person", "Alex Smith", "-person", "Sam Taylor", "-source", "email");
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                Assert.Equal(
                    new[] { "WhatsApp Chat with Alex Smith and Sam Taylor.txt", "beach.jpg" },
                    archive.Entries.Select(e => e.FullName));
                using var reader = new StreamReader(archive.Entries[0].Open());
                Assert.Contains("01/07/2021, 11:30 - Sam Taylor: beach.jpg (file attached)\n✉ Photo\n", reader.ReadToEnd());
            }

            // Into a new database it's a WhatsApp chat, with the subjects as part of the text.
            var otherDb = temp.File("other.db");
            Assert.Equal(0, Cli.Run(new[] { "-init", "-import", zip, "-dbname", otherDb }, new StringWriter(), new StringWriter()));
            var json = temp.File("other.json");
            Assert.Equal(0, Cli.Run(new[] { "-export", json, "-chat", "Alex Smith and Sam Taylor", "-dbname", otherDb }, new StringWriter(), new StringWriter()));
            using var document = JsonDocument.Parse(File.ReadAllText(json));
            var messages = document.RootElement.GetProperty("messages").EnumerateArray().ToList();
            Assert.Equal(new[] { "✉ Lunch?\nEmail 1", "✉ Re: Lunch?\nEmail 2\n\n> Email 1\n\nSee you", "✉ Photo" },
                messages.Select(m => m.GetProperty("text").GetString()));
            Assert.Equal("stored", messages[2].GetProperty("attachment").GetProperty("state").GetString());
        }

        [Fact]
        public void EmailAttachmentsWithTheSameNameAreRenamedInWhatsAppFormat()
        {
            ImportAttachmentEmail("5@x", "Fri, 2 Jul 2021 09:00:00 +0100", "One", "image001.png", new byte[] { 1 });
            ImportAttachmentEmail("6@x", "Fri, 2 Jul 2021 10:00:00 +0100", "Two", "image001.png", new byte[] { 2 });
            ImportAttachmentEmail("7@x", "Fri, 2 Jul 2021 11:00:00 +0100", "Three", "image001.png", new byte[] { 1 });

            var zip = temp.File("out.zip");
            var (exitCode, _, error) = Run("-export", zip, "-person", "Alex Smith", "-person", "Sam Taylor", "-source", "email");
            Assert.Equal(0, exitCode);
            Assert.Equal("", error);
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            Assert.Equal(new byte[] { 1 }, ReadEntry(archive, "image001.png"));
            Assert.Equal(new byte[] { 2 }, ReadEntry(archive, "image001 (2).png"));
            using var reader = new StreamReader(archive.Entries[0].Open());
            var lines = reader.ReadToEnd().Split('\n');
            Assert.Contains("02/07/2021, 09:00 - Sam Taylor: image001.png (file attached)", lines);
            Assert.Contains("02/07/2021, 10:00 - Sam Taylor: image001 (2).png (file attached)", lines);
            Assert.Contains("02/07/2021, 11:00 - Sam Taylor: image001.png (file attached)", lines);
        }

        private static byte[] ReadEntry(System.IO.Compression.ZipArchive archive, string name)
        {
            using var stream = archive.GetEntry(name)!.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return bytes.ToArray();
        }

        [Theory]
        [InlineData(new[] { "-person", "Alex Smith" }, "two or more -person")]
        [InlineData(new[] { "-person", "Alex Smith", "-person", "Nobody" }, "No one called 'Nobody'")]
        [InlineData(new[] { "-chat", "Sam Taylor", "-direct" }, "can't be used with -chat")]
        [InlineData(new[] { "-person", "Alex Smith", "-person", "Sam Taylor", "-source", "fax" }, "Unknown source 'fax'")]
        public void BadSelectionsAreErrors(string[] args, string message)
        {
            var (exitCode, _, error) = Run(new[] { "-export", temp.File("x.json") }.Concat(args).ToArray());

            Assert.Equal(1, exitCode);
            Assert.Contains(message, error);
        }

        [Fact]
        public void AChatNameUsedByTwoSourcesNeedsSource()
        {
            var more = temp.SubDirectory("more");
            File.Copy(Path.Combine(input, "3.eml"), Path.Combine(more, "3b.eml"));
            File.AppendAllText(Path.Combine(more, "3b.eml"), "\n");
            using (var context = NarrativeDatabase.Open(db))
            {
                Assert.Equal(3, context.Emails.Count());
            }
            var email = Path.Combine(more, "4.eml");
            File.WriteAllText(email, File.ReadAllText(Path.Combine(input, "1.eml")).Replace("<1@x>", "<4@x>"));
            RunOk("-import", email, "-chat", "Sam Taylor");

            var (exitCode, _, error) = Run("-export", temp.File("x.json"), "-chat", "Sam Taylor");
            Assert.Equal(1, exitCode);
            Assert.Contains("Add -source", error);
            Assert.Equal(new[] { "Email 1" }, ExportTexts("-chat", "Sam Taylor", "-source", "email"));
        }

        [Fact]
        public void ListsShowSourcesAndAddresses()
        {
            var chats = RunOk("-list-chats", "-source", "email");
            Assert.Equal(2, chats.Trim().Split('\n').Length);
            Assert.All(chats.Trim().Split('\n'), l => Assert.Contains("(Email, ", l));
            Assert.Contains("Lunch? (2021-07-01) (Email, 2 messages", RunOk("-list-chats", "-person", "jo@example.net"));

            var people = RunOk("-list-people").ReplaceLineEndings("\n");
            Assert.Equal("Alex Smith <alex@example.com>\nJo Brown <jo@example.net>\nSam Taylor <sam@example.org>\n", people);
        }

        [Fact]
        public void AnAddressCanBeMadeAnAliasAfterImport()
        {
            var more = temp.SubDirectory("more");
            WriteEmailTo(more, "5.eml", "<5@x>");
            RunOk("-import", Path.Combine(more, "5.eml"));
            Assert.Contains("sam.home@example.org <sam.home@example.org>", RunOk("-list-people"));

            Assert.Contains("now another name for 'Sam Taylor'", RunOk("-alias", "sam.home@example.org", "Sam Taylor"));

            var people = RunOk("-list-people").ReplaceLineEndings("\n");
            Assert.Contains("Sam Taylor (also sam.home@example.org) <sam.home@example.org> <sam@example.org>\n", people);
            Assert.Equal(3, people.Trim().Split('\n').Length);
            Assert.Equal(
                new[] { "Chat 1", "Email 1", "Group 2", "Group 3", "Email 2", "Chat 2", "Email 5" },
                ExportTexts("-person", "Alex Smith", "-person", "Sam Taylor"));
        }

        [Fact]
        public void APersonWithWhatsAppMessagesCantBeMergedAfterImport()
        {
            var (exitCode, _, error) = Run("-alias", "Jo Brown", "Sam Taylor");

            Assert.Equal(1, exitCode);
            Assert.Contains("already has 1 imported messages", error);
        }

        private void WriteEmailTo(string folder, string name, string id)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse("sam.home@example.org"));
            message.To.Add(MailboxAddress.Parse("Alex Smith <alex@example.com>"));
            message.Headers.Replace(HeaderId.Date, "Fri, 2 Jul 2021 08:00:00 +0100");
            message.Subject = "From home";
            message.MessageId = id.Trim('<', '>');
            message.Body = new TextPart("plain") { Text = "Email 5" };
            message.WriteTo(Path.Combine(folder, name));
        }

        [Fact]
        public void ImportingAFolderReportsEmailFiles()
        {
            var more = temp.SubDirectory("more");
            WriteEmailTo(more, "5.eml", "<5@x>");

            var output = RunOk("-import", more);

            Assert.Contains("5.eml: 1 email(s): 1 new, 0 already stored", output);
            Assert.Contains("5.eml: already imported", RunOk("-import", more));
        }

        [Fact]
        public void ARepliesOnlyChainJoinsUpWhicheverOrderItArrives()
        {
            var folder = temp.SubDirectory("chain");
            // Each reply names only its parent (no References), so C can't know it belongs with A
            // until B arrives.
            WriteEmailTo(folder, "a.eml", "<a@chain>");
            foreach (var (name, id, parent) in new[] { ("b.eml", "<b@chain>", "<a@chain>"), ("c.eml", "<c@chain>", "<b@chain>") })
            {
                WriteEmailTo(folder, name, id);
                var message = MimeMessage.Load(Path.Combine(folder, name));
                message.InReplyTo = parent.Trim('<', '>');
                message.Subject = "Re: From home";
                message.WriteTo(Path.Combine(folder, name));
            }

            RunOk("-import", Path.Combine(folder, "c.eml"), Path.Combine(folder, "a.eml"));
            using (var context = NarrativeDatabase.Open(db))
            {
                Assert.Equal(2, context.Conversations.Count(c => c.Name.StartsWith("From home")));
            }
            RunOk("-import", Path.Combine(folder, "b.eml"));

            using (var context = NarrativeDatabase.Open(db))
            {
                var thread = context.Conversations.Include(c => c.Messages).Single(c => c.Name.StartsWith("From home"));
                Assert.Equal(3, thread.Messages.Count);
                Assert.Equal(2, context.ConversationParticipants.Count(p => p.ConversationId == thread.Id));
            }
        }
    }
}
