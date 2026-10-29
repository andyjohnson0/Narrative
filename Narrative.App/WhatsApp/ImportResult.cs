namespace Narrative.App.WhatsApp
{
    public enum ImportStatus
    {
        Imported,

        /// <summary>A text file with the same contents was imported before. Its attachments are still processed.</summary>
        AlreadyImported,

        /// <summary>The file contained no messages.</summary>
        Empty,
    }

    /// <summary>
    /// What happened when one export text file, or one email file, was imported.
    /// </summary>
    public sealed class ImportResult
    {
        /// <summary>The text file's name, prefixed by its zip's name if it has one.</summary>
        public required string FileName { get; init; }

        public required ImportStatus Status { get; init; }

        public string? ChatName { get; init; }

        /// <summary>Messages read from the file.</summary>
        public int Parsed { get; init; }

        /// <summary>Messages not seen in any earlier import.</summary>
        public int Added { get; init; }

        /// <summary>Messages that were already stored.</summary>
        public int Matched { get; init; }

        /// <summary>Stored messages whose content this file improved.</summary>
        public int Updated { get; init; }

        /// <summary>Lines before the first message header, which were ignored.</summary>
        public int OrphanLines { get; init; }

        /// <summary>Attachments the text file named.</summary>
        public int AttachmentsReferenced { get; init; }

        /// <summary>Named attachments whose files weren't in the folder or zip.</summary>
        public int AttachmentsMissing { get; init; }

        /// <summary>Messages whose attachment file this import supplied.</summary>
        public int AttachmentsLinked { get; init; }

        /// <summary>Files in the zip that no message names. Always 0 for a folder.</summary>
        public int UnreferencedFiles { get; init; }

        /// <summary>Problems worth reporting that didn't stop the import.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }
}
