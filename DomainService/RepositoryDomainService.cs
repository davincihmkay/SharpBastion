using SharpBastion.ClientRequestObject;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.ValueObjects;

namespace SharpBastion.DomainService;

public class RepositoryDomainService : IRepositoryDomainService
{
    private readonly string _modelName;
    private readonly string _systemPrompt;

    private readonly ILmStudioClient _lmStudioClient;
    private readonly IClaudeCliClient _claudeCliClient;
    private readonly IExternalLlmProtocol _externalLlmProtocol;

    public RepositoryDomainService(
        ILmStudioClient lmStudioClient,
        IClaudeCliClient claudeCliClient,
        IExternalLlmProtocol externalLlmProtocol,
        AssistantProfile assistantProfile)
    {
        _lmStudioClient = lmStudioClient;
        _claudeCliClient = claudeCliClient;
        _externalLlmProtocol = externalLlmProtocol;
        _modelName = assistantProfile.ModelName;
        _systemPrompt = assistantProfile.SystemPrompt;
    }

    public List<Response> SendMessagesToLmStudio(List<Message> messages, string workingDirectory)
    {
        var responses = new List<Response>();
        Response lastResponse = null;

        foreach (var message in messages)
        {
            string responseId = lastResponse?.Id.Value;

            if (_lmStudioClient.IsOnline().ConfigureAwait(false).GetAwaiter().GetResult())
            {
                var requestObject = new LmStudioClientChatPromptClientRequestObject(_modelName, _systemPrompt, message.Value, responseId);
                var responseObject = _lmStudioClient.SendMessageData(requestObject);

                var lastResponseId = new ResponseId(responseObject.response_id);
                var responseMessages = responseObject.output.Select(x => x.content).ToList();
                var formattedMessage = new Message(string.Join(Environment.NewLine, responseMessages));
                lastResponse = new Response(lastResponseId, formattedMessage);
                responses.Add(lastResponse);
            }
            else
            {
                EnforceExternalRoutingProtocol();

                var requestObject = new ClaudeCliClientChatPromptRequestObject(
                    _systemPrompt, message.Value, responseId, deleteSession: false, workingDirectory);
                var responseObject = _claudeCliClient.SendMessageDataAsync(requestObject).ConfigureAwait(false).GetAwaiter().GetResult();

                var lastResponseId = new ResponseId(responseObject.session_id);
                var formattedMessage = new Message(responseObject.result);
                lastResponse = new Response(lastResponseId, formattedMessage);
                responses.Add(lastResponse);
            }
        }

        return responses;
    }

    public Response AskQuestion(Question question, Response lastResponse, string workingDirectory)
    {
        if (_lmStudioClient.IsOnline().ConfigureAwait(false).GetAwaiter().GetResult())
        {
            var requestObject = new LmStudioClientChatPromptClientRequestObject(_modelName, question.Value, lastResponse.Id.Value);
            var responseObject = _lmStudioClient.SendMessageData(requestObject);
            var newResponseId = new ResponseId(responseObject.response_id);
            var responseMessages = responseObject.output.Select(x => x.content).ToList();
            var formattedMessage = new Message(string.Join(Environment.NewLine, responseMessages));
            return new Response(newResponseId, formattedMessage);
        }
        else
        {
            EnforceExternalRoutingProtocol();

            var requestObject = new ClaudeCliClientChatPromptRequestObject(
                _systemPrompt, question.Value, lastResponse.Id.Value, deleteSession: false, workingDirectory);
            var responseObject = _claudeCliClient.SendMessageDataAsync(requestObject).ConfigureAwait(false).GetAwaiter().GetResult();
            var newResponseId = new ResponseId(responseObject.session_id);
            var formattedMessage = new Message(responseObject.result);
            return new Response(newResponseId, formattedMessage);
        }
    }

    private void EnforceExternalRoutingProtocol()
    {
        if (!_externalLlmProtocol.IsOverridden)
            throw new InvalidOperationException(
                "LmStudio is offline. External routing to Claude CLI (Anthropic) is not permitted. " +
                "Start LmStudio, or override the protocol at startup to allow external routing.");
    }
}