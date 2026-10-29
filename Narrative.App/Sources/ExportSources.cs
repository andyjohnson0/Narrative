using Narrative.App.Mail;
using Narrative.App.WhatsApp;

namespace Narrative.App.Sources
{
    /// <summary>
    /// The exports named on the command line. Disposing closes any zips that were opened.
    /// </summary>
    public sealed class ExportSources : IDisposable
    {
        private readonly List<IExportContainer> containers = new();

        private ExportSources()
        {
        }

        public List<ExportSource> Sources { get; } = new();

        /// <summary>The .eml and .mbox files, in the order found.</summary>
        public List<string> EmailFiles { get; } = new();

        /// <summary>
        /// Expands command-line import arguments into chat text files and their containers. Each argument
        /// is one of the following:
        /// - a text file, imported as a chat with its attachments looked up in the same folder;
        /// - a zip, whose chat text file(s) are imported with attachments looked up in the zip;
        /// - an email file (.eml or .mbox);
        /// - a folder, whose chat text files, zips and email files are imported;
        /// - a wildcard in the file name part (Windows shells don't expand them), e.g. <c>exports\*.zip</c>,
        ///   matched in name order and treated as above.
        /// </summary>
        public static ExportSources Resolve(IEnumerable<string> specs, IHeaderFormat headerFormat)
        {
            var result = new ExportSources();
            try
            {
                foreach (var spec in specs)
                {
                    foreach (var path in ExpandWildcard(spec))
                    {
                        result.Add(path, headerFormat);
                    }
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private static IEnumerable<string> ExpandWildcard(string spec)
        {
            var fileName = Path.GetFileName(spec);
            if (fileName.IndexOfAny(new[] { '*', '?' }) < 0)
            {
                if (!File.Exists(spec) && !Directory.Exists(spec))
                {
                    throw new NarrativeException($"File or folder '{spec}' not found.");
                }
                return new[] { spec };
            }

            var dir = Path.GetDirectoryName(spec);
            if (string.IsNullOrEmpty(dir))
            {
                dir = ".";
            }
            if (dir.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                throw new NarrativeException($"Wildcards are only allowed in the file name: '{spec}'.");
            }
            if (!Directory.Exists(dir))
            {
                throw new NarrativeException($"Folder '{dir}' not found.");
            }

            var matches = Directory.GetFiles(dir, fileName).Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (matches.Count == 0)
            {
                throw new NarrativeException($"No files match '{spec}'.");
            }
            return matches;
        }

        private void Add(string path, IHeaderFormat headerFormat)
        {
            if (Directory.Exists(path))
            {
                AddFolder(path, headerFormat);
            }
            else if (IsZip(path))
            {
                AddZip(path, headerFormat);
            }
            else if (EmailImporter.IsEmailFile(path))
            {
                EmailFiles.Add(path);
            }
            else
            {
                var folder = Track(new DirectoryContainer(Path.GetDirectoryName(path) ?? "."));
                Sources.Add(new ExportSource(folder, Path.GetFileName(path)));
            }
        }

        private void AddFolder(string path, IHeaderFormat headerFormat)
        {
            var folder = Track(new DirectoryContainer(path));
            var chatFiles = ChatFileFinder.Find(folder, headerFormat);
            var zips = folder.FileNames.Where(IsZip).Order(StringComparer.OrdinalIgnoreCase).ToList();
            var emails = folder.FileNames.Where(EmailImporter.IsEmailFile).Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (chatFiles.Count == 0 && zips.Count == 0 && emails.Count == 0)
            {
                throw new NarrativeException($"No WhatsApp exports or email files found in folder '{path}'.");
            }
            EmailFiles.AddRange(emails.Select(e => Path.Combine(path, e)));
            Sources.AddRange(chatFiles.Select(f => new ExportSource(folder, f)));
            foreach (var zip in zips)
            {
                AddZip(Path.Combine(path, zip), headerFormat);
            }
        }

        private void AddZip(string path, IHeaderFormat headerFormat)
        {
            var zip = Track(new ZipContainer(path));
            var chatFiles = ChatFileFinder.Find(zip, headerFormat);
            if (chatFiles.Count == 0)
            {
                throw new NarrativeException($"No WhatsApp chat text file found in '{path}'.");
            }
            Sources.AddRange(chatFiles.Select(f => new ExportSource(zip, f)));
        }

        private static bool IsZip(string path)
        {
            return Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private T Track<T>(T container)
            where T : IExportContainer
        {
            containers.Add(container);
            return container;
        }

        public void Dispose()
        {
            foreach (var container in containers)
            {
                container.Dispose();
            }
            containers.Clear();
        }
    }
}
