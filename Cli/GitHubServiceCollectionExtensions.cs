using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpBastion.Client;
using SharpBastion.Controller;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.Protocol;
using SharpBastion.Service;

namespace SharpBastion.Cli;

public static class GitHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the pull-request flow needs. Keeps Program.cs to a
    /// single line. GitHubClient is transient and only throws for a missing
    /// GITHUB_TOKEN when something actually resolves it.
    /// </summary>
    public static IServiceCollection AddGitHubPullRequestSupport(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SectionName));

        services.AddTransient<IGitHubClient, GitHubClient>();
        services.AddTransient<IGitCliClient, GitCliClient>();
        services.AddSingleton<IGitHubProtocol, GitHubProtocol>();
        services.AddSingleton<PendingCommitQueue>();
        services.AddSingleton<ProcessedReviewCommentLedger>();
        services.AddSingleton<PassJournal>();
        services.AddTransient<IPullRequestSyncService, PullRequestSyncService>();
        services.AddTransient<IPendingCommitExecutionService, PendingCommitExecutionService>();
        services.AddTransient<PullRequestController>();

        // Reads the journal only: resolving these never needs GITHUB_TOKEN.
        services.AddTransient<IPassExportService, PassExportService>();
        services.AddTransient<PassExportController>();

        return services;
    }
}