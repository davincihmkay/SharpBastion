using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;

namespace SharpBastion.Interface;

public interface ILmStudioClient
{
    LmStudioClientChatResponseObject SendMessageData(LmStudioClientChatPromptClientRequestObject clientRequestObject);
    Task<bool> IsOnline();

}