namespace SharpBastion.Interface;

public interface IOutputBroadcaster
{
    void Broadcast(string text);
    void Subscribe(Stream subscriberStream);
}