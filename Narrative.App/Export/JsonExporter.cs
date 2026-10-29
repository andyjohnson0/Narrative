using System.Text.Encodings.Web;
using System.Text.Json;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes an export as JSON. The layout is versioned by <see cref="ExportDocument.FormatVersion"/>.
    /// Timestamps are UK local wall-clock times with no offset, because WhatsApp exports don't say which
    /// zone they're in. An email's own Date, with its offset, is in its email object.
    /// </summary>
    public static class JsonExporter
    {
        /// <param name="embed">Loads a file's bytes to embed as base64, or null to leave them out.</param>
        public static void Write(ExportDocument document, Stream stream, Func<ExportAttachment, byte[]>? embed)
        {
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
                // Keeps non-ASCII text readable; the output is a file, not embedded in HTML.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            json.WriteStartObject();
            json.WriteNumber("formatVersion", document.FormatVersion);
            json.WriteString("exportedAt", Format(document.ExportedAt));

            json.WriteString("title", document.Title);
            if (document.Chat != null)
            {
                json.WriteStartObject("chat");
                json.WriteString("source", document.Chat.Source);
                json.WriteString("name", document.Chat.Name);
                json.WriteEndObject();
            }

            json.WriteStartObject("selection");
            json.WriteStartArray("people");
            foreach (var person in document.Selection.People)
            {
                json.WriteStringValue(person);
            }
            json.WriteEndArray();
            WriteOptionalTime(json, "from", document.Selection.From);
            WriteOptionalTime(json, "to", document.Selection.To);
            if (document.Selection.Me != null)
            {
                json.WriteString("me", document.Selection.Me);
            }
            if (document.Selection.Source != null)
            {
                json.WriteString("source", document.Selection.Source);
            }
            if (document.Selection.Direct)
            {
                json.WriteBoolean("direct", true);
            }
            json.WriteEndObject();

            json.WriteStartArray("people");
            foreach (var person in document.People)
            {
                json.WriteStringValue(person);
            }
            json.WriteEndArray();

            json.WriteStartArray("messages");
            foreach (var m in document.Messages)
            {
                json.WriteStartObject();
                json.WriteNumber("id", m.Id);
                json.WriteString("timestamp", Format(m.Timestamp));
                json.WriteString("source", m.Source);
                json.WriteString("chat", m.Chat);
                if (m.Sender != null)
                {
                    json.WriteString("sender", m.Sender);
                }
                if (m.Email != null)
                {
                    json.WriteStartObject("email");
                    json.WriteString("subject", m.Email.Subject);
                    json.WriteString("messageId", m.Email.MessageId);
                    json.WriteString("date", m.Email.Date);
                    WriteNames(json, "to", m.Email.To);
                    WriteNames(json, "cc", m.Email.Cc);
                    WriteNames(json, "bcc", m.Email.Bcc);
                    json.WriteEndObject();
                }
                json.WriteString("kind", m.Kind);
                json.WriteString("text", m.Text);
                if (m.IsEdited)
                {
                    json.WriteBoolean("edited", true);
                }
                if (m.AttachmentState != null)
                {
                    json.WriteStartObject("attachment");
                    json.WriteString("state", m.AttachmentState);
                    if (m.AttachmentName != null)
                    {
                        json.WriteString("name", m.AttachmentName);
                    }
                    if (m.Attachment != null)
                    {
                        json.WriteNumber("id", m.Attachment.Id);
                    }
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("attachments");
            foreach (var a in document.Attachments)
            {
                json.WriteStartObject();
                json.WriteNumber("id", a.Id);
                json.WriteString("fileName", a.FileName);
                json.WriteString("mediaType", a.MediaType);
                json.WriteString("kind", a.Kind);
                json.WriteNumber("size", a.Size);
                json.WriteString("sha256", a.Sha256);
                if (a.Path != null)
                {
                    json.WriteString("path", a.Path);
                }
                if (embed != null)
                {
                    json.WriteBase64String("data", embed(a));
                }
                json.WriteEndObject();
                // Flushing per file keeps embedded data from piling up in the writer's buffer.
                json.Flush();
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }

        private static void WriteNames(Utf8JsonWriter json, string name, IReadOnlyList<string> names)
        {
            json.WriteStartArray(name);
            foreach (var n in names)
            {
                json.WriteStringValue(n);
            }
            json.WriteEndArray();
        }

        private static void WriteOptionalTime(Utf8JsonWriter json, string name, DateTime? value)
        {
            if (value != null)
            {
                json.WriteString(name, Format(value.Value));
            }
        }

        private static string Format(DateTime value)
        {
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
