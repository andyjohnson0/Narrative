using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Narrative.App.Data;
using Narrative.App.Mail;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes JSON Lines: one object per message, <c>{"userid":"Sam Taylor","content":"..."}</c>, with
    /// only what was said. Made for feeding conversations to other programs, such as language models.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>userid: the sender's main name, so aliases are mapped.</item>
    /// <item>content: the message text. Email loses its quoted lines (all of them, not only the
    /// trailing quote) and the "On ..., X wrote:" lines above them. Media keeps only its caption.
    /// WhatsApp's @mention marks are removed, trailing spaces trimmed and blank lines collapsed;
    /// line breaks stay.</item>
    /// </list>
    /// System messages, deleted placeholders and messages left with no text (media without a caption)
    /// are skipped. Timestamps and subjects are left out; lines are in time order. The file is UTF-8
    /// without a BOM, each line ending in LF.
    /// </remarks>
    public static partial class JsonlExporter
    {
        /// <summary>Writes the lines, and returns how many there are.</summary>
        public static int Write(ExportDocument document, Stream stream)
        {
            var options = new JsonWriterOptions
            {
                // Keeps non-ASCII text readable; the output is a file, not embedded in HTML.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            using var json = new Utf8JsonWriter(stream, options);
            var count = 0;
            foreach (var m in document.Messages)
            {
                var content = Content(m);
                if (m.Sender == null || content == null)
                {
                    continue;
                }
                json.WriteStartObject();
                json.WriteString("userid", m.Sender);
                json.WriteString("content", content);
                json.WriteEndObject();
                json.Flush();
                stream.WriteByte((byte)'\n');
                json.Reset();
                count += 1;
            }
            return count;
        }

        /// <summary>The text to write for a message, or null if it has none worth keeping.</summary>
        public static string? Content(ExportMessage message)
        {
            if (message.Kind is not ("text" or "media"))
            {
                return null;
            }
            // WhatsApp has its own > quote formatting, which is the sender's text, so only email loses quotes.
            var text = message.Source == Conversation.EmailSource ? QuoteTrimmer.RemoveQuotes(message.Text) : message.Text;
            text = Tidy(CsvExporter.StripIsolates(text));
            return text.Length == 0 ? null : text;
        }

        [GeneratedRegex(@"[ \t]+(?=\n|$)")]
        private static partial Regex TrailingSpaces();

        [GeneratedRegex(@"\n{3,}")]
        private static partial Regex BlankLines();

        /// <summary>LF line endings, no trailing spaces, at most one blank line in a row, and no blank lines at either end.</summary>
        private static string Tidy(string text)
        {
            text = TrailingSpaces().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "");
            return BlankLines().Replace(text, "\n\n").Trim('\n');
        }
    }
}
