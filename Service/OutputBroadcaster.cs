using System.Collections.Concurrent;
using System.Text;
using SharpBastion.Interface;

namespace SharpBastion.Service;

public class OutputBroadcaster : IOutputBroadcaster
{
    private readonly ConcurrentDictionary<Stream, byte> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public void Subscribe(Stream subscriberStream) => _subscribers.TryAdd(subscriberStream, 0);

    public void Unsubscribe(Stream subscriberStream)
    {
        if (_subscribers.TryRemove(subscriberStream, out _))
        {
            try { subscriberStream.Dispose(); } catch { /* already gone */ }
        }
    }

    public void Broadcast(string text)
    {
        if (text.Length == 0 || _subscribers.IsEmpty) return;

        var bytes = Encoding.UTF8.GetBytes(text);

        foreach (var subscriber in _subscribers.Keys)
        {
            try
            {
                subscriber.Write(bytes, 0, bytes.Length);
                subscriber.Flush();
            }
            catch
            {
                Unsubscribe(subscriber);
            }
        }
    }
}