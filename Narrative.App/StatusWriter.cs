namespace Narrative.App
{
    /// <summary>
    /// Writes progress to stdout unless quiet, and warnings and errors to stderr always.
    /// </summary>
    public class StatusWriter
    {
        private readonly TextWriter output;
        private readonly TextWriter error;
        private readonly bool quiet;

        public StatusWriter(TextWriter output, TextWriter error, bool quiet)
        {
            this.output = output;
            this.error = error;
            this.quiet = quiet;
        }

        public void Info(string message)
        {
            if (!quiet)
            {
                output.WriteLine(message);
            }
        }

        public void Warning(string message)
        {
            error.WriteLine("Warning: " + message);
        }

        public void Error(string message)
        {
            error.WriteLine("Error: " + message);
        }
    }
}
