using Narrative.App.Mail;

namespace Narrative.Tests
{
    public class QuoteTrimmerTests
    {
        [Theory]
        // The quote and its attribution go, with blank lines among and after them.
        [InlineData("Yes\n\nOn Wed, Sam wrote:\n> Lunch?\n>\n> Or dinner?\n\n", "Yes")]
        [InlineData("Yes\nOn Wed, Sam wrote:\n> Lunch?", "Yes")]
        [InlineData("Yes\n\n> Lunch?\n\n>> Older\n>>\n> More", "Yes")]
        [InlineData("Yes\n\nSam writes:\n> Lunch?", "Yes")]
        [InlineData("Yes\n\nOn Wed, 1 Jul 2021 at 10:00, SAM TAYLOR WROTE:\n> Lunch?", "Yes")]
        // A wrapped attribution goes whole.
        [InlineData("Yes\n\nOn Wed, 1 Jul 2021 at 10:00, Sam Taylor <\nsam@example.org> wrote:\n\n> Lunch?", "Yes")]
        // Without an attribution, only the quote goes.
        [InlineData("Yes\n\nSam said\n> Lunch?", "Yes\n\nSam said")]
        // A signature above the quote stays.
        [InlineData("Yes\n\n--\nAlex\n\nOn Wed, Sam wrote:\n> Lunch?", "Yes\n\n--\nAlex")]
        // Quotes with the sender's text after them stay: inline and bottom-posted replies.
        [InlineData("> Lunch?\nYes\n> Where?\nThe usual", "> Lunch?\nYes\n> Where?\nThe usual")]
        [InlineData("On Wed, Sam wrote:\n> Lunch?\n\nYes", "On Wed, Sam wrote:\n> Lunch?\n\nYes")]
        // Inline replies lose only the quote at the end.
        [InlineData("> Lunch?\nYes\n> See you", "> Lunch?\nYes")]
        // Nothing quoted.
        [InlineData("Yes\nthanks", "Yes\nthanks")]
        [InlineData("", "")]
        [InlineData("Price > cost\nok", "Price > cost\nok")]
        public void TrailingQuotesAreRemoved(string body, string expected)
        {
            Assert.Equal(expected, QuoteTrimmer.TrimTrailingQuote(body));
        }

        [Theory]
        [InlineData("Yes\n\nOn Wed, Sam wrote:\n> Lunch?\n>> Older", "Yes\n")]
        [InlineData("> Lunch?\nYes\n> Where?\nThe usual", "Yes\nThe usual")]
        [InlineData("On Wed, Sam wrote:\n> Lunch?\n\nYes", "\nYes")]
        [InlineData("Hi\n\nOn Wed, 1 Jul 2021, Sam <\nsam@example.org> wrote:\n> Lunch?\nYes", "Hi\n\nYes")]
        [InlineData("Sam said\n> Lunch?\nYes", "Sam said\nYes")]
        [InlineData("> All quote", "")]
        [InlineData("No quotes", "No quotes")]
        public void RemoveQuotesTakesEveryQuoteAndItsAttribution(string body, string expected)
        {
            Assert.Equal(expected, QuoteTrimmer.RemoveQuotes(body));
        }

        [Theory]
        [InlineData("> Lunch?\n> Dinner?")]
        [InlineData("On Wed, Sam wrote:\n> Lunch?")]
        [InlineData("\n\n> Lunch?\n")]
        public void ABodyThatIsAllQuoteIsKept(string body)
        {
            Assert.Equal(body, QuoteTrimmer.TrimTrailingQuote(body));
        }
    }
}
