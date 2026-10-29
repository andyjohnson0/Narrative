namespace Narrative.App.Data
{
    /// <summary>
    /// Someone an email was sent to. Recipients from every stored copy of the email are combined; only
    /// the sender's copy names Bcc recipients.
    /// </summary>
    public class EmailRecipient
    {
        public int EmailId { get; set; }

        public Email Email { get; set; } = null!;

        public int PersonId { get; set; }

        public Person Person { get; set; } = null!;

        public RecipientRole Role { get; set; }
    }
}
