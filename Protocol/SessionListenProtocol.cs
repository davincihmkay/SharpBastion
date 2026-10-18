using SharpBastion.Interface;

namespace SharpBastion.Protocol;

/// <summary>
/// Carries the user's explicit consent decision for allowing a second local
/// process (e.g. another SSH session on this machine) to attach and observe
/// this session's console output in real time, read-only. Defaults to not
/// overridden — no listener endpoint is bound at all until explicitly opted
/// into; there is no partial/half-open state, matching
/// IExternalLlmProtocol/IScriptExecutionProtocol/IKagiSearchProtocol. The
/// override is per-process and requested at startup, alongside the other
/// three protocol prompts.
/// </summary>
public class SessionListenProtocol : ISessionListenProtocol
{
    public bool IsOverridden { get; private set; } = false;

    public void OverrideProtocol() => IsOverridden = true;

    public string Subject => "session-listen restriction and allow read-only observers";

    public string Description =>
        "A second local process (e.g. another SSH session on this machine) can attach and\n" +
        "observe this session's console output in real time, read-only — it cannot type\n" +
        "input, approve reviews, or otherwise act as this session.\n" +
        "Attaching exposes ingested repository content and LLM responses to that observer.";

    public string OverriddenMessage =>
        "Session-listen restriction: OVERRIDDEN. A local read-only listener endpoint is open for this process.";

    public string ActiveMessage =>
        "Session-listen restriction: ACTIVE. No listener endpoint is bound — this session cannot be observed.";
}