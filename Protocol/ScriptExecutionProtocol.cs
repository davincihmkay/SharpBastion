using SharpBastion.Interface;

namespace SharpBastion.Protocol;

/// <summary>Consent gate for unattended scheduled-script execution — see IConsentProtocol.</summary>
public class ScriptExecutionProtocol : IScriptExecutionProtocol
{
    public bool IsOverridden { get; private set; } = false;

    public void OverrideProtocol() => IsOverridden = true;

    public string Subject => "script-execution restriction and allow unattended runs";

    public string Description =>
        "Scheduled Python scripts (Scripts/ only) can run unattended, or wait for interactive approval.\n" +
        "Unattended execution runs due scripts with no per-run confirmation.";

    public string OverriddenMessage => "Script-execution restriction: OVERRIDDEN. Due jobs run unattended.";

    public string ActiveMessage => "Script-execution restriction: ACTIVE. Due jobs are queued — approve via 'review'.";
}