using System.Text;
using System.Text.RegularExpressions;
using SharpBastion.ValueObjects;

namespace SharpBastion.Helper;

public static class WriteProposalParser
{
    private static readonly Regex OpenTagPattern = new(
        @"<file_write\s+path=""(?<path>[^""]+)""\s*>",
        RegexOptions.Compiled);

    private const string CloseTag = "</file_write>";
    private const string OpenTagPrefix = "<file_write";

    public static IReadOnlyList<string> GetProposedPaths(Message llmResponse) =>
        OpenTagPattern.Matches(llmResponse.Value)
            .Select(m => m.Groups["path"].Value)
            .ToList();

    public static FileContent? TryExtractContent(Message llmResponse, string fileName)
    {
        var text = llmResponse.Value;

        var openMatch = OpenTagPattern.Matches(text)
            .FirstOrDefault(m => m.Groups["path"].Value.Equals(fileName, StringComparison.OrdinalIgnoreCase));

        if (openMatch is null) return null;

        var contentStart = openMatch.Index + openMatch.Length;
        var closePos = FindMatchingCloseTag(text, contentStart);
        if (closePos == -1) return null;

        var content = text.Substring(contentStart, closePos - contentStart).Trim();
        return new FileContent(content);
    }

    public static string ReplaceFileWriteBlocks(this Message llmResponse, Func<string, string> buildReplacement)
    {
        var text = llmResponse.Value;
        var sb = new StringBuilder();
        var pos = 0;

        while (true)
        {
            var openMatch = OpenTagPattern.Match(text, pos);
            if (!openMatch.Success)
            {
                sb.Append(text, pos, text.Length - pos);
                break;
            }

            var contentStart = openMatch.Index + openMatch.Length;
            var closePos = FindMatchingCloseTag(text, contentStart);
            if (closePos == -1)
            {
                // Unterminated block — keep the rest of the raw text rather than
                // silently truncating the displayed response.
                sb.Append(text, pos, text.Length - pos);
                break;
            }

            sb.Append(text, pos, openMatch.Index - pos);
            sb.Append(buildReplacement(openMatch.Groups["path"].Value));

            pos = closePos + CloseTag.Length;
        }

        return sb.ToString();
    }

    private static int FindMatchingCloseTag(string text, int searchFrom)
    {
        var depth = 1;
        var pos = searchFrom;

        while (pos < text.Length && depth > 0)
        {
            var nextOpen  = text.IndexOf(OpenTagPrefix, pos, StringComparison.OrdinalIgnoreCase);
            var nextClose = text.IndexOf(CloseTag,      pos, StringComparison.OrdinalIgnoreCase);

            if (nextClose == -1) return -1;

            if (nextOpen != -1 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + OpenTagPrefix.Length;
            }
            else
            {
                depth--;
                if (depth == 0) return nextClose;
                pos = nextClose + CloseTag.Length;
            }
        }

        return -1;
    }
}