using System.Text.RegularExpressions;
using SharpBastion.ValueObjects;

namespace SharpBastion.Helper;

public static class KagiSearchProposalParser
{
    private static readonly Regex TagPattern = new(
        @"<kagi_search\s+query=""(?<query>[^""]+)""\s*/?\s*>(\s*</kagi_search>)?",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> GetProposedQueries(Message llmResponse) =>
        TagPattern.Matches(llmResponse.Value)
            .Select(m => m.Groups["query"].Value)
            .ToList();

    public static string ReplaceKagiSearchBlocks(string text, Func<string, string> buildReplacement) =>
        TagPattern.Replace(text, match => buildReplacement(match.Groups["query"].Value));
}