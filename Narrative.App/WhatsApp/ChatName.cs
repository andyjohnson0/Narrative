using System.Text.RegularExpressions;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Derives a chat name from an export file name. WhatsApp names exports "WhatsApp Chat with X.txt";
    /// a user may append the export date, and Windows may append a copy number.
    /// </summary>
    public static partial class ChatName
    {
        [GeneratedRegex(@"^WhatsApp Chat with (?<name>.+?)(?: \d{4}-\d{2}-\d{2})?(?: \(\d+\))?$",
            RegexOptions.IgnoreCase)]
        private static partial Regex FileNameRegex();

        /// <summary>
        /// Returns the chat name for <paramref name="path"/>, or null if the file name doesn't follow
        /// the WhatsApp pattern.
        /// </summary>
        public static string? FromFileName(string path)
        {
            var match = FileNameRegex().Match(Path.GetFileNameWithoutExtension(path).Trim());
            return match.Success ? match.Groups["name"].Value.Trim() : null;
        }
    }
}
