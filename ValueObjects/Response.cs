namespace SharpBastion.ValueObjects;

public class Response
{
    public readonly ResponseId Id;
    public readonly Message Message;

    public Response(ResponseId responseId, Message message)
    {
        Id = responseId;
        Message = message;
    }
}