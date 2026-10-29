using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Narrative.App.Data
{
    /// <summary>
    /// Finds people by any of their names or email addresses, and adds aliases.
    /// </summary>
    public partial class PersonDirectory
    {
        private readonly NarrativeDbContext db;

        public PersonDirectory(NarrativeDbContext db)
        {
            this.db = db;
        }

        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
        private static partial Regex AddressPattern();

        /// <summary>
        /// True if <paramref name="text"/> is shaped like an email address (<c>x@y.z</c>). A WhatsApp
        /// @handle starts with "@", so it isn't one.
        /// </summary>
        public static bool IsAddress(string text)
        {
            return AddressPattern().IsMatch(text);
        }

        /// <summary>An address as it's stored: trimmed and in lower case.</summary>
        public static string NormaliseAddress(string address)
        {
            return address.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// The person who goes by <paramref name="nameOrAddress"/>, as their main name, an alias, or one
        /// of their email addresses, or null.
        /// </summary>
        public Person? Find(string nameOrAddress)
        {
            var person = db.PersonNames.Where(n => n.Name == nameOrAddress).Select(n => n.Person).SingleOrDefault();
            if (person == null && IsAddress(nameOrAddress))
            {
                var address = NormaliseAddress(nameOrAddress);
                person = db.PersonAddresses.Where(a => a.Address == address).Select(a => a.Person).SingleOrDefault();
            }
            return person;
        }

        /// <summary>
        /// Makes <paramref name="alias"/> (a name or an email address) another name for the person
        /// called <paramref name="name"/>, creating that person if needed, so imports treat messages
        /// under either as theirs.
        /// </summary>
        /// <returns>The person, and false if the alias was already set.</returns>
        /// <remarks>
        /// If the alias already belongs to someone else, that person is merged in. That's refused if
        /// they have WhatsApp messages: WhatsApp dedup pairs messages by person, so merging them
        /// afterwards would mean redoing dedup for every minute the two share. Email dedup goes by
        /// Message-ID, so people with only email are merged at any time.
        /// </remarks>
        public (Person Person, bool Added) AddAlias(string alias, string name)
        {
            if (alias.Length == 0 || name.Length == 0)
            {
                throw new NarrativeException("-alias names can't be empty.");
            }
            var isAddress = IsAddress(alias);
            if (alias == name || (isAddress && IsAddress(name) && NormaliseAddress(alias) == NormaliseAddress(name)))
            {
                throw new NarrativeException($"'{alias}' can't be an alias of itself.");
            }

            var target = Find(name);
            var other = isAddress
                ? db.PersonAddresses.Where(a => a.Address == NormaliseAddress(alias)).Select(a => a.Person).SingleOrDefault()
                : db.PersonNames.Where(n => n.Name == alias).Select(n => n.Person).SingleOrDefault();
            if (other != null && target != null && other.Id == target.Id)
            {
                return (target, false);
            }
            if (other != null)
            {
                var chatMessages = db.Messages.Count(m => m.SenderId == other.Id && m.EmailId == null);
                if (chatMessages > 0)
                {
                    throw new NarrativeException(
                        $"'{alias}' already has {chatMessages} imported messages, so it can't become another name for " +
                        $"'{name}' now. Set aliases before importing: use -init, set the alias, then import again.");
                }
            }

            using var transaction = db.Database.BeginTransaction();
            if (target == null)
            {
                target = Person.Named(name);
                db.People.Add(target);
            }
            if (other == null)
            {
                if (isAddress)
                {
                    target.Addresses.Add(new PersonAddress { Address = NormaliseAddress(alias), Person = target });
                }
                else
                {
                    target.Names.Add(new PersonName { Name = alias, Person = target });
                }
                db.SaveChanges();
            }
            else
            {
                db.SaveChanges();
                Merge(other.Id, target.Id);
                db.ChangeTracker.Clear();
            }
            transaction.Commit();
            return (target, true);
        }

        /// <summary>
        /// Moves everything of one person's to another: their names (which become aliases), addresses,
        /// messages, recipients and chats. Then deletes them.
        /// </summary>
        private void Merge(int from, int to)
        {
            // A person in both lists would break the keys, so those rows are dropped first.
            db.Database.ExecuteSql($"""
                DELETE FROM EmailRecipients WHERE PersonId = {from} AND EXISTS (SELECT 1 FROM EmailRecipients t
                    WHERE t.EmailId = EmailRecipients.EmailId AND t.Role = EmailRecipients.Role AND t.PersonId = {to})
                """);
            db.Database.ExecuteSql($"UPDATE EmailRecipients SET PersonId = {to} WHERE PersonId = {from}");
            db.Database.ExecuteSql($"""
                DELETE FROM ConversationParticipants WHERE PersonId = {from} AND EXISTS (SELECT 1 FROM ConversationParticipants t
                    WHERE t.ConversationId = ConversationParticipants.ConversationId AND t.PersonId = {to})
                """);
            db.Database.ExecuteSql($"UPDATE ConversationParticipants SET PersonId = {to} WHERE PersonId = {from}");
            db.Database.ExecuteSql($"UPDATE Messages SET SenderId = {to} WHERE SenderId = {from}");
            db.Database.ExecuteSql($"UPDATE PersonNames SET PersonId = {to} WHERE PersonId = {from}");
            db.Database.ExecuteSql($"UPDATE PersonAddresses SET PersonId = {to} WHERE PersonId = {from}");
            db.Database.ExecuteSql($"DELETE FROM People WHERE Id = {from}");
        }
    }
}
