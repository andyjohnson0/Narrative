namespace Narrative.App
{
    /// <summary>
    /// An error caused by the user's input or environment, reported as a message without a stack trace.
    /// </summary>
    public class NarrativeException : Exception
    {
        public NarrativeException(string message)
            : base(message)
        {
        }
    }
}
