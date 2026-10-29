using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;

namespace Narrative.App.Export
{
    /// <summary>
    /// Selects the messages an export asks for and builds the format-independent document.
    /// </summary>
    /// <remarks>
    /// With a chat, the export is that chat (or email thread), optionally only some senders' messages.
    /// Without one, it's the conversation between two or more people across every source: each
    /// message one of them sent to another of them. An email's recipients are its To, Cc and Bcc; a
    /// chat message's are the chat's other participants, so group chats count unless
    /// <see cref="ExportRequest.Direct"/> asks for messages between those people alone.
    /// </remarks>
    public class ExportBuilder
    {
        private readonly NarrativeDbContext db;

        public ExportBuilder(NarrativeDbContext db)
        {
            this.db = db;
        }

        public ExportDocument Build(ExportRequest request)
        {
            var source = request.Source == null ? null : SourceName(request.Source);
            Conversation? conversation = null;
            if (request.Chat != null)
            {
                if (request.Direct)
                {
                    throw new NarrativeException("-direct is for a conversation between people across chats, so it can't be used with -chat.");
                }
                conversation = FindChat(request.Chat, source);
            }
            else if (request.People.Distinct().Count() < 2)
            {
                throw new NarrativeException(
                    "-export needs -chat <name>, or two or more -person names for the conversation between them across every chat and email.");
            }

            // -person and -me accept any of a person's names or addresses; the export uses their main name.
            var resolved = Resolve(request, conversation);
            var selectedPeople = request.People.Select(p => resolved[p].DisplayName).Distinct().ToList();
            var me = request.Me == null ? null : resolved[request.Me];

            var query = db.Messages.AsNoTracking()
                .Include(m => m.Sender)
                .Include(m => m.Attachment)
                .Include(m => m.Conversation)
                .Include(m => m.Email).ThenInclude(e => e!.Recipients).ThenInclude(r => r.Person)
                .AsQueryable();
            var ids = request.People.Select(p => resolved[p].Id).Distinct().ToList();
            if (conversation != null)
            {
                query = query.Where(m => m.ConversationId == conversation.Id);
                if (ids.Count > 0)
                {
                    query = query.Where(m => m.SenderId != null && ids.Contains(m.SenderId.Value));
                }
            }
            else
            {
                var direct = request.Direct;
                query = query.Where(m => m.SenderId != null && ids.Contains(m.SenderId.Value) && (
                    m.EmailId == null
                        ? m.Conversation.Participants.Count(p => ids.Contains(p.PersonId)) >= 2
                          && (!direct || m.Conversation.Participants.All(p => ids.Contains(p.PersonId)))
                        : m.Email!.Recipients.Any(r => ids.Contains(r.PersonId) && r.PersonId != m.SenderId)
                          && (!direct || m.Email.Recipients.All(r => ids.Contains(r.PersonId)))));
                if (source != null)
                {
                    query = query.Where(m => m.Conversation.Source == source);
                }
            }
            if (request.From != null)
            {
                query = query.Where(m => m.Timestamp >= request.From);
            }
            if (request.To != null)
            {
                query = query.Where(m => m.Timestamp < request.To);
            }
            var messages = query.AsSplitQuery().OrderBy(m => m.Timestamp).ThenBy(m => m.ConversationId).ThenBy(m => m.Seq).ToList();

            var people = messages.Where(m => m.Sender != null).Select(m => m.Sender!.DisplayName)
                .Distinct().Order(StringComparer.Ordinal).ToList();
            var senderNumbers = people.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i + 1);

