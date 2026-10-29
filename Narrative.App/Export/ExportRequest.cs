namespace Narrative.App.Export
{
    public enum ExportFormat
    {
        Json,
        Xml,
        Markdown,
        Html,

        /// <summary>One line per message: timestamp, person, text. No attachment files.</summary>
        Csv,

        /// <summary>WhatsApp's own text export format, as a .txt or a .zip with the attachments.</summary>
        WhatsApp,

        /// <summary>A user-supplied Scriban template.</summary>
        Template,
    }

    /// <summary>
    /// What happens to stored attachment files.
    /// </summary>
    public enum AttachmentMode
    {
        /// <summary>Write them to a folder beside the export, named after it with a <c>_files</c> suffix.</summary>
        Folder,

        /// <summary>Put them inside the export: base64 in JSON and XML, data URIs in templates.</summary>
        Embed,

        /// <summary>Describe them but don't write them.</summary>
        None,
    }

    /// <summary>
    /// One export: which messages, which format, and where to write.
    /// </summary>
    public sealed record ExportRequest
    {
        /// <summary>
        /// The chat or email thread to export, or null to export the conversation between
        /// <see cref="People"/> across every chat and thread.
        /// </summary>
        public string? Chat { get; init; }

        /// <summary>WhatsApp or Email to export only that source, or null for both.</summary>
        public string? Source { get; init; }

        /// <summary>
        /// Without <see cref="Chat"/>: only messages between <see cref="People"/> alone, leaving out
        /// group chats and emails that went to anyone else.
        /// </summary>
        public bool Direct { get; init; }

        /// <summary>
        /// Senders to include. Empty means every sender, plus system messages. Without
        /// <see cref="Chat"/> there must be at least two: the export is then the messages each sent
        /// to another of them.
        /// </summary>
        public IReadOnlyList<string> People { get; init; } = Array.Empty<string>();

        /// <summary>Inclusive.</summary>
        public DateTime? From { get; init; }

        /// <summary>Exclusive.</summary>
        public DateTime? To { get; init; }

        /// <summary>The person whose messages are shown as the reader's own (on the right in HTML), or null.</summary>
        public string? Me { get; init; }

        public required ExportFormat Format { get; init; }

        /// <summary>The template file, for <see cref="ExportFormat.Template"/>.</summary>
        public string? TemplatePath { get; init; }

        public AttachmentMode Attachments { get; init; } = AttachmentMode.Folder;

        /// <summary>For CSV: write it for Excel (a BOM, and formula-like values made safe).</summary>
        public bool Excel { get; init; }

        public required string OutputPath { get; init; }

        /// <summary>
        /// Works out the format from an output file's extension, or returns null if it doesn't say.
        /// </summary>
        public static ExportFormat? FormatFromExtension(string path)
        {
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".json" => ExportFormat.Json,
                ".xml" => ExportFormat.Xml,
                ".md" or ".markdown" => ExportFormat.Markdown,
                ".html" or ".htm" => ExportFormat.Html,
                ".csv" => ExportFormat.Csv,
                ".txt" or ".zip" => ExportFormat.WhatsApp,
                _ => null,
            };
        }

        /// <summary>
        /// Parses a format name as typed on the command line, or returns null if it isn't one.
        /// </summary>
        public static ExportFormat? ParseFormat(string name)
        {
            return name.ToLowerInvariant() switch
            {
                "json" => ExportFormat.Json,
                "xml" => ExportFormat.Xml,
                "md" or "markdown" => ExportFormat.Markdown,
                "html" => ExportFormat.Html,
                "csv" => ExportFormat.Csv,
                "whatsapp" or "txt" => ExportFormat.WhatsApp,
                _ => null,
            };
        }
    }
}
