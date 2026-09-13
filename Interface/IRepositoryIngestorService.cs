using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IRepositoryIngestorService
{
    IngestRepositoriesResultView IngestRepositories(IngestRepositoriesCommand command);
    AskQuestionResultView AskQuestion(AskRepositoryQuestionCommand command);
    AskQuestionResultView ResolveKagiSearch(ResolveKagiSearchCommand command);
}