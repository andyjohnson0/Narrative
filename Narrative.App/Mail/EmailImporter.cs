using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Narrative.App.Data;
using Narrative.App.WhatsApp;

namespace Narrative.App.Mail
{
    /// <summary>
    /// Imports .eml and mbox files. Each email becomes a message from its sender in its thread, plus a
    /// media message for each attachment, all linked to an <see cref="Data.Email"/> row with its subject
    /// and recipients.
    /// </summary>
    /// <remarks>
    /// The same email can arrive in several files (sent and received copies, an .eml and an mbox).
    /// Copies are matched by Message-ID and stored once; recipients are combined across them, since
    /// only the sender's copy names Bcc recipients.
    /// </remarks>
    public partial class EmailImporter
    {
        private readonly NarrativeDbContext db;

        public EmailImporter(NarrativeDbContext db)
        {
            this.db = db;
        }

        public static bool IsEmailFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mbox", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Imports one .eml or .mbox file.
        /// </summary>
        /// <param name="chatName">Put every email into the conversation with this name instead of its thread, or null.</param>
        public ImportResult Import(string path, string? chatName = null)
        {
            var fileName = Path.GetFileName(path);
            string sha256;
            using (var stream = File.OpenRead(path))
            {
                sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            if (db.ImportFiles.Any(f => f.Sha256 == sha256))
            {
                return new ImportResult { FileName = fileName, Status = ImportStatus.AlreadyImported };
            }

            var kind = Path.GetExtension(path).Equals(".mbox", StringComparison.OrdinalIgnoreCase)
                ? ImportFileKind.Mbox
                : ImportFileKind.Eml;
            var counts = new Counts();
            var warnings = new List<string>();

            using var transaction = db.Database.BeginTransaction();
            var importFile = new ImportFile
            {
                Kind = kind,
                FileName = fileName,
                Sha256 = sha256,
                ImportedAt = DateTime.Now,
            };
            db.ImportFiles.Add(importFile);
            db.SaveChanges();
            var importFileId = importFile.Id;
            var store = new AttachmentStore(db);
            DateTime? first = null, last = null;

            using (var stream = File.OpenRead(path))
            {
                var parser = new MimeParser(stream, kind == ImportFileKind.Mbox ? MimeFormat.Mbox : MimeFormat.Entity);
                for (int position = 1; !parser.IsEndOfStream; position++)
                {
                    MimeMessage message;
                    try
                    {
                        message = parser.ParseMessage();
                    }
                    catch (FormatException)
                    {
                        if (kind == ImportFileKind.Mbox && position == 1)
                        {
                            break;
                        }
                        throw new NarrativeException(kind == ImportFileKind.Mbox
                            ? $"'{fileName}': email {position} can't be read."
                            : $"'{fileName}' isn't an email file.");
                    }

                    using (message)
                    {
                        var parsed = EmailReader.Read(message, out var problem);
                        if (parsed == null)
                        {
                            warnings.Add(Where(fileName, kind, position) + $"skipped: {problem}");
                        }
                        else
                        {
                            counts.Parsed += 1;
                            ImportEmail(parsed, importFileId, position, chatName, store, counts, warnings, Where(fileName, kind, position));
                            first = first == null || parsed.LocalTime < first ? parsed.LocalTime : first;
                            last = last == null || parsed.LocalTime > last ? parsed.LocalTime : last;
                        }
                    }

                    // Each email is saved as it goes, so a big mbox doesn't pile up in the change tracker.
                    db.SaveChanges();
                    db.ChangeTracker.Clear();
                    if (kind == ImportFileKind.Eml)
                    {
                        break;
                    }
                }
            }

            if (counts.Parsed == 0)
            {
                transaction.Rollback();
                return new ImportResult { FileName = fileName, Status = ImportStatus.Empty, Warnings = warnings };
            }

            var file = db.ImportFiles.Single(f => f.Id == importFileId);
            file.FirstTimestamp = first!.Value;
            file.LastTimestamp = last!.Value;
            file.ParsedCount = counts.Parsed;
            file.AddedCount = counts.Added;
            file.MatchedCount = counts.Matched;
            file.UpdatedCount = counts.Updated;
            file.AttachmentsReferenced = counts.Attachments;
            db.SaveChanges();
            transaction.Commit();
            db.ChangeTracker.Clear();

            return new ImportResult
            {
                FileName = fileName,
                Status = ImportStatus.Imported,
                Parsed = counts.Parsed,
                Added = counts.Added,
                Matched = counts.Matched,
                Updated = counts.Updated,
                AttachmentsReferenced = counts.Attachments,
                AttachmentsLinked = counts.Attachments,
                Warnings = warnings,
            };
        }

        private sealed class Counts
        {
            public int Parsed { get; set; }

            public int Added { get; set; }

            public int Matched { get; set; }

            public int Updated { get; set; }

            public int Attachments { get; set; }
        }

        private static string Where(string fileName, ImportFileKind kind, int position)
        {
            return kind == ImportFileKind.Mbox ? $"{fileName}: email {position}: " : $"{fileName}: ";
        }

        private void ImportEmail(ParsedEmail parsed, int importFileId, int position, string? chatName, AttachmentStore store,
            Counts counts, List<string> warnings, string where)
        {
            var people = new People(db);
            var sender = parsed.From == null ? null : people.Get(parsed.From);

            var email = db.Emails
                .Include(e => e.Recipients)
                .Include(e => e.Messages)
                .SingleOrDefault(e => e.InternetMessageId == parsed.MessageId);
            if (email != null)
            {
                counts.Matched += 1;
                var stored = email.Messages[0];
                if (stored.SenderId != sender?.Id || email.Date != DateText(parsed.Date))
                {
                    warnings.Add(where + "another copy of this email has a different sender or date; the first one imported is kept.");
                }
                var conversation = LoadConversation(stored.ConversationId);
                if (AddRecipients(email, conversation, parsed, people))
                {
                    counts.Updated += 1;
                }
                AddSources(email.Messages, importFileId, position, parsed);
                return;
            }

            counts.Added += 1;
            var thread = chatName == null ? FindThread(parsed) : FindNamed(chatName);
            thread ??= NewThread(parsed);
            email = new Email
            {
                InternetMessageId = parsed.MessageId,
                Subject = parsed.Subject,
                InReplyTo = parsed.InReplyTo,
                References = parsed.References.Count == 0 ? null : string.Join(" ", parsed.References),
                Date = DateText(parsed.Date),
            };
            db.Emails.Add(email);
            AddParticipant(thread, sender);
            AddRecipients(email, thread, parsed, people);

            // Like a WhatsApp text sent with photos: the body, then a media message for each file.
            var seq = thread.Id == 0 ? 0 : db.Messages.Count(m => m.ConversationId == thread.Id && m.Timestamp == parsed.LocalTime);
            var messages = new List<Message>();
            if (parsed.Body.Length > 0 || parsed.Attachments.Count == 0)
            {
                messages.Add(NewMessage(MessageKind.Text, parsed.Body));
            }
            foreach (var attachment in parsed.Attachments)
            {
                var message = NewMessage(MessageKind.Media, "");
                message.AttachmentName = attachment.FileName;
                message.AttachmentId = store.GetOrCreate(attachment.FileName, attachment.Bytes, attachment.MediaType).Id;
                messages.Add(message);
                counts.Attachments += 1;
            }
            db.Messages.AddRange(messages);
            AddSources(messages, importFileId, position, parsed);

            Message NewMessage(MessageKind kind, string text)
            {
                return new Message
                {
                    Conversation = thread,
                    Sender = sender,
                    Timestamp = parsed.LocalTime,
                    Seq = seq++,
                    Kind = kind,
                    Text = text,
                    ContentAsOf = parsed.LocalTime,
                    Email = email,
                };
            }
        }

        private void AddSources(IEnumerable<Message> messages, int importFileId, int position, ParsedEmail parsed)
        {
            foreach (var message in messages)
            {
                db.MessageSources.Add(new MessageSource
                {
                    Message = message,
                    ImportFileId = importFileId,
                    LineNumber = position,
                    SenderName = parsed.From == null ? null : parsed.From.Name ?? parsed.From.Address,
                    RawText = "",
                });
            }
        }

        /// <summary>Adds the recipients this copy names that aren't stored yet. Returns true if there were any.</summary>
        private static bool AddRecipients(Email email, Conversation conversation, ParsedEmail parsed, People people)
        {
            var added = false;
            foreach (var recipient in parsed.Recipients)
            {
                var person = people.Get(recipient.Address);
                if (!email.Recipients.Any(r => r.Role == recipient.Role && (ReferenceEquals(r.Person, person) || (person.Id != 0 && r.PersonId == person.Id))))
                {
                    email.Recipients.Add(new EmailRecipient { Email = email, Person = person, Role = recipient.Role });
                    added = true;
                }
                AddParticipant(conversation, person);
            }
            return added;
        }

        private static void AddParticipant(Conversation conversation, Person? person)
        {
            if (person != null && !conversation.Participants.Any(p => ReferenceEquals(p.Person, person) || (person.Id != 0 && p.PersonId == person.Id)))
            {
                conversation.Participants.Add(new ConversationParticipant { Conversation = conversation, Person = person });
            }
        }

        private Conversation LoadConversation(int id)
        {
            return db.Conversations.Include(c => c.Participants).Single(c => c.Id == id);
        }

        /// <summary>
        /// The thread an email belongs to: that of the emails it refers to, of the emails that refer to
        /// it, and the one started by the same first email. Null if it starts a new thread.
        /// </summary>
        /// <remarks>
        /// Emails can arrive in any order, and some clients leave out References, so a reply can start a
        /// thread of its own before the email that links it to the rest arrives. That email then joins
        /// the threads together, and the result doesn't depend on the order of import.
        /// </remarks>
        private Conversation? FindThread(ParsedEmail parsed)
        {
            var referenced = parsed.References.Append(parsed.InReplyTo).OfType<string>().Distinct().ToList();
            var id = parsed.MessageId;
            var pattern = "%" + id + "%";
            var linked = db.Emails
                .Where(e => referenced.Contains(e.InternetMessageId)
                    || e.InReplyTo == id
                    || (e.References != null && EF.Functions.Like(e.References, pattern)))
                .SelectMany(e => e.Messages.Select(m => m.ConversationId))
                .ToList();
            var threadKey = parsed.ThreadKey;
            linked.AddRange(db.Conversations
                .Where(c => c.Source == Conversation.EmailSource && c.ThreadKey == threadKey)
                .Select(c => c.Id));
            var ids = linked.Distinct().Order().ToList();
            if (ids.Count == 0)
            {
                return null;
            }
            foreach (var other in ids.Skip(1))
            {
                MergeThreads(other, ids[0]);
            }
            return LoadConversation(ids[0]);
        }

        /// <summary>
        /// Moves one thread's messages and participants into another, and deletes it. The moved
        /// messages are renumbered among any others at the same time.
        /// </summary>
        private void MergeThreads(int from, int to)
        {
            var times = db.Messages.Where(m => m.ConversationId == from).Select(m => m.Timestamp).Distinct().ToList();
            db.Database.ExecuteSql($"UPDATE Messages SET ConversationId = {to} WHERE ConversationId = {from}");
            db.Database.ExecuteSql($"""
                DELETE FROM ConversationParticipants WHERE ConversationId = {from} AND EXISTS (SELECT 1 FROM ConversationParticipants t
                    WHERE t.PersonId = ConversationParticipants.PersonId AND t.ConversationId = {to})
                """);
            db.Database.ExecuteSql($"UPDATE ConversationParticipants SET ConversationId = {to} WHERE ConversationId = {from}");
            db.Database.ExecuteSql($"DELETE FROM Conversations WHERE Id = {from}");

            var clashes = db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == to && times.Contains(m.Timestamp))
                .Select(m => new { m.Id, m.Timestamp, m.Seq })
                .AsEnumerable()
                .GroupBy(m => m.Timestamp)
                .Where(g => g.Count() > 1);
            foreach (var minute in clashes)
            {
                var seq = 0;
                foreach (var m in minute.OrderBy(m => m.Seq).ThenBy(m => m.Id))
                {
                    var (messageId, value) = (m.Id, seq++);
                    db.Database.ExecuteSql($"UPDATE Messages SET Seq = {value} WHERE Id = {messageId}");
                }
            }
        }

