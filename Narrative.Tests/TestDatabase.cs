using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;
using Narrative.App.Sources;
using Narrative.App.WhatsApp;

namespace Narrative.Tests
{
    /// <summary>
    /// A stored message flattened for comparison.
    /// </summary>
    public sealed record StoredMessage(string Chat, DateTime Timestamp, string? Sender, MessageContent Content);

    /// <summary>
    /// Helpers for tests that use a real SQLite file.
    /// </summary>
    public static class TestDatabase
    {
        public static string Create(TempDirectory temp, string name = "test.db")
        {
            var path = temp.File(name);
            NarrativeDatabase.Create(path, force: false);
            return path;
        }

        public static List<ImportResult> Import(string dbPath, IEnumerable<string> files, string? chatName = null)
        {
            var results = new List<ImportResult>();
            foreach (var file in files)
            {
                using var db = NarrativeDatabase.Open(dbPath);
                results.Add(new WhatsAppImporter(db).Import(file, chatName));
            }
            return results;
        }

        /// <summary>
        /// Imports text files, zips or folders, resolved as the CLI resolves them.
        /// </summary>
        public static List<ImportResult> ImportPaths(string dbPath, IEnumerable<string> paths, string? chatName = null)
        {
            var results = new List<ImportResult>();
            using var exports = ExportSources.Resolve(paths, AndroidUkHeaderFormat.Instance);
            foreach (var source in exports.Sources)
            {
                using var db = NarrativeDatabase.Open(dbPath);
                results.Add(new WhatsAppImporter(db).Import(source, chatName));
            }
            return results;
        }

        /// <summary>
        /// All messages in conversation order.
        /// </summary>
        public static List<StoredMessage> Messages(string dbPath)
        {
            using var db = NarrativeDatabase.Open(dbPath);
            return db.Messages.AsNoTracking()
                .Include(m => m.Conversation)
                .Include(m => m.Sender)
                .OrderBy(m => m.ConversationId)
                .ThenBy(m => m.Timestamp)
                .ThenBy(m => m.Seq)
                .AsEnumerable()
                .Select(m => new StoredMessage(m.Conversation.Name, m.Timestamp, m.Sender?.DisplayName, m.Content))
                .ToList();
        }

        /// <summary>
        /// Each message's attachment state and stored file hash, in conversation order.
        /// </summary>
        public static List<(AttachmentState State, string? Sha256)> Attachments(string dbPath)
        {
            using var db = NarrativeDatabase.Open(dbPath);
            return db.Messages.AsNoTracking()
                .Include(m => m.Attachment)
                .OrderBy(m => m.ConversationId)
                .ThenBy(m => m.Timestamp)
                .ThenBy(m => m.Seq)
                .AsEnumerable()
                .Select(m => (m.AttachmentState, m.Attachment?.Sha256))
                .ToList();
        }

        public static string Sha256(byte[] bytes)
        {
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        }
    }
}
