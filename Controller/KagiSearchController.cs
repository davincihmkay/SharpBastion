using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class KagiSearchController
{
    private readonly PendingKagiSearchQueue _pendingKagiSearchQueue;
    private readonly IRepositoryIngestorService _repositoryIngestorService;

    public KagiSearchController(
        PendingKagiSearchQueue pendingKagiSearchQueue,
        IRepositoryIngestorService repositoryIngestorService)
    {
        _pendingKagiSearchQueue = pendingKagiSearchQueue;
        _repositoryIngestorService = repositoryIngestorService;
    }

    public IReadOnlyList<PendingKagiSearchView> ListPendingSearches() =>
        _pendingKagiSearchQueue.ListPending()
            .Select(p => new PendingKagiSearchView(p.Id, p.Query.Value))
            .ToList();

    public AskQuestionResultView ResolveSearch(ResolveKagiSearchRequestObject requestObject)
    {
        var command = ResolveKagiSearchCommand.PopulateFromRequestObject(requestObject);
        return _repositoryIngestorService.ResolveKagiSearch(command);
    }
}