namespace Narrative.App.Sources
{
    /// <summary>
    /// A flat set of files that an export's text and attachments are read from: a folder or a zip.
    /// </summary>
    public interface IExportContainer : IDisposable
    {
        /// <summary>The folder or zip path, for messages.</summary>
        string Path { get; }

        /// <summary>True for a zip, whose files all belong to one export.</summary>
        bool IsArchive { get; }

        /// <summary>The file names in the container, without any folder part.</summary>
        IReadOnlyCollection<string> FileNames { get; }

        bool Contains(string fileName);

        byte[] ReadAllBytes(string fileName);
    }
}
