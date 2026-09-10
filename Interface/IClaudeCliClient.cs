using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;

namespace SharpBastion.Interface;

public interface IClaudeCliClient
{
    Task<ClaudeCliClientChatResponseObject> SendMessageDataAsync(ClaudeCliClientChatPromptRequestObject clientRequestObject);
}