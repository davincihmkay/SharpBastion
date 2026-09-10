namespace SharpBastion.ValueObjects;

public class Message
{
    public readonly string Value;

    public Message(string message)
    {
        Value = message;
    }
}