        private Conversation FindNamed(string chatName)
        {
            var conversation = db.Conversations.Include(c => c.Participants)
                .SingleOrDefault(c => c.Source == Conversation.EmailSource && c.Name == chatName);
            if (conversation == null)
            {
                conversation = new Conversation { Source = Conversation.EmailSource, Name = chatName };
                db.Conversations.Add(conversation);
            }
            return conversation;
        }

        private Conversation NewThread(ParsedEmail parsed)
        {
            var baseName = $"{ThreadSubject(parsed.Subject)} ({parsed.LocalTime:yyyy-MM-dd})";
            var name = baseName;
            for (int n = 2; db.Conversations.Any(c => c.Source == Conversation.EmailSource && c.Name == name); n++)
            {
                name = $"{baseName} ({n})";
            }
            var conversation = new Conversation { Source = Conversation.EmailSource, Name = name, ThreadKey = parsed.ThreadKey };
            db.Conversations.Add(conversation);
            return conversation;
        }

        [GeneratedRegex(@"^\s*((re|fw|fwd|aw|sv|wg)(\[\d+\])?\s*:\s*)+", RegexOptions.IgnoreCase)]
        private static partial Regex ReplyPrefixes();

        /// <summary>The subject without its Re: and Fwd: prefixes, or "(no subject)".</summary>
        public static string ThreadSubject(string subject)
        {
            var stripped = ReplyPrefixes().Replace(subject, "").Trim();
            return stripped.Length == 0 ? "(no subject)" : stripped;
        }

