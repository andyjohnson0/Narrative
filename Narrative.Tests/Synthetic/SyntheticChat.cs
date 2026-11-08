using System.Globalization;
using System.Text;
using Narrative.App.Data;

namespace Narrative.Tests.Synthetic
{
    /// <summary>
    /// A made-up WhatsApp chat that can be rendered as Android UK exports. Exports of different ranges,
    /// with or without media and at different points in time, overlap the way real exports do.
    /// </summary>
    public sealed class SyntheticChat
    {
        public const string EncryptionNotice =
            "Messages and calls are end-to-end encrypted. Only people in this chat can read, listen to, or share them. Learn more.";

        private static readonly string[] Words =
        {
            "apple", "bicycle", "cloud", "dinner", "engine", "forest", "garden", "harbour", "island", "jacket",
            "kettle", "lemon", "meeting", "number", "orange", "pencil", "quiet", "river", "station", "ticket",
            "umbrella", "valley", "window", "yellow", "zebra", "tomorrow", "later", "maybe", "great", "thanks",
        };

        public SyntheticChat(string name, IReadOnlyList<string> participants, IReadOnlyList<SyntheticMessage> messages)
        {
            Name = name;
            Participants = participants;
            Messages = messages;
        }

        public string Name { get; }

        /// <summary>The chat's members. The first is the person who makes the exports.</summary>
        public IReadOnlyList<string> Participants { get; }

        public IReadOnlyList<SyntheticMessage> Messages { get; }

        public string Owner => Participants[0];

        /// <summary>
        /// Generates a random chat. The same seed always gives the same chat.
        /// </summary>
        public static SyntheticChat Generate(
            int seed,
            int messageCount,
            string name = "Bob Sample",
            string[]? participants = null)
        {
            participants ??= new[] { "Alice Example", name };
            var rng = new Random(seed);
            var messages = new List<SyntheticMessage>(messageCount);
            var time = new DateTime(2020, 1, 1, 9, 0, 0);
            int attachmentNumber = 0;

            for (int i = 0; i < messageCount; i++)
            {
                var previous = i > 0 ? messages[i - 1] : null;

                // An identical repeat in the same minute, which a naive dedup would collapse.
                if (previous != null && !previous.IsSystem && !previous.IsMedia && rng.NextDouble() < 0.03)
                {
                    messages.Add(new SyntheticMessage
                    {
                        Index = i,
                        Timestamp = previous.Timestamp,
                        Sender = previous.Sender,
                        Text = previous.Text,
                    });
                    continue;
                }

                time = time.AddMinutes(NextGap(rng));
                var roll = rng.NextDouble();
                if (roll < 0.01)
                {
                    messages.Add(new SyntheticMessage
                    {
                        Index = i,
                        Timestamp = time,
                        Text = $"Your security code with {participants[1]} changed. Tap to learn more.",
                    });
                    continue;
                }

                var sender = participants[rng.Next(participants.Length)];
                if (roll < 0.07)
                {
                    attachmentNumber += 1;
                    var attachment = AttachmentName(rng, time, attachmentNumber);
                    var captioned = attachment.StartsWith("IMG-") || attachment.StartsWith("VID-");
                    // Some media is forwarded: a new name for a file already in the chat.
                    var earlier = messages.Where(m => m.IsMedia).ToList();
                    var contentKey = earlier.Count > 0 && rng.NextDouble() < 0.1
                        ? earlier[rng.Next(earlier.Count)].ContentKey
                        : attachment;
                    messages.Add(new SyntheticMessage
                    {
                        Index = i,
                        Timestamp = time,
                        Sender = sender,
                        Attachment = attachment,
                        AttachmentContentKey = contentKey,
                        Text = captioned && rng.NextDouble() < 0.4 ? Sentence(rng) : "",
                        DeletedAt = rng.NextDouble() < 0.05 ? i + rng.Next(1, 40) : null,
                    });
                    continue;
                }

                var text = Sentence(rng);
                if (rng.NextDouble() < 0.08)
                {
                    text += "\n" + Sentence(rng);
                    if (rng.NextDouble() < 0.5)
                    {
                        text += "\n\n" + Sentence(rng);
                    }
                }
                var edited = rng.NextDouble() < 0.05;
                messages.Add(new SyntheticMessage
                {
                    Index = i,
                    Timestamp = time,
                    Sender = sender,
                    Text = text,
                    EditedAt = edited ? i + rng.Next(1, 40) : null,
                    EditedText = edited ? text + " " + Words[rng.Next(Words.Length)] : null,
                    DeletedAt = !edited && rng.NextDouble() < 0.04 ? i + rng.Next(1, 40) : null,
                });
            }

            // Seconds come from their own generator, so adding them didn't change any seed's chat.
            // Messages in one minute get increasing seconds, as they were sent in order.
            var seconds = new Random(seed * 7919 + 1);
            foreach (var minute in messages.GroupBy(m => m.Timestamp))
            {
                var drawn = minute.Select(_ => seconds.Next(60)).Order().ToList();
                foreach (var (m, second) in minute.Zip(drawn))
                {
                    m.Second = second;
                }
            }

            return new SyntheticChat(name, participants, messages);
        }

