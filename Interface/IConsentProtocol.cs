namespace SharpBastion.Interface;

/// <summary>
/// Shared shape so Program.cs's prompt loop can resolve consent generically
/// — IExternalLlmProtocol and IScriptExecutionProtocol both implement this
/// without the loop needing to know which one it's handling.
/// </summary>
public interface IConsentProtocol
{
    bool IsOverridden { get; }
    void OverrideProtocol();

    /// <summary>Completes "Override {Subject}? [y/n]:".</summary>
    string Subject { get; }

    /// <summary>
    /// Printed above the prompt; each '\n'-separated line gets its own
    /// "[PROTOCOL] " prefix.
    /// </summary>
    string Description { get; }

    string OverriddenMessage { get; }
    string ActiveMessage { get; }
}