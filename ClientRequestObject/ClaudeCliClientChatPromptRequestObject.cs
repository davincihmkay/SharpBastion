namespace SharpBastion.ClientRequestObject;

[Serializable]
public class ClaudeCliClientChatPromptRequestObject
{
    public string prompt { get; set; }
    public string systemPrompt { get; set; }
    public string? sessionId { get; set; } = null;
    public bool deleteSession { get; set; } = false;
    public string workingDirectory { get; set; }

    public ClaudeCliClientChatPromptRequestObject(
        string systemPrompt,
        string prompt,
        string? sessionId,
        bool deleteSession,
        string workingDirectory)
    {
        this.systemPrompt = systemPrompt;
        this.prompt = prompt;
        this.sessionId = sessionId;
        this.deleteSession = deleteSession;
        this.workingDirectory = workingDirectory;
    }
}