namespace SharpBastion.Views;

/// <summary>
/// One ordered piece of an AskQuestionResultView's rendering — either a run of
/// plain LLM prose, or a file-write proposal outcome. Replaces the old single
/// flattened DisplayMessage string precisely so Program.cs can colorize each
/// ProposalDisplaySegment's diff (via IConsoleHighlighter) without any layer
/// below it (Service/Views) ever producing ANSI-decorated or pre-rendered
/// text itself.
/// </summary>
public abstract class AskDisplaySegment
{
}

public sealed class TextDisplaySegment : AskDisplaySegment
{
    public string Text { get; }

    public TextDisplaySegment(string text)
    {
        Text = text;
    }
}

public sealed class ProposalDisplaySegment : AskDisplaySegment
{
    public ProposalOutcomeView Outcome { get; }

    public ProposalDisplaySegment(ProposalOutcomeView outcome)
    {
        Outcome = outcome;
    }
}