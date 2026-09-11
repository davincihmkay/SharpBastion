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
    public readonly string? Diff;

    public ProposalOutcomeView(string path, ProposalStatus status, string? diff)
    {
        Path = path;
        Status = status;
        Diff = diff;
    }

    public string ToDisplayText() => Status switch
    {
        ProposalStatus.Queued =>
            $"[FILE WRITE QUEUED] {Path}{Environment.NewLine}{Diff}{Environment.NewLine}" +
            "(queued for review — run 'review' to approve or reject)",
        ProposalStatus.Rejected =>
            $"[FILE WRITE REJECTED] {Path} — resolves outside the repository root.",
        _ =>
            $"[FILE WRITE SKIPPED] {Path} — disallowed file type."
    };
}