        private static string DateText(DateTimeOffset date)
        {
            return date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Finds the person for each address: by the address, then by the display name (so an email
        /// from "Sam Taylor" is the WhatsApp Sam Taylor), else a new person named by the display name,
        /// or by the address when there isn't one. The address is then recorded as theirs.
        /// </summary>
        private sealed class People
        {
            private readonly NarrativeDbContext db;
            private readonly Dictionary<string, Person> byAddress = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Person> byName = new(StringComparer.Ordinal);

            public People(NarrativeDbContext db)
            {
                this.db = db;
            }

            public Person Get(EmailAddress address)
            {
                if (byAddress.TryGetValue(address.Address, out var person))
                {
                    return person;
                }
                person = db.PersonAddresses.Where(a => a.Address == address.Address).Select(a => a.Person).SingleOrDefault();
                if (person == null)
                {
                    var name = address.Name ?? address.Address;
                    if (!byName.TryGetValue(name, out person))
                    {
                        person = db.PersonNames.Where(n => n.Name == name).Select(n => n.Person).SingleOrDefault();
                    }
                    if (person == null)
                    {
                        person = Person.Named(name);
                        db.People.Add(person);
                    }
                    byName[name] = person;
                    db.PersonAddresses.Add(new PersonAddress { Address = address.Address, Person = person });
                }
                byAddress[address.Address] = person;
                return person;
            }
        }
    }
}
