using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MimeKit;
using MimeKit.Text;
using MimeKit.Utils;
using Narrative.App.Data;

namespace Narrative.App.Mail
{
    /// <summary>
    /// Turns a MIME message into what narrative keeps: who, when, the subject, the body as text, and
    /// the attached files. Everything else in the headers is dropped.
    /// </summary>
    /// <remarks>
    /// <para>The body is the plain-text version where the email has one, else its HTML converted to
    /// text. Format=flowed text is unwrapped. The quoted copy of earlier emails at the end of a reply
    /// is left out of <see cref="ParsedEmail.Body"/> (see <see cref="QuoteTrimmer"/>); quotes the
    /// sender wrote after, as in inline replies, are kept. <see cref="ParsedEmail.FullBody"/> has
    /// everything.</para>
    /// <para>Attachments are parts marked as attachments, forwarded emails (kept as .eml files), and
    /// other inline files such as photos pasted into the message. Images embedded in an HTML body
    /// (inside multipart/related, such as logos in signatures) aren't kept, because the HTML isn't
    /// either; nor are other versions of the body (inside multipart/alternative, such as AMP), unnamed
    /// text parts, or signature parts.</para>
    /// </remarks>
    public static class EmailReader
    {
        public static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        static EmailReader()
        {
            CodePages.EnsureRegistered();
        }

        /// <summary>
        /// Reads a message, or returns null with a reason if it can't be placed in time.
        /// </summary>
        public static ParsedEmail? Read(MimeMessage message, out string? problem)
        {
            problem = null;
            var date = SentDate(message);
            if (date == null)
            {
                problem = "no usable Date or Received header";
                return null;
            }

            var from = message.From.Mailboxes.FirstOrDefault() ?? message.Sender;
            var recipients = new List<ParsedRecipient>();
            AddRecipients(recipients, message.To, RecipientRole.To);
            AddRecipients(recipients, message.Cc, RecipientRole.Cc);
            AddRecipients(recipients, message.Bcc, RecipientRole.Bcc);

            var body = new List<string>();
            CollectText(message.Body, body);
            var attachments = new List<ParsedAttachment>();
            CollectAttachments(message.Body, attachments, embedded: false);

            var subject = (message.Subject ?? "").Trim();
            var fromAddress = from == null ? null : AddressOf(from);
            var text = string.Join("\n\n", body.Where(b => b.Length > 0));
            // The stand-in id comes from the whole body, so it doesn't change if the trimming does.
            var messageId = string.IsNullOrWhiteSpace(message.MessageId)
                ? StandIn(fromAddress, date.Value, subject, text)
                : message.MessageId.Trim();

            return new ParsedEmail
            {
                MessageId = messageId,
                Subject = subject,
                InReplyTo = string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo.Trim(),
                References = message.References.Where(r => !string.IsNullOrWhiteSpace(r)).ToList(),
                Date = date.Value,
                LocalTime = TimeZoneInfo.ConvertTime(date.Value, UkTime).DateTime,
                From = fromAddress,
                Recipients = recipients,
                Body = QuoteTrimmer.TrimTrailingQuote(text),
                FullBody = text,
                Attachments = attachments,
            };
        }

        /// <summary>
        /// The Date header, or failing that the time the newest Received header records.
        /// </summary>
        private static DateTimeOffset? SentDate(MimeMessage message)
        {
            if (message.Date != DateTimeOffset.MinValue)
            {
                return message.Date;
            }
            foreach (var header in message.Headers.Where(h => h.Id == HeaderId.Received))
            {
                var semicolon = header.Value.LastIndexOf(';');
                if (semicolon >= 0 && DateUtils.TryParse(header.Value[(semicolon + 1)..].Trim(), out var received))
                {
                    return received;
                }
            }
            return null;
        }

