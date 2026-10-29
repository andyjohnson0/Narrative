using System.Text;
using Narrative.App.WhatsApp;

namespace Narrative.App.Sources
{
    /// <summary>
    /// Finds the chat text files among the files in a folder or zip.
    /// </summary>
    public static class ChatFileFinder
    {
        private const int LinesToSniff = 10;

        /// <summary>
        /// Returns the names of the chat text files in <paramref name="container"/>, in name order. A
        /// chat file is a .txt that has a WhatsApp export name or starts like an export. A .txt that
        /// another chat file names as an attachment is a shared document, so it is left out.
        /// </summary>
        public static IReadOnlyList<string> Find(IExportContainer container, IHeaderFormat headerFormat)
        {
            var candidates = container.FileNames
                .Where(n => n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(n => (Name: n, Text: Decode(container.ReadAllBytes(n))))
                .Where(c => c.Name == "_chat.txt"
                    || ChatName.FromFileName(c.Name) != null
                    || StartsLikeExport(c.Text, headerFormat))
                .ToList();

            var attached = candidates
                .Where(c => candidates.Any(other => other.Name != c.Name
                    && other.Text.Contains(c.Name + ExportParser.AttachedSuffix, StringComparison.Ordinal)))
                .Select(c => c.Name)
                .ToHashSet();

            return candidates
                .Select(c => c.Name)
                .Where(n => !attached.Contains(n))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Decode(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static bool StartsLikeExport(string text, IHeaderFormat headerFormat)
        {
            return text.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .Take(LinesToSniff)
                .Any(l => headerFormat.TryParse(l, out _, out _));
        }
    }
}
