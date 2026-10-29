namespace Narrative.App.Data
{
    /// <summary>
    /// Whether a media message's file is available. Derived from the message, never stored.
    /// </summary>
    public enum AttachmentState
    {
        /// <summary>The message isn't media.</summary>
        None,

        /// <summary>Every export said "&lt;Media omitted&gt;", so even the file name is unknown.</summary>
        Omitted,

        /// <summary>An export named the file, but no import supplied it.</summary>
        Missing,

        /// <summary>The file is in the database.</summary>
        Stored,
    }
}
