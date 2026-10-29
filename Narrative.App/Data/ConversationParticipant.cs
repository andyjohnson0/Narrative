namespace Narrative.App.Data
{
    /// <summary>
    /// Links a person to a conversation they have sent messages in, or (in an email thread) received them.
    /// </summary>
    public class ConversationParticipant
    {
        public int ConversationId { get; set; }

        public Conversation Conversation { get; set; } = null!;

        public int PersonId { get; set; }

        public Person Person { get; set; } = null!;
    }
}
