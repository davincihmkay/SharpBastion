namespace SharpBastion.Domain;

public class Folder
{
    private readonly string _name;
    private readonly List<Folder> _subFolders;
    private readonly List<File> _files;

    private static readonly HashSet<string> _excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", ".vs", "packages", "vendor", "dist", "build" };

    /// <summary>
    /// Read-only view over the directory-exclusion set. The backing set (_excludedDirs)
    /// is private so this guardrail cannot be mutated at runtime by external callers.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedDirs = _excludedDirs;

    public string Name => _name;
    public IReadOnlyList<Folder> SubFolders => _subFolders;
    public IReadOnlyList<File> Files => _files;

    public Folder(string name, List<Folder> subFolders, List<File> files)
    {
        _name = name;
        _subFolders = subFolders;
        _files = files;
    }

    public string GetTreeStructure(string hyphenPrepend)
    {
        return hyphenPrepend + _name + Environment.NewLine + _subFolders.Select(x => x.GetTreeStructure(hyphenPrepend + hyphenPrepend)).ToList();
    }

    public IReadOnlyList<File> GetAllFiles()
    {
        var files = new List<File>(_files);
        files.AddRange(_subFolders.SelectMany(f => f.GetAllFiles()));
        return files;
    }

    public void AttachFile(File file)
    {
        var pathParts = file.Name.Split('/');
        var directoryNames = pathParts.Take(pathParts.Length - 1);

        var folder = this;
        var cumulativePath = _name;

        foreach (var directoryName in directoryNames)
        {
            cumulativePath = cumulativePath.Length == 0 ? directoryName : $"{cumulativePath}/{directoryName}";
            folder = folder.GetOrCreateSubFolder(cumulativePath);
        }

        folder.AddFile(file);
    }

    private Folder GetOrCreateSubFolder(string fullRelativePath)
    {
        var existing = _subFolders.FirstOrDefault(f => f.Name.Equals(fullRelativePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var created = new Folder(fullRelativePath, new List<Folder>(), new List<File>());
        _subFolders.Add(created);
        return created;
    }

    private void AddFile(File file) => _files.Add(file);
}