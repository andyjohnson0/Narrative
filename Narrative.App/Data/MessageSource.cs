namespace Narrative.App.Data
{
    /// <summary>
    /// Records that a message appeared in an imported file, with the text exactly as that file had it.
    /// </summary>
    public class MessageSource
    {
        public long Id { get; set; }

        public long MessageId { get; set; }

        public Message Message { get; set; } = null!;

        public int ImportFileId { get; set; }

        public ImportFile ImportFile { get; set; } = null!;

        /// <summary>
        /// 1-based line number of the message header in a WhatsApp file; for email, the email's 1-based
        /// position in the file (always 1 for a .eml).
        /// </summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// The sender's name as this file gave it, or null for a system message. It can be an alias of
        /// the message's sender. For email it's the From display name, or the address if there's none.
        /// </summary>
        public string? SenderName { get; set; }

        /// <summary>The text after the sender, including continuation lines. Empty for email.</summary>
        public required string RawText { get; set; }
    }
}
