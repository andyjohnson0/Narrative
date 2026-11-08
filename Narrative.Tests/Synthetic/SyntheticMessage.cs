namespace Narrative.Tests.Synthetic
{
    /// <summary>
    /// One message in a synthetic chat's true history, before any export is made.
    /// </summary>
    public sealed class SyntheticMessage
    {
        public int Index { get; init; }

        /// <summary>The minute the message was sent in.</summary>
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// The second within that minute. Exports show minutes only, but an export that rounds times
        /// puts a message sent at :30 or later in the next minute.
        /// </summary>
        public int Second { get; set; }

        /// <summary>The sender, or null for a system message.</summary>
        public string? Sender { get; init; }

        /// <summary>The original text. For media this is the caption, possibly empty.</summary>
        public string Text { get; init; } = "";

        /// <summary>The attachment file name, if this is a media message.</summary>
        public string? Attachment { get; init; }

        /// <summary>
        /// What the attachment's bytes are generated from. Two messages with the same key share
        /// identical files (a forwarded file). Defaults to the attachment name.
        /// </summary>
        public string? AttachmentContentKey { get; init; }

        public string? ContentKey => AttachmentContentKey ?? Attachment;

        /// <summary>The index of the message at which this one was edited, if it was.</summary>
        public int? EditedAt { get; init; }

        public string? EditedText { get; init; }

        /// <summary>The index of the message at which this one was deleted, if it was.</summary>
        public int? DeletedAt { get; init; }

        public bool IsSystem => Sender == null;

        public bool IsMedia => Attachment != null;
    }
}
