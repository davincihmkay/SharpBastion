using SharpBastion.ValueObjects;

namespace SharpBastion.Interface;

public interface IRepositoryDomainService
{
    List<Response> SendMessagesToLmStudio(List<Message> messages, string workingDirectory);
    Response AskQuestion(Question question, Response lastResponse, string workingDirectory);
}