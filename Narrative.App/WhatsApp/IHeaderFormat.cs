namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Recognises the line that starts a message in one export dialect (platform + locale).
    /// </summary>
    public interface IHeaderFormat
    {
        /// <summary>
        /// Tries to parse <paramref name="line"/> as a message header.
        /// </summary>
        /// <param name="line">One line of the export.</param>
        /// <param name="timestamp">The message time, if the line is a header.</param>
        /// <param name="rest">Everything after the timestamp: "Sender: text", or a system message.</param>
        bool TryParse(string line, out DateTime timestamp, out string rest);
    }
}
