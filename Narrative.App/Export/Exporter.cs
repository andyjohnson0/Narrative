using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;

namespace Narrative.App.Export
{
    /// <param name="FilesFolder">Where attachment files were written, or null if they weren't written
    /// to a folder.</param>
    public sealed record ExportResult(int Messages, int Attachments, string? FilesFolder, IReadOnlyList<string> Warnings)
    {
        public ExportResult(int messages, int attachments, string? filesFolder)
            : this(messages, attachments, filesFolder, Array.Empty<string>())
        {
        }
    }

    /// <summary>
    /// Runs an export: builds the document, writes attachment files if asked, then writes the format.
    /// </summary>
    public class Exporter
    {
        private readonly NarrativeDbContext db;

        public Exporter(NarrativeDbContext db)
        {
            this.db = db;
        }

        public ExportResult Export(ExportRequest request)
        {
            var template = request.Format switch
            {
                ExportFormat.Template => TemplateExporter.Parse(
                    ReadTemplate(request.TemplatePath
                        ?? throw new ArgumentException("A template export needs a template path.", nameof(request))),
                    request.TemplatePath),
                ExportFormat.Markdown or ExportFormat.Html => TemplateExporter.Parse(
                    TemplateExporter.BuiltInTemplate(request.Format), $"built-in {request.Format} template"),
                _ => null,
            };

            if (request.Format == ExportFormat.WhatsApp && request.Attachments == AttachmentMode.Embed)
            {
                throw new NarrativeException(
                    "A WhatsApp text export can't embed files. Export to a .zip to keep them together, or use -attachments folder or none.");
            }
            if (request.Excel && request.Format != ExportFormat.Csv)
            {
                throw new NarrativeException("-excel only applies to CSV exports.");
            }
            if (request.Format == ExportFormat.Csv && request.Attachments == AttachmentMode.Embed)
            {
                throw new NarrativeException("A CSV export can't embed files; it only names them.");
            }

            var document = new ExportBuilder(db).Build(request);
            if (request.Format == ExportFormat.WhatsApp)
            {
                return ExportWhatsApp(request, document);
            }

            string? filesFolder = null;
            // A CSV only names files, so writing them would be clutter.
            if (request.Attachments == AttachmentMode.Folder && document.Attachments.Count > 0 && request.Format != ExportFormat.Csv)
            {
                filesFolder = WriteFiles(document.Attachments, request.OutputPath);
            }
            var embed = request.Attachments == AttachmentMode.Embed ? LoadBytes : (Func<ExportAttachment, byte[]>?)null;

            var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!;
            Directory.CreateDirectory(directory);
            try
            {
                using var stream = File.Create(request.OutputPath);
                switch (request.Format)
                {
                    case ExportFormat.Json:
                        JsonExporter.Write(document, stream, embed);
                        break;
                    case ExportFormat.Xml:
                        XmlExporter.Write(document, stream, embed);
                        break;
                    case ExportFormat.Csv:
                        using (var writer = new StreamWriter(stream, CsvExporter.Encoding(request.Excel)))
                        {
                            CsvExporter.Write(document, writer, request.Excel);
                        }
                        break;
                    default:
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        {
                            TemplateExporter.Write(document, writer, template!, embed);
                        }
                        break;
                }
            }
            catch
            {
                // A half-written export is worse than none.
                File.Delete(request.OutputPath);
                throw;
            }
            return new ExportResult(document.Messages.Count, document.Attachments.Count, filesFolder);
        }

