using SharpBastion.Interface;

namespace SharpBastion.Protocol;

/// <summary>
/// Consent gate for skipping the per-commit confirmation. Defaults to not
/// overridden: every pending commit asks for 'y' in 'review' before the
/// system stages, commits, pushes and replies on the pull request thread.
/// Once overridden, that one prompt is skipped for the rest of the session.
///
/// What the override does NOT change: file writes are never covered — they
/// always require approval via PendingWriteQueue, and that approval is the
/// only point where a human reads the diff. A sync still only queues; a
/// commit still only happens inside an interactive 'review' (including the
/// review step of 'pr sync --all'). All commit-time checks still apply.
/// </summary>
public class GitHubProtocol : IGitHubProtocol
{
    public bool IsOverridden { get; private set; } = false;

    public void OverrideProtocol() => IsOverridden = true;

    public string Subject => "github restriction and skip the per-commit confirmation";

    public string Description =>
        "Approved file changes can be committed, pushed and replied to on GitHub without a per-commit confirmation.\n" +
        "File writes still always require approval.\n" +
        "The approved content and reply text leave this machine to GitHub as soon as a write is approved.";

    public string OverriddenMessage =>
        "GitHub restriction: OVERRIDDEN. Commits, pushes and replies run without a per-commit prompt (writes still need approval).";

    public string ActiveMessage =>
        "GitHub restriction: ACTIVE. Every commit, push and reply asks for confirmation in 'review'.";
}