using System.Globalization;
using Narrative.App.WhatsApp;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes messages in WhatsApp's own export format (Android, UK locale), so an export can be read
    /// by anything that reads WhatsApp exports, including narrative's importer.
    /// </summary>
    /// <remarks>
    /// Each message is written as WhatsApp wrote it: <c>dd/MM/yyyy, HH:mm - Sender: text</c>, with the
    /// same markers for media, deleted and edited messages. Like a real export, the file starts with an
    /// end-to-end encryption notice, is UTF-8 without a BOM, and uses LF line endings.
    /// Email is written the same way, so a conversation across sources reads as one chat: the body
    /// as the text, each attachment as a media message, and the subject as a line of its own on the
    /// email's first message. Times lose their seconds. Importing such an export gives a WhatsApp
    /// chat, not the emails.
    /// </remarks>
    public static class WhatsAppTextExporter
    {
        public const string EncryptionNotice =
            "Messages and calls are end-to-end encrypted. Only people in this chat can read, listen to, or share them. Learn more.";

        /// <summary>Starts the line that gives an email's subject, as in the Markdown export.</summary>
        public const string SubjectMarker = "✉ ";

        /// <summary>The name WhatsApp gives the text file: <c>WhatsApp Chat with &lt;chat&gt;.txt</c>.</summary>
        public static string ChatFileName(string chat)
        {
            return $"WhatsApp Chat with {chat}.txt";
        }

        /// <param name="attachmentNames">Names to give some messages' attachments instead of their own,
        /// when the files are written under other names.</param>
        public static void Write(ExportDocument document, TextWriter output,
            IReadOnlyDictionary<ExportMessage, string>? attachmentNames = null)
        {
            if (document.Messages.Count == 0)
            {
                return;
            }
            output.Write(Header(document.Messages[0].Timestamp));
            output.Write(EncryptionNotice);
            output.Write('\n');
            foreach (var m in document.Messages)
            {
                output.Write(Header(m.Timestamp));
                if (m.Sender != null)
                {
                    output.Write(m.Sender);
                    output.Write(": ");
                }
                output.Write(WithSubject(m, Body(m, attachmentName: attachmentNames?.GetValueOrDefault(m))));
                output.Write('\n');
            }
        }

        /// <summary>
        /// Puts an email's subject on its first message, as a line of its own: before the body, or
        /// after the file name of an attachment, because WhatsApp's media marker must come first.
        /// </summary>
        private static string WithSubject(ExportMessage m, string body)
        {
            if (!m.EmailStart || m.Email == null || m.Email.Subject.Length == 0)
            {
                return body;
            }
            var subject = SubjectMarker + m.Email.Subject;
            if (m.Kind != "media")
            {
                return body.Length == 0 ? subject : subject + "\n" + body;
            }
            var lineEnd = body.IndexOf('\n');
            return lineEnd < 0 ? body + "\n" + subject : body[..lineEnd] + "\n" + subject + body[lineEnd..];
        }

        private static string Header(DateTime timestamp)
        {
            return timestamp.ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture) + " - ";
        }

        /// <summary>
        /// A message's text with WhatsApp's markers: the inverse of <see cref="ExportParser.Interpret"/>.
        /// </summary>
        /// <param name="editedMarker">Whether to add WhatsApp's edited marker to an edited message.</param>
        /// <param name="attachmentName">The attachment's name, if not the message's own.</param>
        public static string Body(ExportMessage m, bool editedMarker = true, string? attachmentName = null)
        {
            var body = m.Text;
            if (m.Kind == "media")
            {
                attachmentName ??= m.AttachmentName;
                var first = attachmentName != null ? attachmentName + ExportParser.AttachedSuffix : ExportParser.MediaOmitted;
                body = m.Text.Length == 0 ? first : first + "\n" + m.Text;
            }
            if (m.IsEdited && editedMarker)
            {
                body += " " + ExportParser.EditedMarker;
            }
            return body;
        }
    }
}
