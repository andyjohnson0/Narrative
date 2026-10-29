namespace Narrative.App.Data
{
    /// <summary>
    /// A stored attachment file, identified by its content. The same file shared by several messages
    /// or exports is stored once. The bytes live in <see cref="AttachmentData"/> so that queries over
    /// attachments don't load them.
    /// </summary>
    public class Attachment
    {
        public int Id { get; set; }

        /// <summary>SHA-256 of the file contents, as lower-case hex.</summary>
        public required string Sha256 { get; set; }

        public long Size { get; set; }

        /// <summary>MIME type, guessed from the file name.</summary>
        public required string MediaType { get; set; }

        public AttachmentKind Kind { get; set; }

        /// <summary>The file name the attachment was first imported under.</summary>
        public required string FileName { get; set; }

        public AttachmentData Data { get; set; } = null!;
    }
}
