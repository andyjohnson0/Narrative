using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Narrative.App.WhatsApp;

namespace Narrative.App.Data
{
    /// <summary>
    /// Stores attachment files by content, so each file is stored once however many messages, exports
    /// or emails supply it. Used inside an import's transaction.
    /// </summary>
    public class AttachmentStore
    {
        private readonly NarrativeDbContext db;
        private readonly Dictionary<string, Attachment> bySha = new(StringComparer.Ordinal);

        public AttachmentStore(NarrativeDbContext db)
        {
            this.db = db;
        }

        /// <summary>
        /// The stored attachment with these bytes, storing it first if it's new.
        /// </summary>
        /// <param name="fileName">The name it was supplied under. Its type is worked out from this.</param>
        /// <param name="mediaType">The MIME type, when the source says (email does), or null to guess it from the name.</param>
        public Attachment GetOrCreate(string fileName, byte[] bytes, string? mediaType = null)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (bySha.TryGetValue(sha256, out var attachment))
            {
                return attachment;
            }

            attachment = db.Attachments.SingleOrDefault(a => a.Sha256 == sha256);
            if (attachment == null)
            {
                attachment = new Attachment
                {
                    Sha256 = sha256,
                    Size = bytes.LongLength,
                    MediaType = mediaType ?? AttachmentTypes.MediaTypeOf(fileName),
                    Kind = AttachmentTypes.KindOf(fileName, mediaType),
                    FileName = fileName,
                };
                db.Attachments.Add(attachment);
                db.SaveChanges();

                // The bytes are written outside the change tracker, so each file's memory is released
                // once it's stored rather than held until the import's final save. The import's
                // transaction still covers them.
                db.Database.ExecuteSql(
                    $"INSERT INTO AttachmentData (AttachmentId, Bytes) VALUES ({attachment.Id}, {bytes})");
            }
            bySha[sha256] = attachment;
            return attachment;
        }
    }
}
