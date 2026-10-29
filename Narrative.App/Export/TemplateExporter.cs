using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Narrative.App.Export
{
    /// <summary>
    /// Writes an export through a Scriban template: one of the built-in Markdown and HTML templates,
    /// or one the user supplies.
    /// </summary>
    /// <remarks>
    /// Templates see the <see cref="ExportDocument"/> with snake_case names (<c>chat.name</c>,
    /// <c>messages</c>, <c>days</c>, <c>m.attachment_state</c> and so on), plus these functions:
    /// <list type="bullet">
    /// <item><c>attachment_url a</c>: a link to a stored file: its path beside the export, a data URI
    /// when files are embedded, or null when they aren't exported.</item>
    /// <item><c>markdown_escape text</c>: escapes Markdown syntax and keeps line breaks.</item>
    /// </list>
    /// Scriban's own functions are available too, e.g. <c>html.escape</c> and
    /// <c>object.format value "dddd d MMMM yyyy"</c> for .NET date formats.
    /// </remarks>
    public static partial class TemplateExporter
    {
        public static string BuiltInTemplate(ExportFormat format)
        {
            var name = format switch
            {
                ExportFormat.Markdown => "markdown.scriban",
                ExportFormat.Html => "html.scriban",
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Only Markdown and HTML have built-in templates."),
            };
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Narrative.App.Export.Templates.{name}")
                ?? throw new InvalidOperationException($"Built-in template {name} is missing from the assembly.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Parses a template, so errors are reported before anything is written.
        /// </summary>
        /// <param name="templateName">Shown in error messages.</param>
        public static Template Parse(string templateText, string templateName)
        {
            var template = Template.Parse(templateText, templateName);
            if (template.HasErrors)
            {
                throw new NarrativeException(
                    $"Template '{templateName}' has errors:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, template.Messages.Select(m => "  " + m)));
            }
            return template;
        }

        /// <param name="embed">Loads a file's bytes for a data URI, or null if files aren't embedded.</param>
        public static void Write(ExportDocument document, TextWriter output, Template template,
            Func<ExportAttachment, byte[]>? embed)
        {
            var templateName = template.SourceFilePath;

            var globals = new ScriptObject();
            globals.Import(document, renamer: StandardMemberRenamer.Default);
            globals.Import("attachment_url", new Func<ExportAttachment?, string?>(a => AttachmentUrl(a, embed)));
            globals.Import("markdown_escape", new Func<string?, string>(MarkdownEscape));

            var context = new TemplateContext
            {
                MemberRenamer = StandardMemberRenamer.Default,
                // The defaults stop runaway templates, but a long chat legitimately loops many times.
                LoopLimit = 0,
                LimitToString = 0,
            };
            context.PushCulture(CultureInfo.InvariantCulture);
            context.PushGlobal(globals);
            context.PushOutput(new TextWriterOutput(output));
            try
            {
                template.Render(context);
            }
            catch (ScriptRuntimeException ex)
            {
                throw new NarrativeException($"Template '{templateName}' failed: {ex.Message}");
            }
        }

        private static string? AttachmentUrl(ExportAttachment? attachment, Func<ExportAttachment, byte[]>? embed)
        {
            if (attachment == null)
            {
                return null;
            }
            if (embed != null)
            {
                return $"data:{attachment.MediaType};base64,{Convert.ToBase64String(embed(attachment))}";
            }
            if (attachment.Path != null)
            {
                return string.Join("/", attachment.Path.Split('/').Select(Uri.EscapeDataString));
            }
            return null;
        }

        [GeneratedRegex(@"[\\`*_\[\]<>#|~&]")]
        private static partial Regex MarkdownSpecial();

        [GeneratedRegex(@"^(\s*)([-+=]|\d+[.)])(?=\s|$)")]
        private static partial Regex MarkdownLineStart();

        /// <summary>
        /// Escapes text so Markdown shows it as written: inline syntax is backslash-escaped, line
        /// starts that would make a list or heading are escaped, and each line break is kept.
        /// </summary>
        public static string MarkdownEscape(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            var lines = text.Split('\n').Select(line =>
            {
                var escaped = MarkdownSpecial().Replace(line.TrimEnd('\r'), @"\$0");
                return MarkdownLineStart().Replace(escaped, m =>
                {
                    // "-" becomes "\-", and "1." becomes "1\.".
                    var marker = m.Groups[2].Value;
                    return m.Groups[1].Value + marker[..^1] + "\\" + marker[^1];
                });
            });
            // A backslash at the end of a line is a hard line break in CommonMark. A blank line would
            // end the paragraph, so it's kept as a non-breaking space.
            return string.Join("\\\n", lines.Select(l => l.Length == 0 ? "&nbsp;" : l));
        }
    }
}