        /// <summary>
        /// Stands in for a missing Message-ID, so copies of the same email still match.
        /// </summary>
        private static string StandIn(EmailAddress? from, DateTimeOffset date, string subject, string body)
        {
            var key = string.Join("\n", from?.Address ?? "", date.ToString("O", CultureInfo.InvariantCulture), subject, body);
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        private static void AddRecipients(List<ParsedRecipient> recipients, InternetAddressList list, RecipientRole role)
        {
            foreach (var mailbox in list.Mailboxes)
            {
                var address = AddressOf(mailbox);
                if (address.Address.Length > 0 && !recipients.Any(r => r.Address.Address == address.Address && r.Role == role))
                {
                    recipients.Add(new ParsedRecipient(address, role));
                }
            }
        }

        private static EmailAddress AddressOf(MailboxAddress mailbox)
        {
            var address = PersonDirectory.NormaliseAddress(mailbox.Address ?? "");
            // Some clients quote the name again, or repeat the address as the name.
            var name = (mailbox.Name ?? "").Trim().Trim('\'', '"').Trim();
            if (name.Length == 0 || PersonDirectory.NormaliseAddress(name) == address)
            {
                return new EmailAddress(address, null);
            }
            return new EmailAddress(address, name);
        }

        /// <summary>
        /// Adds the body text of <paramref name="entity"/>: the plain version of each alternative where
        /// there is one (else its HTML as text), and every inline text part of a mixed message in order.
        /// </summary>
        private static void CollectText(MimeEntity? entity, List<string> parts)
        {
            switch (entity)
            {
                case MultipartAlternative alternative:
                    CollectText(alternative.FirstOrDefault(HasPlainText) ?? alternative.LastOrDefault(), parts);
                    break;
                case MultipartRelated related:
                    CollectText(related.Root, parts);
                    break;
                case Multipart multipart:
                    foreach (var child in multipart)
                    {
                        CollectText(child, parts);
                    }
                    break;
                case TextPart text when !text.IsAttachment && text.IsPlain:
                    parts.Add(Normalise(Flowed(text)));
                    break;
                case TextPart text when !text.IsAttachment && text.IsHtml:
                    parts.Add(Normalise(HtmlText.Convert(text.Text)));
                    break;
            }
        }

        private static bool HasPlainText(MimeEntity entity)
        {
            return entity switch
            {
                TextPart text => text.IsPlain && !text.IsAttachment,
                Multipart multipart => multipart.Any(HasPlainText),
                _ => false,
            };
        }

        private static string Flowed(TextPart text)
        {
            if (text.ContentType.Parameters.TryGetValue("format", out string? format)
                && format.Equals("flowed", StringComparison.OrdinalIgnoreCase))
            {
                var delsp = text.ContentType.Parameters.TryGetValue("delsp", out string? value)
                    && value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                return new FlowedToText { DeleteSpace = delsp }.Convert(text.Text);
            }
            return text.Text;
        }

        /// <summary>LF line endings, no trailing spaces on lines, and no blank lines at either end.</summary>
        private static string Normalise(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
            return string.Join("\n", lines).Trim('\n');
        }

        private static readonly HashSet<string> SignatureTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pkcs7-signature",
            "application/x-pkcs7-signature",
            "application/pgp-signature",
        };

        private static void CollectAttachments(MimeEntity? entity, List<ParsedAttachment> attachments, bool embedded)
        {
            switch (entity)
            {
                case MultipartRelated related:
                    // Everything but the root is a resource for the HTML body.
                    foreach (var child in related)
                    {
                        CollectAttachments(child, attachments, embedded || child != related.Root);
                    }
                    break;
                case MultipartAlternative alternative:
                    // Other versions of the body (HTML, AMP, calendar data) aren't files someone sent.
                    foreach (var child in alternative)
                    {
                        CollectAttachments(child, attachments, embedded: true);
                    }
                    break;
                case Multipart multipart:
                    foreach (var child in multipart)
                    {
                        CollectAttachments(child, attachments, embedded);
                    }
                    break;
                case MessagePart { Message: not null } forwarded:
                    using (var stream = new MemoryStream())
                    {
                        forwarded.Message.WriteTo(stream);
                        var name = forwarded.ContentDisposition?.FileName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = SafeName(forwarded.Message.Subject) + ".eml";
                        }
                        attachments.Add(new ParsedAttachment(name.Trim(), "message/rfc822", stream.ToArray()));
                    }
                    break;
                case MimePart part when IsAttachment(part, embedded):
                    using (var stream = new MemoryStream())
                    {
                        part.Content?.DecodeTo(stream);
                        var mediaType = part.ContentType.MimeType.ToLowerInvariant();
                        var name = part.FileName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = $"attachment-{attachments.Count + 1}" +
                                   (MimeTypes.TryGetExtension(mediaType, out var extension) ? extension : ".bin");
                        }
                        attachments.Add(new ParsedAttachment(name.Trim(), mediaType, stream.ToArray()));
                    }
                    break;
            }
        }

        private static bool IsAttachment(MimePart part, bool embedded)
        {
            if (SignatureTypes.Contains(part.ContentType.MimeType))
            {
                return false;
            }
            if (part.IsAttachment)
            {
                return true;
            }
            if (part is TextPart && string.IsNullOrWhiteSpace(part.FileName))
            {
                // Part of the body, or another form of it.
                return false;
            }
            return !embedded;
        }

        private static string SafeName(string? subject)
        {
            var name = string.IsNullOrWhiteSpace(subject) ? "forwarded" : subject.Trim();
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
