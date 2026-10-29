using Narrative.App.Data;
using Narrative.App.Queries;
using Narrative.Tests.Synthetic;

namespace Narrative.Tests
{
    public class NarrativeQueriesTests : IDisposable
    {
        private readonly TempDirectory temp = new();
        private readonly string dbPath;
        private readonly SyntheticChat bob;

        public NarrativeQueriesTests()
        {
            dbPath = TestDatabase.Create(temp);
            bob = SyntheticChat.Generate(seed: 20, messageCount: 300, name: "Bob Sample");
            var group = SyntheticChat.Generate(seed: 21, messageCount: 100, name: "Walking Group",
                participants: new[] { "Alice Example", "Carol Test", "Dan Placeholder" });
            TestDatabase.Import(dbPath, new[]
            {
                bob.Write(temp.Path, new ExportSpec(0, 300)),
                group.Write(temp.Path, new ExportSpec(0, 100)),
            });
        }

        public void Dispose()
        {
            temp.Dispose();
        }

        [Fact]
        public void ListsAllChats()
        {
            using var db = NarrativeDatabase.Open(dbPath);

            var chats = new NarrativeQueries(db).ListChats();

            Assert.Equal(new[] { "Bob Sample", "Walking Group" }, chats.Select(c => c.Name));
            Assert.Equal(300, chats[0].MessageCount);
            Assert.Equal(bob.Messages[0].Timestamp, chats[0].First);
            Assert.Equal(bob.Messages[^1].Timestamp, chats[0].Last);
        }

        [Fact]
        public void ListsChatsForAPerson()
        {
            using var db = NarrativeDatabase.Open(dbPath);
            var queries = new NarrativeQueries(db);

            Assert.Equal(new[] { "Bob Sample", "Walking Group" }, queries.ListChats("Alice Example").Select(c => c.Name));
            Assert.Equal(new[] { "Walking Group" }, queries.ListChats("Carol Test").Select(c => c.Name));
            Assert.Empty(queries.ListChats("Nobody"));
        }

        [Fact]
        public void ListsPeople()
        {
            using var db = NarrativeDatabase.Open(dbPath);
            var queries = new NarrativeQueries(db);

            Assert.Equal(new[] { "Alice Example", "Bob Sample", "Carol Test", "Dan Placeholder" }, queries.ListPeople());
            Assert.Equal(new[] { "Alice Example", "Bob Sample" }, queries.ListPeople("Bob Sample"));
        }

        [Fact]
        public void ReproducesAConversationBetweenPeopleInATimeRange()
        {
            using var db = NarrativeDatabase.Open(dbPath);
            var from = bob.Messages[100].Timestamp;
            var to = bob.Messages[200].Timestamp;

            var messages = new NarrativeQueries(db).GetConversation("Bob Sample", new[] { "Alice Example", "Bob Sample" }, from, to);

            var expected = bob.Messages
                .Where(m => !m.IsSystem && m.Timestamp >= from && m.Timestamp < to)
                .Select(m => (m.Timestamp, m.Sender))
                .ToList();
            Assert.Equal(expected, messages.Select(m => (m.Timestamp, m.Sender?.DisplayName)).ToList());
        }

        [Fact]
        public void ConversationCanBeLimitedToOnePerson()
        {
            using var db = NarrativeDatabase.Open(dbPath);

            var messages = new NarrativeQueries(db).GetConversation("Bob Sample", new[] { "Bob Sample" }, DateTime.MinValue, DateTime.MaxValue);

            Assert.NotEmpty(messages);
            Assert.All(messages, m => Assert.Equal("Bob Sample", m.Sender?.DisplayName));
        }
    }
}
