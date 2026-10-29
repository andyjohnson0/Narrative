using Narrative.App.WhatsApp;

namespace Narrative.App.Sources
{
    /// <summary>
    /// One chat text file to import, and the container its attachments are read from.
    /// </summary>
    public sealed class ExportSource
    {
        public ExportSource(IExportContainer container, string textFileName)
        {
            Container = container;
            TextFileName = textFileName;
        }

        public IExportContainer Container { get; }

        public string TextFileName { get; }

        /// <summary>The zip's file name, or null if the text file is in a folder.</summary>
        public string? ContainerName => Container.IsArchive ? System.IO.Path.GetFileName(Container.Path) : null;

        /// <summary>A name for messages: the text file, prefixed by its zip if it has one.</summary>
        public string DisplayName => ContainerName == null ? TextFileName : $"{ContainerName}/{TextFileName}";

        /// <summary>
        /// The chat name from the text file's name or, failing that, the zip's name. Android names
        /// both after the chat; a date may have been added to the zip's name.
        /// </summary>
        public string? DefaultChatName =>
            ChatName.FromFileName(TextFileName)
            ?? (ContainerName == null ? null : ChatName.FromFileName(ContainerName));
    }
}
