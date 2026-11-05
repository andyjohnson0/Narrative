using Narrative.App.Data;

namespace Narrative.App.Mail
{
    /// <summary>A sender or recipient: an address in lower case, and the display name if the email gave one.</summary>
    public sealed record EmailAddress(string Address, string? Name);

    public sealed record ParsedRecipient(EmailAddress Address, RecipientRole Role);

    /// <summary>An attached file, decoded.</summary>
    public sealed record ParsedAttachment(string FileName, string MediaType, byte[] Bytes);

    /// <summary>
    /// The parts of an email that narrative keeps.
    /// </summary>
    public sealed class ParsedEmail
    {
        /// <summary>The Message-ID without angle brackets, or a <c>sha256:</c> stand-in.</summary>
        public required string MessageId { get; init; }

        public required string Subject { get; init; }

        public string? InReplyTo { get; init; }

        /// <summary>The References ids, oldest first.</summary>
        public required IReadOnlyList<string> References { get; init; }

        /// <summary>When it was sent, with the sender's offset.</summary>
        public required DateTimeOffset Date { get; init; }

        /// <summary><see cref="Date"/> as UK wall-clock time.</summary>
        public required DateTime LocalTime { get; init; }

        public EmailAddress? From { get; init; }

        public required IReadOnlyList<ParsedRecipient> Recipients { get; init; }

        /// <summary>
        /// The body as plain text with LF line endings, without the quoted copy of earlier emails at its
        /// end. Quoted lines with the sender's own text after them are kept.
        /// </summary>
        public required string Body { get; init; }

        /// <summary>The whole body, trailing quote included.</summary>
        public required string FullBody { get; init; }

        public required IReadOnlyList<ParsedAttachment> Attachments { get; init; }

        /// <summary>The id of the thread's first email: the oldest reference, else the reply-to id, else this email's own.</summary>
        public string ThreadKey => References.Count > 0 ? References[0] : InReplyTo ?? MessageId;
    }
}
