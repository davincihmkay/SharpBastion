using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Commands;
using SharpBastion.Constants;
using SharpBastion.Domain;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.PromptBuilder;
using SharpBastion.ValueObjects;
using SharpBastion.Views;
using Domain_File = SharpBastion.Domain.File;
using File = SharpBastion.Domain.File;

namespace SharpBastion.Service;

public class RepositoryIngestorService : IRepositoryIngestorService
{
    private IRepositoryDomainService _repositoryDomainService;
    private readonly IRepomixCliClient _repomixCliClient;
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly List<Repository> _cachedRepositories;

    public RepositoryIngestorService(IRepositoryDomainService repositoryDomainService,
        IRepomixCliClient repomixCliClient,
        PendingWriteQueue pendingWriteQueue)
    {
        _repositoryDomainService = repositoryDomainService;
        _repomixCliClient = repomixCliClient;
        _pendingWriteQueue = pendingWriteQueue;

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

    public OperationResultView AskQuestion(AskRepositoryQuestionCommand command)
    {
        var repository = _cachedRepositories.SingleOrDefault(x => x.Name == command.RepositoryName.Value);
        if (repository == null)
            return new OperationResultView(false, $"Repository '{command.RepositoryName.Value}' not found. Ingest it first.");

        var lastResponse = repository.GetLastRespone();
        if (lastResponse == null)
            return new OperationResultView(false, $"Repository '{command.RepositoryName.Value}' has no prior response — not ingested most likely.");

        var response = _repositoryDomainService.AskQuestion(command.Question, lastResponse, repository.TempDirectory);
        repository.AddResponse(response);

        // Pass 1: existing files
        foreach (var file in repository.GetAllFiles())
        {
            var proposedContent = WriteProposalParser.TryExtractContent(response.Message, file.Name);
            file.ApplyProposal(proposedContent);
            if (file.HasPendingWrite)
            {
                var enqueued = _pendingWriteQueue.Enqueue(file);
                if (!enqueued)
                    Console.WriteLine($"[SKIP] {file.Name} already pending review.");
            }
        }

        // Pass 2: new files — repository determines existence, File owns proposal application.
        // Attached to the repository's tree immediately (regardless of eventual approve/reject)
        // so later ask/review cycles treat this path as an existing, trackable file instead of
        // re-running this branch — and re-diffing against an empty baseline — indefinitely.
        foreach (var proposedPath in WriteProposalParser.GetProposedPaths(response.Message))
        {
            if (repository.ContainsFile(proposedPath)) continue;

            var absolutePath = new LocalPath(Path.Combine(repository.Name, proposedPath));

            if (!PathGuard.IsWithinRoot(repository.Name, absolutePath.Value)) continue;

            var newFile = new File(proposedPath, absolutePath, new FileContent(string.Empty));
            repository.AttachNewFile(newFile);

            var proposedContent = WriteProposalParser.TryExtractContent(response.Message, proposedPath);
            newFile.ApplyProposal(proposedContent);
            if (newFile.HasPendingWrite)
            {
                var enqueued = _pendingWriteQueue.Enqueue(newFile);
                if (!enqueued)
                    Console.WriteLine($"[SKIP] {newFile.Name} already pending review.");
            }
        }

        return new OperationResultView(true, repository.GetLastRespone().Message.Value);
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