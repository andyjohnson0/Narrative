namespace Narrative.App.Data
{
    /// <summary>
    /// The bytes of an attachment, kept apart from its metadata.
    /// </summary>
    public class AttachmentData
    {
        public int AttachmentId { get; set; }

        public required byte[] Bytes { get; set; }
    }
}
