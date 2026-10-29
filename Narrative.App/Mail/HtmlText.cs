using System.Text;
using System.Text.RegularExpressions;
using MimeKit.Text;

namespace Narrative.App.Mail
{
    /// <summary>
    /// Converts an HTML email body to plain text, for the emails that have no plain-text version.
    /// Blocks and line breaks become new lines, blockquotes become <c>&gt;</c> lines as in a plain-text
    /// reply, and scripts, styles and the head are dropped.
    /// </summary>
    public static partial class HtmlText
    {
        private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "div", "tr", "li", "ul", "ol", "table", "h1", "h2", "h3", "h4", "h5", "h6", "pre", "hr", "section", "article",
            "header", "footer", "blockquote",
        };

        private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
        {
            "head", "script", "style", "title",
        };

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        public static string Convert(string html)
        {
            var lines = new List<string>();
            var line = new StringBuilder();
            int quoteDepth = 0, hiddenDepth = 0;

            void EndLine()
            {
                var text = line.ToString().Trim();
                lines.Add(quoteDepth == 0 ? text : new string('>', quoteDepth) + (text.Length == 0 ? "" : " " + text));
                line.Clear();
            }

            void EndBlock()
            {
                if (line.ToString().Trim().Length > 0)
                {
                    EndLine();
                }
                line.Clear();
            }

            using var reader = new StringReader(html);
            var tokenizer = new HtmlTokenizer(reader);
            while (tokenizer.ReadNextToken(out var token))
            {
                switch (token)
                {
                    case HtmlDataToken data when hiddenDepth == 0:
                        line.Append(Whitespace().Replace(data.Data, " "));
                        break;
                    case HtmlTagToken tag when Hidden.Contains(tag.Name):
                        if (!tag.IsEmptyElement)
                        {
                            hiddenDepth = Math.Max(0, hiddenDepth + (tag.IsEndTag ? -1 : 1));
                        }
                        break;
                    case HtmlTagToken tag when tag.Name.Equals("br", StringComparison.OrdinalIgnoreCase):
                        EndLine();
                        break;
                    case HtmlTagToken tag when Blocks.Contains(tag.Name):
                        EndBlock();
                        if (tag.Name.Equals("p", StringComparison.OrdinalIgnoreCase) && tag.IsEndTag)
                        {
                            // A blank line between paragraphs, as in plain text.
                            lines.Add(quoteDepth == 0 ? "" : new string('>', quoteDepth));
                        }
                        if (tag.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase) && !tag.IsEmptyElement)
                        {
                            if (tag.IsEndTag)
                            {
                                // The quote's last paragraph break isn't part of it.
                                while (lines.Count > 0 && IsBlank(lines[^1]))
                                {
                                    lines.RemoveAt(lines.Count - 1);
                                }
                            }
                            quoteDepth = Math.Max(0, quoteDepth + (tag.IsEndTag ? -1 : 1));
                        }
                        break;
                }
            }
            EndBlock();

            // At most one blank line in a row.
            var result = new List<string>();
            foreach (var l in lines)
            {
                if (l.Length == 0 && (result.Count == 0 || result[^1].Length == 0))
                {
                    continue;
                }
                result.Add(l);
            }
            while (result.Count > 0 && IsBlank(result[^1]))
            {
                result.RemoveAt(result.Count - 1);
            }
            return string.Join("\n", result).Trim('\n');
        }

        /// <summary>An empty line, or an empty quoted line (only &gt; marks).</summary>
        private static bool IsBlank(string line)
        {
            return line.All(c => c == '>');
        }
    }
}
