namespace Narrative.App.Data
{
    /// <summary>
    /// A name a person goes by in exports. Every person has one for their
    /// <see cref="Person.DisplayName"/>, plus one for each alias. A name belongs to one person.
    /// </summary>
    public class PersonName
    {
        public required string Name { get; set; }

        public int PersonId { get; set; }

        public Person Person { get; set; } = null!;
    }
}
