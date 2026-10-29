using System.Globalization;
using System.Text.RegularExpressions;

namespace Narrative.App.WhatsApp
{
    /// <summary>
    /// Android with UK locale: <c>dd/MM/yyyy, HH:mm - rest</c>.
    /// </summary>
    public partial class AndroidUkHeaderFormat : IHeaderFormat
    {
        public static readonly AndroidUkHeaderFormat Instance = new();

        [GeneratedRegex(@"^(?<date>\d{2}/\d{2}/\d{4}), (?<time>\d{2}:\d{2}) - (?<rest>.*)$")]
        private static partial Regex HeaderRegex();

        public bool TryParse(string line, out DateTime timestamp, out string rest)
        {
            timestamp = default;
            rest = "";

            var match = HeaderRegex().Match(line);
            if (!match.Success)
            {
                return false;
            }
            if (!DateTime.TryParseExact(
                    match.Groups["date"].Value + " " + match.Groups["time"].Value,
                    "dd/MM/yyyy HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out timestamp))
            {
                return false;
            }
            rest = match.Groups["rest"].Value;
            return true;
        }
    }
}
