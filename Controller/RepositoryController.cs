using SharpBastion.Commands;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class RepositoryController
{
    private IRepositoryIngestorService _repositoryIngestorService;
    
    public RepositoryController(IRepositoryIngestorService repositoryIngestorService)
    {
        _repositoryIngestorService = repositoryIngestorService;   
    }
    
    public OperationResultView IngestRepositories(IngestRepositoriesRequestObject requestObject)
    {
        var command = IngestRepositoriesCommand.PopulateFromRequestObject(requestObject);
        return _repositoryIngestorService.IngestRepositories(command);
    }
    
    public OperationResultView AskQuestion(AskQuestionRequestObject requestObject)
    {
        var command = AskRepositoryQuestionCommand.PopulateFromRequestObject(requestObject);
        return _repositoryIngestorService.AskQuestion(command);
    }
}