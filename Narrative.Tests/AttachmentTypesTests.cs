using Narrative.App.Data;
using Narrative.App.WhatsApp;

namespace Narrative.Tests
{
    public class AttachmentTypesTests
    {
        [Theory]
        [InlineData("IMG-20240315-WA0001.jpg", AttachmentKind.Image, "image/jpeg")]
        [InlineData("VID-20240315-WA0002.mp4", AttachmentKind.Video, "video/mp4")]
        [InlineData("PTT-20240315-WA0003.opus", AttachmentKind.VoiceNote, "audio/ogg")]
        [InlineData("AUD-20240315-WA0004.m4a", AttachmentKind.Audio, "audio/mp4")]
        [InlineData("STK-20240315-WA0005.webp", AttachmentKind.Sticker, "image/webp")]
        [InlineData("DOC-20240315-WA0006.pdf", AttachmentKind.Document, "application/pdf")]
        [InlineData("Bob Sample.vcf", AttachmentKind.Contact, "text/vcard")]
        [InlineData("holiday.PNG", AttachmentKind.Image, "image/png")]
        [InlineData("song.mp3", AttachmentKind.Audio, "audio/mpeg")]
        [InlineData("Minutes.docx", AttachmentKind.Document, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
        [InlineData("mystery.xyz", AttachmentKind.Document, "application/octet-stream")]
        public void ClassifiesByPrefixAndExtension(string fileName, AttachmentKind kind, string mediaType)
        {
            Assert.Equal(kind, AttachmentTypes.KindOf(fileName));
            Assert.Equal(mediaType, AttachmentTypes.MediaTypeOf(fileName));
        }
    }
}
