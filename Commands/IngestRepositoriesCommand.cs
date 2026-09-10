using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class IngestRepositoriesCommand
{
    public required List<LocalPath> RepositoryPaths;
    public required bool IsBestPractice = false;

    public static IngestRepositoriesCommand PopulateFromRequestObject(
        IngestRepositoriesRequestObject requestObject)
    {
        return new IngestRepositoriesCommand()
        {
            RepositoryPaths = requestObject.Paths.Select(x => new LocalPath(x)).ToList(),
            IsBestPractice = requestObject.IsBesPractice
        };
    }
}