namespace SharpBastion.Views;

public class AskQuestionResultView
{
    public readonly bool Success;
    public readonly string DisplayMessage;
    public readonly IReadOnlyList<string> Notices;

    public AskQuestionResultView(bool success, string displayMessage, IReadOnlyList<string> notices)
    {
        Success = success;
        DisplayMessage = displayMessage;
        Notices = notices;
    }

    public AskQuestionResultView(bool success, string displayMessage)
        : this(success, displayMessage, Array.Empty<string>())
    {
    }
}