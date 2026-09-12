using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class PendingKagiSearch
{
    public string Id { get; }
    public KagiQuery Query { get; }
    public RepositoryName RepositoryName { get; }

    public ResponseId RespondingTo { get; }

    public PendingKagiSearch(KagiQuery query, RepositoryName repositoryName, ResponseId respondingTo)
    {
        Id = Guid.NewGuid().ToString("N");
        Query = query;
        RepositoryName = repositoryName;
        RespondingTo = respondingTo;
    }
}