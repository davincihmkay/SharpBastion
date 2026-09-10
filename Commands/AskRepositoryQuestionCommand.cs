using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class AskRepositoryQuestionCommand
{
    public required Question Question;
    public required RepositoryName RepositoryName;

    public static AskRepositoryQuestionCommand PopulateFromRequestObject(
        AskQuestionRequestObject requestObject)
    {
        return new AskRepositoryQuestionCommand()
        {
            Question = new Question(requestObject.Question),
            RepositoryName = new RepositoryName(requestObject.RepositoryName)
        };
    }
}