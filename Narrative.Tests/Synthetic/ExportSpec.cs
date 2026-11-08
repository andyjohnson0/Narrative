namespace Narrative.Tests.Synthetic
{
    /// <summary>
    /// Describes one export of a synthetic chat.
    /// </summary>
    /// <param name="From">Index of the first message included.</param>
    /// <param name="To">Index after the last message included.</param>
    /// <param name="WithMedia">True to export attachment names, false for "&lt;Media omitted&gt;".</param>
    /// <param name="AsOf">
    /// How far the chat had progressed when the export was made: edits and deletes made at an earlier
    /// index are visible. Defaults to <paramref name="To"/>.
    /// </param>
    public sealed record ExportSpec(int From, int To, bool WithMedia = true, int? AsOf = null)
    {
        /// <summary>
        /// Like WhatsApp's limit on media in an export: only the last this-many messages get attachment
        /// names and files; older media shows as "&lt;Media omitted&gt;". Null for no limit.
        /// </summary>
        public int? MediaWindow { get; init; }

        /// <summary>
        /// Indexes of messages whose attachments are named in the text but whose files are absent, as
        /// when Android leaves files out of an export.
        /// </summary>
        public IReadOnlySet<int> DroppedFiles { get; init; } = new HashSet<int>();

        /// <summary>
        /// True for an export made by a WhatsApp version that rounds each message's time to the
        /// nearest minute, where others drop the seconds.
        /// </summary>
        public bool RoundsTimes { get; init; }

        public int EffectiveAsOf => AsOf ?? To;

        public bool Contains(int index) => index >= From && index < To;

        /// <summary>True if this export names the attachment of the message at <paramref name="index"/>.</summary>
        public bool NamesMedia(int index) => WithMedia && (MediaWindow == null || index >= To - MediaWindow);
    }
}
