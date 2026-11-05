using System.Globalization;
using MimeKit;
using MimeKit.Text;
using MimeKit.Utils;

namespace Narrative.Tests.Synthetic
{
    /// <summary>A generated person: a display name (or none) and an address.</summary>
    public sealed record MailPerson(string? Name, string Address)
    {
        public MailboxAddress Mailbox => new(Name ?? "", Address);

        /// <summary>The name narrative gives a new person: the display name, else the address.</summary>
        public string StoredName => Name ?? Address;
    }

    /// <summary>
    /// What the database should hold for one generated email, whichever copies of it were imported.
    /// </summary>
    public sealed class ExpectedEmail
    {
        public required int Thread { get; init; }

        /// <summary>The Message-ID, or null if the email has none.</summary>
        public required string? MessageId { get; init; }

        public required string Subject { get; init; }

        public required MailPerson From { get; init; }

        public required DateTimeOffset Date { get; init; }

        /// <summary>The body as narrative should store it: the sender's own text, without the quote.</summary>
        public required string Body { get; init; }

        /// <summary>The body as sent: the sender's text, then the previous email quoted.</summary>
        public required string FullBody { get; init; }

        public required IReadOnlyList<(string Name, byte[] Bytes)> Attachments { get; init; }

        /// <summary>Recipients named in every copy (To and Cc).</summary>
        public required IReadOnlyList<(MailPerson Person, string Role)> Recipients { get; init; }

        /// <summary>Bcc recipients, named only in the sender's copy.</summary>
        public required IReadOnlyList<MailPerson> Bcc { get; init; }

        /// <summary>The sender's copy (with Bcc) and the copy a recipient received (without).</summary>
        public required MimeMessage SentCopy { get; init; }

        public required MimeMessage ReceivedCopy { get; init; }

        public DateTime LocalTime => TimeZoneInfo.ConvertTime(Date, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime;
    }

    /// <summary>
    /// Generates a seeded set of email threads between a few people, with the variety real mailboxes
    /// have: replies quoting what they answer, plain, HTML-only, format=flowed and multipart bodies,
    /// attachments, inline images embedded in HTML (which aren't kept), Cc and Bcc, non-ASCII names,
    /// senders in other time zones and across the clock changes, an email without a Message-ID, and
    /// replies whose client left out References. Each email has a sender's copy and a received copy,
    /// which can be written as .eml files or into an mbox.
    /// </summary>
    public sealed class SyntheticMailbox
    {
        public static readonly MailPerson Me = new("Alex Smith", "alex@example.com");
        public static readonly MailPerson Sam = new("Sam Taylor", "sam.taylor@example.org");
        public static readonly MailPerson Jo = new("Zoë Ångström", "jo@example.net");
        public static readonly MailPerson Pat = new(null, "pat@example.co.uk");

        private static readonly MailPerson[] Others = { Sam, Jo, Pat };

        private static readonly string[] Words =
        {
            "lunch", "tomorrow", "plans", "photos", "the", "garden", "train", "meeting", "draft", "notes", "weekend",
            "holiday", "kitchen", "invoice", "\"quotes\"", "a,comma", "café", "naïve", "ok", "thanks", "see", "you",
        };

        private static readonly TimeSpan[] Offsets =
        {
            TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.FromHours(-5), TimeSpan.FromHours(-8), TimeSpan.FromHours(5.5),
        };

        public SyntheticMailbox(int seed, int threads = 6)
        {
            var random = new Random(seed);
            var emails = new List<ExpectedEmail>();
            // Starts just before the clocks go forward, so some threads cross the change.
            var time = new DateTimeOffset(2021, 3, 27, 22, 0, 0, TimeSpan.Zero);
            var count = 0;
            for (int t = 0; t < threads; t++)
            {
                var other = Others[random.Next(Others.Length)];
                var subject = t == 2 ? "" : Sentence(random, 3);
                var length = random.Next(1, 5);
                ExpectedEmail? previous = null;
                var references = new List<string>();
                for (int i = 0; i < length; i++)
                {
                    count += 1;
                    var from = i % 2 == 0 ? Me : other;
                    var to = from == Me ? other : Me;
                    time = time.AddMinutes(random.Next(1, 600)).AddSeconds(random.Next(60));
                    var offset = Offsets[random.Next(Offsets.Length)];
                    var email = Generate(random, seed, count, t, from, to, subject, time.ToOffset(offset), previous, references);
                    emails.Add(email);
                    if (email.MessageId != null)
                    {
                        references.Add(email.MessageId);
                    }
                    previous = email;
                }
            }
            Emails = emails;
        }

        public IReadOnlyList<ExpectedEmail> Emails { get; }

