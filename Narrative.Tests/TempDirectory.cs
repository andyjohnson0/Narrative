namespace Narrative.Tests
{
    /// <summary>
    /// A uniquely named directory that is deleted when disposed.
    /// </summary>
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "narrative-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string SubDirectory(string name)
        {
            var dir = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a file still held open shouldn't fail the test.
            }
        }
    }
}
