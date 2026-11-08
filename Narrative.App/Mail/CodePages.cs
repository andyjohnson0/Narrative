using System.Text;

namespace Narrative.App.Mail
{
    /// <summary>
    /// Makes the legacy code pages (windows-1252, windows-1250, KOI8-R, Shift_JIS and so on) available.
    /// </summary>
    /// <remarks>
    /// .NET has only UTF-8, UTF-16, ASCII and Latin-1 built in. Without the others MimeKit decodes text
    /// in those charsets as Latin-1, so windows-1252's euro sign, curly quotes and dashes (0x80-0x9F)
    /// become C1 control characters. The email classes call <see cref="EnsureRegistered"/> from their
    /// static constructors, so the encodings are there however email is read.
    /// </remarks>
    public static class CodePages
    {
        private static readonly Lazy<bool> Registered = new(() =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return true;
        });

        public static void EnsureRegistered()
        {
            _ = Registered.Value;
        }
    }
}
