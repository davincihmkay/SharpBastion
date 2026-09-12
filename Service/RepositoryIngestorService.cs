using System.Text;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Commands;
using SharpBastion.Constants;
using SharpBastion.Domain;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.PromptBuilder;
using SharpBastion.ValueObjects;
using SharpBastion.Views;
using File = SharpBastion.Domain.File;

namespace SharpBastion.Service;

public class RepositoryIngestorService : IRepositoryIngestorService
{
    private readonly IRepositoryDomainService _repositoryDomainService;
    private readonly IRepomixCliClient _repomixCliClient;
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly IKagiClient _kagiClient;
    private readonly IKagiSearchProtocol _kagiSearchProtocol;
    private readonly PendingKagiSearchQueue _pendingKagiSearchQueue;
    private readonly AssistantProfile _assistantProfile;
    private readonly List<Repository> _cachedRepositories;

    public RepositoryIngestorService(
        IRepositoryDomainService repositoryDomainService,
        IRepomixCliClient repomixCliClient,
        PendingWriteQueue pendingWriteQueue,
        IKagiClient kagiClient,
        IKagiSearchProtocol kagiSearchProtocol,
        PendingKagiSearchQueue pendingKagiSearchQueue,
        AssistantProfile assistantProfile)
    {
        _repositoryDomainService = repositoryDomainService;
        _repomixCliClient = repomixCliClient;
        _pendingWriteQueue = pendingWriteQueue;
        _kagiClient = kagiClient;
        _kagiSearchProtocol = kagiSearchProtocol;
        _pendingKagiSearchQueue = pendingKagiSearchQueue;
        _assistantProfile = assistantProfile;

        // TODO: Caching should be hashed and be saved and retrieved from file
        _cachedRepositories = new List<Repository>();
    }

