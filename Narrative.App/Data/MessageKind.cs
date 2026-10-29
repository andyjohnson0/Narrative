namespace Narrative.App.Data
{
    /// <summary>
    /// What a stored message represents.
    /// </summary>
    public enum MessageKind
    {
        /// <summary>Ordinary text from a participant.</summary>
        Text,

        /// <summary>A line from the chat itself rather than a participant, e.g. a security code change.</summary>
        System,

        /// <summary>An attachment, with an optional caption. The attachment name is unknown if the export omitted media.</summary>
        Media,

        /// <summary>A placeholder for a message that was deleted before the export was made.</summary>
        Deleted,
    }
}
