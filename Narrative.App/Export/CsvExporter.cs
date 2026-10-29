using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes one line per message under a heading line: timestamp, person, text, kind, edited,
    /// attachment, source, chat and subject. Made to open cleanly in Excel as well as other tools.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>timestamp: <c>yyyy-MM-dd HH:mm:ss</c>, UK time. WhatsApp exports only give minutes, so
    /// their seconds are :00.</item>
    /// <item>person: their main name, so aliases are mapped; empty for a system message.</item>
    /// <item>text: what WhatsApp shows (media and deleted markers included, the edited marker not), on
    /// one line: each run of line breaks, and the spaces around it, becomes one space.</item>
    /// <item>kind: text, media, deleted or system. edited: true or false.</item>
    /// <item>attachment: the file name the message gives, or empty (including media left out of the
    /// export, whose name is unknown).</item>
    /// <item>source: WhatsApp or Email. chat: the chat or email thread's name. subject: the email's
    /// subject, on each of its lines (its body and each attachment), or empty for a chat message.</item>
    /// </list>
    /// Text fields are quoted with quotes doubled (RFC 4180). The invisible marks WhatsApp puts around
    /// @mentions (U+2068 and U+2069, and their U+2066/7 siblings) are removed. Lines end in CRLF, as
    /// RFC 4180 says, and the file is UTF-8 without a BOM, which suits Python and most other tools.
    /// <para>For Excel (<c>-excel</c>), the file gets a BOM, which Excel needs to read UTF-8, and a
    /// text field that would start with = + - @ or a tab gets a leading apostrophe, so Excel shows it
    /// instead of treating it as a formula (an @mention, or a phone-number name like +44 ...).</para>
    /// </remarks>
    public static partial class CsvExporter
    {
        public const string Heading = "timestamp,person,text,kind,edited,attachment,source,chat,subject";

        /// <summary>
        /// UTF-8, with a BOM for Excel: without one, Excel reads the file in the local code page.
        /// </summary>
        public static Encoding Encoding(bool excel)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: excel);
        }

        public static void Write(ExportDocument document, TextWriter output, bool excel = false)
        {
            output.Write(Heading);
            output.Write("\r\n");
            foreach (var m in document.Messages)
            {
                output.Write(m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                output.Write(',');
                output.Write(Field(m.Sender, excel));
                output.Write(',');
                output.Write(Field(WhatsAppTextExporter.Body(m, editedMarker: false), excel));
                output.Write(',');
                output.Write(m.Kind);
                output.Write(',');
                output.Write(m.IsEdited ? "true" : "false");
                output.Write(',');
                output.Write(Field(m.AttachmentName, excel));
                output.Write(',');
                output.Write(m.Source);
                output.Write(',');
                output.Write(Field(m.Chat, excel));
                output.Write(',');
                output.Write(Field(m.Email?.Subject, excel));
                output.Write("\r\n");
            }
        }

        /// <summary>A text value as a CSV field: cleaned, on one line, safe from Excel if asked, and quoted.</summary>
        public static string Field(string? value, bool excel = false)
        {
            var text = SingleLine(StripIsolates(value ?? ""));
            return Quote(excel ? GuardFormula(text) : text);
        }

        [GeneratedRegex(@"[ \t]*(?:[\r\n]|\p{Zl}|\p{Zp})+[ \t]*")]
        private static partial Regex LineBreaks();

        /// <summary>Replaces each run of line breaks, and the spaces around it, with one space.</summary>
        public static string SingleLine(string text)
        {
            return LineBreaks().Replace(text, " ");
        }

        /// <summary>
        /// Removes the Unicode directional isolates (U+2066 to U+2069). WhatsApp wraps @mention names in
        /// U+2068 and U+2069; they're invisible but break searching and comparing.
        /// </summary>
        public static string StripIsolates(string text)
        {
            return text.Any(IsIsolate) ? new string(text.Where(c => !IsIsolate(c)).ToArray()) : text;
        }

        private static bool IsIsolate(char c)
        {
            return c >= '\x2066' && c <= '\x2069';
        }

        /// <summary>
        /// Puts an apostrophe before a value Excel would read as a formula, even when it's quoted.
        /// </summary>
        public static string GuardFormula(string value)
        {
            return value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' ? "'" + value : value;
        }

        public static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