    public OperationResultView IngestRepositories(IngestRepositoriesCommand command)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Reason)>();

        foreach (var path in command.RepositoryPaths)
        {
            try
            {
                var repository = GetRepository(new LocalPath(path.Value), command.IsBestPractice);
                var messages = RepositoryPromptBuilder.BuildIngestionMessages(repository);
                var responses = _repositoryDomainService.SendMessagesToLmStudio(messages, repository.TempDirectory);
                foreach (var response in responses)
                    repository.AddResponse(response);

                _cachedRepositories.Add(repository);
                succeeded.Add(path.Value);
            }
            catch (Exception ex)
            {
                failed.Add((path.Value, ex.Message));
            }
        }

        var success = failed.Count == 0;
        var message = success
            ? $"Successfully ingested repositories: {string.Join(", ", succeeded)}"
            : $"Ingested {succeeded.Count}/{command.RepositoryPaths.Count} repositories. " +
              $"Failed: {string.Join("; ", failed.Select(f => $"{f.Path} ({f.Reason})"))}";

        return new OperationResultView(success, message);
    }

    public AskQuestionResultView AskQuestion(AskRepositoryQuestionCommand command)
    {
        var repository = _cachedRepositories.SingleOrDefault(x => x.Name == command.RepositoryName.Value);
        if (repository == null)
            return new AskQuestionResultView(false, $"Repository '{command.RepositoryName.Value}' not found. Ingest it first.");

        var lastResponse = repository.GetLastRespone();
        if (lastResponse == null)
            return new AskQuestionResultView(false, $"Repository '{command.RepositoryName.Value}' has no prior response — not ingested most likely.");

        var response = _repositoryDomainService.AskQuestion(command.Question, lastResponse, repository.TempDirectory);
        repository.AddResponse(response);

        var notices = new List<string>();
        var displayMessage = ProcessResponse(response, repository, _assistantProfile.KagiSearchMaxRoundTrips, notices);

        return new AskQuestionResultView(true, displayMessage, notices);
    }

    public AskQuestionResultView ResolveKagiSearch(ResolveKagiSearchCommand command)
    {
        if (!_pendingKagiSearchQueue.TryResolve(command.Id, out var pending) || pending is null)
            return new AskQuestionResultView(false, $"No pending search found for '{command.Id}'. Already resolved or unknown.");

        if (!command.Approve)
            return new AskQuestionResultView(true, $"Rejected: {pending.Query.Value}");

        var repository = _cachedRepositories.SingleOrDefault(x => x.Name == pending.RepositoryName.Value);
        if (repository is null)
            return new AskQuestionResultView(false, $"Repository '{pending.RepositoryName.Value}' is no longer cached — cannot deliver search results.");

        var respondingTo = new Response(pending.RespondingTo, new Message(string.Empty));
        var followUp = ExecuteKagiSearch(pending, respondingTo, repository);
        repository.AddResponse(followUp);

        var notices = new List<string>();
        var displayMessage = ProcessResponse(followUp, repository, _assistantProfile.KagiSearchMaxRoundTrips, notices);

        return new AskQuestionResultView(true, displayMessage, notices);
    }

    private string ProcessResponse(Response response, Repository repository, int kagiAutoChainRemaining, List<string> notices)
    {
        var afterFileWrites = ProcessFileWriteProposals(response, repository, notices);
        return ProcessKagiSearchProposals(response, repository, afterFileWrites, kagiAutoChainRemaining, notices);
    }

    private string ProcessFileWriteProposals(Response response, Repository repository, List<string> notices)
    {
        var proposalOutcomes = new Dictionary<string, ProposalOutcomeView>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: existing files
        foreach (var file in repository.GetAllFiles())
        {
            var proposedContent = WriteProposalParser.TryExtractContent(response.Message, file.Name);
            if (proposedContent is null) continue;

            file.ApplyProposal(proposedContent);
            proposalOutcomes[file.Name] = DescribeOutcome(file.Name, file);

            if (file.HasPendingWrite && !_pendingWriteQueue.Enqueue(file))
                notices.Add($"{file.Name} already pending review.");
        }

        // Pass 2: new files
        foreach (var proposedPath in WriteProposalParser.GetProposedPaths(response.Message))
        {
            if (repository.ContainsFile(proposedPath)) continue;

            var absolutePath = new LocalPath(Path.Combine(repository.Name, proposedPath));

            if (!PathGuard.IsWithinRoot(repository.Name, absolutePath.Value))
            {
                proposalOutcomes[proposedPath] = new ProposalOutcomeView(proposedPath, ProposalStatus.Rejected, null);
                continue;
            }

            var newFile = new File(proposedPath, absolutePath, new FileContent(string.Empty));
            repository.AttachNewFile(newFile);

            var proposedContent = WriteProposalParser.TryExtractContent(response.Message, proposedPath);
            newFile.ApplyProposal(proposedContent);
            proposalOutcomes[proposedPath] = DescribeOutcome(proposedPath, newFile);

            if (newFile.HasPendingWrite && !_pendingWriteQueue.Enqueue(newFile))
                notices.Add($"{newFile.Name} already pending review.");
        }

        return response.Message.ReplaceFileWriteBlocks(path =>
            proposalOutcomes.TryGetValue(path, out var outcome)
                ? outcome.ToDisplayText()
                : $"[FILE WRITE SKIPPED] {path} — not queued.");
    }

    private string ProcessKagiSearchProposals(
        Response response, Repository repository, string textSoFar, int kagiAutoChainRemaining, List<string> notices)
    {
        var queries = KagiSearchProposalParser.GetProposedQueries(response.Message);
        if (queries.Count == 0) return textSoFar;

        var outcomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var queryText in queries)
        {
            var query = new KagiQuery(queryText);
            var pending = new PendingKagiSearch(query, new RepositoryName(repository.Name), response.Id);

            if (_kagiSearchProtocol.IsOverridden && kagiAutoChainRemaining > 0)
            {
                Console.WriteLine($"\n[SEARCH] Executing '{query.Value}' (unattended — kagi-search protocol overridden).");

                var followUp = ExecuteKagiSearch(pending, response, repository);
                repository.AddResponse(followUp);

                var nestedText = ProcessResponse(followUp, repository, kagiAutoChainRemaining - 1, notices);
                outcomes[queryText] = $"[SEARCH EXECUTED] {query.Value}{Environment.NewLine}{nestedText}";
            }
            else
            {
                var reason = _kagiSearchProtocol.IsOverridden
                    ? $"auto-chain cap ({_assistantProfile.KagiSearchMaxRoundTrips}) reached; requires manual 'review'"
                    : "run 'review' to approve or reject";

                if (_pendingKagiSearchQueue.Enqueue(pending))
                    outcomes[queryText] = $"[SEARCH QUEUED] {query.Value} — {reason}.";
                else
                    notices.Add($"{query.Value} already pending review.");
            }
        }

        return KagiSearchProposalParser.ReplaceKagiSearchBlocks(textSoFar, q =>
            outcomes.TryGetValue(q, out var outcome) ? outcome : $"[SEARCH SKIPPED] {q} — not processed.");
    }

    private Response ExecuteKagiSearch(PendingKagiSearch pending, Response respondingTo, Repository repository)
    {
        var searchResponse = _kagiClient
            .SearchAsync(new KagiClientSearchRequestObject(pending.Query.Value))
            .ConfigureAwait(false).GetAwaiter().GetResult();

        var formatted = FormatKagiResults(pending.Query.Value, searchResponse);
        return _repositoryDomainService.AskQuestion(new Question(formatted), respondingTo, repository.TempDirectory);
    }

    private static string FormatKagiResults(string query, KagiClientSearchResponseObject searchResponse)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Kagi search results for query: \"{query}\"");

        foreach (var result in searchResponse.Data?.Search ?? new List<KagiSearchResultItem>())
        {
            sb.AppendLine($"- {result.Title} ({result.Url})");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                sb.AppendLine($"  {result.Snippet}");
        }

        var content = sb.ToString();
        return content.Length > File.MaxBytesPerFile
            ? content[..File.MaxBytesPerFile] + Environment.NewLine + "[TRUNCATED]"
            : content;
    }

    private static ProposalOutcomeView DescribeOutcome(string path, File file)
    {
        var diff = file.GetPendingWriteDiff();
        return diff is not null
            ? new ProposalOutcomeView(path, ProposalStatus.Queued, diff)
            : new ProposalOutcomeView(path, ProposalStatus.Skipped, null);
    }

    private Repository GetRepository(LocalPath repositoryBasePath, bool isBestPractice)
    {
        var metaData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Folder rootFolder;
        if (_repomixCliClient.IsInitialized())
        {
            var clientRequestObject = new RepomixCliClientPackageRepositoryRequestObject(repositoryBasePath.Value);
            var result = _repomixCliClient.PackageRepositoryToXml(clientRequestObject).ConfigureAwait(false).GetAwaiter().GetResult();
            var directoryStructureLines = result.directory_structure.Split(Environment.NewLine).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var i = 0;
            rootFolder = GetFolderFromXml(directoryStructureLines, ref i, -1, string.Empty, result.files.file, repositoryBasePath.Value);
            metaData.Add(RepositoryConstants.XmlFileName, result.XmlFileName);
            metaData.Add(RepositoryConstants.TempDirectory, result.TempDirectory);
        }
        else
        {
            rootFolder = GetFolder(repositoryBasePath, repositoryBasePath.Value);
        }

        return new Repository(repositoryBasePath.Value, rootFolder, metaData, new List<Response>());
    }

    private static Folder GetFolderFromXml(List<string> lines, ref int i, int currentIndent, string fullPath, List<RepoFile> repoFiles, string repositoryBasePath)
    {
        var files = new List<File>();
        var subFolders = new List<Folder>();

        while (i < lines.Count)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart().Length;

            if (indent <= currentIndent)
                break;

            var trimmed = line.TrimStart();
            i++;

            var itemFullPath = string.IsNullOrEmpty(fullPath)
                ? trimmed.TrimEnd('/')
                : $"{fullPath}/{trimmed.TrimEnd('/')}";

            if (trimmed.EndsWith("/"))
            {
                subFolders.Add(GetFolderFromXml(lines, ref i, indent, itemFullPath, repoFiles, repositoryBasePath));
            }
            else
            {
                var repoFile = repoFiles.SingleOrDefault(x => x.path == itemFullPath);
                if (repoFile != null)
                {
                    var absolutePath = new LocalPath(Path.Combine(repositoryBasePath, itemFullPath));
                    files.Add(new File(itemFullPath, absolutePath, new FileContent(repoFile.content)));
                }
            }
        }

        return new Folder(fullPath, subFolders, files);
    }

    private Folder GetFolder(LocalPath path, string repositoryBasePath)
    {
        var folders = Directory.EnumerateDirectories(path.Value, "", SearchOption.TopDirectoryOnly)
            .Where(f => !Folder.ExcludedDirs.Any(d => f.Contains($@"/{d}/") || f.EndsWith($@"/{d}")))
            .Select(f => GetFolder(new LocalPath(f), repositoryBasePath))
            .ToList();

        var files = Directory.EnumerateFiles(path.Value, "", SearchOption.TopDirectoryOnly)
            .Where(f => File.AllowedExtensions.Contains(Path.GetExtension(f)))
            .Select(f => GetFile(new LocalPath(f), repositoryBasePath))
            .ToList();

        return new Folder(ToRelativePath(path.Value, repositoryBasePath), folders, files);
    }

    private File GetFile(LocalPath filePath, string repositoryBasePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath.Value);
            var relativeName = ToRelativePath(filePath.Value, repositoryBasePath);

            if (fileInfo.Length > File.MaxBytesPerFile)
            {
                Console.Error.WriteLine($"[TRUNCATE] {filePath} ({fileInfo.Length} bytes)");
                return new File(relativeName, filePath, new FileContent(string.Empty));
            }
            var content = System.IO.File.ReadAllText(filePath.Value);
            return new File(relativeName, filePath, new FileContent(content));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SKIP] {filePath}: {ex.Message}");
            return null;
        }
    }

    private static string ToRelativePath(string absolutePath, string repositoryBasePath)
    {
        var relative = Path.GetRelativePath(repositoryBasePath, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        return relative == "." ? string.Empty : relative;
    }
}