        /// <summary>
        /// An attachment name in WhatsApp's Android style, with a mix of types like a real chat.
        /// </summary>
        private static string AttachmentName(Random rng, DateTime time, int number)
        {
            var stem = $"{time:yyyyMMdd}-WA{number:0000}";
            var roll = rng.NextDouble();
            return roll switch
            {
                < 0.55 => $"IMG-{stem}.jpg",
                < 0.65 => $"VID-{stem}.mp4",
                < 0.80 => $"PTT-{stem}.opus",
                < 0.85 => $"AUD-{stem}.m4a",
                < 0.92 => $"STK-{stem}.webp",
                < 0.96 => $"DOC-{stem}.pdf",
                _ => $"{Words[rng.Next(Words.Length)]} contact {number}.vcf",
            };
        }

        /// <summary>
        /// The bytes of a synthetic attachment file: deterministic and distinct per content key.
        /// </summary>
        public static byte[] AttachmentBytes(string contentKey)
        {
            var seed = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(contentKey));
            var length = 64 + (seed[0] | (seed[1] << 8)) % 2000;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = seed[i % seed.Length];
            }
            Encoding.ASCII.GetBytes("SYNTHETIC").CopyTo(bytes, 0);
            return bytes;
        }

        private static int NextGap(Random rng)
        {
            var roll = rng.NextDouble();
            if (roll < 0.35)
            {
                return 0;
            }
            if (roll < 0.75)
            {
                return rng.Next(1, 11);
            }
            if (roll < 0.95)
            {
                return rng.Next(11, 600);
            }
            return rng.Next(600, 5000);
        }

        private static string Sentence(Random rng)
        {
            var count = rng.Next(1, 12);
            var words = Enumerable.Range(0, count).Select(_ => Words[rng.Next(Words.Length)]);
            var sentence = string.Join(' ', words);
            // Some text contains a colon, which must not be mistaken for a sender separator.
            return rng.NextDouble() < 0.05 ? "note: " + sentence : sentence;
        }

        /// <summary>
        /// Renders an export as WhatsApp would write it.
        /// </summary>
        public string Render(ExportSpec spec)
        {
            var sb = new StringBuilder();
            var first = Messages[spec.From];
            sb.Append(Header(TimeIn(first, spec))).Append(EncryptionNotice).Append('\n');
            for (int i = spec.From; i < spec.To; i++)
            {
                var m = Messages[i];
                sb.Append(Header(TimeIn(m, spec)));
                if (!m.IsSystem)
                {
                    sb.Append(m.Sender).Append(": ");
                }
                sb.Append(RenderText(m, spec)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes an export to <paramref name="directory"/> and returns its path. By default the file is
        /// named with the export date appended, "WhatsApp Chat with &lt;name&gt; &lt;date&gt;.txt", with a
        /// Windows-style " (n)" copy suffix if a different export already has that name.
        /// </summary>
        public string Write(string directory, ExportSpec spec, string? fileName = null)
        {
            var content = Render(spec);
            string path;
            if (fileName != null)
            {
                path = Path.Combine(directory, fileName);
            }
            else
            {
                var stem = DefaultStem(spec);
                path = Path.Combine(directory, stem + ".txt");
                for (int n = 2; File.Exists(path) && File.ReadAllText(path) != content; n++)
                {
                    path = Path.Combine(directory, $"{stem} ({n}).txt");
                }
            }
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        /// <summary>The minute an export shows for a message.</summary>
        public static DateTime TimeIn(SyntheticMessage m, ExportSpec spec)
        {
            return spec.RoundsTimes && m.Second >= 30 ? m.Timestamp.AddMinutes(1) : m.Timestamp;
        }

        /// <summary>
        /// The time the database should hold for a message after importing <paramref name="exports"/>:
        /// the earliest any of them showed, which is the true minute unless every export rounded it up.
        /// </summary>
        public static DateTime ExpectedTimestamp(SyntheticMessage m, IEnumerable<ExportSpec> exports)
        {
            return exports.Where(e => e.Contains(m.Index)).Min(e => TimeIn(m, e));
        }

        private static string Header(DateTime timestamp)
        {
            return timestamp.ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture) + " - ";
        }

        private string RenderText(SyntheticMessage m, ExportSpec spec)
        {
            if (m.IsSystem)
            {
                return m.Text;
            }
            if (m.DeletedAt < spec.EffectiveAsOf)
            {
                return DeletedPlaceholder(m);
            }
            if (m.IsMedia)
            {
                var first = spec.NamesMedia(m.Index) ? $"{m.Attachment} (file attached)" : "<Media omitted>";
                return m.Text.Length == 0 ? first : first + "\n" + m.Text;
            }
            if (m.EditedAt < spec.EffectiveAsOf)
            {
                return m.EditedText + " <This message was edited>";
            }
            return m.Text;
        }

        private string DeletedPlaceholder(SyntheticMessage m)
        {
            return m.Sender == Owner ? "You deleted this message" : "This message was deleted";
        }

        /// <summary>
        /// The content the database should hold for a message after importing <paramref name="exports"/>:
        /// the most complete version any of them showed.
        /// </summary>
        public MessageContent ExpectedContent(SyntheticMessage m, IEnumerable<ExportSpec> exports)
        {
            var views = exports.Where(e => e.Contains(m.Index)).ToList();
            if (views.Count == 0)
            {
                throw new ArgumentException($"No export contains message {m.Index}.");
            }
            if (m.IsSystem)
            {
                return new MessageContent(MessageKind.System, m.Text, false, null);
            }

            var visible = views.Where(e => !(m.DeletedAt < e.EffectiveAsOf)).ToList();
            if (visible.Count == 0)
            {
                return new MessageContent(MessageKind.Deleted, DeletedPlaceholder(m), false, null);
            }
            if (m.IsMedia)
            {
                var attachment = visible.Any(e => e.NamesMedia(m.Index)) ? m.Attachment : null;
                return new MessageContent(MessageKind.Media, m.Text, false, attachment);
            }
            if (visible.Any(e => m.EditedAt < e.EffectiveAsOf))
            {
                return new MessageContent(MessageKind.Text, m.EditedText!, true, null);
            }
            return new MessageContent(MessageKind.Text, m.Text, false, null);
        }

        /// <summary>
        /// The attachment state the database should show for a message after importing
        /// <paramref name="exports"/>, of which <paramref name="supplyingFiles"/> were written with their
        /// attachment files (as a zip or folder), and the content key of the stored file if any.
        /// </summary>
        public (AttachmentState State, string? ContentKey) ExpectedAttachment(
            SyntheticMessage m, IEnumerable<ExportSpec> exports, IEnumerable<ExportSpec> supplyingFiles)
        {
            var content = ExpectedContent(m, exports);
            if (content.Kind != MessageKind.Media)
            {
                return (AttachmentState.None, null);
            }
            if (content.AttachmentName == null)
            {
                return (AttachmentState.Omitted, null);
            }
            return supplyingFiles.Any(e => SuppliesFile(e, m))
                ? (AttachmentState.Stored, m.ContentKey)
                : (AttachmentState.Missing, null);
        }

        /// <summary>True if an export written with files includes the file for <paramref name="m"/>.</summary>
        public static bool SuppliesFile(ExportSpec spec, SyntheticMessage m)
        {
            return m.IsMedia
                && spec.Contains(m.Index)
                && spec.NamesMedia(m.Index)
                && !(m.DeletedAt < spec.EffectiveAsOf)
                && !spec.DroppedFiles.Contains(m.Index);
        }

        /// <summary>
        /// The attachment files that go with an export: (file name, bytes).
        /// </summary>
        public IEnumerable<(string Name, byte[] Bytes)> AttachmentFiles(ExportSpec spec)
        {
            return Messages
                .Where(m => SuppliesFile(spec, m))
                .Select(m => (m.Attachment!, AttachmentBytes(m.ContentKey!)));
        }

        /// <summary>
        /// The default text file name: "WhatsApp Chat with &lt;name&gt; &lt;date&gt;".
        /// </summary>
        public string DefaultStem(ExportSpec spec)
        {
            return $"WhatsApp Chat with {Name} {Messages[spec.To - 1].Timestamp:yyyy-MM-dd}";
        }

        /// <summary>
        /// Writes an export as Android shares it with media: a zip holding "WhatsApp Chat with
        /// &lt;name&gt;.txt" and the attachment files. The zip's name has the date appended, and
        /// a " (n)" suffix if a different zip already has that name.
        /// </summary>
        /// <param name="directory">Where to write the zip.</param>
        /// <param name="spec">The export.</param>
        /// <param name="extraFiles">Further files to add, e.g. ones no message names.</param>
        public string WriteZip(string directory, ExportSpec spec, IEnumerable<(string Name, byte[] Bytes)>? extraFiles = null)
        {
            var stem = DefaultStem(spec);
            var path = Path.Combine(directory, stem + ".zip");
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(directory, $"{stem} ({n}).zip");
            }

            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            void Add(string name, byte[] bytes)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
            Add($"WhatsApp Chat with {Name}.txt", new UTF8Encoding(false).GetBytes(Render(spec)));
            foreach (var (name, bytes) in AttachmentFiles(spec).Concat(extraFiles ?? Enumerable.Empty<(string, byte[])>()))
            {
                Add(name, bytes);
            }
            return path;
        }

        /// <summary>
        /// Writes an export's text file and attachment files into <paramref name="directory"/>, as if a
        /// zip had been extracted there. Returns the text file's path.
        /// </summary>
        public string WriteFolder(string directory, ExportSpec spec)
        {
            Directory.CreateDirectory(directory);
            var path = Write(directory, spec);
            foreach (var (name, bytes) in AttachmentFiles(spec))
            {
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
            }
            return path;
        }
    }
}
