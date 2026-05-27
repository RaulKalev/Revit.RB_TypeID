using RB_TypeName.Models;
using System.Text.RegularExpressions;

namespace RB_TypeName.Services
{
    public static class RbrObjectIdParser
    {
        // Matches: ME-DUC-B01-0001  (R-S-LevelCode-0000)
        private static readonly Regex IdPattern = new Regex(
            @"^([A-Z0-9]+)-([A-Z0-9]+)-([A-Z][0-9]{2})-([0-9]{4})$",
            RegexOptions.Compiled);

        public static bool TryParse(string value, out ParsedRbrObjectId parsed)
        {
            parsed = null;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var match = IdPattern.Match(value.Trim());
            if (!match.Success)
                return false;

            parsed = new ParsedRbrObjectId
            {
                DisciplineCode = match.Groups[1].Value,
                ObjectCode     = match.Groups[2].Value,
                LevelCode      = match.Groups[3].Value,
                RunningNumber  = int.Parse(match.Groups[4].Value)
            };

            return true;
        }
    }
}
