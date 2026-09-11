using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class File
{
    private readonly string _name;
    private readonly LocalPath _path;
    private FileContent _content;
    private PendingFileWrite? _pendingWrite;

    public string Name => _name;
    public string Content => _content.Value;

    public static int MaxBytesPerFile = 25_000;

    private static readonly HashSet<string> _allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".json", ".yaml", ".yml", ".xml", ".txt", ".md", ".config", ".toml", ".ini", ".sql", ".html", ".css",
        ".js", ".ts", ".proto", ".sh", ".ps1", ".bat", ".py"
    };

    public static readonly IReadOnlySet<string> AllowedExtensions = _allowedExtensions;

    private static readonly HashSet<string> _allowedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".gitignore", ".dockerignore", "LICENSE", "NOTICE", "README", "CONTRIBUTING", "CHANGELOG"
    };

    /// <summary>
    /// Read-only view over the extensionless-filename allowlist. See AllowedExtensions.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedFileNames = _allowedFileNames;

    public File(string name, LocalPath path, FileContent content)
    {
        _name = name;
        _path = path;
        _content = content;
    }

    public bool HasPendingWrite => _pendingWrite is not null;

    public PendingFileWrite? GetPendingWrite() => _pendingWrite;

    public void ApplyProposal(FileContent? proposedContent)
    {
        _pendingWrite = null;
        if (proposedContent is null) return;

        var fileName = Path.GetFileName(_name);
        var extension = Path.GetExtension(_name);
        if (!AllowedExtensions.Contains(extension) && !AllowedFileNames.Contains(fileName))
        {
            Console.WriteLine($"[SKIP] {_name}: extension '{extension}' not in allowlist, write proposal ignored.");
            return;
        }

        _pendingWrite = new PendingFileWrite(_path, proposedContent);
    }

    public void AcceptPendingWrite()
    {
        if (_pendingWrite is null) return;

        _content = _pendingWrite.ProposedContent;
        _pendingWrite = null;
    }

    public void DiscardPendingWrite() => _pendingWrite = null;
}