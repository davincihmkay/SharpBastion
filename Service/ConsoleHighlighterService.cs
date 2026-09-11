using System.Text;
using Smdn.LibHighlightSharp;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.Options;

namespace SharpBastion.Service;

/// <summary>
/// Colorizes review diffs via Smdn.LibHighlightSharp (GeneratorOutputType.EscapeSequencesXterm256).
///
/// Each full file (original and proposed) is highlighted whole, not line-by-line —
/// a lone diff line (e.g. a bare "{") has no syntactic context on its own, so
/// per-line highlighting degrades badly on multi-line constructs (block comments,
/// multi-line strings). The two highlighted line arrays are re-attached to the
/// plain-text diff structure from DiffFormatter.BuildDiffLines by (Old/New)Index,
/// so diff correctness is still computed on ground-truth plain text — only the
/// *display* line is swapped for its highlighted counterpart.
///
/// Any failure here (unknown theme/syntax id, library error, index/parity
/// mismatch) falls back to the plain BuildUnifiedDiff output rather than
/// throwing — highlighting is cosmetic and must never block the
/// write-approval flow.
/// </summary>
public class ConsoleHighlighterService : IConsoleHighlighter
{
    private readonly HighlightProfile _highlightProfile;

    public ConsoleHighlighterService(HighlightProfile highlightProfile)
    {
        _highlightProfile = highlightProfile;
    }

    public string HighlightDiff(string originalContent, string proposedContent, string languageId)
    {
        try
        {
            var oldHighlighted = HighlightWholeFile(originalContent, languageId);
            var newHighlighted = HighlightWholeFile(proposedContent, languageId);

            var diffLines = DiffFormatter.BuildDiffLines(originalContent, proposedContent);
            if (diffLines.Count == 0)
                return "(no changes)";

            var sb = new StringBuilder();
            foreach (var line in diffLines)
            {
                if (sb.Length > 0)
                    sb.Append(Environment.NewLine);

                if (line.Marker == '@')
                {
                    sb.Append(line.Text);
                    continue;
                }

                var displayText = ResolveDisplayLine(line, oldHighlighted, newHighlighted);
                sb.Append(FormatMarkedLine(line.Marker, displayText));
            }

            return sb.ToString();
        }
        catch
        {
            return DiffFormatter.BuildUnifiedDiff(originalContent, proposedContent);
        }
    }

    private static string FormatMarkedLine(char marker, string text)
    {
        const string reset = "\x1b[0m";
        var color = marker switch
        {
            '+' => "\x1b[32m",
            '-' => "\x1b[31m",
            _ => null
        };

        return color is null
            ? $"  {text}"
            : $"{color}{marker}{reset} {text}";
    }

    private static string ResolveDisplayLine(DiffFormatter.DiffLine line, string[] oldLines, string[] newLines)
    {
        if (line.NewIndex is int newIdx && newIdx >= 0 && newIdx < newLines.Length)
            return newLines[newIdx];

        if (line.OldIndex is int oldIdx && oldIdx >= 0 && oldIdx < oldLines.Length)
            return oldLines[oldIdx];

        return line.Text;
    }

    private string[] HighlightWholeFile(string content, string languageId)
    {
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();

        using var hl = new Highlight(GeneratorOutputType.EscapeSequencesXterm256);
        hl.SetTheme(_highlightProfile.Theme);
        hl.SetSyntax(languageId);

        var highlighted = hl.Generate(content);
        return highlighted.Replace("\r\n", "\n").Split('\n');
    }
}