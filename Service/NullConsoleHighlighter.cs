using SharpBastion.Helper;
using SharpBastion.Interface;

namespace SharpBastion.Service;

/// <summary>
/// No-op highlighter used when coloring isn't capability-appropriate (NO_COLOR,
/// redirected output — see Program.cs's IConsoleHighlighter registration).
/// Delegates to the same plain-text DiffFormatter path 'review' always used
/// before colorization existed, so disabling color is a strict no-op on output.
/// </summary>
public class NullConsoleHighlighter : IConsoleHighlighter
{
    public string HighlightDiff(string originalContent, string proposedContent, string languageId) =>
        DiffFormatter.BuildUnifiedDiff(originalContent, proposedContent);
}