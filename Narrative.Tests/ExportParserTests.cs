using Narrative.App.Data;
using Narrative.App.WhatsApp;

namespace Narrative.Tests
{
    public class ExportParserTests
    {
        private static ExportParseResult Parse(string text)
        {
            return new ExportParser(AndroidUkHeaderFormat.Instance).Parse(new StringReader(text));
        }

        [Fact]
        public void ParsesHeaderSenderAndText()
        {
            var result = Parse("31/12/2021, 23:59 - Alice Example: Hello there\n");

            var m = Assert.Single(result.Messages);
            Assert.Equal(new DateTime(2021, 12, 31, 23, 59, 0), m.Timestamp);
            Assert.Equal("Alice Example", m.Sender);
            Assert.Equal("Hello there", m.RawText);
            Assert.Equal(new MessageContent(MessageKind.Text, "Hello there", false, null), m.Content);
            Assert.Equal(1, m.LineNumber);
        }

        [Fact]
        public void JoinsContinuationLinesIncludingBlankOnes()
        {
            var result = Parse(
                "01/02/2022, 10:00 - Alice Example: line one\n" +
                "line two\n" +
                "\n" +
                "line four\n" +
                "01/02/2022, 10:01 - Bob Sample: next\n");

            Assert.Equal(2, result.Messages.Count);
            Assert.Equal("line one\nline two\n\nline four", result.Messages[0].Content.Text);
            Assert.Equal(5, result.Messages[1].LineNumber);
        }

        [Fact]
        public void HandlesCrLfLineEndings()
        {
            var result = Parse("01/02/2022, 10:00 - Alice Example: one\r\ntwo\r\n01/02/2022, 10:01 - Bob Sample: three\r\n");

            Assert.Equal("one\ntwo", result.Messages[0].Content.Text);
            Assert.Equal("three", result.Messages[1].Content.Text);
        }

        [Theory]
        [InlineData("Messages and calls are end-to-end encrypted. No one outside of this chat can read them.")]
        [InlineData("Messages and calls are end-to-end encrypted. Only people in this chat can read them.")]
        [InlineData("Messages to yourself are end-to-end encrypted. No one outside of this chat can read them.")]
        public void SkipsEncryptionNotices(string notice)
        {
            var result = Parse(
                $"01/02/2022, 10:00 - {notice}\n" +
                "01/02/2022, 10:00 - Alice Example: hi\n");

            Assert.Single(result.Messages);
            Assert.Equal(1, result.SkippedNotices);
        }

        [Fact]
        public void KeepsOtherSystemLinesWithNoSender()
        {
            var result = Parse("01/02/2022, 10:00 - Your security code with Bob Sample changed. Tap to learn more.\n");

            var m = Assert.Single(result.Messages);
            Assert.Null(m.Sender);
            Assert.Equal(MessageKind.System, m.Content.Kind);
        }

        [Fact]
        public void GroupChatLinesKeepFileOrderAndPhoneNumberSenders()
        {
            // Real group exports put "created group" after the encryption notice with an earlier date,
            // and occasionally list system lines a minute or two out of order.
            var result = Parse(
                "05/03/2026, 09:07 - Messages and calls are end-to-end encrypted. Only people in this chat can read them.\n" +
                "22/02/2026, 16:47 - Alice Example created group \"Trip. March 14th to 16th\"\n" +
                "05/03/2026, 09:27 - +44 7700 900123: hello all\n" +
                "08/03/2026, 16:53 - +44 7700 900456 joined using this group's invite link\n" +
                "08/03/2026, 16:52 - ~ Bob Sample left\n");

            Assert.Equal(new[] { 2, 3, 4, 5 }, result.Messages.Select(m => m.LineNumber));
            Assert.Equal(new DateTime(2026, 2, 22, 16, 47, 0), result.Messages[0].Timestamp);
            Assert.Equal("+44 7700 900123", result.Messages[1].Sender);
            Assert.All(new[] { 0, 2, 3 }, i => Assert.Null(result.Messages[i].Sender));
        }

        [Fact]
        public void ColonInTextIsNotTakenAsSender()
        {
            var result = Parse("01/02/2022, 10:00 - Alice Example: note: time is 10:30\n");

            Assert.Equal("Alice Example", result.Messages[0].Sender);
            Assert.Equal("note: time is 10:30", result.Messages[0].Content.Text);
        }

        [Fact]
        public void RecordsLinesBeforeTheFirstHeader()
        {
            var result = Parse("stray\nlines\n01/02/2022, 10:00 - Alice Example: hi\n");

            Assert.Equal(new[] { 1, 2 }, result.OrphanLines);
            Assert.Single(result.Messages);
        }

        [Fact]
        public void InvalidDateIsTreatedAsContinuation()
        {
            var result = Parse("01/02/2022, 10:00 - Alice Example: hi\n31/02/2022, 10:00 - Bob Sample: not a date\n");

            var m = Assert.Single(result.Messages);
            Assert.Equal("hi\n31/02/2022, 10:00 - Bob Sample: not a date", m.Content.Text);
        }

        [Theory]
        [InlineData("hello <This message was edited>", MessageKind.Text, "hello", true, null)]
        [InlineData("<Media omitted>", MessageKind.Media, "", false, null)]
        [InlineData("<Media omitted>\na caption", MessageKind.Media, "a caption", false, null)]
        [InlineData("IMG-20220201-WA0001.jpg (file attached)", MessageKind.Media, "", false, "IMG-20220201-WA0001.jpg")]
        [InlineData("IMG-20220201-WA0001.jpg (file attached)\na caption", MessageKind.Media, "a caption", false, "IMG-20220201-WA0001.jpg")]
        [InlineData("You deleted this message", MessageKind.Deleted, "You deleted this message", false, null)]
        [InlineData("This message was deleted", MessageKind.Deleted, "This message was deleted", false, null)]
        [InlineData("null", MessageKind.Text, "null", false, null)]
        public void InterpretsMarkers(string raw, MessageKind kind, string text, bool edited, string? attachment)
        {
            Assert.Equal(new MessageContent(kind, text, edited, attachment), ExportParser.Interpret(raw));
        }
    }
}
