using Microsoft.EntityFrameworkCore;
using Narrative.App.Data;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    /// <summary>
    /// Some WhatsApp versions round message times to the nearest minute where others drop the
    /// seconds, so one export can show a message a minute later than another.
    /// </summary>
    public class ShiftedTimeTests
    {
        private static readonly DateTime T = new(2019, 5, 12, 9, 10, 0);

        private static SyntheticMessage Text(int index, int minute, int second, string sender, string text, int? editedAt = null)
        {
            return new SyntheticMessage
            {
                Index = index,
                Timestamp = T.AddMinutes(minute),
                Second = second,
                Sender = sender,
                Text = text,
                EditedAt = editedAt,
                EditedText = editedAt == null ? null : text + " again",
            };
        }

        /// <summary>A run of three in one minute, of which a rounding export moves the last two.</summary>
        private static SyntheticChat Run()
        {
            return new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                Text(0, 0, 10, "Bob Sample", "Hi"),
                Text(1, 1, 5, "Alice Example", "one"),
                Text(2, 1, 40, "Alice Example", "two"),
                Text(3, 1, 50, "Alice Example", "three"),
                Text(4, 3, 0, "Bob Sample", "ok"),
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ARoundedExportPairsWithATruncatedOneInEitherOrder(bool roundedFirst)
        {
            var chat = Run();
            using var temp = new TempDirectory();
            var truncated = chat.Write(temp.Path, new ExportSpec(0, 5), "WhatsApp Chat with Bob Sample 2019-06-26.txt");
            var rounded = chat.Write(temp.Path, new ExportSpec(0, 5) { RoundsTimes = true }, "WhatsApp Chat with Bob Sample 2019-07-06.txt");
            var db = TestDatabase.Create(temp);

            var results = TestDatabase.Import(db, roundedFirst ? new[] { rounded, truncated } : new[] { truncated, rounded });

            Assert.Equal(0, results[1].Added);
            Assert.Equal(5, results[1].Matched);
            Assert.Equal(2, results[1].Shifted);
            // A rounded copy stored first is moved back to the earlier minute.
            Assert.Equal(roundedFirst ? 2 : 0, results[1].Updated);
            var stored = TestDatabase.Messages(db);
            Assert.Equal(new[] { "Hi", "one", "two", "three", "ok" }, stored.Select(m => m.Content.Text));
            Assert.Equal(new[] { 0, 1, 1, 1, 3 }, stored.Select(m => (int)(m.Timestamp - T).TotalMinutes));
        }

        [Fact]
        public void EachExportsOwnTimeIsKeptAsASource()
        {
            var chat = Run();
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);
            TestDatabase.Import(db, new[]
            {
                chat.Write(temp.Path, new ExportSpec(0, 5) { RoundsTimes = true }, "WhatsApp Chat with Bob Sample 2019-07-06.txt"),
                chat.Write(temp.Path, new ExportSpec(0, 5), "WhatsApp Chat with Bob Sample 2019-06-26.txt"),
            });

            using var context = NarrativeDatabase.Open(db);
            var two = context.Messages.AsNoTracking().Include(m => m.Sources).Single(m => m.Text == "two");
            Assert.Equal(T.AddMinutes(1), two.Timestamp);
            Assert.Equal(new[] { T.AddMinutes(1), T.AddMinutes(2) }, two.Sources.Select(s => s.Timestamp).Order());
        }

        [Fact]
        public void AShiftedMessageKeepsItsPlaceAmongTheOthers()
        {
            // The shifted message is followed in its true minute by one the rounding doesn't move.
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                Text(0, 0, 10, "Alice Example", "one"),
                Text(1, 0, 45, "Alice Example", "two"),
                Text(2, 1, 5, "Bob Sample", "three"),
                Text(3, 1, 20, "Alice Example", "four"),
            });
            foreach (var roundedFirst in new[] { false, true })
            {
                using var temp = new TempDirectory();
                var truncated = chat.Write(temp.Path, new ExportSpec(0, 4), "WhatsApp Chat with Bob Sample 2019-06-26.txt");
                var rounded = chat.Write(temp.Path, new ExportSpec(0, 4) { RoundsTimes = true }, "WhatsApp Chat with Bob Sample 2019-07-06.txt");
                var db = TestDatabase.Create(temp);

                TestDatabase.Import(db, roundedFirst ? new[] { rounded, truncated } : new[] { truncated, rounded });

                var stored = TestDatabase.Messages(db);
                Assert.Equal(new[] { "one", "two", "three", "four" }, stored.Select(m => m.Content.Text));
                Assert.Equal(new[] { 0, 0, 1, 1 }, stored.Select(m => (int)(m.Timestamp - T).TotalMinutes));
            }
        }

        [Fact]
        public void IdenticalMessagesInNeighbouringMinutesStaySeparate()
        {
            // Two real "ok"s a minute apart. The later export lacks the first (deleted for me), so its
            // second "ok" must pair with the stored one in its own minute, not the one before.
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                Text(0, 0, 10, "Bob Sample", "Hi"),
                Text(1, 1, 10, "Alice Example", "ok"),
                Text(2, 2, 10, "Alice Example", "ok"),
                Text(3, 3, 10, "Bob Sample", "bye"),
            });
            using var temp = new TempDirectory();
            var all = chat.Write(temp.Path, new ExportSpec(0, 4), "WhatsApp Chat with Bob Sample 2019-06-26.txt");
            var later = temp.File("WhatsApp Chat with Bob Sample 2019-07-06.txt");
            File.WriteAllLines(later, File.ReadAllLines(all).Where((_, i) => i != 2));
            var db = TestDatabase.Create(temp);

            var results = TestDatabase.Import(db, new[] { all, later });

            Assert.Equal(0, results[1].Shifted);
            var stored = TestDatabase.Messages(db);
            Assert.Equal(new[] { "Hi", "ok", "ok", "bye" }, stored.Select(m => m.Content.Text));
            Assert.Equal(new[] { 0, 1, 2, 3 }, stored.Select(m => (int)(m.Timestamp - T).TotalMinutes));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AShiftedVariantAtTheStartOfAFileIsPaired(bool roundedFirst)
        {
            // The chat starts with a photo sent at :45. A rounded export without media shows it a
            // minute late as "<Media omitted>", in a minute before which that export has nothing.
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                new SyntheticMessage { Index = 0, Timestamp = T, Second = 45, Sender = "Bob Sample", Attachment = "IMG-20190512-WA0001.jpg" },
                Text(1, 3, 0, "Alice Example", "nice"),
            });
            using var temp = new TempDirectory();
            var withMedia = chat.Write(temp.Path, new ExportSpec(0, 2), "WhatsApp Chat with Bob Sample 2019-06-26.txt");
            var rounded = chat.Write(temp.Path, new ExportSpec(0, 2, WithMedia: false) { RoundsTimes = true }, "WhatsApp Chat with Bob Sample 2019-07-06.txt");
            var db = TestDatabase.Create(temp);

            TestDatabase.Import(db, roundedFirst ? new[] { rounded, withMedia } : new[] { withMedia, rounded });

            var stored = TestDatabase.Messages(db);
            Assert.Equal(2, stored.Count);
            Assert.Equal(T, stored[0].Timestamp);
            Assert.Equal("IMG-20190512-WA0001.jpg", stored[0].Content.AttachmentName);
        }

        [Fact]
        public void AShiftedVariantInsideTheFileIsPaired()
        {
            // A rounded export made after an edit: the edited copy is a minute later.
            var chat = new SyntheticChat("Bob Sample", new[] { "Alice Example", "Bob Sample" }, new[]
            {
                Text(0, 0, 10, "Bob Sample", "Hi"),
                Text(1, 1, 40, "Alice Example", "typo", editedAt: 2),
                Text(2, 3, 0, "Bob Sample", "ok"),
            });
            using var temp = new TempDirectory();
            var db = TestDatabase.Create(temp);

            var results = TestDatabase.Import(db, new[]
            {
                chat.Write(temp.Path, new ExportSpec(0, 2), "WhatsApp Chat with Bob Sample 2019-06-26.txt"),
                chat.Write(temp.Path, new ExportSpec(0, 3) { RoundsTimes = true }, "WhatsApp Chat with Bob Sample 2019-07-06.txt"),
            });

            Assert.Equal(1, results[1].Shifted);
            var stored = TestDatabase.Messages(db);
            Assert.Equal(new[] { "Hi", "typo again", "ok" }, stored.Select(m => m.Content.Text));
            Assert.Equal(new[] { 0, 1, 3 }, stored.Select(m => (int)(m.Timestamp - T).TotalMinutes));
        }
    }
}
