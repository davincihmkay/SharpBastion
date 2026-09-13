namespace SharpBastion.Views;

public class IngestRepositoriesResultView
{
    public readonly bool Success;
    public readonly string Message;
    public readonly IReadOnlyList<string> IngestedRepositoryPaths;

    public IngestRepositoriesResultView(bool success, string message, IReadOnlyList<string> ingestedRepositoryPaths)
    {
        Success = success;
        Message = message;
        IngestedRepositoryPaths = ingestedRepositoryPaths;
    }
}