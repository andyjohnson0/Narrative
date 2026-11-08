using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;
using Narrative.App.Sources;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Imports WhatsApp export files into the database, merging messages that earlier exports of the
    /// same chat already supplied, and storing the attachment files the export provides.
    /// </summary>
    public class WhatsAppImporter
    {
        private readonly NarrativeDbContext db;
        private readonly ExportParser parser;

        public WhatsAppImporter(NarrativeDbContext db)
            : this(db, new ExportParser(AndroidUkHeaderFormat.Instance))
        {
        }

        public WhatsAppImporter(NarrativeDbContext db, ExportParser parser)
        {
            this.db = db;
            this.parser = parser;
        }

        /// <summary>
        /// Imports one export text file, looking for its attachments in the same folder.
        /// </summary>
        /// <param name="path">The export text file.</param>
        /// <param name="chatName">The chat name, or null to derive it from the file name.</param>
        public ImportResult Import(string path, string? chatName = null)
        {
            using var folder = new DirectoryContainer(Path.GetDirectoryName(path) ?? ".");
            return Import(new ExportSource(folder, Path.GetFileName(path)), chatName);
        }

        /// <summary>
        /// Imports one export text file and the attachments its container holds.
        /// </summary>
        /// <param name="source">The text file and its folder or zip.</param>
        /// <param name="chatName">The chat name, or null to derive it from the file or zip name.</param>
        public ImportResult Import(ExportSource source, string? chatName = null)
        {
            var bytes = source.Container.ReadAllBytes(source.TextFileName);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var parsed = Parse(bytes);

            // The same text may come back in a zip after being imported on its own, so its messages
            // are already stored but its attachments may not be.
            var previous = db.ImportFiles.FirstOrDefault(f => f.Sha256 == sha256);
            if (previous != null)
            {
                return AttachToPreviousImport(source, previous, parsed);
            }

            chatName ??= source.DefaultChatName
                ?? throw new NarrativeException(
                    $"Can't work out the chat name from '{source.DisplayName}'. Use -chat <name> to give it.");

            if (parsed.Messages.Count == 0)
            {
                return new ImportResult
                {
                    FileName = source.DisplayName,
                    Status = ImportStatus.Empty,
                    ChatName = chatName,
                    OrphanLines = parsed.OrphanLines.Count,
                };
            }

            using var transaction = db.Database.BeginTransaction();

            var conversation = GetOrCreateConversation(chatName);
            var people = GetOrCreatePeople(conversation, parsed.Messages);

            var first = parsed.Messages.Min(m => m.Timestamp);
            var last = parsed.Messages.Max(m => m.Timestamp);
            var importFile = new ImportFile
            {
                Kind = ImportFileKind.WhatsApp,
                Conversation = conversation,
                FileName = source.TextFileName,
                ContainerName = source.ContainerName,
                Sha256 = sha256,
                ImportedAt = DateTime.Now,
                FirstTimestamp = first,
                LastTimestamp = last,
                ParsedCount = parsed.Messages.Count,
            };
            db.ImportFiles.Add(importFile);

            // Some WhatsApp versions round each message's time to the nearest minute where others drop
            // the seconds, so a message can be a minute later in one export than in another. The
            // minutes either side of the file are loaded too, so its first and last messages can pair.
            var storedByMinute = (conversation.Id == 0
                    ? new List<Message>()
                    : db.Messages
                        .Where(m => m.ConversationId == conversation.Id
                            && m.Timestamp >= first.AddMinutes(-1) && m.Timestamp <= last.AddMinutes(1))
                        .ToList())
                .GroupBy(m => m.Timestamp)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Seq).ToList());

            // People created by this import have Id 0, which no stored message has, so they never match.
            int? SenderId(ParsedMessage p) => p.Sender == null ? null : people[p.Sender].Id;

            // The file's messages by minute, each minute's in file order.
            var minutes = Enumerable.Range(0, parsed.Messages.Count)
                .GroupBy(i => parsed.Messages[i].Timestamp)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Pair messages over each run of consecutive minutes, since a pair can be a minute apart.
            var pairs = new Message?[parsed.Messages.Count];
            int shifted = 0;
            foreach (var run in MinuteRuns(minutes.Keys.Union(storedByMinute.Keys)))
            {
                var incoming = run.SelectMany(t => minutes.GetValueOrDefault(t) ?? new List<int>()).ToList();
                if (incoming.Count == 0)
                {
                    continue;
                }
                var stored = run.SelectMany(t => storedByMinute.GetValueOrDefault(t) ?? new List<Message>()).ToList();
                var matches = MessageMatcher.Match(
                    incoming.Select(i => (SenderId(parsed.Messages[i]), parsed.Messages[i].Content, parsed.Messages[i].Timestamp)).ToList(),
                    stored.Select(m => (m.SenderId, m.Content, m.Timestamp)).ToList());
                foreach (var (k, j) in matches)
                {
                    pairs[incoming[k]] = stored[j];
                    if (stored[j].Timestamp != parsed.Messages[incoming[k]].Timestamp)
                    {
                        shifted += 1;
                    }
                }
            }

            var resolved = new Message[parsed.Messages.Count];
            var vacated = new HashSet<DateTime>();
            int added = 0, matched = 0, updated = 0;
            for (int i = 0; i < parsed.Messages.Count; i++)
            {
                var p = parsed.Messages[i];
                var message = pairs[i];
                if (message != null)
                {
                    var (content, tookIncoming) = MessageMatcher.Merge(message.Content, message.ContentAsOf, p.Content, last);
                    var improved = content != message.Content;
                    message.Content = content;
                    if (tookIncoming)
                    {
                        message.ContentAsOf = last;
                    }

                    // Of two times a minute apart, the earlier is the one with the seconds dropped, as
                    // most exports have it. Keeping it makes the result independent of import order.
                    if (p.Timestamp < message.Timestamp)
                    {
                        vacated.Add(message.Timestamp);
                        message.Timestamp = p.Timestamp;
                        improved = true;
                    }
                    if (improved)
                    {
                        updated += 1;
                    }
                    matched += 1;
                }
                else
                {
                    message = new Message
                    {
                        Conversation = conversation,
                        Sender = p.Sender == null ? null : people[p.Sender],
                        Timestamp = p.Timestamp,
                        Content = p.Content,
                        ContentAsOf = last,
                    };
                    db.Messages.Add(message);
                    added += 1;
                }
                resolved[i] = message;

                db.MessageSources.Add(new MessageSource
                {
                    Message = message,
                    ImportFile = importFile,
                    LineNumber = p.LineNumber,
                    SenderName = p.Sender,
                    Timestamp = p.Timestamp,
                    RawText = p.RawText,
                });
            }

            // Renumber the order within every minute this file has messages in or took a message from.
            // A message paired across minutes counts only in the minute it ends up in. One that moved
            // here from the next minute was sent after everything stored in this minute, so in the
            // stored order it goes after them.
            foreach (var minute in minutes.Keys.Union(vacated))
            {
                var stored = (storedByMinute.GetValueOrDefault(minute) ?? new List<Message>())
                    .Concat(storedByMinute.GetValueOrDefault(minute.AddMinutes(1)) ?? new List<Message>())
                    .Where(m => m.Timestamp == minute)
                    .ToList();
                var incoming = (minutes.GetValueOrDefault(minute) ?? new List<int>())
                    .Select(i => resolved[i])
                    .Where(m => m.Timestamp == minute)
                    .ToList();
                var ordered = MessageMatcher.MergeOrder(stored, incoming);
                for (int seq = 0; seq < ordered.Count; seq++)
                {
                    ordered[seq].Seq = seq;
                }
            }

            var attachments = AttachFiles(source, parsed.Messages, resolved);

            importFile.AddedCount = added;
            importFile.MatchedCount = matched;
            importFile.UpdatedCount = updated;
            importFile.AttachmentsReferenced = attachments.Referenced;
            importFile.AttachmentsMissing = attachments.Missing;

            db.SaveChanges();
            transaction.Commit();
            db.ChangeTracker.Clear();

            return new ImportResult
            {
                FileName = source.DisplayName,
                Status = ImportStatus.Imported,
                ChatName = chatName,
                Parsed = parsed.Messages.Count,
                Added = added,
                Matched = matched,
                Updated = updated,
                Shifted = shifted,
                OrphanLines = parsed.OrphanLines.Count,
                AttachmentsReferenced = attachments.Referenced,
                AttachmentsMissing = attachments.Missing,
                AttachmentsLinked = attachments.Linked,
                UnreferencedFiles = attachments.Unreferenced,
            };
        }

        /// <summary>
        /// Splits minutes into runs of consecutive minutes, in time order. Messages can pair only
        /// within a run, since a pair is at most a minute apart.
        /// </summary>
        private static List<List<DateTime>> MinuteRuns(IEnumerable<DateTime> minutes)
        {
            var runs = new List<List<DateTime>>();
            foreach (var minute in minutes.Order())
            {
                if (runs.Count == 0 || minute - runs[^1][^1] > TimeSpan.FromMinutes(1))
                {
                    runs.Add(new List<DateTime>());
                }
                runs[^1].Add(minute);
            }
            return runs;
        }

        private ExportParseResult Parse(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return parser.Parse(reader);
        }

        /// <summary>
        /// Links attachments for a text file whose messages an earlier import stored. Its messages are
        /// found through the earlier import's line numbers.
        /// </summary>
        private ImportResult AttachToPreviousImport(ExportSource source, ImportFile previous, ExportParseResult parsed)
        {
            using var transaction = db.Database.BeginTransaction();

            var messagesByLine = db.MessageSources
                .Where(s => s.ImportFileId == previous.Id)
                .Include(s => s.Message)
                .ToDictionary(s => s.LineNumber, s => s.Message);
            var messages = parsed.Messages.Select(p => messagesByLine.GetValueOrDefault(p.LineNumber)).ToList();

            var attachments = AttachFiles(source, parsed.Messages, messages);

            db.SaveChanges();
            transaction.Commit();
            db.ChangeTracker.Clear();

            return new ImportResult
            {
                FileName = source.DisplayName,
                Status = ImportStatus.AlreadyImported,
                AttachmentsReferenced = attachments.Referenced,
                AttachmentsMissing = attachments.Missing,
                AttachmentsLinked = attachments.Linked,
                UnreferencedFiles = attachments.Unreferenced,
            };
        }

        private sealed class AttachmentCounts
        {
            public int Referenced { get; set; }

            public int Missing { get; set; }

            public int Linked { get; set; }

            public int Unreferenced { get; set; }
        }

        /// <summary>
        /// Stores the attachment files that <paramref name="source"/> supplies and links them to the
        /// messages that name them.
        /// </summary>
        /// <param name="source">Where to find the files.</param>
        /// <param name="parsed">The parsed messages.</param>
        /// <param name="messages">The stored message for each parsed message, or null if unknown.</param>
        private AttachmentCounts AttachFiles(ExportSource source, IReadOnlyList<ParsedMessage> parsed, IReadOnlyList<Message?> messages)
        {
            var counts = new AttachmentCounts();
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            var store = new AttachmentStore(db);

            // Each new file is saved as soon as it's read (see GetOrCreateAttachment). Change detection
            // scans every tracked entity, thousands of messages in a large export, so it's left to the
            // import's final save rather than repeated for every file.
            var autoDetectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                LinkFiles(source, parsed, messages, counts, referenced, store);
            }
            finally
            {
                db.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
            }

            // In a zip every file belongs to the export, so a file nothing names is worth reporting.
            // A folder can hold anything.
            if (source.Container.IsArchive)
            {
                counts.Unreferenced = source.Container.FileNames
                    .Count(n => n != source.TextFileName && !referenced.Contains(n));
            }
            return counts;
        }

        private void LinkFiles(ExportSource source, IReadOnlyList<ParsedMessage> parsed, IReadOnlyList<Message?> messages,
            AttachmentCounts counts, HashSet<string> referenced, AttachmentStore store)
        {
            for (int i = 0; i < parsed.Count; i++)
            {
                var name = parsed[i].Content.AttachmentName;
                if (name == null)
                {
                    continue;
                }
                counts.Referenced += 1;
                referenced.Add(name);
                if (!source.Container.Contains(name))
                {
                    counts.Missing += 1;
                    continue;
                }

                // A message that shows as deleted, or that already has its file, needs nothing.
                var message = messages[i];
                if (message == null || message.AttachmentState != AttachmentState.Missing || message.AttachmentName != name)
                {
                    continue;
                }
                message.Attachment = store.GetOrCreate(name, source.Container.ReadAllBytes(name));
                counts.Linked += 1;
            }
        }

        private Conversation GetOrCreateConversation(string name)
        {
            var conversation = db.Conversations
                .Include(c => c.Participants)
                .SingleOrDefault(c => c.Source == Conversation.WhatsAppSource && c.Name == name);
            if (conversation == null)
            {
                conversation = new Conversation { Source = Conversation.WhatsAppSource, Name = name };
                db.Conversations.Add(conversation);
            }
            return conversation;
        }

        /// <summary>
        /// Maps each sender name in the export to a person, by main name or alias, creating people for
        /// new names. Two names that are aliases of each other map to the same person, so dedup pairs
        /// their messages.
        /// </summary>
        private Dictionary<string, Person> GetOrCreatePeople(Conversation conversation, IEnumerable<ParsedMessage> messages)
        {
            var names = messages.Where(m => m.Sender != null).Select(m => m.Sender!).Distinct().ToList();
            var people = db.PersonNames.Include(n => n.Person)
                .Where(n => names.Contains(n.Name))
                .ToDictionary(n => n.Name, n => n.Person);
            foreach (var name in names)
            {
                if (!people.TryGetValue(name, out var person))
                {
                    person = Person.Named(name);
                    db.People.Add(person);
                    people[name] = person;
                }
                if (!conversation.Participants.Any(cp => ReferenceEquals(cp.Person, person) || (person.Id != 0 && cp.PersonId == person.Id)))
                {
                    conversation.Participants.Add(new ConversationParticipant { Conversation = conversation, Person = person });
                }
            }
            return people;
        }
    }
}
