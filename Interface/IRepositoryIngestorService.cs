using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IRepositoryIngestorService
{
    OperationResultView IngestRepositories(IngestRepositoriesCommand command);
    OperationResultView AskQuestion(AskRepositoryQuestionCommand command);
}