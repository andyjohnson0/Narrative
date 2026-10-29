using System.Text;
using System.Text.RegularExpressions;
using Narrative.App.Data;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// The messages read from one export, plus anything that couldn't be read.
    /// </summary>
    public sealed class ExportParseResult
    {
        public List<ParsedMessage> Messages { get; } = new();

        /// <summary>Line numbers of text before the first message header, which belongs to no message.</summary>
        public List<int> OrphanLines { get; } = new();

        /// <summary>Number of end-to-end encryption notices skipped.</summary>
        public int SkippedNotices { get; set; }
    }

    /// <summary>
    /// Reads a WhatsApp text export into messages.
    /// </summary>
    public partial class ExportParser
    {
        public const string EditedMarker = "<This message was edited>";
        public const string MediaOmitted = "<Media omitted>";

        public const string AttachedSuffix = " (file attached)";

        // WhatsApp adds one of these at the top of every export, timestamped with the export's first
        // message. It isn't part of the conversation, and its wording varies between app versions and
        // between ordinary chats and a chat with yourself.
        private static readonly string[] EncryptionNoticePrefixes =
        {
            "Messages and calls are end-to-end encrypted.",
            "Messages to this chat and calls are now secured with end-to-end encryption.",
            "Messages to yourself are end-to-end encrypted.",
        };

        private static readonly string[] DeletedPlaceholders =
        {
            "You deleted this message",
            "This message was deleted",
        };

        private readonly IHeaderFormat headerFormat;

        public ExportParser(IHeaderFormat headerFormat)
        {
            this.headerFormat = headerFormat;
        }

        [GeneratedRegex(@"^(?<sender>[^:]{1,80}): (?<text>.*)$", RegexOptions.Singleline)]
        private static partial Regex SenderRegex();

        public ExportParseResult Parse(TextReader reader)
        {
            var result = new ExportParseResult();

            int lineNumber = 0;
            int headerLine = 0;
            DateTime timestamp = default;
            string? rest = null;
            var text = new StringBuilder();

            void Flush()
            {
                if (rest == null)
                {
                    return;
                }
                text.Insert(0, rest);
                // WhatsApp doesn't send trailing blank lines, so any here are file padding.
                var message = CreateMessage(headerLine, timestamp, text.ToString().TrimEnd('\n'));
                if (message == null)
                {
                    result.SkippedNotices += 1;
                }
                else
                {
                    result.Messages.Add(message);
                }
                text.Clear();
                rest = null;
            }

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber += 1;
                if (headerFormat.TryParse(line, out var lineTimestamp, out var lineRest))
                {
                    Flush();
                    headerLine = lineNumber;
                    timestamp = lineTimestamp;
                    rest = lineRest;
                }
                else if (rest != null)
                {
                    text.Append('\n').Append(line);
                }
                else
                {
                    result.OrphanLines.Add(lineNumber);
                }
            }
            Flush();

            return result;
        }

        /// <summary>
        /// Builds a message from a header's remainder plus continuation lines. Returns null for an
        /// encryption notice.
        /// </summary>
        private static ParsedMessage? CreateMessage(int lineNumber, DateTime timestamp, string rest)
        {
            var match = SenderRegex().Match(rest);
            if (!match.Success)
            {
                if (EncryptionNoticePrefixes.Any(p => rest.StartsWith(p, StringComparison.Ordinal)))
                {
                    return null;
                }
                return new ParsedMessage(lineNumber, timestamp, null, rest,
                    new MessageContent(MessageKind.System, rest, false, null));
            }

            var sender = match.Groups["sender"].Value;
            var rawText = match.Groups["text"].Value;
            return new ParsedMessage(lineNumber, timestamp, sender, rawText, Interpret(rawText));
        }

        /// <summary>
        /// Works out what a sender's message text represents.
        /// </summary>
        public static MessageContent Interpret(string rawText)
        {
            var text = rawText;
            bool isEdited = false;
            if (text.EndsWith(EditedMarker, StringComparison.Ordinal))
            {
                text = text[..^EditedMarker.Length].TrimEnd(' ');
                isEdited = true;
            }

            if (DeletedPlaceholders.Contains(text))
            {
                return new MessageContent(MessageKind.Deleted, text, isEdited, null);
            }

            // Media is "<Media omitted>" or "<file name> (file attached)", optionally followed by caption lines.
            var newline = text.IndexOf('\n');
            var firstLine = newline < 0 ? text : text[..newline];
            if (firstLine == MediaOmitted)
            {
                var caption = newline < 0 ? "" : text[(newline + 1)..];
                return new MessageContent(MessageKind.Media, caption, isEdited, null);
            }
            if (firstLine.EndsWith(AttachedSuffix, StringComparison.Ordinal))
            {
                var attachment = firstLine[..^AttachedSuffix.Length];
                var caption = newline < 0 ? "" : text[(newline + 1)..];
                return new MessageContent(MessageKind.Media, caption, isEdited, attachment);
            }

            return new MessageContent(MessageKind.Text, text, isEdited, null);
        }
    }
}