        /// <summary>
        /// Writes WhatsApp's own format. A .zip is laid out like WhatsApp's: the chat file, named the way
        /// WhatsApp names it, and the attachment files at the top level. Otherwise the text goes to the
        /// output file and attachments are written beside it, as an extracted export has them. Either way
        /// each file has the name its message gives, so narrative (or anything else) can import it back.
        /// </summary>
        private ExportResult ExportWhatsApp(ExportRequest request, ExportDocument document)
        {
            var warnings = new List<string>();
            var renamed = new Dictionary<ExportMessage, string>();
            var files = request.Attachments == AttachmentMode.None
                ? new List<(string Name, ExportAttachment Attachment)>()
                : FilesByMessageName(document, warnings, renamed);
            var zip = Path.GetExtension(request.OutputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!;
            Directory.CreateDirectory(directory);
            if (!zip)
            {
                CheckNotOverwritingOtherFiles(files, directory);
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            try
            {
                using var stream = File.Create(request.OutputPath);
                if (zip)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                    var chatEntry = archive.CreateEntry(WhatsAppTextExporter.ChatFileName(SafeFileName(document.Chat?.Name ?? document.Title)));
                    using (var writer = new StreamWriter(chatEntry.Open(), encoding))
                    {
                        WhatsAppTextExporter.Write(document, writer, renamed);
                    }
                    foreach (var (name, attachment) in files)
                    {
                        // Media is already compressed, so the fastest level loses almost nothing.
                        using var entry = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                        entry.Write(LoadBytes(attachment));
                    }
                }
                else
                {
                    using (var writer = new StreamWriter(stream, encoding))
                    {
                        WhatsAppTextExporter.Write(document, writer, renamed);
                    }
                    foreach (var (name, attachment) in files)
                    {
                        File.WriteAllBytes(Path.Combine(directory, name), LoadBytes(attachment));
                    }
                }
            }
            catch
            {
                File.Delete(request.OutputPath);
                throw;
            }
            return new ExportResult(document.Messages.Count, files.Count, zip || files.Count == 0 ? null : directory, warnings);
        }

        /// <summary>
        /// The stored files to write, each under the name its message gives (a file can be named
        /// differently in different exports, so it may be written more than once). A name that would
        /// reach outside the folder is skipped, and so is a second, different file with a name already
        /// used: an importer links files by name, so only one can be right.
        /// </summary>
        /// <remarks>
        /// Email attachments often share generic names (<c>image001.png</c>), and nothing else
        /// identifies them, so an email attachment is renamed instead: made a plain file name, with
        /// " (n)" added if a different file already has that name. <paramref name="renamed"/> gets the
        /// name each such message should give.
        /// </remarks>
        private static List<(string Name, ExportAttachment Attachment)> FilesByMessageName(
            ExportDocument document, List<string> warnings, Dictionary<ExportMessage, string> renamed)
        {
            var byName = new Dictionary<string, ExportAttachment>(StringComparer.OrdinalIgnoreCase);
            var files = new List<(string, ExportAttachment)>();
            // Names WhatsApp messages give are kept, so a renamed email attachment must avoid them all.
            var reserved = document.Messages
                .Where(m => m.Email == null && m.AttachmentName != null)
                .Select(m => m.AttachmentName!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var m in document.Messages)
            {
                if (m.Attachment == null || m.AttachmentName == null)
                {
                    continue;
                }
                var name = m.AttachmentName;
                if (m.Email != null)
                {
                    name = SafeFileName(name);
                    var original = name;
                    for (int n = 2; byName.TryGetValue(name, out var other) ? other.Id != m.Attachment.Id : reserved.Contains(name); n++)
                    {
                        name = $"{Path.GetFileNameWithoutExtension(original)} ({n}){Path.GetExtension(original)}";
                    }
                    if (name != m.AttachmentName)
                    {
                        renamed.Add(m, name);
                    }
                }
                if (SafeFileName(name) != name)
                {
                    warnings.Add($"Not writing attachment '{name}': it isn't a plain file name.");
                }
                else if (!byName.TryGetValue(name, out var existing))
                {
                    byName.Add(name, m.Attachment);
                    files.Add((name, m.Attachment));
                }
                else if (existing.Id != m.Attachment.Id)
                {
                    warnings.Add($"Two different files are named '{name}'; only the first is written, so the message at {m.Timestamp:yyyy-MM-dd HH:mm} will get the wrong file if this export is imported.");
                }
            }
            return files;
        }

        /// <summary>
        /// Attachments go beside the text file, where other files may already be. Refuses before
        /// writing anything if that would replace a file with different content.
        /// </summary>
        private static void CheckNotOverwritingOtherFiles(List<(string Name, ExportAttachment Attachment)> files, string directory)
        {
            var clashes = new List<string>();
            foreach (var (name, attachment) in files)
            {
                var path = Path.Combine(directory, name);
                if (!File.Exists(path))
                {
                    continue;
                }
                using var existing = File.OpenRead(path);
                if (existing.Length != attachment.Size || Convert.ToHexStringLower(SHA256.HashData(existing)) != attachment.Sha256)
                {
                    clashes.Add(name);
                }
            }
            if (clashes.Count > 0)
            {
                throw new NarrativeException(
                    $"{clashes.Count} attachment file(s) would replace different files already in {directory}, " +
                    $"e.g. '{clashes[0]}'. Export to an empty folder, or to a .zip.");
            }
        }

        private static string ReadTemplate(string path)
        {
            if (!File.Exists(path))
            {
                throw new NarrativeException($"Template '{path}' not found.");
            }
            return File.ReadAllText(path);
        }

        private byte[] LoadBytes(ExportAttachment attachment)
        {
            return db.AttachmentData.AsNoTracking()
                .Where(d => d.AttachmentId == attachment.Id)
                .Select(d => d.Bytes)
                .Single();
        }

        /// <summary>
        /// Writes each file to a folder named after the export (<c>chat.html</c> gets <c>chat_files</c>)
        /// and records where it went. Files keep their names; a clash gets a " (n)" suffix.
        /// </summary>
        private string WriteFiles(IReadOnlyList<ExportAttachment> attachments, string outputPath)
        {
            var folderName = Path.GetFileNameWithoutExtension(outputPath) + "_files";
            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, folderName);
            Directory.CreateDirectory(folder);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attachment in attachments)
            {
                var name = UniqueName(SafeFileName(attachment.FileName), used);
                // One file at a time, so memory stays at the size of the largest file.
                File.WriteAllBytes(Path.Combine(folder, name), LoadBytes(attachment));
                attachment.Path = folderName + "/" + name;
            }
            return folder;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length == 0 || safe == "." || safe == ".." ? "attachment" : safe;
        }

        private static string UniqueName(string name, HashSet<string> used)
        {
            var candidate = name;
            for (int n = 2; !used.Add(candidate); n++)
            {
                candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
            }
            return candidate;
        }
    }
}
