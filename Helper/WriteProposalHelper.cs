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

    /// <summary>
    /// One ordered chunk of a raw LLM response: either a run of plain prose
    /// (ProposalPath is null, PlainText is the prose) or a marker for a
    /// &lt;file_write path="..."&gt; block found at this position (PlainText is
    /// empty, ProposalPath is the path attribute). Used by
    /// RepositoryIngestorService to build an AskQuestionResultView's Segments.
    /// Replaces the old ReplaceFileWriteBlocks, which concatenated everything
    /// into a single string and so couldn't let the CLI colorize just the
    /// diff portions independently of the surrounding prose.
    /// </summary>
    public readonly record struct MessageSegment(string PlainText, string? ProposalPath);

    public static IReadOnlyList<MessageSegment> SplitSegments(Message llmResponse)
    {
        var text = llmResponse.Value;
        var segments = new List<MessageSegment>();
        var pos = 0;

        while (true)
        {
            var openMatch = OpenTagPattern.Match(text, pos);
            if (!openMatch.Success)
            {
                if (pos < text.Length)
                    segments.Add(new MessageSegment(text[pos..], null));
                break;
            }

            var contentStart = openMatch.Index + openMatch.Length;
            var closePos = FindMatchingCloseTag(text, contentStart);
            if (closePos == -1)
            {
                // Unterminated block — keep the rest of the raw text rather than
                // silently truncating the displayed response.
                segments.Add(new MessageSegment(text[pos..], null));
                break;
            }

            if (openMatch.Index > pos)
                segments.Add(new MessageSegment(text[pos..openMatch.Index], null));

            segments.Add(new MessageSegment(string.Empty, openMatch.Groups["path"].Value));

            pos = closePos + CloseTag.Length;
        }

        return segments;
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