using Microsoft.EntityFrameworkCore;
using MimeKit;
using Narrative.App;
using Narrative.App.Data;
using Narrative.App.Mail;
using Narrative.App.WhatsApp;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    /// <summary>
    /// An email as stored, flattened for comparison. <see cref="Thread"/> is the conversation id, which
    /// depends on import order, so comparisons use it only to check which emails share a thread.
    /// </summary>
    public sealed record StoredEmail(
        string MessageId,
        string Subject,
        string Sender,
        DateTime Timestamp,
        string Body,
        string Attachments,
        string Recipients,
        int Thread)
    {
        public StoredEmail WithoutThread() => this with { Thread = 0 };
    }

    public class EmailImportTests : IDisposable
    {
        private readonly TempDirectory temp = new();

        public void Dispose()
        {
            temp.Dispose();
        }

        public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5 };

        private static List<ImportResult> Import(string db, IEnumerable<string> paths, string? chatName = null)
        {
            var results = new List<ImportResult>();
            foreach (var path in paths)
            {
                using var context = NarrativeDatabase.Open(db);
                results.Add(new EmailImporter(context).Import(path, chatName));
            }
            return results;
        }

        public static List<StoredEmail> Stored(string db)
        {
            using var context = NarrativeDatabase.Open(db);
            return context.Emails.AsNoTracking()
                .Include(e => e.Recipients).ThenInclude(r => r.Person)
                .Include(e => e.Messages).ThenInclude(m => m.Sender)
                .Include(e => e.Messages).ThenInclude(m => m.Attachment)
                .AsSplitQuery()
                .AsEnumerable()
                .Select(e =>
                {
                    var messages = e.Messages.OrderBy(m => m.Seq).ToList();
                    return new StoredEmail(
                        e.InternetMessageId,
                        e.Subject,
                        messages[0].Sender!.DisplayName,
                        messages[0].Timestamp,
                        messages.Where(m => m.Kind == MessageKind.Text).Select(m => m.Text).SingleOrDefault() ?? "",
                        string.Join("|", messages.Where(m => m.Kind == MessageKind.Media).Select(m => m.AttachmentName + "=" + m.Attachment!.Sha256)),
                        string.Join("|", e.Recipients.Select(r => r.Role + ":" + r.Person.DisplayName).Order(StringComparer.Ordinal)),
                        messages[0].ConversationId);
                })
                .OrderBy(e => e.Timestamp).ThenBy(e => e.MessageId, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Checks the database holds exactly the generated emails, each in its own thread's conversation.</summary>
        private static void AssertMatches(SyntheticMailbox mailbox, List<StoredEmail> stored, bool withBcc)
        {
            Assert.Equal(mailbox.Emails.Count, stored.Count);
            foreach (var expected in mailbox.Emails)
            {
                var actual = expected.MessageId != null
                    ? stored.Single(s => s.MessageId == expected.MessageId)
                    : stored.Single(s => s.MessageId.StartsWith("sha256:", StringComparison.Ordinal) && s.Timestamp == expected.LocalTime);
                Assert.Equal(expected.Subject, actual.Subject);
                Assert.Equal(expected.From.StoredName, actual.Sender);
                Assert.Equal(expected.LocalTime, actual.Timestamp);
                Assert.Equal(expected.Body, actual.Body);
                Assert.Equal(
                    string.Join("|", expected.Attachments.Select(a => a.Name + "=" + TestDatabase.Sha256(a.Bytes))),
                    actual.Attachments);
                var recipients = expected.Recipients.Select(r => r.Role + ":" + r.Person.StoredName)
                    .Concat(withBcc ? expected.Bcc.Select(p => "Bcc:" + p.StoredName) : Enumerable.Empty<string>())
                    .Order(StringComparer.Ordinal);
                Assert.Equal(string.Join("|", recipients), actual.Recipients);
            }

            // Each generated thread is one conversation, and no two share one.
            var threads = mailbox.Emails.Zip(mailbox.Emails.Select(e => e.MessageId != null
                    ? stored.Single(s => s.MessageId == e.MessageId).Thread
                    : stored.Single(s => s.MessageId.StartsWith("sha256:", StringComparison.Ordinal) && s.Timestamp == e.LocalTime).Thread))
                .GroupBy(x => x.First.Thread, x => x.Second)
                .Select(g => g.Distinct().ToList())
                .ToList();
            Assert.All(threads, t => Assert.Single(t));
            Assert.Equal(threads.Count, threads.Select(t => t[0]).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void ReceivedCopiesImportAsGenerated(int seed)
        {
            var mailbox = new SyntheticMailbox(seed);
            var db = TestDatabase.Create(temp);

            var results = Import(db, mailbox.WriteEml(temp.SubDirectory("in"), sentCopies: false));

            Assert.All(results, r => Assert.Equal(ImportStatus.Imported, r.Status));
            AssertMatches(mailbox, Stored(db), withBcc: false);
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void CopiesInAnyOrderAndAnyFileGiveTheSameResult(int seed)
        {
            var mailbox = new SyntheticMailbox(seed);
            var received = mailbox.WriteEml(temp.SubDirectory("received"), sentCopies: false);
            var shuffled = received.OrderBy(_ => new Random(seed).Next()).ToList();
            new Random(seed).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shuffled));
            var mbox = mailbox.WriteMbox(temp.File("sent.mbox"), sentCopies: true);
            var reversedMbox = mailbox.WriteMbox(temp.File("sent-reversed.mbox"), sentCopies: true,
                Enumerable.Range(0, mailbox.Emails.Count).Reverse());

            var orders = new[]
            {
                received.Append(mbox).ToList(),
                new[] { mbox }.Concat(shuffled).ToList(),
                shuffled.Append(reversedMbox).ToList(),
                new[] { reversedMbox }.Concat(received).ToList(),
            };
            List<StoredEmail>? first = null;
            for (int i = 0; i < orders.Length; i++)
            {
                var db = TestDatabase.Create(temp, $"order{i}.db");
                Import(db, orders[i]);
                var stored = Stored(db);
                AssertMatches(mailbox, stored, withBcc: true);
                var content = stored.Select(s => s.WithoutThread()).ToList();
                if (first == null)
                {
                    first = content;
                }
                else
                {
                    Assert.Equal(first, content);
                }
            }
        }

        [Fact]
        public void TheSameEmailInTwoFilesIsStoredOnceWithASourceForEach()
        {
            var mailbox = new SyntheticMailbox(7);
            var db = TestDatabase.Create(temp);
            var eml = mailbox.WriteEml(temp.SubDirectory("in"), sentCopies: false);
            var mbox = mailbox.WriteMbox(temp.File("all.mbox"), sentCopies: false);

            Import(db, eml);
            var result = Import(db, new[] { mbox }).Single();

            Assert.Equal(mailbox.Emails.Count, result.Parsed);
            Assert.Equal(0, result.Added);
            Assert.Equal(mailbox.Emails.Count, result.Matched);
            using var context = NarrativeDatabase.Open(db);
            Assert.All(context.Messages.Include(m => m.Sources).ToList(), m => Assert.Equal(2, m.Sources.Count));
            Assert.Equal(mailbox.Emails.Count, context.Emails.Count());
        }

        [Fact]
        public void ReimportingAFileSkipsIt()
        {
            var mailbox = new SyntheticMailbox(1);
            var db = TestDatabase.Create(temp);
            var mbox = mailbox.WriteMbox(temp.File("all.mbox"), sentCopies: true);
            Import(db, new[] { mbox });
            var before = Stored(db);

            Assert.Equal(ImportStatus.AlreadyImported, Import(db, new[] { mbox }).Single().Status);
            Assert.Equal(before, Stored(db));
        }

        private string WriteEmail(string name, string date, string body = "Hello", string? extraHeaders = null, string? messageId = "<one@example>")
        {
            var path = temp.File(name);
            File.WriteAllText(path, string.Join("\r\n",
                "From: Sam Taylor <sam@example.org>",
                "To: Alex Smith <alex@example.com>",
                "Subject: Test",
                date.Length == 0 ? "X-No-Date: yes" : "Date: " + date,
                messageId == null ? "X-No-Id: yes" : "Message-ID: " + messageId,
                extraHeaders ?? "X-Nothing: here",
                "Content-Type: text/plain; charset=utf-8",
                "",
                body,
                ""));
            return path;
        }

        [Theory]
        [InlineData("Thu, 1 Jul 2021 10:00:00 +0000", "2021-07-01 11:00:00")]
        [InlineData("Thu, 1 Jul 2021 10:00:00 +0100", "2021-07-01 10:00:00")]
        [InlineData("Wed, 30 Jun 2021 19:30:15 -0800", "2021-07-01 04:30:15")]
        [InlineData("Mon, 4 Jan 2021 10:00:00 +0000", "2021-01-04 10:00:00")]
        [InlineData("Sun, 31 Oct 2021 01:30:00 +0000", "2021-10-31 01:30:00")]
        public void DatesAreStoredAsUkTime(string header, string expected)
        {
            var db = TestDatabase.Create(temp);
            Import(db, new[] { WriteEmail("a.eml", header) });

            var email = Stored(db).Single();
            Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), email.Timestamp);
        }

        [Fact]
        public void WithoutADateTheNewestReceivedHeaderIsUsed()
        {
            var db = TestDatabase.Create(temp);
            var path = WriteEmail("a.eml", "", extraHeaders: string.Join("\r\n",
                "Received: from b by c; Thu, 1 Jul 2021 10:05:00 +0000",
                "Received: from a by b; Thu, 1 Jul 2021 10:00:00 +0000"));

            Import(db, new[] { path });

            Assert.Equal(new DateTime(2021, 7, 1, 11, 5, 0), Stored(db).Single().Timestamp);
        }

        [Fact]
        public void AnEmailWithNoDateAtAllIsSkippedWithAWarning()
        {
            var db = TestDatabase.Create(temp);

            var result = Import(db, new[] { WriteEmail("a.eml", "") }).Single();

            Assert.Equal(ImportStatus.Empty, result.Status);
            Assert.Contains("no usable Date", Assert.Single(result.Warnings));
            Assert.Empty(Stored(db));
        }

        [Fact]
        public void ACopyWithADifferentSenderOrDateIsReported()
        {
            var db = TestDatabase.Create(temp);
            Import(db, new[] { WriteEmail("a.eml", "Thu, 1 Jul 2021 10:00:00 +0000") });

            var result = Import(db, new[] { WriteEmail("b.eml", "Thu, 1 Jul 2021 12:00:00 +0000", body: "Other") }).Single();

            Assert.Equal(1, result.Matched);
            Assert.Contains("different sender or date", Assert.Single(result.Warnings));
            Assert.Equal("Hello", Stored(db).Single().Body);
        }

        [Fact]
        public void ANonEmailFileIsAnError()
        {
            var db = TestDatabase.Create(temp);
            var path = temp.File("junk.eml");
            File.WriteAllBytes(path, Array.Empty<byte>());

            var exception = Assert.Throws<NarrativeException>(() => Import(db, new[] { path }));
            Assert.Contains("isn't an email", exception.Message);
            using var context = NarrativeDatabase.Open(db);
            Assert.Empty(context.ImportFiles);
        }

        [Fact]
        public void TheTrailingQuoteIsLeftOutButKeptAsTheSourceAndCrlfBecomesLf()
        {
            var db = TestDatabase.Create(temp);
            Import(db, new[] { WriteEmail("a.eml", "Thu, 1 Jul 2021 10:00:00 +0000", "Yes please  \r\n\r\nOn Wed, Sam wrote:\r\n> Lunch?\r\n> \r\n") });

            Assert.Equal("Yes please", Stored(db).Single().Body);
            using var context = NarrativeDatabase.Open(db);
            Assert.Equal("Yes please\n\nOn Wed, Sam wrote:\n> Lunch?\n>", context.MessageSources.Single().RawText);
        }

        [Fact]
        public void InlineQuotesAreKept()
        {
            var db = TestDatabase.Create(temp);
            Import(db, new[] { WriteEmail("a.eml", "Thu, 1 Jul 2021 10:00:00 +0000", "> Lunch?\r\nYes\r\n> Where?\r\nThe usual\r\n") });

            Assert.Equal("> Lunch?\nYes\n> Where?\nThe usual", Stored(db).Single().Body);
        }

        [Fact]
        public void TheTrailingQuoteOfAnHtmlOnlyEmailIsLeftOut()
        {
            var db = TestDatabase.Create(temp);
            var path = WriteEmail("a.eml", "Thu, 1 Jul 2021 10:00:00 +0000",
                "<html><body><div>Yes please</div><div class=\"gmail_quote\"><div>On Wed, Sam Taylor &lt;sam@example.org&gt; wrote:</div>" +
                "<blockquote><p>Lunch?</p><blockquote><p>Older</p></blockquote></blockquote></div></body></html>");
            File.WriteAllText(path, File.ReadAllText(path).Replace("text/plain", "text/html"));

            Import(db, new[] { path });

            Assert.Equal("Yes please", Stored(db).Single().Body);
        }

        [Fact]
        public void HtmlBecomesText()
        {
            var html = "<html><head><title>T</title><style>p{}</style></head><body><p>One<br>two &amp; three</p>\n" +
                       "<div>Four   five</div><blockquote><p>Quoted</p><p>Again</p></blockquote><script>x()</script></body></html>";

            Assert.Equal("One\ntwo & three\n\nFour five\n> Quoted\n>\n> Again", HtmlText.Convert(html));
        }

        [Fact]
        public void SubjectsLoseTheirReplyPrefixesInThreadNames()
        {
            Assert.Equal("Lunch", EmailImporter.ThreadSubject("Re: RE: Fwd: Lunch"));
            Assert.Equal("Lunch", EmailImporter.ThreadSubject("AW:Lunch"));
            Assert.Equal("(no subject)", EmailImporter.ThreadSubject("Re: "));
            Assert.Equal("Rex: Lunch", EmailImporter.ThreadSubject("Rex: Lunch"));
        }

        [Fact]
        public void ThreadsAreNamedBySubjectAndDate()
        {
            var mailbox = new SyntheticMailbox(1);
            var db = TestDatabase.Create(temp);
            Import(db, mailbox.WriteEml(temp.SubDirectory("in"), sentCopies: false));

            using var context = NarrativeDatabase.Open(db);
            var first = mailbox.Emails[0];
            var name = context.Messages.Where(m => m.Email!.InternetMessageId == first.MessageId).Select(m => m.Conversation.Name).First();
            Assert.Equal($"{first.Subject} ({first.LocalTime:yyyy-MM-dd})", name);
            Assert.Contains(context.Conversations, c => c.Name.StartsWith("(no subject) (", StringComparison.Ordinal));
            Assert.All(context.Conversations, c => Assert.Equal(Conversation.EmailSource, c.Source));
        }

        [Fact]
        public void ChatPutsEveryEmailInOneConversation()
        {
            var mailbox = new SyntheticMailbox(2);
            var db = TestDatabase.Create(temp);

            Import(db, mailbox.WriteEml(temp.SubDirectory("in"), sentCopies: false), chatName: "Mail with Sam");

            using var context = NarrativeDatabase.Open(db);
            var conversation = context.Conversations.Single();
            Assert.Equal("Mail with Sam", conversation.Name);
            Assert.Null(conversation.ThreadKey);
            Assert.Equal(mailbox.Emails.Count, context.Emails.Count());
        }

        [Fact]
        public void AttachmentsAreMediaMessagesAfterTheBody()
        {
            var mailbox = new SyntheticMailbox(3);
            var db = TestDatabase.Create(temp);
            Import(db, mailbox.WriteEml(temp.SubDirectory("in"), sentCopies: false));

            using var context = NarrativeDatabase.Open(db);
            var withFiles = mailbox.Emails.Where(e => e.Attachments.Count > 0).ToList();
            Assert.NotEmpty(withFiles);
            foreach (var expected in withFiles)
            {
                var messages = context.Messages.Include(m => m.Attachment)
                    .Where(m => m.Email!.Date != null && m.Timestamp == expected.LocalTime)
                    .OrderBy(m => m.Seq).ToList();
                var media = messages.Where(m => m.Kind == MessageKind.Media).ToList();
                Assert.Equal(expected.Attachments.Select(a => a.Name), media.Select(m => m.AttachmentName));
                Assert.All(media, m => Assert.Equal(AttachmentState.Stored, m.AttachmentState));
                Assert.Equal(expected.Body.Length > 0, messages[0].Kind == MessageKind.Text);
            }
            // The logos embedded in HTML bodies aren't kept.
            Assert.DoesNotContain(context.Attachments, a => a.FileName == "logo.png");
            Assert.All(context.Attachments.Where(a => a.FileName.EndsWith(".jpg")), a => Assert.Equal(AttachmentKind.Image, a.Kind));
        }

        [Fact]
        public void PeopleAreFoundByAddressThenByName()
        {
            var db = TestDatabase.Create(temp);
            var chat = temp.File("WhatsApp Chat with Sam Taylor.txt");
            File.WriteAllText(chat, string.Join("\n",
                "01/07/2021, 09:00 - Messages and calls are end-to-end encrypted. No one outside of this chat can read them.",
                "01/07/2021, 09:00 - Sam Taylor: Hi",
                ""));
            TestDatabase.Import(db, new[] { chat });

            Import(db, new[] { WriteEmail("a.eml", "Thu, 1 Jul 2021 10:00:00 +0000") });
            // The same address under another display name is still Sam.
            var other = WriteEmail("b.eml", "Thu, 1 Jul 2021 11:00:00 +0000", messageId: "<two@example>");
            File.WriteAllText(other, File.ReadAllText(other).Replace("Sam Taylor <sam@", "\"S. Taylor\" <SAM@"));
            Import(db, new[] { other });

            using var context = NarrativeDatabase.Open(db);
            var sam = context.People.Include(p => p.Addresses).Single(p => p.DisplayName == "Sam Taylor");
            Assert.Equal("sam@example.org", Assert.Single(sam.Addresses).Address);
            Assert.Equal(3, context.Messages.Count(m => m.SenderId == sam.Id));
            Assert.Equal(new[] { "Alex Smith", "Sam Taylor" }, context.People.Select(p => p.DisplayName).OrderBy(n => n).ToArray());
        }
    }
}
