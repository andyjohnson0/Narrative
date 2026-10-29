using System.IO.Compression;

namespace Narrative.App.Sources
{
    /// <summary>
    /// The files in a zip, read in place without extracting. Files are looked up by name alone; if
    /// two entries in different folders share a name, the first wins.
    /// </summary>
    public sealed class ZipContainer : IExportContainer
    {
        private readonly ZipArchive archive;
        private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);

        public ZipContainer(string path)
        {
            Path = path;
            try
            {
                archive = ZipFile.OpenRead(path);
            }
            catch (InvalidDataException ex)
            {
                throw new NarrativeException($"'{path}' is not a valid zip file: {ex.Message}");
            }
            foreach (var entry in archive.Entries)
            {
                // Directory entries have an empty name.
                if (entry.Name.Length > 0)
                {
                    entries.TryAdd(entry.Name, entry);
                }
            }
        }

        public string Path { get; }

        public bool IsArchive => true;

        public IReadOnlyCollection<string> FileNames => entries.Keys;

        public bool Contains(string fileName)
        {
            return entries.ContainsKey(fileName);
        }

        public byte[] ReadAllBytes(string fileName)
        {
            if (!entries.TryGetValue(fileName, out var entry))
            {
                throw new FileNotFoundException($"'{fileName}' is not in '{Path}'.");
            }
            // Reading straight into an array of the entry's size avoids holding a second copy.
            var bytes = new byte[entry.Length];
            using var stream = entry.Open();
            try
            {
                stream.ReadExactly(bytes);
            }
            catch (EndOfStreamException)
            {
                throw new NarrativeException($"'{fileName}' in '{Path}' is shorter than the zip says; the zip may be damaged.");
            }
            return bytes;
        }

        public void Dispose()
        {
            archive.Dispose();
        }
    }
}
