namespace Narrative.App.Export
{
    /// <summary>
    /// Everything an export contains, independent of format. Every format is written from this, so
    /// they can't disagree about what's included.
    /// </summary>
    public sealed class ExportDocument
    {
        /// <summary>Version of the JSON and XML layouts. Bump it when they change incompatibly.</summary>
        public const int CurrentFormatVersion = 2;

        public int FormatVersion => CurrentFormatVersion;

        public required DateTime ExportedAt { get; init; }

        /// <summary>The chat exported with -chat, or null for a conversation between people across chats.</summary>
        public required ExportChat? Chat { get; init; }

        /// <summary>The chat's name, or the people's names for a conversation between them.</summary>
        public required string Title { get; init; }

        public required ExportSelection Selection { get; init; }

        /// <summary>The senders of the exported messages, by name.</summary>
        public required IReadOnlyList<string> People { get; init; }

        /// <summary>The exported messages in conversation order.</summary>
        public required IReadOnlyList<ExportMessage> Messages { get; init; }

        /// <summary>The stored files the exported messages refer to, each once, in order of first use.</summary>
        public required IReadOnlyList<ExportAttachment> Attachments { get; init; }

        public DateTime? First => Messages.Count == 0 ? null : Messages[0].Timestamp;

        public DateTime? Last => Messages.Count == 0 ? null : Messages[^1].Timestamp;

        /// <summary>The messages grouped by calendar day, for templates that show day headings.</summary>
        public IReadOnlyList<ExportDay> Days => Messages
            .GroupBy(m => m.Timestamp.Date)
            .Select(g => new ExportDay(g.Key, g.ToList()))
            .ToList();
    }

    public sealed record ExportChat(string Source, string Name);

    /// <summary>
    /// What was asked for. <see cref="People"/> is empty when every sender (and system messages) was
    /// included. <see cref="To"/> is exclusive. <see cref="Me"/> is the person whose messages are shown
    /// as the reader's own, or null. <see cref="Source"/> limits the export to one source (WhatsApp or
    /// Email), and <see cref="Direct"/> to messages between the selected people alone.
    /// </summary>
    public sealed record ExportSelection(IReadOnlyList<string> People, DateTime? From, DateTime? To, string? Me = null,
        string? Source = null, bool Direct = false);

    public sealed record ExportDay(DateTime Date, IReadOnlyList<ExportMessage> Messages);

    public sealed class ExportMessage
    {
        public required long Id { get; init; }

        /// <summary>UK local wall-clock time: to the minute for WhatsApp, to the second for email.</summary>
        public required DateTime Timestamp { get; init; }

        /// <summary>Where the message came from: WhatsApp or Email.</summary>
        public required string Source { get; init; }

        /// <summary>The name of the chat or email thread the message is in.</summary>
        public required string Chat { get; init; }

        /// <summary>The email the message is part of, or null for a chat message.</summary>
        public ExportEmail? Email { get; init; }

        /// <summary>
        /// True for the first exported message of an email: its body, or its first attachment when it
        /// has no body. Templates show the subject there.
        /// </summary>
        public bool EmailStart { get; init; }

        /// <summary>The sender's name, or null for a system message.</summary>
        public required string? Sender { get; init; }

        /// <summary>
        /// The sender's 1-based position in <see cref="ExportDocument.People"/>, or 0 for a system
        /// message. Lets styles tell senders apart.
        /// </summary>
        public required int SenderNumber { get; init; }

        /// <summary>True if the sender is <see cref="ExportSelection.Me"/>.</summary>
        public bool IsMe { get; init; }

        /// <summary>text, system, media or deleted.</summary>
        public required string Kind { get; init; }

        /// <summary>The message text. For media, the caption, which may be empty.</summary>
        public required string Text { get; init; }

        public required bool IsEdited { get; init; }

        /// <summary>
        /// For media: omitted (the export left media out, so the file's name is unknown), missing (the
        /// file is named but no import supplied it) or stored. Null for other kinds.
        /// </summary>
        public required string? AttachmentState { get; init; }

        /// <summary>The file name the export gave, if known.</summary>
        public required string? AttachmentName { get; init; }

        /// <summary>The stored file, when <see cref="AttachmentState"/> is stored.</summary>
        public required ExportAttachment? Attachment { get; init; }
    }

    /// <summary>
    /// An email's details. Every message from the same email shares one.
    /// </summary>
    public sealed class ExportEmail
    {
        public required string Subject { get; init; }

        /// <summary>The Message-ID, or a <c>sha256:</c> stand-in for an email without one.</summary>
        public required string MessageId { get; init; }

        /// <summary>The Date header, with the sender's offset.</summary>
        public required string Date { get; init; }

        public required IReadOnlyList<string> To { get; init; }

        public required IReadOnlyList<string> Cc { get; init; }

        public required IReadOnlyList<string> Bcc { get; init; }
    }

    public sealed class ExportAttachment
    {
        public required int Id { get; init; }

        public required string FileName { get; init; }

        public required string MediaType { get; init; }

        /// <summary>image, video, voiceNote, audio, sticker, contact or document.</summary>
        public required string Kind { get; init; }

        public required long Size { get; init; }

        public required string Sha256 { get; init; }

        /// <summary>
        /// Where the file was written, relative to the export file and with forward slashes, or null
        /// if files weren't written alongside.
        /// </summary>
        public string? Path { get; set; }
    }
}