        private static ExpectedEmail Generate(Random random, int seed, int number, int thread, MailPerson from, MailPerson to,
            string subject, DateTimeOffset date, ExpectedEmail? previous, List<string> references)
        {
            // Thread 3's second email has no Message-ID; thread 4's replies leave out References.
            var messageId = thread == 3 && number % 2 == 0 && previous != null ? null : $"{seed}.{number}@synthetic.example";
            var keepReferences = thread != 4;

            var recipients = new List<(MailPerson, string)> { (to, "To") };
            if (random.Next(3) == 0)
            {
                recipients.Add((from == Jo || to == Jo ? Sam : Jo, "Cc"));
            }
            var bcc = random.Next(4) == 0 && from == Me ? new List<MailPerson> { Pat == to ? Sam : Pat } : new List<MailPerson>();

            // The body: new text, then the previous email (with its own quote) quoted, as a reply does.
            // narrative keeps only the new text.
            var paragraphs = Enumerable.Range(0, random.Next(1, 4))
                .Select(_ => string.Join("\n", Enumerable.Range(0, random.Next(1, 3)).Select(_ => Sentence(random, random.Next(2, 9)))))
                .ToList();
            var body = string.Join("\n\n", paragraphs);
            var fullBody = body;
            if (previous != null && previous.FullBody.Length > 0)
            {
                var quoted = string.Join("\n", previous.FullBody.Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
                fullBody += $"\n\nOn {previous.Date:ddd, d MMM yyyy}, {previous.From.StoredName} wrote:\n" + quoted;
            }

            var attachments = new List<(string, byte[])>();
            for (int a = random.Next(4) == 0 ? random.Next(1, 3) : 0; a > 0; a--)
            {
                var bytes = new byte[random.Next(10, 200)];
                random.NextBytes(bytes);
                attachments.Add(random.Next(2) == 0 ? ($"photo {number}-{a}.jpg", bytes) : ($"notes-{number}-{a}.pdf", bytes));
            }
            // One email is all attachment and no text.
            if (number == 5)
            {
                body = "";
                fullBody = "";
                if (attachments.Count == 0)
                {
                    attachments.Add(($"scan-{number}.pdf", new byte[] { 1, 2, 3, (byte)seed }));
                }
            }

            var style = number % 4;
            MimeMessage Build(bool withBcc)
            {
                var message = new MimeMessage();
                message.From.Add(from.Mailbox);
                foreach (var (person, role) in recipients)
                {
                    (role == "To" ? message.To : message.Cc).Add(person.Mailbox);
                }
                if (withBcc)
                {
                    message.Bcc.AddRange(bcc.Select(p => p.Mailbox));
                }
                message.Subject = Subject(subject, previous);
                message.Date = date;
                if (messageId != null)
                {
                    message.MessageId = messageId;
                }
                else
                {
                    // MimeKit gives every new message an id.
                    message.Headers.Remove(HeaderId.MessageId);
                }
                if (previous?.MessageId != null)
                {
                    message.InReplyTo = previous.MessageId;
                }
                if (previous != null && keepReferences)
                {
                    message.References.AddRange(references);
                }
                if (!withBcc)
                {
                    message.Headers.Add(HeaderId.Received, "from mx.example.org by mail.example.com; " + DateUtils.FormatDate(date.AddSeconds(5)));
                }
                message.Body = BodyFor(style, fullBody, attachments, number);
                return message;
            }

            return new ExpectedEmail
            {
                Thread = thread,
                MessageId = messageId,
                Subject = Subject(subject, previous).Trim(),
                From = from,
                Date = date,
                Body = body,
                FullBody = fullBody,
                Attachments = attachments,
                Recipients = recipients,
                Bcc = bcc,
                SentCopy = Build(withBcc: true),
                ReceivedCopy = Build(withBcc: false),
            };
        }

        private static string Subject(string subject, ExpectedEmail? previous)
        {
            return previous == null ? subject : "Re: " + subject;
        }

        /// <summary>
        /// The MIME body in one of four styles, each of which narrative should read as <paramref name="body"/>.
        /// </summary>
        private static MimeEntity BodyFor(int style, string body, List<(string Name, byte[] Bytes)> attachments, int number)
        {
            var builder = new BodyBuilder();
            switch (style)
            {
                case 0:
                    builder.TextBody = body;
                    break;
                case 1:
                    // Alternative versions: the plain one is what's kept.
                    builder.TextBody = body;
                    builder.HtmlBody = "<html><body><p>Ignore this HTML version</p></body></html>";
                    break;
                case 2:
                    // HTML only, with an embedded logo that isn't kept.
                    builder.HtmlBody = Html(body) + "<img src=\"cid:logo@example\">";
                    var logo = builder.LinkedResources.Add("logo.png", new byte[] { 137, 80, 78, 71, (byte)number });
                    logo.ContentId = "logo@example";
                    break;
                default:
                    // Format=flowed: long lines are sent wrapped with a trailing space at each soft break.
                    var text = new TextPart(TextFormat.Plain) { Text = Flow(body) };
                    text.ContentType.Parameters.Add("format", "flowed");
                    if (attachments.Count == 0)
                    {
                        return text;
                    }
                    var mixed = new Multipart("mixed") { text };
                    foreach (var (name, bytes) in attachments)
                    {
                        mixed.Add(Attachment(name, bytes));
                    }
                    return mixed;
            }
            foreach (var (name, bytes) in attachments)
            {
                builder.Attachments.Add(Attachment(name, bytes));
            }
            return builder.ToMessageBody();
        }

        private static MimePart Attachment(string name, byte[] bytes)
        {
            var type = name.EndsWith(".jpg", StringComparison.Ordinal) ? new ContentType("image", "jpeg") : new ContentType("application", "pdf");
            return new MimePart(type)
            {
                Content = new MimeContent(new MemoryStream(bytes)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = name,
            };
        }

        /// <summary>The body as HTML: paragraphs, line breaks, and blockquotes for quoted lines.</summary>
        private static string Html(string body)
        {
            var html = new System.Text.StringBuilder("<html><head><style>p { margin: 0 }</style></head><body>");
            var depth = 0;
            foreach (var paragraph in body.Split("\n\n"))
            {
                var lines = paragraph.Split('\n');
                var quoted = lines.All(l => l.StartsWith('>'));
                if (quoted != (depth > 0))
                {
                    html.Append(quoted ? "<blockquote>" : "</blockquote>");
                    depth = quoted ? 1 : 0;
                }
                var text = lines.Select(l => quoted ? l.TrimStart('>').TrimStart() : l);
                html.Append("<p>").Append(string.Join("<br>", text.Select(System.Net.WebUtility.HtmlEncode))).Append("</p>\n");
            }
            if (depth > 0)
            {
                html.Append("</blockquote>");
            }
            return html.Append("</body></html>").ToString();
        }

        /// <summary>Wraps each long line at spaces, flowed style. Quoted lines are short enough to leave.</summary>
        private static string Flow(string body)
        {
            return string.Join("\r\n", body.Split('\n').Select(line =>
            {
                if (line.Length <= 30 || line.StartsWith('>'))
                {
                    return line;
                }
                var cut = line.LastIndexOf(' ', 30);
                return cut <= 0 ? line : line[..(cut + 1)] + "\r\n" + line[(cut + 1)..];
            }));
        }

        private static string Sentence(Random random, int words)
        {
            var sentence = string.Join(" ", Enumerable.Range(0, words).Select(_ => Words[random.Next(Words.Length)]));
            return char.ToUpper(sentence[0], CultureInfo.InvariantCulture) + sentence[1..];
        }

        /// <summary>Writes each email's chosen copy as its own .eml file, and returns the paths.</summary>
        public List<string> WriteEml(string folder, bool sentCopies, IEnumerable<int>? indices = null)
        {
            Directory.CreateDirectory(folder);
            var paths = new List<string>();
            foreach (var i in indices ?? Enumerable.Range(0, Emails.Count))
            {
                var path = Path.Combine(folder, $"{(sentCopies ? "sent" : "received")}-{i:D3}.eml");
                (sentCopies ? Emails[i].SentCopy : Emails[i].ReceivedCopy).WriteTo(path);
                paths.Add(path);
            }
            return paths;
        }

        /// <summary>Writes the chosen copies into one mbox file.</summary>
        public string WriteMbox(string path, bool sentCopies, IEnumerable<int>? indices = null)
        {
            using var stream = File.Create(path);
            foreach (var i in indices ?? Enumerable.Range(0, Emails.Count))
            {
                var message = sentCopies ? Emails[i].SentCopy : Emails[i].ReceivedCopy;
                var from = Emails[i].From.Address;
                var line = $"From {from} {Emails[i].Date.UtcDateTime:ddd MMM d HH:mm:ss yyyy}\n";
                stream.Write(System.Text.Encoding.ASCII.GetBytes(line));
                using var body = new MemoryStream();
                var options = FormatOptions.Default.Clone();
                options.NewLineFormat = NewLineFormat.Unix;
                message.WriteTo(options, body);
                // mboxrd quoting: a body line starting "From " gets a ">".
                var text = System.Text.Encoding.UTF8.GetString(body.ToArray());
                text = System.Text.RegularExpressions.Regex.Replace(text, "^(>*From )", ">$1", System.Text.RegularExpressions.RegexOptions.Multiline);
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text.TrimEnd('\n') + "\n\n"));
            }
            return path;
        }
    }
}
