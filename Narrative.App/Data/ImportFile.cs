namespace Narrative.App.Data
{
    /// <summary>
    /// An export or email file that has been imported. The hash stops the same file being imported twice.
    /// </summary>
    public class ImportFile
    {
        public int Id { get; set; }

        public ImportFileKind Kind { get; set; }

        /// <summary>The chat a WhatsApp file was imported into. Null for email files, which can span threads.</summary>
        public int? ConversationId { get; set; }

        public Conversation? Conversation { get; set; }

        /// <summary>The chat text file's or email file's name.</summary>
        public required string FileName { get; set; }

        /// <summary>The zip the text file was read from, or null if it was read from a folder.</summary>
        public string? ContainerName { get; set; }

        /// <summary>SHA-256 of the file contents, as lower-case hex.</summary>
        public required string Sha256 { get; set; }

        public DateTime ImportedAt { get; set; }

        public DateTime FirstTimestamp { get; set; }

        public DateTime LastTimestamp { get; set; }

        public int ParsedCount { get; set; }

        public int AddedCount { get; set; }

        public int MatchedCount { get; set; }

        public int UpdatedCount { get; set; }

        /// <summary>Attachments the text file named.</summary>
        public int AttachmentsReferenced { get; set; }

        /// <summary>Named attachments whose files weren't beside the text file or in its zip.</summary>
        public int AttachmentsMissing { get; set; }
    }
}
