namespace Narrative.App.Data
{
    /// <summary>
    /// An email address a person uses, in lower case. Email senders and recipients are found by
    /// address first, so a person's address keeps working whatever display name an email gives.
    /// </summary>
    public class PersonAddress
    {
        public required string Address { get; set; }

        public int PersonId { get; set; }

        public Person Person { get; set; } = null!;
    }
}
