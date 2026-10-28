using System.Text;

namespace SharpBastion.Helper;

/// <summary>
/// Produces a unified-diff-style rendering of two text blobs, line by line.
/// Presentation-only — used by the CLI review flow so full proposed file
/// content is never dumped verbatim to the console, and by the pass report
/// through <see cref="Compare"/>, which returns the same diff as structured data.
/// </summary>
public static class DiffFormatter
{
    private const int ContextLines = 3;

    public static string BuildUnifiedDiff(string originalContent, string proposedContent)
    {
        var oldLines = SplitLines(originalContent);
        var newLines = SplitLines(proposedContent);

        var ops = Diff(oldLines, newLines);
        var hunks = GroupIntoHunks(ops);

        if (hunks.Count == 0)
            return "(no changes)";

        return string.Join(Environment.NewLine, hunks.Select(FormatHunk));
    }

    /// <summary>
    /// The same diff as <see cref="BuildUnifiedDiff"/>, as data: hunks of lines
    /// with 1-based line numbers, plus total added and removed line counts.
    /// </summary>
    public static DiffResult Compare(string originalContent, string proposedContent)
    {
        var oldLines = SplitLines(originalContent);
        var newLines = SplitLines(proposedContent);

        var ops = Diff(oldLines, newLines);
        var hunks = GroupIntoHunks(ops);

        var added = ops.Count(o => o.Kind == OpKind.Insert);
        var removed = ops.Count(o => o.Kind == OpKind.Delete);

        var views = hunks
            .Select(h => new DiffHunk(
                h.OldStart,
                h.OldCount,
                h.NewStart,
                h.NewCount,
                h.Ops.Select(ToDiffLine).ToList()))
            .ToList();

        return new DiffResult(views, added, removed);
    }

    private static DiffLine ToDiffLine(Op op) =>
        new(
            op.Kind switch
            {
                OpKind.Insert => DiffLineKind.Added,
                OpKind.Delete => DiffLineKind.Removed,
                _ => DiffLineKind.Context
            },
            op.Line,
            op.OldIndex >= 0 ? op.OldIndex + 1 : (int?)null,
            op.NewIndex >= 0 ? op.NewIndex + 1 : (int?)null);

    private static List<string> SplitLines(string content) =>
        string.IsNullOrEmpty(content)
            ? new List<string>()
            : content.Replace("\r\n", "\n").Split('\n').ToList();

    private enum OpKind { Equal, Delete, Insert }

    private sealed record Op(OpKind Kind, string Line, int OldIndex, int NewIndex);

    /// <summary>
    /// Classic LCS-table diff. Quadratic in line count — fine for the
    /// source-file sizes this tool operates on (see File.MaxBytesPerFile).
    /// </summary>
    private static List<Op> Diff(List<string> oldLines, List<string> newLines)
    {
        var n = oldLines.Count;
        var m = newLines.Count;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            lcs[i, j] = oldLines[i] == newLines[j]
                ? lcs[i + 1, j + 1] + 1
                : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var ops = new List<Op>();
        var a = 0;
        var b = 0;
        while (a < n && b < m)
        {
            if (oldLines[a] == newLines[b])
            {
                ops.Add(new Op(OpKind.Equal, oldLines[a], a, b));
                a++; b++;
            }
            else if (lcs[a + 1, b] >= lcs[a, b + 1])
            {
                ops.Add(new Op(OpKind.Delete, oldLines[a], a, -1));
                a++;
            }
            else
            {
                ops.Add(new Op(OpKind.Insert, newLines[b], -1, b));
                b++;
            }
        }

        while (a < n) { ops.Add(new Op(OpKind.Delete, oldLines[a], a, -1)); a++; }
        while (b < m) { ops.Add(new Op(OpKind.Insert, newLines[b], -1, b)); b++; }

        return ops;
    }

    private sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount, List<Op> Ops);

    private static List<Hunk> GroupIntoHunks(List<Op> ops)
    {
        var changeIndexes = ops
            .Select((op, idx) => (op, idx))
            .Where(x => x.op.Kind != OpKind.Equal)
            .Select(x => x.idx)
            .ToList();

        if (changeIndexes.Count == 0) return new List<Hunk>();

        var ranges = new List<(int start, int end)>();
        var rangeStart = Math.Max(0, changeIndexes[0] - ContextLines);
        var rangeEnd = Math.Min(ops.Count - 1, changeIndexes[0] + ContextLines);

        foreach (var idx in changeIndexes.Skip(1))
        {
            var lo = Math.Max(0, idx - ContextLines);
            var hi = Math.Min(ops.Count - 1, idx + ContextLines);

            if (lo <= rangeEnd + 1)
            {
                rangeEnd = Math.Max(rangeEnd, hi);
            }
            else
            {
                ranges.Add((rangeStart, rangeEnd));
                rangeStart = lo;
                rangeEnd = hi;
            }
        }
        ranges.Add((rangeStart, rangeEnd));

        var hunks = new List<Hunk>();
        foreach (var (start, end) in ranges)
        {
            var slice = ops.GetRange(start, end - start + 1);
            var oldNums = slice.Where(o => o.OldIndex >= 0).Select(o => o.OldIndex).ToList();
            var newNums = slice.Where(o => o.NewIndex >= 0).Select(o => o.NewIndex).ToList();

            var oldStart = oldNums.Count > 0 ? oldNums.Min() + 1 : 0;
            var newStart = newNums.Count > 0 ? newNums.Min() + 1 : 0;

            hunks.Add(new Hunk(oldStart, oldNums.Count, newStart, newNums.Count, slice));
        }

        return hunks;
    }

    private static string FormatHunk(Hunk hunk)
    {
        var sb = new StringBuilder();
        sb.Append($"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");

        foreach (var op in hunk.Ops)
        {
            sb.Append(Environment.NewLine);
            sb.Append(op.Kind switch
            {
                OpKind.Equal => "  " + op.Line,
                OpKind.Delete => "- " + op.Line,
                OpKind.Insert => "+ " + op.Line,
                _ => op.Line
            });
        }

        return sb.ToString();
    }
}

public enum DiffLineKind
{
    Context,
    Removed,
    Added
}

/// <summary>One line of a diff. Numbers are 1-based; null on the side where the line does not exist.</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber);

public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines);

/// <summary>Hunks with context, plus totals over the whole diff.</summary>
public sealed record DiffResult(IReadOnlyList<DiffHunk> Hunks, int Added, int Removed);