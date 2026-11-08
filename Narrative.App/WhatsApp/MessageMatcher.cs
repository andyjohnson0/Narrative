using Narrative.App.Data;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Decides which incoming messages are copies of stored ones, and how to combine their content.
    /// All methods are pure so the rules can be tested on their own.
    /// </summary>
    public static class MessageMatcher
    {
        // Within a minute only the ratio matters: an identical message is worth two variants.
        private const int ExactScore = 4;
        private const int ShiftedExactScore = 3;
        private const int VariantScore = 2;
        private const int ShiftedVariantScore = 1;

        /// <summary>
        /// Pairs incoming messages with stored messages from the same conversation and minute. Both
        /// lists must be in conversation order. This is a sequence alignment: pairs never cross, and the
        /// pairing chosen maximises the score, where an identical message scores twice as much as the
        /// same sender's message in a different state (media, edit or delete). So where identical messages
        /// repeat within a minute, each is paired with the copy in the same position relative to its
        /// neighbours, even if one of them was later deleted or edited.
        /// </summary>
        /// <param name="incoming">Incoming messages as (sender key, content); a null sender is a system message.</param>
        /// <param name="stored">Stored messages in the same form.</param>
        /// <returns>A map from incoming index to stored index. Unmapped incoming messages are new.</returns>
        public static Dictionary<int, int> MatchMinute(
            IReadOnlyList<(int? Sender, MessageContent Content)> incoming,
            IReadOnlyList<(int? Sender, MessageContent Content)> stored)
        {
            return Match(
                incoming.Select(x => (x.Sender, x.Content, default(DateTime))).ToList(),
                stored.Select(x => (x.Sender, x.Content, default(DateTime))).ToList());
        }

        /// <summary>
        /// Pairs incoming messages with stored messages over a run of adjacent minutes, allowing a
        /// pair a minute apart, because some WhatsApp versions round times to the nearest minute where
        /// others drop the seconds. Both lists must be in time order, and in conversation order within
        /// a minute. Rounding never reorders messages, so pairs still never cross.
        /// </summary>
        /// <remarks>
        /// Scores, highest first: identical in the same minute, identical a minute apart, a variant in
        /// the same minute, a variant a minute apart. So a message a minute away from its identical
        /// copy pairs with that copy rather than with a mere variant (such as a deleted placeholder) in
        /// its own minute, and a message with an identical copy in its own minute never pairs across.
        /// Within one minute this is <see cref="MatchMinute"/>.
        /// </remarks>
        /// <param name="incoming">Incoming messages as (sender key, content, minute).</param>
        /// <param name="stored">Stored messages in the same form.</param>
        /// <returns>A map from incoming index to stored index. Unmapped incoming messages are new.</returns>
        public static Dictionary<int, int> Match(
            IReadOnlyList<(int? Sender, MessageContent Content, DateTime Minute)> incoming,
            IReadOnlyList<(int? Sender, MessageContent Content, DateTime Minute)> stored)
        {
            int n = incoming.Count, m = stored.Count;

            int Score(int i, int j)
            {
                var (sender, content, minute) = incoming[i];
                var other = stored[j];
                if (sender != other.Sender)
                {
                    return 0;
                }
                var gap = Math.Abs((minute - other.Minute).Ticks);
                if (gap > TimeSpan.TicksPerMinute)
                {
                    return 0;
                }
                var sameMinute = gap == 0;
                if (content == other.Content)
                {
                    return sameMinute ? ExactScore : ShiftedExactScore;
                }
                if (AreVariants(content, other.Content))
                {
                    return sameMinute ? VariantScore : ShiftedVariantScore;
                }
                return 0;
            }

            // Incoming i can pair only with stored [low[i], high[i]): those within a minute of it. Both
            // lists are in time order, so the bounds never decrease, and the table is a band.
            var low = new int[n];
            var high = new int[n];
            for (int i = 0, lo = 0, hi = 0; i < n; i++)
            {
                var minute = incoming[i].Minute;
                while (lo < m && minute - stored[lo].Minute > TimeSpan.FromMinutes(1))
                {
                    lo++;
                }
                hi = Math.Max(hi, lo);
                while (hi < m && stored[hi].Minute - minute <= TimeSpan.FromMinutes(1))
                {
                    hi++;
                }
                low[i] = lo;
                high[i] = hi;
            }

            // best(i, j) is the highest score for aligning incoming[i..] with stored[j..]. Row i keeps
            // j in [low[i], high[i] + 1] (capped at m). Outside that it equals a kept value: for
            // j < low[i] no stored message before low[i] can pair with incoming[i..], so it's
            // best(i, low[i]); for j > high[i] incoming i can't pair, so it's best(i + 1, j).
            var rows = new int[n][];
            int Best(int i, int j)
            {
                for (; i < n; i++)
                {
                    if (j >= m)
                    {
                        return 0;
                    }
                    j = Math.Max(j, low[i]);
                    if (j <= Math.Min(high[i] + 1, m))
                    {
                        return rows[i][j - low[i]];
                    }
                }
                return 0;
            }

            for (int i = n - 1; i >= 0; i--)
            {
                var end = Math.Min(high[i] + 1, m);
                var row = rows[i] = new int[end - low[i] + 1];
                for (int j = end; j >= low[i]; j--)
                {
                    if (j == m)
                    {
                        row[j - low[i]] = 0;
                        continue;
                    }
                    var score = j < high[i] ? Score(i, j) : 0;
                    var skipStored = j + 1 <= end ? row[j + 1 - low[i]] : Best(i + 1, j + 1);
                    row[j - low[i]] = Math.Max(
                        score > 0 ? score + Best(i + 1, j + 1) : 0,
                        Math.Max(Best(i + 1, j), skipStored));
                }
            }

            var matches = new Dictionary<int, int>();
            for (int i = 0, j = 0; i < n && j < m;)
            {
                if (j < low[i])
                {
                    j = low[i];
                    continue;
                }
                var score = j < high[i] ? Score(i, j) : 0;
                if (score > 0 && Best(i, j) == score + Best(i + 1, j + 1))
                {
                    matches[i] = j;
                    i++;
                    j++;
                }
                else if (Best(i + 1, j) >= Best(i, j + 1))
                {
                    i++;
                }
                else
                {
                    j++;
                }
            }
            return matches;
        }

        /// <summary>
        /// True if two different contents could be the same message exported at different times or
        /// with different options.
        /// </summary>
        public static bool AreVariants(MessageContent a, MessageContent b)
        {
            if (a.Kind == MessageKind.Deleted || b.Kind == MessageKind.Deleted)
            {
                return a.Kind != MessageKind.System && b.Kind != MessageKind.System;
            }
            if (a.Kind == MessageKind.Media && b.Kind == MessageKind.Media)
            {
                return a.AttachmentName == null || b.AttachmentName == null || a.AttachmentName == b.AttachmentName;
            }
            if (a.Kind == MessageKind.Text && b.Kind == MessageKind.Text)
            {
                return a.IsEdited || b.IsEdited;
            }
            return false;
        }

        /// <summary>
        /// Combines a stored message's content with an incoming variant of it, keeping the most
        /// complete version: real content beats a deleted placeholder, an attachment name beats an
        /// omitted one, and an edited version beats an unedited one or an older edit.
        /// </summary>
        /// <param name="stored">The stored content.</param>
        /// <param name="storedAsOf">The last timestamp of the export the stored content came from.</param>
        /// <param name="incoming">The incoming content.</param>
        /// <param name="incomingAsOf">The last timestamp of the export the incoming content came from.</param>
        /// <returns>The merged content, and whether the incoming export supplied the text.</returns>
        public static (MessageContent Content, bool TookIncomingText) Merge(
            MessageContent stored, DateTime storedAsOf,
            MessageContent incoming, DateTime incomingAsOf)
        {
            if (stored == incoming || incoming.Kind == MessageKind.Deleted)
            {
                return (stored, false);
            }
            if (stored.Kind == MessageKind.Deleted)
            {
                return (incoming, true);
            }

            if (stored.Kind == MessageKind.Media && incoming.Kind == MessageKind.Media)
            {
                // Exports without media drop the caption too, so a non-empty caption is better.
                var takeText = incoming.Text.Length > 0 && (stored.Text.Length == 0 || IsNewerEdit(stored, storedAsOf, incoming, incomingAsOf));
                return (new MessageContent(
                    MessageKind.Media,
                    takeText ? incoming.Text : stored.Text,
                    takeText ? incoming.IsEdited : stored.IsEdited,
                    stored.AttachmentName ?? incoming.AttachmentName), takeText);
            }

            if (IsNewerEdit(stored, storedAsOf, incoming, incomingAsOf))
            {
                return (incoming, true);
            }
            return (stored, false);
        }

        private static bool IsNewerEdit(MessageContent stored, DateTime storedAsOf, MessageContent incoming, DateTime incomingAsOf)
        {
            return incoming.IsEdited && (!stored.IsEdited || incomingAsOf > storedAsOf);
        }

        /// <summary>
        /// Merges the stored order of a minute's messages with the order in an incoming export. The
        /// export's order wins for messages it contains; a stored message it lacks stays directly after
        /// the stored message it followed.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="stored">Stored messages in their current order.</param>
        /// <param name="incoming">Messages in export order: matched stored messages and new ones.</param>
        public static List<T> MergeOrder<T>(IReadOnlyList<T> stored, IReadOnlyList<T> incoming)
            where T : class
        {
            var storedIndex = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
            for (int j = 0; j < stored.Count; j++)
            {
                storedIndex[stored[j]] = j;
            }
            var inIncoming = new HashSet<T>(incoming, ReferenceEqualityComparer.Instance);

            var result = new List<T>(stored.Count + incoming.Count);
            int nextStored = 0;

            // Emits stored-only messages up to (excluding) index end.
            void EmitStoredOnly(int end)
            {
                for (; nextStored < end; nextStored++)
                {
                    if (!inIncoming.Contains(stored[nextStored]))
                    {
                        result.Add(stored[nextStored]);
                    }
                }
            }

            foreach (var item in incoming)
            {
                if (!storedIndex.TryGetValue(item, out var j))
                {
                    result.Add(item);
                    continue;
                }
                EmitStoredOnly(j);
                result.Add(item);
                nextStored = Math.Max(nextStored, j + 1);

                // Keep stored-only messages that followed this one directly after it.
                while (nextStored < stored.Count && !inIncoming.Contains(stored[nextStored]))
                {
                    result.Add(stored[nextStored]);
                    nextStored += 1;
                }
            }
            EmitStoredOnly(stored.Count);
            return result;
        }
    }
}
