namespace Narrative.App.Data
{
    /// <summary>
    /// The email a message came from. An email is stored as its body message plus one media message
    /// per attachment, as WhatsApp stores a text and the photos sent with it, and they all point here.
    /// </summary>
    public class Email
    {
        public int Id { get; set; }

        /// <summary>
        /// The Message-ID header without its angle brackets, or a <c>sha256:</c> hash of the sender,
        /// date, subject and body when the email has none. The same email in several files is stored once.
        /// </summary>
        public required string InternetMessageId { get; set; }

        public required string Subject { get; set; }

        /// <summary>The In-Reply-To message id, or null.</summary>
        public string? InReplyTo { get; set; }

        /// <summary>The References message ids, separated by spaces, oldest first, or null.</summary>
        public string? References { get; set; }

        /// <summary>The Date header as sent, with its offset (<c>yyyy-MM-ddTHH:mm:sszzz</c>).</summary>
        public required string Date { get; set; }

        public List<EmailRecipient> Recipients { get; set; } = new();

        public List<Message> Messages { get; set; } = new();
    }
}
