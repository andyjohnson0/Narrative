using Narrative.App.WhatsApp;

namespace Narrative.Tests
{
    public class ChatNameTests
    {
        [Theory]
        [InlineData("WhatsApp Chat with Bob Sample.txt", "Bob Sample")]
        [InlineData("WhatsApp Chat with Bob Sample 2024-04-18.txt", "Bob Sample")]
        [InlineData(@"C:\exports\WhatsApp Chat with Bob Sample 2024-04-18.txt", "Bob Sample")]
        [InlineData("WhatsApp Chat with Bob Sample (1).txt", "Bob Sample")]
        [InlineData("WhatsApp Chat with Bob Sample 2024-04-18 (2).txt", "Bob Sample")]
        [InlineData("WhatsApp Chat with Book Club 2022.txt", "Book Club 2022")]
        [InlineData("whatsapp chat with Bob Sample.txt", "Bob Sample")]
        public void DerivesNameFromWhatsAppFileName(string path, string expected)
        {
            Assert.Equal(expected, ChatName.FromFileName(path));
        }

        [Theory]
        [InlineData("chat.txt")]
        [InlineData("WhatsApp Chat with .txt")]
        [InlineData("Export of Bob Sample.txt")]
        public void ReturnsNullForOtherFileNames(string path)
        {
            Assert.Null(ChatName.FromFileName(path));
        }
    }
}
