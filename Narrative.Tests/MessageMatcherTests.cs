using Narrative.App.Data;
using Narrative.App.WhatsApp;

namespace Narrative.Tests
{
    public class MessageMatcherTests
    {
        private static readonly DateTime Older = new(2022, 1, 1);
        private static readonly DateTime Newer = new(2023, 1, 1);

        private static MessageContent Text(string text, bool edited = false) => new(MessageKind.Text, text, edited, null);

        private static MessageContent Media(string? attachment, string caption = "") => new(MessageKind.Media, caption, false, attachment);

        private static MessageContent Deleted() => new(MessageKind.Deleted, "You deleted this message", false, null);

        private static (int? Sender, MessageContent Content) From(int? sender, MessageContent content) => (sender, content);

        private static Dictionary<int, int> Match(
            (int? Sender, MessageContent Content)[] incoming,
            (int? Sender, MessageContent Content)[] stored)
        {
            return MessageMatcher.MatchMinute(incoming, stored);
        }

        [Fact]
        public void MatchesIdenticalMessages()
        {
            var matches = Match(
                new[] { From(1, Text("a")), From(2, Text("b")) },
                new[] { From(1, Text("a")), From(2, Text("b")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1 }, matches);
        }

        [Fact]
        public void SameTextFromDifferentSendersDoesNotMatch()
        {
            Assert.Empty(Match(new[] { From(1, Text("ok")) }, new[] { From(2, Text("ok")) }));
        }

        [Fact]
        public void IdenticalRepeatsMatchOneToOne()
        {
            var matches = Match(new[] { From(1, Text("ok")), From(1, Text("ok")) }, new[] { From(1, Text("ok")) });

            Assert.Single(matches);
        }

        [Fact]
        public void RepeatIsMatchedByPositionAmongNeighbours()
        {
            // Stored: ha, what, ha. An export starting mid-minute has only: what, ha.
            var matches = Match(
                new[] { From(2, Text("what")), From(1, Text("ha")) },
                new[] { From(1, Text("ha")), From(2, Text("what")), From(1, Text("ha")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 1, [1] = 2 }, matches);
        }

        [Fact]
        public void RepeatWhereTheFirstWasLaterDeleted()
        {
            // Stored from an older export: X, X. A newer export: deleted, X.
            var matches = Match(
                new[] { From(1, Deleted()), From(1, Text("X")) },
                new[] { From(1, Text("X")), From(1, Text("X")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1 }, matches);
        }

        [Fact]
        public void RepeatWhereTheFirstWasLaterEdited()
        {
            var matches = Match(
                new[] { From(1, Text("X!", edited: true)), From(1, Text("X")) },
                new[] { From(1, Text("X")), From(1, Text("X")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1 }, matches);
        }

        [Fact]
        public void PairsVariantsInOrder()
        {
            var matches = Match(
                new[] { From(1, Media("IMG-1.jpg")), From(1, Deleted()), From(2, Text("x")), From(1, Text("hi there", edited: true)) },
                new[] { From(1, Media(null)), From(1, Text("original")), From(2, Text("x")), From(1, Text("hi")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 2, [3] = 3 }, matches);
        }

        [Fact]
        public void DoesNotPairUnrelatedText()
        {
            Assert.Empty(Match(new[] { From(1, Text("new message")) }, new[] { From(1, Text("old message")) }));
        }

        [Fact]
        public void DoesNotPairVariantsFromDifferentSenders()
        {
            Assert.Empty(Match(new[] { From(1, Media("IMG-1.jpg")) }, new[] { From(2, Media(null)) }));
        }

        [Fact]
        public void DoesNotPairDifferentAttachments()
        {
            Assert.False(MessageMatcher.AreVariants(Media("IMG-1.jpg"), Media("IMG-2.jpg")));
        }

        [Fact]
        public void RealContentBeatsDeletedPlaceholder()
        {
            Assert.Equal(Text("hi"), MessageMatcher.Merge(Deleted(), Newer, Text("hi"), Older).Content);
            Assert.Equal(Text("hi"), MessageMatcher.Merge(Text("hi"), Older, Deleted(), Newer).Content);
        }

        [Fact]
        public void AttachmentNameBeatsOmittedMedia()
        {
            Assert.Equal(Media("IMG-1.jpg"), MessageMatcher.Merge(Media(null), Older, Media("IMG-1.jpg"), Newer).Content);
            Assert.Equal(Media("IMG-1.jpg"), MessageMatcher.Merge(Media("IMG-1.jpg"), Older, Media(null), Newer).Content);
        }

        [Fact]
        public void CaptionIsKeptWhenOneExportLacksIt()
        {
            var merged = MessageMatcher.Merge(Media(null, ""), Newer, Media("IMG-1.jpg", "caption"), Older).Content;

            Assert.Equal(Media("IMG-1.jpg", "caption"), merged);
        }

        [Fact]
        public void EditedBeatsUnedited()
        {
            Assert.Equal(Text("hi!", true), MessageMatcher.Merge(Text("hi"), Newer, Text("hi!", true), Older).Content);
            Assert.Equal(Text("hi!", true), MessageMatcher.Merge(Text("hi!", true), Older, Text("hi"), Newer).Content);
        }

        [Fact]
        public void LaterEditBeatsEarlierEdit()
        {
            Assert.Equal(Text("v3", true), MessageMatcher.Merge(Text("v2", true), Older, Text("v3", true), Newer).Content);
            Assert.Equal(Text("v3", true), MessageMatcher.Merge(Text("v3", true), Newer, Text("v2", true), Older).Content);
        }

        [Fact]
        public void MergeOrderFollowsIncomingAndKeepsStoredOnlyMessagesInPlace()
        {
            string a = "a", b = "b", c = "c", x = "x";
            // Stored: a b c. Incoming has a, new x, c (b missing from this export).
            var result = MessageMatcher.MergeOrder(new[] { a, b, c }, new[] { a, x, c });

            Assert.Equal(new[] { "a", "b", "x", "c" }, result);
        }

        [Fact]
        public void MergeOrderAppendsNewMessagesToEmptyMinute()
        {
            var result = MessageMatcher.MergeOrder(Array.Empty<string>(), new[] { "p", "q" });

            Assert.Equal(new[] { "p", "q" }, result);
        }

        private static readonly DateTime M = new(2019, 5, 12, 9, 11, 0);

        private static (int? Sender, MessageContent Content, DateTime Minute) At(int minute, int? sender, MessageContent content)
            => (sender, content, M.AddMinutes(minute));

        [Fact]
        public void AnIdenticalCopyAMinuteAwayBeatsAVariantInTheSameMinute()
        {
            // A rounded export has "x" a minute late, where the stored minute holds a deleted message.
            var matches = MessageMatcher.Match(
                new[] { At(1, 1, Text("x")), At(2, 1, Deleted()) },
                new[] { At(0, 1, Text("x")), At(1, 1, Deleted()) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1 }, matches);
        }

        [Fact]
        public void AnIdenticalCopyInTheSameMinuteBeatsOneAMinuteAway()
        {
            var matches = MessageMatcher.Match(
                new[] { At(1, 1, Text("ok")) },
                new[] { At(0, 1, Text("ok")), At(1, 1, Text("ok")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 1 }, matches);
        }

        [Fact]
        public void NothingPairsTwoMinutesApart()
        {
            Assert.Empty(MessageMatcher.Match(new[] { At(2, 1, Text("ok")) }, new[] { At(0, 1, Text("ok")) }));
        }

        [Fact]
        public void VariantsPairAMinuteApart()
        {
            var matches = MessageMatcher.Match(new[] { At(1, 1, Media(null)) }, new[] { At(0, 1, Media("IMG-1.jpg")) });

            Assert.Equal(new Dictionary<int, int> { [0] = 0 }, matches);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void TheBandedAlignmentScoresAsWellAsAFullOne(int seed)
        {
            // Random messages over a long run of consecutive minutes, compared with a full table.
            var rng = new Random(seed);
            var contents = new[] { Text("a"), Text("b"), Text("a", edited: true), Media(null), Media("IMG-1.jpg"), Deleted() };
            List<(int? Sender, MessageContent Content, DateTime Minute)> Random(int count)
            {
                var minute = 0;
                return Enumerable.Range(0, count).Select(_ =>
                {
                    minute += rng.Next(3) == 0 ? 1 : 0;
                    return At(minute, rng.Next(2), contents[rng.Next(contents.Length)]);
                }).ToList();
            }
            var incoming = Random(120);
            var stored = Random(110);
            var matches = MessageMatcher.Match(incoming, stored);

            Assert.True(matches.Keys.Zip(matches.Keys.Skip(1)).All(p => p.First < p.Second));
            Assert.True(matches.Values.Zip(matches.Values.Skip(1)).All(p => p.First < p.Second), "pairs cross");
            Assert.Equal(FullTableBest(incoming, stored), matches.Sum(p => Score(incoming[p.Key], stored[p.Value])));
        }

        /// <summary>The scoring <see cref="MessageMatcher.Match"/> documents, written out independently.</summary>
        private static int Score(
            (int? Sender, MessageContent Content, DateTime Minute) a,
            (int? Sender, MessageContent Content, DateTime Minute) b)
        {
            var gap = Math.Abs((a.Minute - b.Minute).TotalMinutes);
            if (a.Sender != b.Sender || gap > 1)
            {
                return 0;
            }
            if (a.Content == b.Content)
            {
                return gap == 0 ? 4 : 3;
            }
            if (MessageMatcher.AreVariants(a.Content, b.Content))
            {
                return gap == 0 ? 2 : 1;
            }
            return 0;
        }

        private static int FullTableBest(
            List<(int? Sender, MessageContent Content, DateTime Minute)> incoming,
            List<(int? Sender, MessageContent Content, DateTime Minute)> stored)
        {
            var best = new int[incoming.Count + 1, stored.Count + 1];
            for (int i = incoming.Count - 1; i >= 0; i--)
            {
                for (int j = stored.Count - 1; j >= 0; j--)
                {
                    var score = Score(incoming[i], stored[j]);
                    best[i, j] = Math.Max(score > 0 ? score + best[i + 1, j + 1] : 0, Math.Max(best[i + 1, j], best[i, j + 1]));
                }
            }
            return best[0, 0];
        }
    }
}
