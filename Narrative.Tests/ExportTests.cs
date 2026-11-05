using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Narrative.App;
using Narrative.App.Export;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class ExportTests : IDisposable
    {
        private readonly TempDirectory temp = new();
        private readonly string db;

        private static readonly byte[] Photo = Encoding.ASCII.GetBytes("JPEG photo bytes");
        private static readonly byte[] Voice = Encoding.ASCII.GetBytes("OPUS voice bytes");
        private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("PDF bytes");

        /// <summary>
        /// A small chat with one of everything an export has to show: markup-like text, multi-line
        /// text, stored, missing and omitted media, a deleted and an edited message, a system line, a
        /// voice note and a document with a space in its name, over two days.
        /// </summary>
        private static readonly string ChatText = string.Join("\n",
            "01/05/2022, 12:00 - Messages and calls are end-to-end encrypted. No one outside of this chat can read them.",
            "01/05/2022, 12:00 - Alice Example: Hello <b>&</b> *world*",
            "- not a list",
            "",
            "1. nor this",
            "01/05/2022, 12:01 - Bob Sample: IMG-20220501-WA0001.jpg (file attached)",
            "A caption",
            "01/05/2022, 12:02 - Bob Sample: IMG-20220501-WA0002.jpg (file attached)",
            "01/05/2022, 12:03 - Alice Example: <Media omitted>",
            "01/05/2022, 12:04 - Bob Sample: This message was deleted",
            "01/05/2022, 12:05 - Alice Example: Fixed it\u0001 <This message was edited>",
            "02/05/2022, 09:00 - Bob Sample added Carol Test",
            "02/05/2022, 09:01 - Bob Sample: PTT-20220502-WA0003.opus (file attached)",
            "02/05/2022, 09:02 - Carol Test: report final.pdf (file attached)",
            "");

        public ExportTests()
        {
            var folder = temp.SubDirectory("in");
            var chatFile = Path.Combine(folder, "WhatsApp Chat with Bob Sample.txt");
            File.WriteAllText(chatFile, ChatText);
            File.WriteAllBytes(Path.Combine(folder, "IMG-20220501-WA0001.jpg"), Photo);
            File.WriteAllBytes(Path.Combine(folder, "PTT-20220502-WA0003.opus"), Voice);
            File.WriteAllBytes(Path.Combine(folder, "report final.pdf"), Pdf);
            db = TestDatabase.Create(temp);
            TestDatabase.Import(db, new[] { chatFile });
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        private (int ExitCode, string Output, string Error) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = Cli.Run(args.Concat(new[] { "-dbname", db }).ToArray(), output, error);
            return (exitCode, output.ToString(), error.ToString());
        }

        private string Export(string fileName, params string[] extra)
        {
            var path = temp.File(fileName);
            var (exitCode, _, error) = Run(new[] { "-export", path, "-chat", "Bob Sample" }.Concat(extra).ToArray());
            Assert.True(exitCode == 0, error);
            return path;
        }

        private static List<JsonElement> Messages(JsonDocument json)
        {
            return json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        }

        [Fact]
        public void JsonHasEveryMessageWithItsState()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Export("chat.json")));
            var messages = Messages(json);

            Assert.Equal(2, json.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("Bob Sample", json.RootElement.GetProperty("chat").GetProperty("name").GetString());
            Assert.Equal(new[] { "Alice Example", "Bob Sample", "Carol Test" },
                json.RootElement.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
            Assert.Equal(new[] { "text", "media", "media", "media", "deleted", "text", "system", "media", "media" },
                messages.Select(m => m.GetProperty("kind").GetString()));
            Assert.Equal("Hello <b>&</b> *world*\n- not a list\n\n1. nor this", messages[0].GetProperty("text").GetString());
            Assert.Equal("2022-05-01T12:00:00", messages[0].GetProperty("timestamp").GetString());
            Assert.Equal(new[] { "stored", "missing", "omitted" },
                messages.Skip(1).Take(3).Select(m => m.GetProperty("attachment").GetProperty("state").GetString()));
            Assert.False(messages[3].GetProperty("attachment").TryGetProperty("name", out _));
            Assert.True(messages[5].GetProperty("edited").GetBoolean());
            Assert.False(messages[6].TryGetProperty("sender", out _));
        }

        [Fact]
        public void FolderModeWritesEachFileBesideTheExport()
        {
            var path = Export("chat.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var attachments = json.RootElement.GetProperty("attachments").EnumerateArray().ToList();

            Assert.Equal(new[] { "image", "voiceNote", "document" }, attachments.Select(a => a.GetProperty("kind").GetString()));
            Assert.Equal("chat_files/report final.pdf", attachments[2].GetProperty("path").GetString());
            var expected = new[] { Photo, Voice, Pdf };
            for (int i = 0; i < attachments.Count; i++)
            {
                var file = Path.Combine(Path.GetDirectoryName(path)!, attachments[i].GetProperty("path").GetString()!);
                Assert.Equal(expected[i], File.ReadAllBytes(file));
                Assert.Equal(TestDatabase.Sha256(expected[i]), attachments[i].GetProperty("sha256").GetString());
                Assert.False(attachments[i].TryGetProperty("data", out _));
            }
        }

        [Fact]
        public void EmbedModePutsFilesInsideTheExport()
        {
            var path = Export("chat.json", "-attachments", "embed");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var attachments = json.RootElement.GetProperty("attachments").EnumerateArray().ToList();

            Assert.Equal(new[] { Photo, Voice, Pdf }, attachments.Select(a => a.GetProperty("data").GetBytesFromBase64()));
            Assert.All(attachments, a => Assert.False(a.TryGetProperty("path", out _)));
            Assert.False(Directory.Exists(temp.File("chat_files")));
        }

        [Fact]
        public void EmbeddedHtmlUsesDataUris()
        {
            var html = File.ReadAllText(Export("chat.html", "-attachments", "embed"));

            Assert.Contains($"src=\"data:image/jpeg;base64,{Convert.ToBase64String(Photo)}\"", html);
            Assert.Contains($"src=\"data:audio/ogg;base64,{Convert.ToBase64String(Voice)}\"", html);
        }

        [Fact]
        public void NoneModeDescribesFilesWithoutWritingThem()
        {
            var path = Export("chat.html", "-attachments", "none");
            var html = File.ReadAllText(path);

            Assert.False(Directory.Exists(temp.File("chat_files")));
            Assert.Contains("IMG-20220501-WA0001.jpg (stored, not exported)", html);
        }

        [Fact]
        public void XmlHasTheSameContentAsJson()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Export("chat.json")));
            var xml = XDocument.Load(Export("chat.xml"));
            var messages = xml.Root!.Element("messages")!.Elements("message").ToList();

            Assert.Equal(Messages(json).Select(m => m.GetProperty("kind").GetString()), messages.Select(m => (string?)m.Attribute("kind")));
            Assert.Equal(Messages(json).Select(m => m.GetProperty("id").GetInt64()), messages.Select(m => (long)m.Attribute("id")!));
            Assert.Equal("Hello <b>&</b> *world*\n- not a list\n\n1. nor this", (string?)messages[0].Element("text"));
            Assert.Equal("stored", (string?)messages[1].Element("attachment")!.Attribute("state"));
            Assert.Equal(3, xml.Root!.Element("attachments")!.Elements("attachment").Count());
        }

        [Fact]
        public void XmlReplacesCharactersItCannotHold()
        {
            var xml = XDocument.Load(Export("chat.xml"));

            var edited = xml.Root!.Element("messages")!.Elements("message").Single(m => (string?)m.Attribute("edited") == "true");
            Assert.Equal("Fixed it�", (string?)edited.Element("text"));
        }

        [Fact]
        public void MarkdownEscapesTextAndShowsPlaceholders()
        {
            var md = File.ReadAllText(Export("chat.md"));

            Assert.Contains(@"**12:00 Alice Example:** Hello \<b\>\&\</b\> \*world\*\", md);
            Assert.Contains("\n\\- not a list\\\n&nbsp;\\\n1\\. nor this\n", md);
            Assert.Contains("![IMG-20220501-WA0001.jpg](chat_files/IMG-20220501-WA0001.jpg)\\\nA caption", md);
            Assert.Contains("*[attachment not available: IMG-20220501-WA0002.jpg]*", md);
            Assert.Contains("*[media left out of the export]*", md);
            Assert.Contains("*[message deleted]*", md);
            Assert.Contains("*(edited)*", md);
            Assert.Contains("[report final.pdf](chat_files/report%20final.pdf)", md);
            Assert.Contains("## Monday 2 May 2022", md);
            Assert.Contains("*09:00 · Bob Sample added Carol Test*", md);
        }

        [Fact]
        public void HtmlEscapesTextAndShowsMediaAndPlaceholders()
        {
            var html = File.ReadAllText(Export("chat.html"));

            Assert.Contains("Hello &lt;b&gt;&amp;&lt;/b&gt; *world*", html);
            Assert.DoesNotContain("<b>&</b>", html);
            Assert.Contains("<img class=\"image\" src=\"chat_files/IMG-20220501-WA0001.jpg\"", html);
            Assert.Contains("<audio src=\"chat_files/PTT-20220502-WA0003.opus\"", html);
            Assert.Contains("href=\"chat_files/report%20final.pdf\"", html);
            Assert.Contains("Attachment not available: IMG-20220501-WA0002.jpg", html);
            Assert.Contains("Media left out of the export", html);
            Assert.Contains("Message deleted", html);
            Assert.Contains("<h2>Sunday 1 May 2022</h2>", html);
        }

        [Fact]
        public void HtmlNamesTheSenderOnlyAtTheStartOfARun()
        {
            var html = File.ReadAllText(Export("chat.html"));

            // Bob's 12:01 and 12:02 messages are a run; his 12:04 message follows one of Alice's, and
            // his 09:01 message starts a new day after a system line.
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(html, "<span class=\"who\">Bob Sample</span>"));
            Assert.Contains("<div class=\"msg s2 media\"><time>12:02</time>", html);
            Assert.Contains("<p class=\"sys\"><time>09:00</time> · Bob Sample added Carol Test</p>", html);
        }

        [Fact]
        public void HtmlGivesEachSenderAColourClass()
        {
            var html = File.ReadAllText(Export("chat.html"));

            Assert.Contains(".s1 { --hue: 137; }", html);
            Assert.Contains(".s3 { --hue: 51; }", html);
            Assert.DoesNotContain(".s4 ", html);
            Assert.DoesNotContain("style=", html);
            Assert.Contains("content-visibility: auto", html);
        }

        [Fact]
        public void MeMarksThatPersonsMessagesAsTheReadersOwn()
        {
            static int MineIn(string html) =>
                System.Text.RegularExpressions.Regex.Count(html, "<div class=\"msg [^\"]*\\bme\\b");

            Assert.Equal(0, MineIn(File.ReadAllText(Export("plain.html"))));
            Assert.Equal(4, MineIn(File.ReadAllText(Export("mine.html", "-me", "Bob Sample"))));

            using var json = JsonDocument.Parse(File.ReadAllText(Export("mine.json", "-me", "Bob Sample")));
            Assert.Equal("Bob Sample", json.RootElement.GetProperty("selection").GetProperty("me").GetString());
            var xml = XDocument.Load(Export("mine.xml", "-me", "Bob Sample"));
            Assert.Equal("Bob Sample", xml.Root!.Element("selection")!.Attribute("me")!.Value);
        }

        [Fact]
        public void PeopleFilterLeavesOutOtherSendersAndSystemMessages()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Export("chat.json", "-person", "Bob Sample", "Carol Test")));

            Assert.Equal(new[] { "Bob Sample", "Carol Test" }, json.RootElement.GetProperty("selection").GetProperty("people").EnumerateArray().Select(p => p.GetString()));
            Assert.All(Messages(json), m => Assert.NotEqual("Alice Example", m.GetProperty("sender").GetString()));
            Assert.Equal(5, Messages(json).Count);
        }

        [Theory]
        [InlineData("-from", "2022-05-02", 3)]
        [InlineData("-to", "2022-05-01", 6)]
        [InlineData("-from", "2022-05-01 12:02", 7)]
        [InlineData("-to", "2022-05-01 12:02", 2)]
        public void DateFiltersSelectTheRange(string option, string value, int expected)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Export("chat.json", option, value)));

            Assert.Equal(expected, Messages(json).Count);
        }

        [Fact]
        public void CustomTemplateRendersTheModel()
        {
            var template = temp.File("senders.txt.scriban");
            File.WriteAllText(template, "{{ for m in messages }}{{ m.timestamp | object.format 'HH:mm' }} {{ m.sender ?? 'system' }}|{{ end }}");

            var text = File.ReadAllText(Export("senders.txt", "-template", template));

            Assert.Equal("12:00 Alice Example|12:01 Bob Sample|12:02 Bob Sample|12:03 Alice Example|12:04 Bob Sample|" +
                         "12:05 Alice Example|09:00 system|09:01 Bob Sample|09:02 Carol Test|", text);
        }

        [Fact]
        public void TemplateErrorsAreReportedAndNothingIsWritten()
        {
            var template = temp.File("broken.scriban");
            File.WriteAllText(template, "{{ for m in messages }}");
            var output = temp.File("out.txt");

            var (exitCode, _, error) = Run("-export", output, "-chat", "Bob Sample", "-template", template);

            Assert.Equal(1, exitCode);
            Assert.Contains("broken.scriban", error);
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void TemplateRuntimeErrorsLeaveNoPartialFile()
        {
            var template = temp.File("bad.scriban");
            File.WriteAllText(template, "start {{ messages[0].no_such_thing.deeper }}");
            var output = temp.File("out.txt");

            var (exitCode, _, _) = Run("-export", output, "-chat", "Bob Sample", "-template", template);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(output));
        }

        [Theory]
        [InlineData(new[] { "-export", "x.json" }, "-export needs -chat")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Nobody" }, "No chat called 'Nobody'")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Bob Sample", "-person", "Zed" }, "Not in chat 'Bob Sample': 'Zed'")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Bob Sample", "-me", "Zed" }, "Not in chat 'Bob Sample': 'Zed'")]
        [InlineData(new[] { "-export", "x.doc", "-chat", "Bob Sample" }, "Can't tell the format")]
        [InlineData(new[] { "-export", "x.txt", "-chat", "Bob Sample", "-format", "pdf" }, "Unknown format 'pdf'")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Bob Sample", "-attachments", "zip" }, "Unknown -attachments mode")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Bob Sample", "-from", "May 1" }, "-from 'May 1' isn't a date")]
        [InlineData(new[] { "-export", "x.json", "-chat", "Bob Sample", "-from", "2022-05-02", "-to", "2022-05-01" }, "-from must be before -to")]
        [InlineData(new[] { "-list-chats", "-person", "A", "B" }, "takes one -person")]
        public void BadRequestsAreReported(string[] args, string message)
        {
            var (exitCode, _, error) = Run(args);

            Assert.Equal(1, exitCode);
            Assert.Contains(message, error);
        }

        [Fact]
        public void FormatOptionOverridesTheExtension()
        {
            var path = Export("chat.txt", "-format", "md");

            Assert.StartsWith("# Bob Sample", File.ReadAllText(path));
        }

        [Fact]
        public void ShowTemplateWritesTheBuiltInTemplate()
        {
            var (exitCode, output, _) = Run("-show-template", "html");

            Assert.Equal(0, exitCode);
            Assert.Equal(TemplateExporter.BuiltInTemplate(ExportFormat.Html), output);
            Assert.Contains("<!DOCTYPE html>", output);
        }

        [Fact]
        public void ShownTemplateGivesTheSameOutputAsTheBuiltIn()
        {
            var template = temp.File("my.scriban");
            File.WriteAllText(template, Run("-show-template", "md").Output);

            var builtIn = File.ReadAllText(Export("a.md"));
            var custom = File.ReadAllText(Export("a.md", "-template", template));

            Assert.Equal(builtIn, custom);
        }

        [Fact]
        public void CsvHasOneLinePerMessage()
        {
            var path = Export("chat.csv");

            const string chat = ",WhatsApp,\"Bob Sample\",\"\"";
            var expected = string.Join("\r\n",
                "timestamp,person,text,kind,edited,attachment,source,chat,subject",
                "2022-05-01 12:00:00,\"Alice Example\",\"Hello <b>&</b> *world* - not a list 1. nor this\",text,false,\"\"" + chat,
                "2022-05-01 12:01:00,\"Bob Sample\",\"IMG-20220501-WA0001.jpg (file attached) A caption\",media,false,\"IMG-20220501-WA0001.jpg\"" + chat,
                "2022-05-01 12:02:00,\"Bob Sample\",\"IMG-20220501-WA0002.jpg (file attached)\",media,false,\"IMG-20220501-WA0002.jpg\"" + chat,
                "2022-05-01 12:03:00,\"Alice Example\",\"<Media omitted>\",media,false,\"\"" + chat,
                "2022-05-01 12:04:00,\"Bob Sample\",\"This message was deleted\",deleted,false,\"\"" + chat,
                "2022-05-01 12:05:00,\"Alice Example\",\"Fixed it\u0001\",text,true,\"\"" + chat,
                "2022-05-02 09:00:00,\"\",\"Bob Sample added Carol Test\",system,false,\"\"" + chat,
                "2022-05-02 09:01:00,\"Bob Sample\",\"PTT-20220502-WA0003.opus (file attached)\",media,false,\"PTT-20220502-WA0003.opus\"" + chat,
                "2022-05-02 09:02:00,\"Carol Test\",\"report final.pdf (file attached)\",media,false,\"report final.pdf\"" + chat,
                "");
            Assert.Equal(expected, File.ReadAllText(path));
            Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.False(Directory.Exists(temp.File("chat_files")));
        }

        [Theory]
        [InlineData("plain", "\"plain\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("a, b", "\"a, b\"")]
        [InlineData("", "\"\"")]
        public void CsvQuotesAndDoublesQuotes(string value, string expected)
        {
            Assert.Equal(expected, CsvExporter.Quote(value));
        }

        [Theory]
        [InlineData("one\ntwo", "one two")]
        [InlineData("one\r\ntwo", "one two")]
        [InlineData("one \n\n  two", "one two")]
        [InlineData("one\u2028two", "one two")]
        [InlineData("no breaks", "no breaks")]
        public void CsvTextIsOneLine(string text, string expected)
        {
            Assert.Equal(expected, CsvExporter.SingleLine(text));
        }

        [Theory]
        [InlineData("=SUM(A1)", "'=SUM(A1)")]
        [InlineData("+44 7700 900123", "'+44 7700 900123")]
        [InlineData("-1", "'-1")]
        [InlineData("@Bob hi", "'@Bob hi")]
        [InlineData("email me@example.com", "email me@example.com")]
        [InlineData("a - b", "a - b")]
        [InlineData("", "")]
        public void CsvGuardsAgainstExcelFormulas(string value, string expected)
        {
            Assert.Equal(expected, CsvExporter.GuardFormula(value));
        }

        [Fact]
        public void CsvStripsMentionMarksAndGuardsOnlyForExcel()
        {
            // WhatsApp writes a mention as "@" + U+2068 + name + U+2069.
            var mention = "@" + (char)0x2068 + "Bob Sample" + (char)0x2069 + " see\nthis";

            Assert.Equal("\"@Bob Sample see this\"", CsvExporter.Field(mention));
            Assert.Equal("\"'@Bob Sample see this\"", CsvExporter.Field(mention, excel: true));
            Assert.Equal("\"\"", CsvExporter.Field(null));
        }

        [Fact]
        public void ExcelCsvHasABomAndOtherwiseTheSameRows()
        {
            var plain = Export("plain.csv");
            var excel = Export("excel.csv", "-excel");

            var excelBytes = File.ReadAllBytes(excel);
            Assert.True(excelBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            // The fixture has no formula-like values, so only the BOM differs.
            Assert.Equal(File.ReadAllBytes(plain), excelBytes[3..]);
        }

        [Fact]
        public void ExcelIsOnlyForCsv()
        {
            var (exitCode, _, error) = Run("-export", temp.File("x.json"), "-chat", "Bob Sample", "-excel");

            Assert.Equal(1, exitCode);
            Assert.Contains("-excel only applies to CSV", error);
        }

        [Fact]
        public void CsvCantEmbedFiles()
        {
            var (exitCode, _, error) = Run("-export", temp.File("x.csv"), "-chat", "Bob Sample", "-attachments", "embed");

            Assert.Equal(1, exitCode);
            Assert.Contains("CSV export can't embed", error);
        }

        [Fact]
        public void JsonlHasOneLinePerMessageWithSomethingToSay()
        {
            var path = temp.File("chat.jsonl");
            var (exitCode, output, error) = Run("-export", path, "-chat", "Bob Sample");
            Assert.True(exitCode == 0, error);

            var text = File.ReadAllText(path);
            var lines = text.Split('\n');
            Assert.Equal("", lines[^1]);
            Assert.DoesNotContain('\r', text);
            Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            // System lines, deleted placeholders and media without a caption are left out.
            var messages = lines[..^1].Select(l => JsonDocument.Parse(l).RootElement).ToList();
            Assert.Equal(
                new[]
                {
                    ("Alice Example", "Hello <b>&</b> *world*\n- not a list\n\n1. nor this"),
                    ("Bob Sample", "A caption"),
                    ("Alice Example", "Fixed it\u0001"),
                },
                messages.Select(m => (m.GetProperty("userid").GetString()!, m.GetProperty("content").GetString()!)));
            Assert.All(messages, m => Assert.Equal(2, m.EnumerateObject().Count()));
            Assert.Contains("Hello <b>&</b>", lines[0]);
            Assert.Contains("Exported 3 messages", output);
            Assert.False(Directory.Exists(temp.File("chat_files")));
        }

        [Fact]
        public void JsonlCantEmbedFiles()
        {
            var (exitCode, _, error) = Run("-export", temp.File("x.jsonl"), "-chat", "Bob Sample", "-attachments", "embed");

            Assert.Equal(1, exitCode);
            Assert.Contains("JSONL export can't embed", error);
        }

        [Fact]
        public void JsonlKeepsWhatsAppQuotesAndTidiesText()
        {
            ExportMessage Message(string source, string kind, string text) => new()
            {
                Id = 1,
                Timestamp = new DateTime(2022, 5, 1),
                Source = source,
                Chat = "Chat",
                Sender = "Bob Sample",
                SenderNumber = 1,
                Kind = kind,
                Text = text,
                IsEdited = false,
                AttachmentState = null,
                AttachmentName = null,
                Attachment = null,
            };
            var mention = "@" + (char)0x2068 + "Bob Sample" + (char)0x2069 + " hi  \n\n\n\nbye\n";

            // WhatsApp's > formatting is the sender's own text.
            Assert.Equal("> a quote\nmine", JsonlExporter.Content(Message("WhatsApp", "text", "> a quote\nmine")));
            Assert.Equal("Mine", JsonlExporter.Content(Message("Email", "text", "> Lunch?\nMine\n\nOn Wed, Sam wrote:\n> Old")));
            Assert.Equal("@Bob Sample hi\n\nbye", JsonlExporter.Content(Message("WhatsApp", "text", mention)));
            Assert.Null(JsonlExporter.Content(Message("Email", "text", "> All quote")));
            Assert.Null(JsonlExporter.Content(Message("WhatsApp", "media", "")));
            Assert.Null(JsonlExporter.Content(Message("WhatsApp", "system", "Bob Sample added Carol Test")));
            Assert.Null(JsonlExporter.Content(Message("WhatsApp", "deleted", "")));
        }

        [Fact]
        public void WhatsAppTextIsTheOriginalExport()
        {
            var text = File.ReadAllText(Export("out.txt", "-attachments", "none"));

            // Only the encryption notice's wording differs: the export uses current WhatsApp wording.
            var lines = text.Split('\n');
            Assert.Equal("01/05/2022, 12:00 - " + WhatsAppTextExporter.EncryptionNotice, lines[0]);
            Assert.Equal(ChatText.Split('\n').Skip(1), lines.Skip(1));
            Assert.DoesNotContain("\r", text);
            Assert.False(File.ReadAllBytes(temp.File("out.txt")).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        /// <summary>
        /// Imports an export into a new database and checks it holds exactly what this one does.
        /// </summary>
        private void AssertImportsBackIdentically(string exportPath)
        {
            var copy = TestDatabase.Create(temp, "copy.db");
            TestDatabase.ImportPaths(copy, new[] { exportPath });
            Assert.Equal(TestDatabase.Messages(db), TestDatabase.Messages(copy));
            Assert.Equal(TestDatabase.Attachments(db), TestDatabase.Attachments(copy));
        }

        [Fact]
        public void WhatsAppZipImportsBackIdentically()
        {
            var path = Export("WhatsApp Chat with Bob Sample.zip");

            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.Equal(
                    new[] { "IMG-20220501-WA0001.jpg", "PTT-20220502-WA0003.opus", "WhatsApp Chat with Bob Sample.txt", "report final.pdf" },
                    zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            }
            AssertImportsBackIdentically(path);
        }

        [Fact]
        public void WhatsAppTextWritesAttachmentsBesideItAndImportsBackIdentically()
        {
            var folder = temp.SubDirectory("whatsapp");
            var path = Path.Combine(folder, "WhatsApp Chat with Bob Sample.txt");
            var (exitCode, output, error) = Run("-export", path, "-chat", "Bob Sample");
            Assert.True(exitCode == 0, error);

            Assert.Equal(Photo, File.ReadAllBytes(Path.Combine(folder, "IMG-20220501-WA0001.jpg")));
            Assert.Equal(Pdf, File.ReadAllBytes(Path.Combine(folder, "report final.pdf")));
            Assert.Equal(4, Directory.GetFiles(folder).Length);
            Assert.Contains("3 attachment files written to", output);
            AssertImportsBackIdentically(folder);
        }

        [Fact]
        public void ImportingAWhatsAppExportIntoTheSameDatabaseChangesNothing()
        {
            var before = TestDatabase.Messages(db);
            var path = Export("WhatsApp Chat with Bob Sample.zip");

            var result = TestDatabase.ImportPaths(db, new[] { path }).Single();

            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Updated);
            Assert.Equal(before, TestDatabase.Messages(db));
        }

        [Fact]
        public void WhatsAppTextWontReplaceDifferentFiles()
        {
            var folder = temp.SubDirectory("busy");
            File.WriteAllText(Path.Combine(folder, "report final.pdf"), "someone else's report");
            var path = Path.Combine(folder, "chat.txt");

            var (exitCode, _, error) = Run("-export", path, "-chat", "Bob Sample");

            Assert.Equal(1, exitCode);
            Assert.Contains("would replace different files", error);
            Assert.False(File.Exists(path));
            Assert.Equal("someone else's report", File.ReadAllText(Path.Combine(folder, "report final.pdf")));
        }

        [Fact]
        public void WhatsAppTextCantEmbedFiles()
        {
            var (exitCode, _, error) = Run("-export", temp.File("x.txt"), "-chat", "Bob Sample", "-attachments", "embed");

            Assert.Equal(1, exitCode);
            Assert.Contains("can't embed files", error);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void OverlappingSyntheticExportsRoundTripThroughWhatsAppFormat(int seed)
        {
            // Overlapping exports with edits, deletions, omitted media and missing files, merged into
            // one chat, then written out as a single WhatsApp export and imported into a new database.
            var chat = SyntheticChat.Generate(seed, messageCount: 600, name: "Round Trip");
            var dir = temp.SubDirectory("synthetic");
            var exports = new[]
            {
                chat.WriteZip(dir, new ExportSpec(0, 300, WithMedia: false)),
                chat.WriteZip(dir, new ExportSpec(100, 450) { MediaWindow = 200 }),
                chat.WriteZip(dir, new ExportSpec(250, 600)),
            };
            var merged = TestDatabase.Create(temp, "merged.db");
            TestDatabase.ImportPaths(merged, exports);
            var path = temp.File("round trip.zip");
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.True(Cli.Run(new[] { "-export", path, "-chat", "Round Trip", "-dbname", merged }, output, error) == 0, error.ToString());

            var copy = TestDatabase.Create(temp, "copy.db");
            TestDatabase.ImportPaths(copy, new[] { path });
            Assert.Equal(TestDatabase.Messages(merged), TestDatabase.Messages(copy));
            Assert.Equal(TestDatabase.Attachments(merged), TestDatabase.Attachments(copy));
        }

        [Fact]
        public void LongChatsExportCompletely()
        {
            // More messages than Scriban's default loop limit of 1000.
            var chat = SyntheticChat.Generate(seed: 70, messageCount: 1500, name: "Long Chat");
            TestDatabase.ImportPaths(db, new[] { chat.WriteZip(temp.SubDirectory("long"), new ExportSpec(0, 1500)) });

            foreach (var file in new[] { "long.md", "long.html", "long.json", "long.xml" })
            {
                var path = temp.File(file);
                var (exitCode, _, error) = Run("-export", path, "-chat", "Long Chat");
                Assert.True(exitCode == 0, error);
            }

            using var json = JsonDocument.Parse(File.ReadAllText(temp.File("long.json")));
            var stored = TestDatabase.Messages(db).Where(m => m.Chat == "Long Chat").ToList();
            Assert.Equal(stored.Select(m => (m.Timestamp, m.Sender, m.Content.Text)),
                Messages(json).Select(m => (
                    DateTime.Parse(m.GetProperty("timestamp").GetString()!),
                    m.TryGetProperty("sender", out var s) ? s.GetString() : null,
                    m.GetProperty("text").GetString()!)));
            Assert.Equal(stored.Count, XDocument.Load(temp.File("long.xml")).Root!.Element("messages")!.Elements().Count());
            Assert.Equal(stored.Count(m => m.Sender != null),
                File.ReadAllText(temp.File("long.html")).Split("<div class=\"msg ").Length - 1);
        }

        [Theory]
        [InlineData("plain text", "plain text")]
        [InlineData("a*b_c`d", @"a\*b\_c\`d")]
        [InlineData("# not a heading", @"\# not a heading")]
        [InlineData("- item", @"\- item")]
        [InlineData("12. item", @"12\. item")]
        [InlineData("2023. A good year", @"2023\. A good year")]
        [InlineData("line one\nline two", "line one\\\nline two")]
        [InlineData("a\n\nb", "a\\\n&nbsp;\\\nb")]
        [InlineData("[link](url) <tag> & |pipe| ~strike~", @"\[link\](url) \<tag\> \& \|pipe\| \~strike\~")]
        public void MarkdownEscapeKeepsTextAsWritten(string text, string expected)
        {
            Assert.Equal(expected, TemplateExporter.MarkdownEscape(text));
        }
    }
}
