namespace SharpBastion.Interface;

public interface IGitCliClient
{
    /// <summary>Current branch name, or "HEAD" when detached — callers must refuse the latter.</summary>
    Task<string> GetCurrentBranchAsync(string repositoryDirectory);

    Task<string> GetRemoteUrlAsync(string repositoryDirectory, string remote);

    /// <summary>Resolves a revision to a full commit SHA.</summary>
    Task<string> ResolveCommitAsync(string repositoryDirectory, string revision);

    /// <summary>Repository-relative ('/'-separated) paths with any staged, unstaged or untracked change.</summary>
    Task<IReadOnlyList<string>> GetChangedPathsAsync(string repositoryDirectory);

    Task FetchBranchAsync(string repositoryDirectory, string remote, string branch);

    /// <summary>True when 'ancestor' is reachable from 'descendant' (fast-forward is possible).</summary>
    Task<bool> IsAncestorAsync(string repositoryDirectory, string ancestorRevision, string descendantRevision);

    /// <summary>Stages exactly the given absolute paths; each must resolve inside the repository.</summary>
    Task StageAsync(string repositoryDirectory, IReadOnlyCollection<string> absolutePaths);

    /// <summary>Commits the staged changes and returns the new HEAD SHA. Hooks run normally.</summary>
    Task<string> CommitAsync(string repositoryDirectory, string message);

    /// <summary>Pushes HEAD to refs/heads/{branch}. Never forced; a non-fast-forward is an error.</summary>
    Task PushAsync(string repositoryDirectory, string remote, string branch);
}