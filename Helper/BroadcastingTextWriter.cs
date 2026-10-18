using System.Text;
using SharpBastion.Interface;

namespace SharpBastion.Helper;

public sealed class BroadcastingTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly IOutputBroadcaster _broadcaster;

    public BroadcastingTextWriter(TextWriter inner, IOutputBroadcaster broadcaster)
    {
        _inner = inner;
        _broadcaster = broadcaster;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);
        _broadcaster.Broadcast(value.ToString());
    }

    public override void Write(string? value)
    {
        _inner.Write(value);
        if (!string.IsNullOrEmpty(value))
            _broadcaster.Broadcast(value);
    }

    public override void WriteLine(string? value)
    {
        _inner.WriteLine(value);
        _broadcaster.Broadcast((value ?? string.Empty) + _inner.NewLine);
    }

    public override void WriteLine()
    {
        _inner.WriteLine();
        _broadcaster.Broadcast(_inner.NewLine);
    }

    public override void Flush() => _inner.Flush();
}