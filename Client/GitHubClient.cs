using System.Net.Http.Headers;
using System.Net.Http.Json;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;
using SharpBastion.ValueObjects;

namespace SharpBastion.Client;

public class GitHubClient : IGitHubClient
{
    private const string TokenEnvVar = "GITHUB_TOKEN";
    private const int PageSize = 100;
    private const int MaxPages = 10;
    private static readonly Uri BaseUrl = new("https://api.github.com/");

    private readonly HttpClient _httpClient;

    public GitHubClient(IHttpClientFactory clientFactory)
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException(
                $"'{TokenEnvVar}' is not set — SharpBastion cannot authenticate against the GitHub API.\n" +
                $"Create a fine-grained token scoped to the target repository and set the '{TokenEnvVar}' " +
                "environment variable (per-shell or per-process — never committed to config).");

        _httpClient = clientFactory.CreateClient();
        _httpClient.BaseAddress = BaseUrl;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SharpBastion", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Task<GitHubUserResponseObject> GetAuthenticatedUserAsync() =>
        GetAsync<GitHubUserResponseObject>("user", "authenticated user lookup");

    public Task<GitHubPullRequestResponseObject> GetPullRequestAsync(PullRequestReference pullRequest) =>
        GetAsync<GitHubPullRequestResponseObject>(PullPath(pullRequest), "pull request lookup");

    public Task<IReadOnlyList<GitHubReviewCommentResponseObject>> ListReviewCommentsAsync(PullRequestReference pullRequest) =>
        GetPagedAsync<GitHubReviewCommentResponseObject>($"{PullPath(pullRequest)}/comments", "review comment listing");

    public Task<IReadOnlyList<GitHubIssueCommentResponseObject>> ListIssueCommentsAsync(PullRequestReference pullRequest) =>
        GetPagedAsync<GitHubIssueCommentResponseObject>($"{RepoPath(pullRequest)}/issues/{pullRequest.Number}/comments", "issue comment listing");

    public Task<GitHubFileContentResponseObject> GetFileContentAsync(
        PullRequestReference pullRequest, string repositoryRelativePath, string commitSha)
    {
        var segments = repositoryRelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s == ".." || s == "."))
            throw new ArgumentException($"Invalid repository-relative path: '{repositoryRelativePath}'.");

        if (string.IsNullOrWhiteSpace(commitSha))
            throw new ArgumentException("Commit SHA cannot be empty.", nameof(commitSha));

        var escapedPath = string.Join('/', segments.Select(Uri.EscapeDataString));
        var url = $"{RepoPath(pullRequest)}/contents/{escapedPath}?ref={Uri.EscapeDataString(commitSha)}";

        return GetAsync<GitHubFileContentResponseObject>(url, $"file content fetch for '{repositoryRelativePath}'");
    }

    public async Task<GitHubReviewCommentResponseObject> ReplyToReviewCommentAsync(
        PullRequestReference pullRequest, long reviewCommentId, string body)
    {
        RequireBody(body);
        return await PostAsync<GitHubReviewCommentResponseObject>(
            $"{PullPath(pullRequest)}/comments/{reviewCommentId}/replies",
            new GitHubReplyRequestObject(body),
            "review comment reply");
    }

    public async Task<GitHubIssueCommentResponseObject> PostIssueCommentAsync(
        PullRequestReference pullRequest, string body)
    {
        RequireBody(body);
        return await PostAsync<GitHubIssueCommentResponseObject>(
            $"{RepoPath(pullRequest)}/issues/{pullRequest.Number}/comments",
            new GitHubReplyRequestObject(body),
            "issue comment");
    }

    // PullRequestReference has already validated owner/repo against GitHub's
    // allowed character set, so they cannot inject path separators here.
    private static string RepoPath(PullRequestReference pr) => $"repos/{pr.Owner}/{pr.Repository}";

    private static string PullPath(PullRequestReference pr) => $"{RepoPath(pr)}/pulls/{pr.Number}";

    private static void RequireBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body cannot be empty.", nameof(body));
    }

    private async Task<T> GetAsync<T>(string url, string operation)
    {
        using var response = await _httpClient.GetAsync(url);
        await EnsureSuccessAsync(response, operation);
        return await ReadAsync<T>(response, operation);
    }

    private async Task<T> PostAsync<T>(string url, object payload, string operation)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, payload);
        await EnsureSuccessAsync(response, operation);
        return await ReadAsync<T>(response, operation);
    }

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(string url, string operation)
    {
        var all = new List<T>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var items = await GetAsync<List<T>>($"{url}?per_page={PageSize}&page={page}", operation);
            all.AddRange(items);
            if (items.Count < PageSize) break;
        }
        return all;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string operation)
    {
        var result = await response.Content.ReadFromJsonAsync<T>();
        if (result is null)
            throw new ArgumentException($"No response body from GitHub for {operation}.");
        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;

        var message = await TryReadErrorMessageAsync(response);
        throw new HttpRequestException(
            $"GitHub {operation} failed with status {(int)response.StatusCode} {response.StatusCode}" +
            (message is null ? "." : $": {message}"),
            null,
            response.StatusCode);
    }

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<GitHubErrorResponseObject>();
            return error?.Message;
        }
        catch
        {
            return null;
        }
    }
}