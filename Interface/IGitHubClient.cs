using SharpBastion.ClientResponseObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Interface;

public interface IGitHubClient
{
    /// <summary>The user the configured token authenticates as.</summary>
    Task<GitHubUserResponseObject> GetAuthenticatedUserAsync();

    Task<GitHubPullRequestResponseObject> GetPullRequestAsync(PullRequestReference pullRequest);

    /// <summary>Inline (diff-anchored) review comments, all pages up to a fixed cap.</summary>
    Task<IReadOnlyList<GitHubReviewCommentResponseObject>> ListReviewCommentsAsync(PullRequestReference pullRequest);

    /// <summary>Top-level conversation comments on the PR, all pages up to a fixed cap.</summary>
    Task<IReadOnlyList<GitHubIssueCommentResponseObject>> ListIssueCommentsAsync(PullRequestReference pullRequest);

    /// <summary>File content at an exact commit. Path is repository-relative.</summary>
    Task<GitHubFileContentResponseObject> GetFileContentAsync(
        PullRequestReference pullRequest, string repositoryRelativePath, string commitSha);

    Task<GitHubReviewCommentResponseObject> ReplyToReviewCommentAsync(
        PullRequestReference pullRequest, long reviewCommentId, string body);

    Task<GitHubIssueCommentResponseObject> PostIssueCommentAsync(
        PullRequestReference pullRequest, string body);
}