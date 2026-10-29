using Narrative.App.Data;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Classifies attachment files by WhatsApp's name prefix (IMG-, VID-, PTT- etc.) and extension.
    /// </summary>
    public static class AttachmentTypes
    {
        private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".heic"] = "image/heic",
            [".mp4"] = "video/mp4",
            [".3gp"] = "video/3gpp",
            [".mov"] = "video/quicktime",
            [".opus"] = "audio/ogg",
            [".ogg"] = "audio/ogg",
            [".m4a"] = "audio/mp4",
            [".mp3"] = "audio/mpeg",
            [".aac"] = "audio/aac",
            [".amr"] = "audio/amr",
            [".wav"] = "audio/wav",
            [".vcf"] = "text/vcard",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".zip"] = "application/zip",
        };

        private static readonly Dictionary<string, AttachmentKind> Prefixes = new(StringComparer.Ordinal)
        {
            ["IMG-"] = AttachmentKind.Image,
            ["VID-"] = AttachmentKind.Video,
            ["PTT-"] = AttachmentKind.VoiceNote,
            ["AUD-"] = AttachmentKind.Audio,
            ["STK-"] = AttachmentKind.Sticker,
            ["DOC-"] = AttachmentKind.Document,
        };

        public static string MediaTypeOf(string fileName)
        {
            return MediaTypes.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
        }

        /// <summary>
        /// The kind from the file name or, when the name says nothing more specific than a document,
        /// from the MIME type the source gave.
        /// </summary>
        public static AttachmentKind KindOf(string fileName, string? mediaType)
        {
            var kind = KindOf(fileName);
            if (kind != AttachmentKind.Document || mediaType == null || MediaTypes.ContainsKey(Path.GetExtension(fileName)))
            {
                return kind;
            }
            return mediaType switch
            {
                "text/vcard" or "text/x-vcard" => AttachmentKind.Contact,
                _ when mediaType.StartsWith("image/", StringComparison.Ordinal) => AttachmentKind.Image,
                _ when mediaType.StartsWith("video/", StringComparison.Ordinal) => AttachmentKind.Video,
                _ when mediaType.StartsWith("audio/", StringComparison.Ordinal) => AttachmentKind.Audio,
                _ => AttachmentKind.Document,
            };
        }

        public static AttachmentKind KindOf(string fileName)
        {
            foreach (var (prefix, kind) in Prefixes)
            {
                if (fileName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return kind;
                }
            }

            var mediaType = MediaTypeOf(fileName);
            return mediaType switch
            {
                "text/vcard" => AttachmentKind.Contact,
                _ when mediaType.StartsWith("image/", StringComparison.Ordinal) => AttachmentKind.Image,
                _ when mediaType.StartsWith("video/", StringComparison.Ordinal) => AttachmentKind.Video,
                _ when mediaType.StartsWith("audio/", StringComparison.Ordinal) => AttachmentKind.Audio,
                _ => AttachmentKind.Document,
            };
        }
    }
}