            var attachments = new Dictionary<int, ExportAttachment>();
            var attachmentsInOrder = new List<ExportAttachment>();
            var emails = new Dictionary<int, ExportEmail>();
            var exported = new List<ExportMessage>(messages.Count);
            foreach (var m in messages)
            {
                ExportAttachment? attachment = null;
                if (m.Attachment != null && !attachments.TryGetValue(m.Attachment.Id, out attachment))
                {
                    attachment = new ExportAttachment
                    {
                        Id = m.Attachment.Id,
                        FileName = m.Attachment.FileName,
                        MediaType = m.Attachment.MediaType,
                        Kind = CamelCase(m.Attachment.Kind.ToString()),
                        Size = m.Attachment.Size,
                        Sha256 = m.Attachment.Sha256,
                    };
                    attachments.Add(attachment.Id, attachment);
                    attachmentsInOrder.Add(attachment);
                }
                ExportEmail? email = null;
                var emailStart = false;
                if (m.Email != null && !emails.TryGetValue(m.Email.Id, out email))
                {
                    email = ToExport(m.Email);
                    emails.Add(m.Email.Id, email);
                    emailStart = true;
                }
                exported.Add(new ExportMessage
                {
                    Id = m.Id,
                    Timestamp = m.Timestamp,
                    Source = m.Conversation.Source,
                    Chat = m.Conversation.Name,
                    Email = email,
                    EmailStart = emailStart,
                    Sender = m.Sender?.DisplayName,
                    SenderNumber = m.Sender == null ? 0 : senderNumbers[m.Sender.DisplayName],
                    IsMe = me != null && m.SenderId == me.Id,
                    Kind = CamelCase(m.Kind.ToString()),
                    Text = m.Text,
                    IsEdited = m.IsEdited,
                    AttachmentState = m.AttachmentState == AttachmentState.None ? null : CamelCase(m.AttachmentState.ToString()),
                    AttachmentName = m.AttachmentName,
                    Attachment = attachment,
                });
            }

            return new ExportDocument
            {
                ExportedAt = DateTime.Now,
                Chat = conversation == null ? null : new ExportChat(conversation.Source, conversation.Name),
                Title = conversation?.Name ?? JoinNames(selectedPeople),
                Selection = new ExportSelection(selectedPeople, request.From, request.To, me?.DisplayName, source, request.Direct),
                People = people,
                Messages = exported,
                Attachments = attachmentsInOrder,
            };
        }

        /// <summary>WhatsApp or Email, from the name typed on the command line.</summary>
        public static string SourceName(string source)
        {
            return source.ToLowerInvariant() switch
            {
                "whatsapp" => Conversation.WhatsAppSource,
                "email" => Conversation.EmailSource,
                _ => throw new NarrativeException($"Unknown source '{source}'. Use whatsapp or email."),
            };
        }

        private Conversation FindChat(string name, string? source)
        {
            var matches = db.Conversations.AsNoTracking()
                .Include(c => c.Participants)
                .Where(c => c.Name == name && (source == null || c.Source == source))
                .ToList();
            if (matches.Count > 1)
            {
                throw new NarrativeException(
                    $"More than one chat is called '{name}' ({string.Join(", ", matches.Select(c => c.Source))}). Add -source whatsapp or -source email.");
            }
            return matches.SingleOrDefault()
                ?? throw new NarrativeException($"No chat called '{name}'. Use -list-chats to see the chat names.");
        }

        private Dictionary<string, Person> Resolve(ExportRequest request, Conversation? conversation)
        {
            var participantIds = conversation?.Participants.Select(p => p.PersonId).ToHashSet();
            var directory = new PersonDirectory(db);
            var resolved = new Dictionary<string, Person>(StringComparer.Ordinal);
            var unknown = new List<string>();
            foreach (var name in request.People.Append(request.Me).OfType<string>().Distinct())
            {
                var person = directory.Find(name);
                if (person != null && (participantIds == null || participantIds.Contains(person.Id)))
                {
                    resolved[name] = person;
                }
                else
                {
                    unknown.Add(name);
                }
            }
            if (unknown.Count > 0)
            {
                var names = string.Join(", ", unknown.Select(p => $"'{p}'"));
                throw new NarrativeException(conversation != null
                    ? $"Not in chat '{conversation.Name}': {names}. Use -list-people -chat \"{conversation.Name}\" to see who is."
                    : $"No one called {names}. Use -list-people to see who there is.");
            }
            return resolved;
        }

        private static ExportEmail ToExport(Email email)
        {
            List<string> Names(RecipientRole role) => email.Recipients
                .Where(r => r.Role == role)
                .Select(r => r.Person.DisplayName)
                .Order(StringComparer.Ordinal)
                .ToList();

            return new ExportEmail
            {
                Subject = email.Subject,
                MessageId = email.InternetMessageId,
                Date = email.Date,
                To = Names(RecipientRole.To),
                Cc = Names(RecipientRole.Cc),
                Bcc = Names(RecipientRole.Bcc),
            };
        }

        private static string JoinNames(IReadOnlyList<string> names)
        {
            return names.Count <= 1 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        }

        private static string CamelCase(string name)
        {
            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
