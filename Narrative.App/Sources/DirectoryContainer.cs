namespace Narrative.App.Sources
{
    /// <summary>
    /// The files directly inside a folder.
    /// </summary>
    public sealed class DirectoryContainer : IExportContainer
    {
        private IReadOnlyCollection<string>? fileNames;

        public DirectoryContainer(string path)
        {
            Path = string.IsNullOrEmpty(path) ? "." : path;
        }

        public string Path { get; }

        public bool IsArchive => false;

        public IReadOnlyCollection<string> FileNames =>
            fileNames ??= Directory.GetFiles(Path).Select(f => System.IO.Path.GetFileName(f)).ToList();

        public bool Contains(string fileName)
        {
            return IsPlainName(fileName) && File.Exists(System.IO.Path.Combine(Path, fileName));
        }

        public byte[] ReadAllBytes(string fileName)
        {
            if (!IsPlainName(fileName))
            {
                throw new FileNotFoundException($"'{fileName}' is not a plain file name.");
            }
            return File.ReadAllBytes(System.IO.Path.Combine(Path, fileName));
        }

        public void Dispose()
        {
        }

        // An attachment name comes from the export's text, so don't let it reach outside the folder.
        private static bool IsPlainName(string fileName)
        {
            return fileName.Length > 0
                && fileName == System.IO.Path.GetFileName(fileName)
                && fileName is not "." and not "..";
        }
    }
}
