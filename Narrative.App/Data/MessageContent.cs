namespace Narrative.App.Data
{
    /// <summary>
    /// The content of a message, independent of where it was stored or parsed from.
    /// </summary>
    /// <param name="Kind">What the message represents.</param>
    /// <param name="Text">The text with any edit marker removed. For media this is the caption, or empty.</param>
    /// <param name="IsEdited">True if the export marked the message as edited.</param>
    /// <param name="AttachmentName">The attachment file name, or null if there is none or the export omitted media.</param>
    public readonly record struct MessageContent(
        MessageKind Kind,
        string Text,
        bool IsEdited,
        string? AttachmentName);
}
