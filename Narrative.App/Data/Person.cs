namespace Narrative.App.Data
{
    /// <summary>
    /// A message sender. <see cref="DisplayName"/> is their main name; <see cref="Names"/> holds it and
    /// any aliases, each exactly as it appears in exports.
    /// </summary>
    public class Person
    {
        public int Id { get; set; }

        public required string DisplayName { get; set; }

        public List<PersonName> Names { get; set; } = new();

        public List<PersonAddress> Addresses { get; set; } = new();

        public List<ConversationParticipant> Conversations { get; set; } = new();

        /// <summary>
        /// A new person whose only name is <paramref name="name"/>.
        /// </summary>
        public static Person Named(string name)
        {
            var person = new Person { DisplayName = name };
            person.Names.Add(new PersonName { Name = name, Person = person });
            return person;
        }
    }
}
