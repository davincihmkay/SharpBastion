namespace SharpBastion.Views;

public enum ProposalStatus
{
    Queued,
    Skipped,
    Rejected
}

public class ProposalOutcomeView
{
    public readonly string Path;
    public readonly ProposalStatus Status;
    public readonly string? OriginalContent;
    public readonly string? ProposedContent;
    public readonly string? LanguageId;

    public ProposalOutcomeView(
        string path,
        ProposalStatus status,
        string? originalContent,
        string? proposedContent,
        string? languageId)
    {
        Path = path;
        Status = status;
        OriginalContent = originalContent;
        ProposedContent = proposedContent;
        LanguageId = languageId;
    }

    /// <summary>
    /// Status-tag header line — plain text/facts only, deliberately not
    /// colorized here. Coloring the diff itself (Queued only) is the CLI's
    /// job (Program.cs, via IConsoleHighlighter), using OriginalContent /
    /// ProposedContent / LanguageId — this view only carries the ingredients.
    /// </summary>
    public string HeaderLine => Status switch
    {
        ProposalStatus.Queued => $"[FILE WRITE QUEUED] {Path}",
        ProposalStatus.Rejected => $"[FILE WRITE REJECTED] {Path} — resolves outside the repository root.",
        _ => $"[FILE WRITE SKIPPED] {Path} — disallowed file type."
    };

    public string? FooterLine => Status == ProposalStatus.Queued
        ? "(queued for review — run 'review' to approve or reject)"
        : null;
}