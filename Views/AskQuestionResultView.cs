namespace SharpBastion.Views;

public class AskQuestionResultView
{
    public readonly bool Success;
    public readonly IReadOnlyList<AskDisplaySegment> Segments;
    public readonly IReadOnlyList<string> Notices;

    public AskQuestionResultView(bool success, IReadOnlyList<AskDisplaySegment> segments, IReadOnlyList<string> notices)
    {
        Success = success;
        Segments = segments;
        Notices = notices;
    }

    public AskQuestionResultView(bool success, string message)
        : this(success, new AskDisplaySegment[] { new TextDisplaySegment(message) }, Array.Empty<string>())
    {
    }
}