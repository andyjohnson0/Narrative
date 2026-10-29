using Narrative.App.Data;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// One message read from an export file.
    /// </summary>
    /// <param name="LineNumber">1-based line number of the message header.</param>
    /// <param name="Timestamp">Local wall-clock time, to the minute.</param>
    /// <param name="Sender">The sender's display name, or null for a system message.</param>
    /// <param name="RawText">The message text exactly as exported, including continuation lines.</param>
    /// <param name="Content">The interpreted content.</param>
    public sealed record ParsedMessage(
        int LineNumber,
        DateTime Timestamp,
        string? Sender,
        string RawText,
        MessageContent Content);
}
