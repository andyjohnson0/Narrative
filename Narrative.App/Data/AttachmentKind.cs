namespace Narrative.App.Data
{
    /// <summary>
    /// What sort of file an attachment is, from its WhatsApp name prefix or extension.
    /// </summary>
    public enum AttachmentKind
    {
        Image,
        Video,

        /// <summary>A recorded voice note (WhatsApp "PTT", push-to-talk).</summary>
        VoiceNote,

        /// <summary>A shared audio file.</summary>
        Audio,

        Sticker,

        /// <summary>A shared contact card (.vcf).</summary>
        Contact,

        /// <summary>Any other shared file.</summary>
        Document,
    }
}
