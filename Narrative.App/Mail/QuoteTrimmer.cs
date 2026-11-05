using System.Text.RegularExpressions;

namespace Narrative.App.Mail
{
    /// <summary>
    /// Removes the quoted copy of earlier emails that a reply carries at its end, which would otherwise
    /// repeat the whole thread in the timeline.
    /// </summary>
    public static partial class QuoteTrimmer
    {
        [GeneratedRegex(@"\b(?:wrote|writes)\s*:$", RegexOptions.IgnoreCase)]
        private static partial Regex Attribution();

        /// <summary>
        /// Removes the <c>&gt;</c> lines at the end of <paramref name="body"/>, the blank lines among
        /// them, and the "On ..., X wrote:" line that introduces them (which may be wrapped over two
        /// lines). Quotes with the sender's own text after them, as in inline and bottom-posted
        /// replies, are kept. If nothing but the quote would be left, the body is returned unchanged.
        /// </summary>
        /// <param name="body">The body with LF line endings.</param>
        public static string TrimTrailingQuote(string body)
        {
            var lines = body.Split('\n');
            var end = SkipBlank(lines, lines.Length);
            if (end == 0 || !IsQuoted(lines[end - 1]))
            {
                return body;
            }
            while (end > 0 && (IsQuoted(lines[end - 1]) || IsBlank(lines[end - 1])))
            {
                end -= 1;
            }
            end = WithoutAttribution(lines, end);

            var trimmed = string.Join("\n", lines[..end]).TrimEnd();
            return trimmed.Length == 0 ? body : trimmed;
        }

        /// <summary>
        /// Where the text before the quote that ends at <paramref name="end"/> stops, leaving out an
        /// attribution line just above it.
        /// </summary>
        private static int WithoutAttribution(string[] lines, int end)
        {
            var last = SkipBlank(lines, end) - 1;
            if (last < 0 || !Attribution().IsMatch(lines[last].TrimEnd()))
            {
                return end;
            }
            // Long attributions are wrapped: "On Wed, 1 Jul 2021 at 10:00, Sam Taylor <" then "sam@...> wrote:".
            if (!IsReplyIntro(lines[last]) && last > 0 && IsReplyIntro(lines[last - 1]))
            {
                return last - 1;
            }
            return last;
        }

        private static bool IsReplyIntro(string line)
        {
            return line.StartsWith("On ", StringComparison.Ordinal);
        }

        /// <summary>The index just after the last non-blank line before <paramref name="end"/>.</summary>
        private static int SkipBlank(string[] lines, int end)
        {
            while (end > 0 && IsBlank(lines[end - 1]))
            {
                end -= 1;
            }
            return end;
        }

        public static bool IsQuoted(string line)
        {
            return line.StartsWith('>');
        }

        private static bool IsBlank(string line)
        {
            return line.Trim().Length == 0;
        }
    }
}
