namespace Narrative.App.Data
{
    public enum ImportFileKind
    {
        /// <summary>A WhatsApp chat text file.</summary>
        WhatsApp,

        /// <summary>One email in a .eml file.</summary>
        Eml,

        /// <summary>Many emails in an mbox file.</summary>
        Mbox,
    }
}
