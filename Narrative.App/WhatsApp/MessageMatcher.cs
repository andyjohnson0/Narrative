using Narrative.App.Data;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Decides which incoming messages are copies of stored ones, and how to combine their content.
    /// All methods are pure so the rules can be tested on their own.
    /// </summary>
    public static class MessageMatcher
    {
        private const int ExactScore = 2;
        private const int VariantScore = 1;

        /// <summary>
        /// Pairs incoming messages with stored messages from the same conversation and minute. Both
        /// lists must be in conversation order. This is a sequence alignment: pairs never cross, and the
        /// pairing chosen maximises the score, where an identical message scores 2 and the same sender's
        /// message in a different state (media, edit or delete) scores 1. So where identical messages
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
            int n = incoming.Count, m = stored.Count;

            int Score(int i, int j)
            {
                if (incoming[i] == stored[j])
                {
                    return ExactScore;
                }
                if (incoming[i].Sender == stored[j].Sender && AreVariants(incoming[i].Content, stored[j].Content))
                {
                    return VariantScore;
                }
                return 0;
            }

            // best[i, j] is the highest score for aligning incoming[i..] with stored[j..].
            var best = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    var score = Score(i, j);
                    best[i, j] = Math.Max(
                        score > 0 ? score + best[i + 1, j + 1] : 0,
                        Math.Max(best[i + 1, j], best[i, j + 1]));
                }
            }

            var matches = new Dictionary<int, int>();
            for (int i = 0, j = 0; i < n && j < m;)
            {
                var score = Score(i, j);
                if (score > 0 && best[i, j] == score + best[i + 1, j + 1])
                {
                    matches[i] = j;
                    i++;
                    j++;
                }
                else if (best[i + 1, j] >= best[i, j + 1])
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
