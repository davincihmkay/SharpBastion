namespace SharpBastion.Interface;

/// <summary>
/// Renders a colorized, terminal-ready diff for the 'review' command.
/// Implementations must treat coloring as purely cosmetic: on any ambiguity
/// or failure they should degrade to plain text rather than throw — a broken
/// highlighter must never block the write-approval flow.
/// </summary>
public interface IConsoleHighlighter
{
    string HighlightDiff(string originalContent, string proposedContent, string languageId);
}