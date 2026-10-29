using System.ComponentModel.DataAnnotations.Schema;

namespace Narrative.App.Data
{
    /// <summary>
    /// One deduplicated message in a conversation.
    /// </summary>
    public class Message
    {
        public long Id { get; set; }

        public int ConversationId { get; set; }

        public Conversation Conversation { get; set; } = null!;

        /// <summary>The sender, or null for system messages.</summary>
        public int? SenderId { get; set; }

        public Person? Sender { get; set; }

        /// <summary>
        /// UK local wall-clock time. WhatsApp's is as shown in the export, to the minute; an email's is
        /// its Date header converted to UK time, to the second.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Order of this message among all messages in the same conversation and minute.</summary>
        public int Seq { get; set; }

        public MessageKind Kind { get; set; }

        public string Text { get; set; } = "";

        public bool IsEdited { get; set; }

        /// <summary>The attachment's file name as the export gave it, or null if unknown.</summary>
        public string? AttachmentName { get; set; }

        /// <summary>The stored attachment file, or null if no import has supplied it.</summary>
        public int? AttachmentId { get; set; }

        public Attachment? Attachment { get; set; }

        /// <summary>
        /// Timestamp of the last message in the export that supplied the current content. Used to decide
        /// which of two edited versions is newer.
        /// </summary>
        public DateTime ContentAsOf { get; set; }

        /// <summary>The email this message is part of, or null for a message from a chat.</summary>
        public int? EmailId { get; set; }

        public Email? Email { get; set; }

        public List<MessageSource> Sources { get; set; } = new();

        [NotMapped]
        public AttachmentState AttachmentState =>
            Kind != MessageKind.Media ? AttachmentState.None
            : AttachmentName == null ? AttachmentState.Omitted
            : AttachmentId == null && Attachment == null ? AttachmentState.Missing
            : AttachmentState.Stored;

        [NotMapped]
        public MessageContent Content
        {
            get => new(Kind, Text, IsEdited, AttachmentName);
            set
            {
                Kind = value.Kind;
                Text = value.Text;
                IsEdited = value.IsEdited;
                AttachmentName = value.AttachmentName;
            }
        }
    }
}
