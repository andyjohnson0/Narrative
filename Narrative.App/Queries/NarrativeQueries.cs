using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;

namespace Narrative.App.Queries
{
    /// <summary>
    /// A chat with summary figures.
    /// </summary>
    public sealed record ChatSummary(
        string Source,
        string Name,
        int MessageCount,
        DateTime? First,
        DateTime? Last);

    /// <summary>
    /// Read-only queries over the database.
    /// </summary>
    public class NarrativeQueries
    {
        private readonly NarrativeDbContext db;

        public NarrativeQueries(NarrativeDbContext db)
        {
            this.db = db;
        }

        /// <summary>
        /// Lists chats and email threads by name, optionally only those that include
        /// <paramref name="person"/> (by any of their names or addresses), or only those from
        /// <paramref name="source"/>.
        /// </summary>
        public IReadOnlyList<ChatSummary> ListChats(string? person = null, string? source = null)
        {
            var conversations = db.Conversations.AsNoTracking();
            if (person != null)
            {
                var address = PersonDirectory.NormaliseAddress(person);
                conversations = conversations.Where(c => c.Participants.Any(p =>
                    p.Person.Names.Any(n => n.Name == person) || p.Person.Addresses.Any(a => a.Address == address)));
            }
            if (source != null)
            {
                conversations = conversations.Where(c => c.Source == source);
            }
            return conversations
                .OrderBy(c => c.Name)
                .Select(c => new ChatSummary(
                    c.Source,
                    c.Name,
                    c.Messages.Count(),
                    c.Messages.Min(m => (DateTime?)m.Timestamp),
                    c.Messages.Max(m => (DateTime?)m.Timestamp)))
                .ToList();
        }

        /// <summary>
        /// Lists people by name, optionally only those in the chat called <paramref name="chat"/>.
        /// </summary>
        public IReadOnlyList<string> ListPeople(string? chat = null)
        {
            var people = db.People.AsNoTracking();
            if (chat != null)
            {
                people = people.Where(p => p.Conversations.Any(c => c.Conversation.Name == chat));
            }
            return people.OrderBy(p => p.DisplayName).Select(p => p.DisplayName).ToList();
        }

        /// <summary>
        /// Every alias, as (alias, main name), by main name and then alias.
        /// </summary>
        public IReadOnlyList<(string Alias, string Name)> ListAliases()
        {
            return db.PersonNames.AsNoTracking()
                .Where(n => n.Name != n.Person.DisplayName)
                .Select(n => new { Alias = n.Name, n.Person.DisplayName })
                .AsEnumerable()
                .OrderBy(x => x.DisplayName, StringComparer.Ordinal).ThenBy(x => x.Alias, StringComparer.Ordinal)
                .Select(x => (x.Alias, x.DisplayName))
                .ToList();
        }

        /// <summary>
        /// Every email address, as (address, main name), by main name and then address.
        /// </summary>
        public IReadOnlyList<(string Address, string Name)> ListAddresses()
        {
            return db.PersonAddresses.AsNoTracking()
                .Select(a => new { a.Address, a.Person.DisplayName })
                .AsEnumerable()
                .OrderBy(x => x.DisplayName, StringComparer.Ordinal).ThenBy(x => x.Address, StringComparer.Ordinal)
                .Select(x => (x.Address, x.DisplayName))
                .ToList();
        }

        /// <summary>
        /// Reproduces a conversation: the messages sent in chat <paramref name="chat"/> by any of
        /// <paramref name="people"/> (by any of their names), from <paramref name="from"/> (inclusive) to <paramref name="to"/>
        /// (exclusive), in order.
        /// </summary>
        public IReadOnlyList<Message> GetConversation(string chat, IEnumerable<string> people, DateTime from, DateTime to)
        {
            var names = people.ToList();
            return db.Messages.AsNoTracking()
                .Include(m => m.Sender)
                .Where(m => m.Conversation.Name == chat
                    && m.Sender != null && m.Sender.Names.Any(n => names.Contains(n.Name))
                    && m.Timestamp >= from && m.Timestamp < to)
                .OrderBy(m => m.Timestamp)
                .ThenBy(m => m.Seq)
                .ToList();
        }
    }
}
