using SharpBastion.Interface;

namespace SharpBastion.Protocol;

/// <summary>
/// Carries the user's explicit consent decision for routing queries to external LLM backends.
/// Defaults to not overridden. Must be explicitly overridden at startup via user prompt.
/// </summary>
public class ExternalLlmProtocol : IExternalLlmProtocol
{
    public bool IsOverridden { get; private set; } = false;

    public void OverrideProtocol() => IsOverridden = true;

    public string Subject => "external routing restriction";

    public string Description =>
        "If LmStudio is offline, queries can fall back to an External LLM provider.\n" +
        "Repository content would leave this machine.";

    public string OverriddenMessage => "External routing restriction: OVERRIDDEN.";

    public string ActiveMessage => "External routing restriction: ACTIVE. LmStudio must be online for all queries.";
}