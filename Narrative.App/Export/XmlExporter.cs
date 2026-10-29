using System.Globalization;
using System.Text;
using System.Xml;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes an export as XML, with the same content and layout version as the JSON export.
    /// Optionally adds an <c>xml-stylesheet</c> instruction so a browser can apply an XSLT stylesheet.
    /// </summary>
    public static class XmlExporter
    {
        /// <param name="embed">Loads a file's bytes to embed as base64, or null to leave them out.</param>
        /// <param name="stylesheet">A stylesheet URL for an <c>xml-stylesheet</c> instruction, or null.</param>
        public static void Write(ExportDocument document, Stream stream, Func<ExportAttachment, byte[]>? embed, string? stylesheet = null)
        {
            using var xml = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                // Keeps a carriage return in a message from turning into a line feed when read back.
                NewLineHandling = NewLineHandling.Entitize,
            });

            xml.WriteStartDocument();
            if (stylesheet != null)
            {
                xml.WriteProcessingInstruction("xml-stylesheet", $"type=\"text/xsl\" href=\"{stylesheet}\"");
            }
            xml.WriteStartElement("narrative");
            xml.WriteAttributeString("formatVersion", document.FormatVersion.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("exportedAt", Format(document.ExportedAt));

            xml.WriteElementString("title", Clean(document.Title));
            if (document.Chat != null)
            {
                xml.WriteStartElement("chat");
                xml.WriteAttributeString("source", document.Chat.Source);
                xml.WriteAttributeString("name", Clean(document.Chat.Name));
                xml.WriteEndElement();
            }

            xml.WriteStartElement("selection");
            WriteOptionalTime(xml, "from", document.Selection.From);
            WriteOptionalTime(xml, "to", document.Selection.To);
            if (document.Selection.Me != null)
            {
                xml.WriteAttributeString("me", Clean(document.Selection.Me));
            }
            if (document.Selection.Source != null)
            {
                xml.WriteAttributeString("source", document.Selection.Source);
            }
            if (document.Selection.Direct)
            {
                xml.WriteAttributeString("direct", "true");
            }
            foreach (var person in document.Selection.People)
            {
                xml.WriteElementString("person", Clean(person));
            }
            xml.WriteEndElement();

            xml.WriteStartElement("people");
            foreach (var person in document.People)
            {
                xml.WriteElementString("person", Clean(person));
            }
            xml.WriteEndElement();

            xml.WriteStartElement("messages");
            foreach (var m in document.Messages)
            {
                xml.WriteStartElement("message");
                xml.WriteAttributeString("id", m.Id.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("timestamp", Format(m.Timestamp));
                xml.WriteAttributeString("source", m.Source);
                xml.WriteAttributeString("chat", Clean(m.Chat));
                if (m.Sender != null)
                {
                    xml.WriteAttributeString("sender", Clean(m.Sender));
                }
                xml.WriteAttributeString("kind", m.Kind);
                if (m.IsEdited)
                {
                    xml.WriteAttributeString("edited", "true");
                }
                if (m.Email != null)
                {
                    xml.WriteStartElement("email");
                    xml.WriteAttributeString("subject", Clean(m.Email.Subject));
                    xml.WriteAttributeString("messageId", Clean(m.Email.MessageId));
                    xml.WriteAttributeString("date", m.Email.Date);
                    WriteNames(xml, "to", m.Email.To);
                    WriteNames(xml, "cc", m.Email.Cc);
                    WriteNames(xml, "bcc", m.Email.Bcc);
                    xml.WriteEndElement();
                }
                if (m.Text.Length > 0)
                {
                    xml.WriteElementString("text", Clean(m.Text));
                }
                if (m.AttachmentState != null)
                {
                    xml.WriteStartElement("attachment");
                    xml.WriteAttributeString("state", m.AttachmentState);
                    if (m.AttachmentName != null)
                    {
                        xml.WriteAttributeString("name", Clean(m.AttachmentName));
                    }
                    if (m.Attachment != null)
                    {
                        xml.WriteAttributeString("ref", m.Attachment.Id.ToString(CultureInfo.InvariantCulture));
                    }
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();

            xml.WriteStartElement("attachments");
            foreach (var a in document.Attachments)
            {
                xml.WriteStartElement("attachment");
                xml.WriteAttributeString("id", a.Id.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("fileName", Clean(a.FileName));
                xml.WriteAttributeString("mediaType", a.MediaType);
                xml.WriteAttributeString("kind", a.Kind);
                xml.WriteAttributeString("size", a.Size.ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("sha256", a.Sha256);
                if (a.Path != null)
                {
                    xml.WriteAttributeString("path", Clean(a.Path));
                }
                if (embed != null)
                {
                    var bytes = embed(a);
                    xml.WriteBase64(bytes, 0, bytes.Length);
                }
                xml.WriteEndElement();
                xml.Flush();
            }
            xml.WriteEndElement();

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        private static void WriteNames(XmlWriter xml, string element, IReadOnlyList<string> names)
        {
            foreach (var name in names)
            {
                xml.WriteElementString(element, Clean(name));
            }
        }

        private static void WriteOptionalTime(XmlWriter xml, string name, DateTime? value)
        {
            if (value != null)
            {
                xml.WriteAttributeString(name, Format(value.Value));
            }
        }

        private static string Format(DateTime value)
        {
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Replaces characters XML 1.0 can't hold (most control characters, and unpaired surrogates)
        /// with U+FFFD, so one odd character can't make the whole export unreadable.
        /// </summary>
        private static string Clean(string text)
        {
            StringBuilder? cleaned = null;
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                bool valid;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cleaned?.Append(c).Append(text[i + 1]);
                    i += 1;
                    continue;
                }
                valid = !char.IsSurrogate(c) && XmlConvert.IsXmlChar(c);
                if (!valid && cleaned == null)
                {
                    cleaned = new StringBuilder(text.Length).Append(text, 0, i);
                }
                cleaned?.Append(valid ? c : '�');
            }
            return cleaned?.ToString() ?? text;
        }
    }
}
