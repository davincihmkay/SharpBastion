using SharpBastion.Constants;
using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class Repository
{
    public string Name { get; }
    private readonly Folder _rootFolder;
    private List<Response> _responses;
    private Dictionary<string, string> _metaData;

    public string TempDirectory =>
        _metaData[RepositoryConstants.TempDirectory];

    public Folder RootFolder => _rootFolder;

    public Repository(string name, Folder rootFolder, Dictionary<string, string> metaData, List<Response> responses)
    {
        Name = name;
        _rootFolder = rootFolder;
        _metaData = metaData;
        _responses = responses;
    }


    public bool TryGetXmlFileName(out string? xmlFileName) =>
        _metaData.TryGetValue(RepositoryConstants.XmlFileName, out xmlFileName);

    public bool ContainsFile(string relativePath) =>
        _rootFolder.GetAllFiles().Any(f => f.Name.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

    public Response GetLastRespone() => _responses.LastOrDefault();

    public void AddResponse(Response lastResponse) => _responses.Add(lastResponse);

    public IReadOnlyList<File> GetAllFiles() => _rootFolder.GetAllFiles();

    public void AttachNewFile(File file) => _rootFolder.AttachFile(file);
}