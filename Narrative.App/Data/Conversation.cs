namespace Narrative.App.Data
{
    /// <summary>
    /// A single named chat from one source: a WhatsApp chat or an email thread.
    /// </summary>
    public class Conversation
    {
        public const string WhatsAppSource = "WhatsApp";

        public const string EmailSource = "Email";

        public int Id { get; set; }

        public required string Source { get; set; }

        public required string Name { get; set; }

        /// <summary>
        /// For an email thread, the Message-ID of the email that started it. Null for a chat, and for
        /// emails put in a named conversation with -chat.
        /// </summary>
        public string? ThreadKey { get; set; }

        public List<ConversationParticipant> Participants { get; set; } = new();

        public List<Message> Messages { get; set; } = new();
    }
}
